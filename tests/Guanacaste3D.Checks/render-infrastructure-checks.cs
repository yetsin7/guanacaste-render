using System.Numerics;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

/// <summary>
/// Checks the renderer's measurement and planning infrastructure: image codecs, quality metrics, hardware
/// classification, memory-aware quality planning and IES photometry. Each has an analytic or round-trip answer.
/// </summary>
static class RenderInfrastructureChecks
{
    public static void Run(Action<bool, string> check)
    {
        PngAndMetrics(check);
        Hardware(check);
        Planning(check);
        AutomaticQualityChecks.Run(check);
        Photometry(check);
        Portals(check);
        PortalsInTracer(check);
        PortalsOnHugeGlassModels(check);
        ManyLights(check);
        GuidedDenoiser(check);
        LiveUpdates(check);
        UpsamplerSeam(check);
        RadianceCaching(check);
        PhysicalCamera(check);
        DenoiserSeam(check);
    }

    private static void PngAndMetrics(Action<bool, string> check)
    {
        const int width = 37, height = 23; // odd sizes catch stride mistakes
        var rgb = new byte[width * height * 3];
        for (var i = 0; i < rgb.Length; i++) rgb[i] = (byte)(i * 7 + i / 31);
        var image = new RasterImage(width, height, rgb);
        var decoded = PngCodec.Decode(PngCodec.Encode(image));
        check(decoded.Width == width && decoded.Height == height && decoded.Rgb.AsSpan().SequenceEqual(rgb),
            "PNG encode then decode returns the identical pixels");

        var corrupt = PngCodec.Encode(image);
        corrupt[corrupt.Length / 2] ^= 0x55;
        var rejected = false;
        try { PngCodec.Decode(corrupt); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "A corrupted PNG is rejected instead of decoded into garbage");

        var overflowChunk = PngCodec.Encode(image);
        overflowChunk[8] = 0x7f; overflowChunk[9] = 0xff;
        overflowChunk[10] = 0xff; overflowChunk[11] = 0xff;
        rejected = false;
        try { PngCodec.Decode(overflowChunk); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "PNG chunk length overflow is rejected as invalid data");

        var missingEnd = PngCodec.Encode(image);
        Array.Resize(ref missingEnd, missingEnd.Length - 12);
        rejected = false;
        try { PngCodec.Decode(missingEnd); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "PNG without an IEND chunk is rejected");

        var oversized = PngCodec.Encode(image);
        static void SetBigEndian(byte[] bytes, int offset, int value)
        {
            bytes[offset] = (byte)(value >> 24);
            bytes[offset + 1] = (byte)(value >> 16);
            bytes[offset + 2] = (byte)(value >> 8);
            bytes[offset + 3] = (byte)value;
        }
        SetBigEndian(oversized, 16, PngCodec.MaximumDimension);
        SetBigEndian(oversized, 20, PngCodec.MaximumDimension);
        uint crc = 0xffffffff;
        for (var i = 12; i < 29; i++)
        {
            crc ^= oversized[i];
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320u : crc >> 1;
        }
        SetBigEndian(oversized, 29, unchecked((int)~crc));
        rejected = false;
        try { PngCodec.Decode(oversized); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "PNG header requesting excessive decompression memory is rejected");

        // A PNG ends at IEND: accepting appended bytes can hide malformed or polyglot files.
        var trailingBytes = PngCodec.Encode(image).Concat(new byte[] { 0x42 }).ToArray();
        rejected = false;
        try { PngCodec.Decode(trailingBytes); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "PNG rejects bytes appended after IEND");

        // A valid CRC does not make a shorter IHDR truthful: the zlib payload still
        // contains the original last scanline and must be rejected as oversized.
        var excessScanline = PngCodec.Encode(image);
        SetBigEndian(excessScanline, 20, height - 1);
        crc = 0xffffffff;
        for (var i = 12; i < 29; i++)
        {
            crc ^= excessScanline[i];
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320u : crc >> 1;
        }
        SetBigEndian(excessScanline, 29, unchecked((int)~crc));
        rejected = false;
        try { PngCodec.Decode(excessScanline); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "PNG rejects decompressed scanlines beyond IHDR dimensions");

        var invalidRaster = false;
        try { RasterImage.FromBgra32(65536, 65536, ReadOnlySpan<byte>.Empty); }
        catch (ArgumentException) { invalidRaster = true; }
        check(invalidRaster, "BGRA image dimensions cannot wrap a 32-bit pixel count");

        invalidRaster = false;
        try { PngCodec.Encode(new RasterImage(0, 1, [])); }
        catch (ArgumentException) { invalidRaster = true; }
        check(invalidRaster, "PNG encoding rejects invalid image dimensions");

        var test = image.ToDisplay();
        var same = ImageMetrics.Compare(test, test, width, height);
        check(double.IsPositiveInfinity(same.Psnr) && Math.Abs(same.Ssim - 1) < 1e-9 && same.MeanDeltaE == 0 && same.OutOfTolerance == 0,
            "Identical images score infinite PSNR, SSIM 1 and zero color difference");

        var random = new Random(5);
        var noisy = test.Select(p => Vector3.Clamp(p + new Vector3((float)(random.NextDouble() - 0.5) * 0.2f), Vector3.Zero, Vector3.One)).ToArray();
        var slightlyNoisy = test.Select(p => Vector3.Clamp(p + new Vector3((float)(random.NextDouble() - 0.5) * 0.02f), Vector3.Zero, Vector3.One)).ToArray();
        var heavy = ImageMetrics.Compare(noisy, test, width, height);
        var light = ImageMetrics.Compare(slightlyNoisy, test, width, height);
        check(light.Psnr > heavy.Psnr + 10 && light.Ssim > heavy.Ssim && light.MeanDeltaE < heavy.MeanDeltaE,
            "Metrics rank a lightly perturbed image above a heavily perturbed one");
        // A constant offset of 0.1 has MSE 0.01, which is exactly 20 dB.
        var shifted = test.Select(p => p * 0 + new Vector3(0.5f)).ToArray();
        var offset = shifted.Select(p => p + new Vector3(0.1f)).ToArray();
        check(Math.Abs(ImageMetrics.Compare(offset, shifted, width, height).Psnr - 20) < 0.01, "PSNR of a 0.1 offset is 20 dB");
    }

    private static void Hardware(Action<bool, string> check)
    {
        var gpu = HardwareProfile.Classify("NVIDIA GeForce RTX 4050 Laptop GPU", 6439305216);
        check(gpu.Vendor == GpuVendor.Nvidia && !gpu.IsIntegrated && gpu.InferredHardwareRayTracing, "RTX laptop GPU is classified as discrete NVIDIA with RT hardware");
        var integrated = HardwareProfile.Classify("Intel(R) UHD Graphics", 0);
        check(integrated.Vendor == GpuVendor.Intel && integrated.IsIntegrated && !integrated.InferredHardwareRayTracing, "Intel UHD is classified as integrated without RT hardware");
        check(HardwareProfile.Classify("AMD Radeon RX 7600", 8L << 30).InferredHardwareRayTracing &&
              !HardwareProfile.Classify("AMD Radeon RX 580", 8L << 30).InferredHardwareRayTracing &&
              HardwareProfile.Classify("Intel(R) Arc(TM) A750 Graphics", 8L << 30).InferredHardwareRayTracing,
            "RT inference separates RDNA2+ and Arc from older cards");
        var profile = HardwareProfile.Detect(measure: true);
        check(profile.LogicalCores >= 1 && profile.TotalMemoryBytes > 0 && profile.MicrobenchmarkMegaTests > 1 && profile.BackendSummary.Contains("CPU"),
            "Hardware detection reports cores, memory, a positive microbenchmark and the CPU backend");
        Console.WriteLine($"INFO: {profile.CpuName} · {profile.LogicalCores} threads · {profile.VectorBits}-bit SIMD · " +
                          $"{profile.TotalMemoryBytes / 1073741824.0:0.0} GB · GPUs: {string.Join("; ", profile.Gpus.Select(g => $"{g.Name} ({g.DedicatedMemoryBytes / 1073741824.0:0.0} GB)"))} · " +
                          $"{profile.MicrobenchmarkMegaTests:0} Mtests/s");
    }

    private static void Planning(Action<bool, string> check)
    {
        HardwareProfile Machine(int cores, double gigabytes, bool battery = false) => new()
        {
            LogicalCores = cores, AvailableMemoryBytes = (long)(gigabytes * 1073741824), TotalMemoryBytes = (long)(gigabytes * 1073741824), IsOnBattery = battery
        };
        var big = QualityPlanner.Plan(null, Machine(16, 24), 1920, 1080);
        var middle = QualityPlanner.Plan(null, Machine(8, 12), 1920, 1080);
        var small = QualityPlanner.Plan(null, Machine(4, 3), 1920, 1080);
        check(big.Quality == RenderQuality.High && middle.Quality == RenderQuality.Standard && small.Quality == RenderQuality.Draft &&
              big.Notes.Contains(PlanNote.ChosenAutomatically),
            "AUTO picks High for a strong machine, Standard for a middle one and Draft for a weak one");
        check(big.TargetSamples > middle.TargetSamples,
            $"High must exceed Standard samples (High={big.TargetSamples}, Standard={middle.TargetSamples})");
        check(middle.TargetSamples > small.TargetSamples,
            $"Standard must exceed Draft samples (Standard={middle.TargetSamples}, Draft={small.TargetSamples})");
        check(big.Threads > small.Threads,
            $"Strong machine must use more threads (16 cores={big.Threads}, 4 cores={small.Threads})");
        check(QualityPlanner.Plan(null, Machine(64, 256), 1920, 1080).Quality != RenderQuality.Final, "AUTO never chooses Final");
        var final = QualityPlanner.Plan(RenderQuality.Final, Machine(16, 24), 1920, 1080);
        var onBattery = QualityPlanner.Plan(RenderQuality.Final, Machine(16, 24, battery: true), 1920, 1080);
        check(final.Quality == RenderQuality.Final && !final.LowPriorityThreads && onBattery.Quality == RenderQuality.Standard &&
              onBattery.Resources == ResourceMode.Eco && onBattery.Notes.Contains(PlanNote.BatteryLowered),
            "Battery power lowers Final to Standard with half the cores, and says so");
        check(QualityPlanner.Plan(RenderQuality.High, Machine(16, 24), 1920, 1080).Resources == ResourceMode.Balanced &&
              QualityPlanner.Plan(RenderQuality.High, Machine(16, 24), 1920, 1080).Threads == 15,
            "Balanced mode leaves one core to the interface");

        HardwareProfile Measured(double megaTests, bool battery = false) => Machine(16, 24, battery).WithMeasurement(megaTests);
        check(QualityPlanner.PreviewHeight(Machine(16, 24), ResourceMode.Balanced) == 288 &&
              QualityPlanner.PreviewHeight(Measured(QualityPlanner.ReferenceMegaTests), ResourceMode.Balanced) == 288,
            "Preview height keeps the hand-tuned size without a benchmark and at the reference speed");
        check(QualityPlanner.PreviewHeight(Measured(QualityPlanner.ReferenceMegaTests / 4), ResourceMode.Balanced) == 144 &&
              QualityPlanner.PreviewHeight(Measured(QualityPlanner.ReferenceMegaTests * 4), ResourceMode.Balanced) == 540 &&
              QualityPlanner.PreviewHeight(Measured(QualityPlanner.ReferenceMegaTests, battery: true), ResourceMode.Balanced) < 288,
            "Preview height follows the square root of measured speed, within 144–540 px, and shrinks on battery");

        var hardware = new HardwareProfile { CpuName = "Test CPU", LogicalCores = 16 };
        var now = new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc);
        var stored = new MachineCalibration("Test CPU", 16, 2500, now.AddDays(-3));
        check(MachineCalibrationStore.IsValidFor(stored, hardware, now) &&
              !MachineCalibrationStore.IsValidFor(stored with { CpuName = "Other CPU" }, hardware, now) &&
              !MachineCalibrationStore.IsValidFor(stored with { LogicalCores = 8 }, hardware, now) &&
              !MachineCalibrationStore.IsValidFor(stored with { MeasuredUtc = now.AddDays(-40) }, hardware, now) &&
              !MachineCalibrationStore.IsValidFor(stored with { MegaTests = 0 }, hardware, now),
            "A stored benchmark is reused only on the same CPU and thread count, within 30 days");
        var calibrationPath = Path.Combine(Path.GetTempPath(), $"g3d-calibration-{Guid.NewGuid():N}.json");
        try
        {
            var first = MachineCalibrationStore.LoadOrMeasureAsync(calibrationPath).GetAwaiter().GetResult();
            var second = MachineCalibrationStore.LoadOrMeasureAsync(calibrationPath).GetAwaiter().GetResult();
            check(first.MicrobenchmarkMegaTests > 1 && second.MicrobenchmarkMegaTests == first.MicrobenchmarkMegaTests,
                "Calibration measures once and reuses the stored result on the next start");
        }
        finally
        {
            File.Delete(calibrationPath);
        }

        var settings = new RenderSettings();
        check(QualityPlanner.Plan(RenderQuality.Draft, Machine(8, 16), 800, 450).Configure(settings).UseRadianceCache &&
              QualityPlanner.Plan(RenderQuality.Standard, Machine(8, 16), 800, 450).Configure(settings).UseRadianceCache &&
              !QualityPlanner.Plan(RenderQuality.High, Machine(8, 16), 800, 450).Configure(settings).UseRadianceCache &&
              !QualityPlanner.Plan(RenderQuality.Final, Machine(8, 16), 800, 450).Configure(settings).UseRadianceCache,
            "The radiance cache is used for drafts and standard renders, never for High or Final");

        var tight = QualityPlanner.Plan(RenderQuality.High, Machine(16, 0.5), 3840, 2160);
        check(tight.EstimatedBytes <= 0.5 * 0.5 * 1073741824 * 1.001 && tight.OutputWidth < 3840 && tight.Notes.Contains(PlanNote.OutputResolutionReduced) &&
              Math.Abs((double)tight.OutputWidth / tight.OutputHeight - 16.0 / 9) < 0.02,
            "A 4K render on 0.5 GB of free memory shrinks to fit a quarter of it and keeps its aspect ratio");
        check(QualityPlanner.Plan(RenderQuality.Standard, Machine(8, 16), 1280, 720, sceneTriangles: 70_000_000).Notes.Contains(PlanNote.SceneNearBudget),
            "A scene that nearly fills the budget raises a warning");
    }

    private static void Photometry(Action<bool, string> check)
    {
        // Isotropic 100 cd source: flux is 4π · 100 lm exactly.
        const string isotropic = """
            IESNA:LM-63-2002
            [TEST] isotropic
            TILT=NONE
            1 -1 1 5 1 1 2 0 0 0
            1 1 100
            0 45 90 135 180
            0
            100 100 100 100 100
            """;
        var profile = IesProfile.Parse(isotropic);
        check(Math.Abs(profile.LuminousFluxLumens - 4 * MathF.PI * 100) < 4 * MathF.PI * 100 * 0.005f && profile.Title == "isotropic",
            "IES integration of an isotropic 100 cd source gives 4π·100 lumens");
        check(Math.Abs(profile.IntensityTowards(new Vector3(0.3f, -0.2f, -0.9f)) - 100) < 1e-3f, "Isotropic profile returns 100 cd in any direction");

        // Exercise binary-search boundaries, exact knots and interpolation.
        // A 180-degree horizontal table declares bilateral symmetry.
        const string asymmetric = """
            IESNA:LM-63-2002
            [TEST] asymmetric
            TILT=NONE
            1 -1 1 3 3 1 2 0 0 0
            1 1 100
            0 90 180
            0 90 180
            10 20 30
            40 50 60
            70 80 90
            """;
        var measured = IesProfile.Parse(asymmetric);
        check(Math.Abs(measured.Candela(90, 45) - 35) < 1e-4f &&
              Math.Abs(measured.Candela(45, 90) - 45) < 1e-4f &&
              Math.Abs(measured.Candela(90, 90) - 50) < 1e-4f &&
              Math.Abs(measured.Candela(90, 180) - 80) < 1e-4f &&
              Math.Abs(measured.Candela(90, 270) - 50) < 1e-4f &&
              Math.Abs(measured.Candela(-20, 0) - 10) < 1e-4f &&
              Math.Abs(measured.Candela(200, 0) - 30) < 1e-4f,
            "IES angle lookup preserves exact knots, interpolation, symmetry and endpoint clamping");

        // Cosine downlight: I = 200·cos(θ) cd for θ in 0..90°; flux = π·200 lm.
        var rows = string.Join(" ", Enumerable.Range(0, 19).Select(i => (200 * MathF.Cos(i * 5 * MathF.PI / 180)).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)));
        var angles = string.Join(" ", Enumerable.Range(0, 19).Select(i => i * 5));
        var downlight = IesProfile.Parse($"IESNA:LM-63-2002\nTILT=NONE\n1 -1 1 19 1 1 2 0 0 0\n1 1 100\n{angles}\n0\n{rows}\n");
        check(Math.Abs(downlight.LuminousFluxLumens - MathF.PI * 200) < MathF.PI * 200 * 0.01f, "IES cosine downlight integrates to π·200 lumens");
        check(downlight.IntensityTowards(-Vector3.UnitZ) > 199.9f && downlight.IntensityTowards(Vector3.UnitZ) < 1e-3f &&
              Math.Abs(downlight.IntensityTowards(new Vector3(MathF.Sin(MathF.PI / 3), 0, -MathF.Cos(MathF.PI / 3))) - 100) < 1.5f,
            "IES downlight points down, is dark above, and gives 100 cd at 60°");

        var bad = 0;
        foreach (var text in new[] { "", "TILT=NONE\n1 -1 1 2 1 2 2 0 0 0\n1 1 1\n0 90\n0\n1 1", "TILT=NONE\n1 -1 1 3 1 1 2 0 0 0\n1 1 1\n0 90 45\n0\n1 1 1", "TILT=NONE\n1 -1 x" })
        {
            try { IesProfile.Parse(text); }
            catch (InvalidDataException) { bad++; }
        }
        check(bad == 4, "Malformed, unsupported-type and unordered IES files are rejected");

        // Untrusted photometry must not turn finite source values into infinite
        // candela or luminous flux, or exhaust memory while parsing a huge file.
        static bool RejectsIes(string source)
        {
            try { _ = IesProfile.Parse(source); return false; }
            catch (InvalidDataException) { return true; }
        }
        var overflowingIntensity = isotropic.Replace("1 -1 1 5 1 1 2 0 0 0",
            "1 -1 1e38 5 1 1 2 0 0 0", StringComparison.Ordinal);
        var overflowingFlux = isotropic.Replace("100 100 100 100 100",
            "1e38 1e38 1e38 1e38 1e38", StringComparison.Ordinal);
        check(RejectsIes(overflowingIntensity) && RejectsIes(overflowingFlux) &&
              RejectsIes(new string(' ', IesProfile.MaxIesCharacters + 1)),
            "IES rejects overflowed candela, overflowed integrated flux and oversized input");
        check(profile.IntensityTowards(new Vector3(float.NaN, 0, -1)) == 0 &&
              profile.IntensityTowards(new Vector3(float.MaxValue, 0, -1)) == 0 &&
              profile.Candela(float.PositiveInfinity, 0) == 0 &&
              profile.Candela(0, float.NaN) == 0,
            "Invalid IES lookup directions do not introduce light into the renderer");
    }

    private static void Portals(Action<bool, string> check)
    {
        var room = BenchmarkScenes.DaylitRoom();
        var scene = new RenderSceneBuilder().AddBuilding(room.Mesh).Build();
        var glass = Enumerable.Range(0, scene.TriangleCount).Where(t => scene.Materials[scene.TriangleMaterial[t]].Transmission > 0).ToArray();
        var opaque = new Bvh(scene.Positions, scene.Indices,
            Enumerable.Range(0, scene.TriangleCount).Except(glass).ToArray());
        var portals = SkyPortals.TryCreate(scene, glass, opaque, 1e-3f);
        check(portals is { Count: 1 }, "The two triangles of the room window merge into one sky portal");

        var point = new Vector3(2.5f, 2.0f, 0.01f);
        var up = Vector3.UnitZ;
        var random = new Random(21);
        const int samples = 400_000;
        // Reference: cosine-weighted hemisphere sampling. With constant sky radiance 1, E/π is the visible fraction.
        double visible = 0;
        for (var i = 0; i < samples; i++)
        {
            var r1 = random.NextSingle();
            var phi = 2 * MathF.PI * random.NextSingle();
            var radius = MathF.Sqrt(r1);
            var direction = new Vector3(radius * MathF.Cos(phi), radius * MathF.Sin(phi), MathF.Sqrt(1 - r1));
            var hit = new RayHit { Distance = float.MaxValue };
            if (!opaque.Intersect(point, direction, 1e-4f, ref hit)) visible++;
        }
        var reference = visible / samples; // = E / π
        double portalSum = 0, portalSquares = 0, referenceVariance = reference * (1 - reference);
        for (var i = 0; i < samples; i++)
        {
            var contribution = 0.0;
            if (portals!.TrySample(point, up, random.NextSingle(), random.NextSingle(), random.NextSingle(), out var direction, out var pdf))
            {
                var hit = new RayHit { Distance = float.MaxValue };
                if (!opaque.Intersect(point, direction, 1e-4f, ref hit))
                    contribution = Vector3.Dot(up, direction) / pdf / Math.PI; // E/π estimator
            }
            portalSum += contribution;
            portalSquares += contribution * contribution;
        }
        var estimate = portalSum / samples;
        var portalVariance = portalSquares / samples - estimate * estimate;
        Console.WriteLine($"INFO: window sky irradiance E/π — cosine sampling {reference:0.0000} (var {referenceVariance:0.0000}), portal sampling {estimate:0.0000} (var {portalVariance:0.00000})");
        check(Math.Abs(estimate - reference) < 0.03 * reference + 3e-4, "Portal sampling and cosine sampling agree on the sky light reaching the floor");
        check(portalVariance < referenceVariance / 8, "Portal sampling has at least 8x lower variance for the same number of rays");

        // The density of all directions must integrate to one over the sphere of directions that reach a portal.
        double integral = 0;
        const int uniform = 600_000;
        for (var i = 0; i < uniform; i++)
        {
            var z = 1 - 2 * random.NextSingle();
            var phi = 2 * MathF.PI * random.NextSingle();
            var s = MathF.Sqrt(1 - z * z);
            integral += portals!.Pdf(point, up, new Vector3(s * MathF.Cos(phi), s * MathF.Sin(phi), z)) * 4 * Math.PI;
        }
        check(Math.Abs(integral / uniform - 1) < 0.03, "Portal density integrates to one over the sphere");
        check(portals!.TrySample(new Vector3(2.5f, -6, 1), up, 0.3f, 0.3f, 0.3f, out _, out _) is false,
            "A point outside the room, on the closed side, gets no portal sample");

        var closed = new SceneMesh();
        BenchmarkScenes.AddBox(closed, new(-1, -1, -1), new(5, 5, 4), SurfaceKind.Wall);
        BenchmarkScenes.AddBox(closed, new(-0.9f, -0.9f, -0.9f), new(4.9f, 4.9f, 3.9f), SurfaceKind.Wall);
        closed.AddQuad(new(2, 0, 0), new(2, 3, 0), new(2, 3, 2), new(2, 0, 2), SurfaceKind.Glass);
        var closedScene = new RenderSceneBuilder().AddBuilding(closed).Build();
        var closedGlass = Enumerable.Range(0, closedScene.TriangleCount).Where(t => closedScene.Materials[closedScene.TriangleMaterial[t]].Transmission > 0).ToArray();
        var closedOpaque = new Bvh(closedScene.Positions, closedScene.Indices,
            Enumerable.Range(0, closedScene.TriangleCount).Except(closedGlass).ToArray());
        check(SkyPortals.TryCreate(closedScene, closedGlass, closedOpaque, 1e-3f) is null,
            "Glass inside a fully enclosed space is not a sky portal");
    }

    /// <summary>The same room rendered with and without portal sampling must converge to the same picture.</summary>
    private static void PortalsInTracer(Action<bool, string> check)
    {
        var room = BenchmarkScenes.DaylitRoom();
        var scene = new RenderSceneBuilder().AddBuilding(room.Mesh).Build();
        const int width = 96, height = 54, passes = 400;
        PathTracer Render(bool portals, uint seed)
        {
            var tracer = new PathTracer(scene, width, height, room.Camera, room.Settings with { SkyPortals = portals, Seed = seed, FireflyClamp = 0, MaxBounces = 16 });
            for (var i = 0; i < passes; i++) tracer.RenderPass();
            return tracer;
        }
        var off = Render(false, 1);
        var on = Render(true, 2);
        check(on.SkyPortalCount == 1 && off.SkyPortalCount == 0, "The tracer finds the room window as a sky portal and honors the setting");
        var a = off.LinearImage(denoise: false);
        var b = on.LinearImage(denoise: false);
        // Compare in blocks, because single pixels are still noisy after a few hundred samples.
        double relative = 0, meanA = 0, meanB = 0;
        var blocks = 0;
        for (var by = 0; by + 6 <= height; by += 6)
            for (var bx = 0; bx + 6 <= width; bx += 6)
            {
                var sumA = 0.0;
                var sumB = 0.0;
                for (var y = 0; y < 6; y++)
                    for (var x = 0; x < 6; x++)
                    {
                        sumA += a[(by + y) * width + bx + x].Y;
                        sumB += b[(by + y) * width + bx + x].Y;
                    }
                meanA += sumA; meanB += sumB;
                relative += Math.Abs(sumA - sumB) / Math.Max(sumA + sumB, 1e-6);
                blocks++;
            }
        Console.WriteLine($"INFO: portals off/on mean luminance {meanA / blocks / 36:0.0000} / {meanB / blocks / 36:0.0000}, mean block difference {relative / blocks:P1}");
        check(Math.Abs(meanA - meanB) / meanA < 0.015, "Portal sampling does not change the average brightness of the room (within 1.5 %, deep paths)");
        check(relative / blocks < 0.12, "Portal and plain sampling agree block by block");
    }

    /// <summary>
    /// A curtain-wall model has hundreds of thousands of glass triangles; finding portals must not cost seconds.
    /// (A real 3.6-million-triangle Revit export once spent 46 s here.)
    /// </summary>
    private static void PortalsOnHugeGlassModels(Action<bool, string> check)
    {
        var mesh = new SceneMesh();
        BenchmarkScenes.AddBox(mesh, new(-60, -60, -1), new(60, 60, 0), SurfaceKind.Floor);
        var random = new Random(8);
        for (var i = 0; i < 250_000; i++)
        {
            var o = new Vector3(random.NextSingle() * 100 - 50, random.NextSingle() * 100 - 50, random.NextSingle() * 20);
            var u = new Vector3(0.3f + random.NextSingle(), 0, 0);
            var v = new Vector3(0, 0, 0.3f + random.NextSingle());
            mesh.AddQuad(o, o + u, o + u + v, o + v, SurfaceKind.Glass);
        }
        var scene = new RenderSceneBuilder().AddBuilding(mesh).Build();
        var glass = Enumerable.Range(0, scene.TriangleCount).Where(t => scene.Materials[scene.TriangleMaterial[t]].Transmission > 0).ToArray();
        var opaque = new Bvh(scene.Positions, scene.Indices, Enumerable.Range(0, scene.TriangleCount).Except(glass).ToArray());
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var portals = SkyPortals.TryCreate(scene, glass, opaque, 1e-3f);
        timer.Stop();
        Console.WriteLine($"INFO: portal search over {glass.Length} glass triangles took {timer.ElapsedMilliseconds} ms ({portals?.Count ?? 0} portals)");
        check(timer.ElapsedMilliseconds < 3000, "Portal search over 500 000 glass triangles takes under 3 seconds");
        check(portals is null || portals.Count <= SkyPortals.MaximumPortals, "The number of portals is bounded");
    }

    /// <summary>
    /// Resampled importance sampling must give the same average as summing every light, and culling by range may only
    /// drop what is below the threshold. Visibility is ignored here so the sum has an exact answer.
    /// </summary>
    private static void ManyLights(Action<bool, string> check)
    {
        var random = new Random(4);
        var lights = Enumerable.Range(0, 300).Select(_ => new RenderLight(
            new Vector3(random.NextSingle() * 24, random.NextSingle() * 20, 2.7f),
            new Vector3(0.3f + random.NextSingle(), 0.3f + random.NextSingle(), 0.3f + random.NextSingle()))).ToArray();
        var origin = new Vector3(12, 10, 0.01f);
        var normal = Vector3.UnitZ;

        double Exhaustive()
        {
            double sum = 0;
            foreach (var light in lights)
            {
                var toLight = light.Position - origin;
                var distanceSquared = toLight.LengthSquared();
                var cosine = Vector3.Dot(normal, Vector3.Normalize(toLight));
                if (cosine > 0) sum += (0.2126 * light.Intensity.X + 0.7152 * light.Intensity.Y + 0.0722 * light.Intensity.Z) * cosine / distanceSquared;
            }
            return sum;
        }

        double Resampled(int candidates, float threshold, int draws)
        {
            var sampler = new LightSampler(lights, new Vector3(0, 0, 0), new Vector3(24, 20, 3), candidates, threshold);
            double sum = 0;
            for (var i = 0; i < draws; i++)
            {
                var state = new Sampler((uint)(i % 977), (uint)(i / 977), 5);
                if (!sampler.Resample(origin, normal, normal, 1f, ref state, out var chosen, out var weight)) continue;
                var toLight = lights[chosen].Position - origin;
                var cosine = Vector3.Dot(normal, Vector3.Normalize(toLight));
                var luminance = 0.2126 * lights[chosen].Intensity.X + 0.7152 * lights[chosen].Intensity.Y + 0.0722 * lights[chosen].Intensity.Z;
                sum += luminance * cosine / toLight.LengthSquared() * weight;
            }
            return sum / draws;
        }

        var truth = Exhaustive();
        var exact = Resampled(16, 0, 400_000);
        var culled = Resampled(16, 0.0005f, 400_000);
        Console.WriteLine($"INFO: 300 lights, irradiance at one point — all lights {truth:0.00000}, RIS(16) {exact:0.00000}, RIS(16)+cull {culled:0.00000}");
        check(Math.Abs(exact - truth) / truth < 0.01, "Resampled light selection matches summing all 300 lights (within 1 %)");
        check(Math.Abs(culled - truth) / truth < 0.02, "Culling lights below the threshold keeps the result within 2 %");

        var cell = new LightSampler(lights.Take(4).ToArray(), Vector3.Zero, new Vector3(24, 20, 3), 16, 0);
        var all = cell.All(origin, out var exhaustive);
        check(exhaustive && all.Length == 4 && !new LightSampler(lights, Vector3.Zero, new Vector3(24, 20, 3), 16, 0).All(origin, out var many).Length.Equals(-1) && !many,
            "Four lights are evaluated exactly, hundreds are resampled");

        // IES: a 200 cd cosine downlight hung facing down.
        var angles = string.Join(" ", Enumerable.Range(0, 19).Select(i => i * 5));
        var rows = string.Join(" ", Enumerable.Range(0, 19).Select(i => (200 * MathF.Cos(i * 5 * MathF.PI / 180)).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)));
        var profile = IesProfile.Parse($"IESNA:LM-63-2002\nTILT=NONE\n1 -1 1 19 1 1 2 0 0 0\n1 1 100\n{angles}\n0\n{rows}\n");
        var downlight = new RenderLight(new Vector3(5, 5, 2.7f), Vector3.One) { Ies = profile };
        var photometric = new LightSampler([downlight], Vector3.Zero, new Vector3(10, 10, 3), 8, 0);
        var straightDown = photometric.Intensity(0, -Vector3.UnitZ).Y;
        var sixty = photometric.Intensity(0, new Vector3(MathF.Sin(MathF.PI / 3), 0, -MathF.Cos(MathF.PI / 3))).Y;
        check(Math.Abs(straightDown - RenderUnits.FromCandela(200)) < 1e-6f && Math.Abs(sixty - RenderUnits.FromCandela(100)) < RenderUnits.FromCandela(2) &&
              photometric.Intensity(0, Vector3.UnitZ) == Vector3.Zero,
            "An IES downlight is 200 cd straight down, 100 cd at 60 degrees and dark upward, in renderer units");
        var tilted = new RenderLight(new Vector3(5, 5, 2.7f), Vector3.One, SpotDirection: Vector3.UnitX) { Ies = profile };
        var sideways = new LightSampler([tilted], Vector3.Zero, new Vector3(10, 10, 3), 8, 0);
        check(Math.Abs(sideways.Intensity(0, Vector3.UnitX).Y - RenderUnits.FromCandela(200)) < 1e-6f && sideways.Intensity(0, -Vector3.UnitZ).Y < RenderUnits.FromCandela(1),
            "Turning the luminaire points its IES distribution along the new axis");
    }

    /// <summary>The variance-guided filter on synthetic images with known noise: it must remove noise, keep edges, and leave clean data alone.</summary>
    private static void GuidedDenoiser(Action<bool, string> check)
    {
        const int width = 64, height = 64;
        var features = new FeatureBuffer(width, height);
        for (var i = 0; i < width * height; i++)
        {
            features.Normal[i] = Vector3.UnitZ;
            features.Albedo[i] = Vector3.One;
            features.Depth[i] = 5;
        }
        var random = new Random(3);
        double Rmse(Vector3[] image, Func<int, float> truth)
        {
            double sum = 0;
            for (var i = 0; i < image.Length; i++) { var d = image[i].Y - truth(i); sum += d * d; }
            return Math.Sqrt(sum / image.Length);
        }
        float Edge(int index) => index % width < width / 2 ? 1f : 3f;
        Vector3[] Noisy(float sigma)
        {
            var image = new Vector3[width * height];
            for (var i = 0; i < image.Length; i++)
            {
                var gaussian = (float)(Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble()));
                image[i] = new Vector3(Edge(i) + sigma * gaussian);
            }
            return image;
        }
        float[] Variance(float sigma) => Enumerable.Repeat(sigma * sigma, width * height).ToArray();

        var noisy = Noisy(0.4f);
        var filtered = AtrousDenoiser.ApplyGuided(noisy, Variance(0.4f), features);
        var before = Rmse(noisy, Edge);
        var after = Rmse(filtered, Edge);
        check(after < before * 0.5, $"The guided filter at least halves the error of a noisy image ({before:0.000} → {after:0.000})");
        // The step from 1 to 3 is 5 sigma wide here, so the pixels either side of it must not be smeared together.
        var near = Enumerable.Range(0, height).Select(y => (Left: filtered[y * width + width / 2 - 2].Y, Right: filtered[y * width + width / 2 + 1].Y)).ToArray();
        check(near.Average(p => p.Left) < 1.25 && near.Average(p => p.Right) > 2.75, "The guided filter keeps a strong edge sharp");
        var clean = Noisy(0.002f);
        var untouched = AtrousDenoiser.ApplyGuided(clean, Variance(0.002f), features);
        check(Rmse(untouched, Edge) < Rmse(clean, Edge) * 1.1 + 1e-4, "A converged image passes through the guided filter essentially unchanged");

        var room = BenchmarkScenes.DaylitRoom();
        var tracer = new PathTracer(room.Mesh, 48, 27, room.Camera, room.Settings);
        for (var i = 0; i < 12; i++) tracer.RenderPass();
        var variance = tracer.IndirectVarianceOfMean();
        check(variance.All(v => float.IsFinite(v) && v >= 0) && tracer.MeanRelativeNoise() is > 0 and < 2,
            "The tracer reports finite, non-negative variance and a relative noise level");
        var once = new PathTracer(room.Mesh, 16, 9, room.Camera, room.Settings);
        once.RenderPass();
        check(once.IndirectVarianceOfMean().All(v => v >= 1e3f), "With fewer than three samples a pixel reports its variance as unreliable");
    }

    /// <summary>A live preview changes light, size and camera without rebuilding geometry, and must match a fresh render exactly.</summary>
    private static void LiveUpdates(Action<bool, string> check)
    {
        var house = BenchmarkScenes.HouseExterior();
        var scene = new RenderSceneBuilder().AddBuilding(house.Mesh).Build();
        var start = house.Settings with { Seed = 3 };
        var evening = start with { SunDirection = new Vector3(-0.8f, 0.2f, 0.25f), SunIntensity = 1.2f, Sky = SkyKind.Overcast };

        var live = new PathTracer(scene, 64, 36, house.Camera, start);
        for (var i = 0; i < 3; i++) live.RenderPass();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        live.UpdateSettings(evening);
        var elapsed = timer.Elapsed.TotalMilliseconds;
        check(live.SampleCount == 0 && live.AverageSamplesPerPixel == 0, "Changing the settings restarts accumulation");
        for (var i = 0; i < 4; i++) live.RenderPass();
        var fresh = new PathTracer(scene, 64, 36, house.Camera, evening);
        for (var i = 0; i < 4; i++) fresh.RenderPass();
        check(live.LinearImage(false).SequenceEqual(fresh.LinearImage(false)),
            "A tracer updated to new settings renders exactly what a freshly built tracer renders");
        Console.WriteLine($"INFO: UpdateSettings on {scene.TriangleCount} triangles took {elapsed:0.0} ms");

        live.Resize(48, 27);
        live.RenderPass();
        var small = new PathTracer(scene, 48, 27, house.Camera, evening);
        small.RenderPass();
        check(live.Width == 48 && live.Height == 27 && live.LinearImage(false).SequenceEqual(small.LinearImage(false)),
            "Resizing keeps the geometry and renders like a fresh tracer of that size");
        var rejected = false;
        try { live.Resize(0, 10); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        check(rejected, "An invalid size is rejected");

        var darker = new PathTracer(scene, 48, 27, house.Camera, start);
        darker.RenderPass();
        darker.SetCamera(house.Camera with { VerticalFieldOfViewDegrees = 30 });
        check(darker.SampleCount == 0, "Moving the camera restarts accumulation");
    }

    /// <summary>The built-in upscaler returns the input's light when scale is one and never creates light where there is none.</summary>
    private static void UpsamplerSeam(Action<bool, string> check)
    {
        var house = BenchmarkScenes.HouseExterior();
        var scene = new RenderSceneBuilder().AddBuilding(house.Mesh).Build();
        var full = new PathTracer(scene, 64, 36, house.Camera, house.Settings with { MaxBounces = 0 });
        var half = new PathTracer(scene, 32, 18, house.Camera, house.Settings with { MaxBounces = 0 });
        var constant = Enumerable.Repeat(new Vector3(0.5f), 32 * 18).ToArray();
        IUpscaler upscaler = new GuidedUpsampler();
        var up = upscaler.Upscale(constant, half.CaptureFeatures(), full.CaptureFeatures());
        var features = full.CaptureFeatures();
        var lit = Enumerable.Range(0, up.Length).Where(i => !float.IsInfinity(features.Depth[i])).ToArray();
        var sky = Enumerable.Range(0, up.Length).Where(i => float.IsInfinity(features.Depth[i])).ToArray();
        // Constant irradiance 0.5 divided by albedo and multiplied back by the output pixel's albedo gives 0.5 · albedo / albedo.
        check(lit.Length > 100 && sky.All(i => up[i] == Vector3.Zero), "The upsampler leaves sky pixels without indirect light");
        check(lit.Count(i => up[i].X > 0) > lit.Length * 0.97, "The upsampler gives nearly every visible pixel some light");
        var identity = upscaler.Upscale(Enumerable.Repeat(new Vector3(0.3f), 64 * 36).ToArray(), features, features);
        check(lit.All(i => MathF.Abs(identity[i].Y - 0.3f) < 0.02f), "Upsampling at scale one reproduces the input");
    }

    /// <summary>The cache's bookkeeping, then its effect on a picture: close to the deep reference, repeatable, invalidated by region.</summary>
    private static void RadianceCaching(Action<bool, string> check)
    {
        var cache = new RadianceCache(0.5f, 12) { MinimumSamples = 3, WindowSamples = 64 };
        var spot = new Vector3(2.2f, 1.1f, 0.0f);
        cache.Add(spot, Vector3.UnitZ, new Vector3(1));
        cache.Add(spot, Vector3.UnitZ, new Vector3(1));
        check(!cache.TryGet(spot, Vector3.UnitZ, out _), "A cache entry is not used before it has the minimum number of samples");
        cache.Add(spot, Vector3.UnitZ, new Vector3(4));
        check(cache.TryGet(spot, Vector3.UnitZ, out var average) && Math.Abs(average.X - 2) < 1e-4f,
            "A cache entry holds the running mean of its samples");
        check(!cache.TryGet(spot, -Vector3.UnitZ, out _) && !cache.TryGet(spot + new Vector3(3, 0, 0), Vector3.UnitZ, out _),
            "A cache entry is specific to its cell and to the side the surface faces");
        check(cache.TryGet(spot + new Vector3(0.1f, 0.1f, 0.05f), Vector3.UnitZ, out _), "Points in the same cell share an entry");

        for (var i = 0; i < 3; i++)
        {
            cache.Add(new Vector3(10, 10, 0), Vector3.UnitZ, new Vector3(7));
            cache.Add(new Vector3(-10, 4, 1), Vector3.UnitX, new Vector3(9));
        }
        check(cache.OccupiedEntries() == 3, "Three distinct surface cells hold three entries");
        var removed = cache.InvalidateBox(new Vector3(1, 0, -1), new Vector3(3, 2, 1));
        check(removed == 1 && !cache.TryGet(spot, Vector3.UnitZ, out _) && cache.TryGet(new Vector3(10, 10, 0), Vector3.UnitZ, out _) &&
              cache.TryGet(new Vector3(-10, 4, 1), Vector3.UnitX, out _),
            "Invalidating a box forgets only the entries inside it");
        cache.Clear();
        check(cache.OccupiedEntries() == 0, "Clearing empties the cache");

        // Picture-level checks on the closed room, where light bounces many times.
        var room = BenchmarkScenes.DaylitRoom();
        var scene = new RenderSceneBuilder().AddBuilding(room.Mesh).Build();
        const int width = 96, height = 54;
        double MeanLuminance(RenderSettings settings, int passes)
        {
            var tracer = new PathTracer(scene, width, height, room.Camera, settings);
            for (var i = 0; i < passes; i++) tracer.RenderPass();
            return tracer.LinearImage(false).Average(p => p.Y);
        }
        var deep = MeanLuminance(room.Settings with { MaxBounces = 16, FireflyClamp = 0, Seed = 4 }, 500);
        var shallow = MeanLuminance(room.Settings with { MaxBounces = 3, FireflyClamp = 0, Seed = 5 }, 500);
        var cached = MeanLuminance(room.Settings with { MaxBounces = 3, FireflyClamp = 0, Seed = 6, UseRadianceCache = true }, 500);
        Console.WriteLine($"INFO: room mean luminance — deep reference {deep:0.0000}, 3 bounces {shallow:0.0000}, 3 bounces + cache {cached:0.0000}");
        check(Math.Abs(cached - deep) < Math.Abs(shallow - deep) * 0.5 && Math.Abs(cached / deep - 1) < 0.06,
            "The cache brings a 3-bounce render close to the 16-bounce brightness (within 6 %)");

        var warm = new PathTracer(scene, width, height, room.Camera, room.Settings with { UseRadianceCache = true });
        for (var i = 0; i < 20; i++) warm.RenderPass();
        check(warm.RadianceCache is { } filled && filled.OccupiedEntries() > 100 && warm.CacheHitRate > 0.2,
            "A rendering tracer fills its cache and reports a hit rate");
        var filledEntries = warm.RadianceCache!.OccupiedEntries();
        warm.SetCamera(room.Camera with { VerticalFieldOfViewDegrees = 60 });
        check(warm.RadianceCache!.OccupiedEntries() == filledEntries, "Moving the camera keeps the cache");
        check(warm.InvalidateRegion(new Vector3(-1, -1, -1), new Vector3(6, 5, 4)) > 0 && warm.RadianceCache!.OccupiedEntries() == 0,
            "Invalidating the whole room empties the cache");
        warm.RenderPass();
        warm.UpdateSettings(room.Settings with { UseRadianceCache = true, SunIntensity = 0 });
        check(warm.RadianceCache!.OccupiedEntries() == 0, "A lighting change clears the cache");
        warm.UpdateSettings(room.Settings with { UseRadianceCache = false });
        check(warm.RadianceCache is null, "Turning the cache off releases it");
    }

    /// <summary>Exposure, field of view and depth of field follow photographic formulas, and Sunny 16 really exposes a sunlit grey card as grey.</summary>
    private static void PhysicalCamera(Action<bool, string> check)
    {
        float Exposure(double n, double t, double iso = 100, double comp = 0) => PhotographicCamera.Exposure(n, t, iso, comp);
        check(Math.Abs(Exposure(16, 0.01) - 0.4f) < 1e-5f, "Sunny 16 (f/16, 1/100 s, ISO 100) gives the reference exposure");
        check(Math.Abs(Exposure(8, 1 / 400.0) - 0.4f) < 1e-4f, "Equivalent exposures (f/8 at 1/400 s) give the same multiplier");
        check(Math.Abs(Exposure(16, 0.01, 200) - 0.8f) < 1e-5f && Math.Abs(Exposure(16, 0.02) - 0.8f) < 1e-5f && Math.Abs(Exposure(11.3137, 0.01) - 0.8f) < 1e-3f,
            "Doubling ISO, shutter time, or opening the aperture one stop doubles the multiplier");
        check(Math.Abs(Exposure(16, 0.01, 100, 1) - 0.8f) < 1e-5f, "One stop of compensation doubles the multiplier");
        check(Math.Abs(PhotographicCamera.VerticalFieldOfView(24) - 53.13f) < 0.02f && Math.Abs(PhotographicCamera.FocalLength(PhotographicCamera.VerticalFieldOfView(35)) - 35) < 1e-3,
            "A 24 mm lens on a 24 mm-high sensor sees 53.1° vertically, and focal length and field of view round-trip");
        check(Math.Abs(PhotographicCamera.ApertureRadius(50, 2) - 0.0125f) < 1e-6f, "A 50 mm f/2 lens has a 12.5 mm radius opening");
        check(PhotographicCamera.BlurCircleDiameter(50, 2, 3, 3) == 0 && PhotographicCamera.BlurCircleDiameter(50, 2, 3, 12) > PhotographicCamera.BlurCircleDiameter(50, 8, 3, 12) * 3.9,
            "The thin-lens blur is zero at the focus distance and shrinks four times for two stops of aperture");

        // Invalid physical settings must never inject NaN or infinity into the path tracer.
        // Log-space exposure math also handles extreme but finite camera parameters.
        check(double.IsFinite(PhotographicCamera.ExposureValue(1e200, 1e200, 1e200)) &&
              float.IsFinite(Exposure(1e200, 1e200, 1e200)),
            "Extreme finite camera settings keep exposure calculations finite");
        bool Rejects(Action action)
        {
            try { action(); return false; }
            catch (ArgumentException) { return true; }
        }
        check(Rejects(() => Exposure(16, 0.01, 100, double.NaN)) &&
              Rejects(() => Exposure(16, 0.01, 100, double.PositiveInfinity)) &&
              Rejects(() => Exposure(16, 0.01, double.NaN)) &&
              Rejects(() => PhotographicCamera.ExposureValue(double.PositiveInfinity, 0.01)),
            "Nonfinite exposure inputs are rejected before rendering");
        check(Rejects(() => PhotographicCamera.VerticalFieldOfView(double.NaN)) &&
              Rejects(() => PhotographicCamera.VerticalFieldOfView(35, double.PositiveInfinity)) &&
              Rejects(() => PhotographicCamera.FocalLength(0)) &&
              Rejects(() => PhotographicCamera.FocalLength(180)) &&
              Rejects(() => PhotographicCamera.FocalLength(double.NaN)) &&
              Rejects(() => PhotographicCamera.ApertureRadius(50, double.PositiveInfinity)),
            "Invalid lens and field-of-view values cannot create an invalid render camera");
        check(Rejects(() => PhotographicCamera.Create(Vector3.Zero, Vector3.UnitY, 35, 2, double.NaN)) &&
              Rejects(() => PhotographicCamera.Create(Vector3.Zero, Vector3.Zero, 35, 2)) &&
              Rejects(() => PhotographicCamera.Create(new Vector3(float.NaN, 0, 0), Vector3.UnitY, 35, 2)) &&
              Rejects(() => PhotographicCamera.BlurCircleDiameter(50, 2, 0.05, 2)) &&
              Rejects(() => PhotographicCamera.BlurCircleDiameter(50, 2, 3, 0)),
            "Focus distances, camera vectors and thin-lens blur calculations reject invalid geometry");

        // A sunlit 18 % grey card at noon, photographed with Sunny 16, must land at scene-referred mid-grey.
        var mesh = new SceneMesh();
        BenchmarkScenes.AddBox(mesh, new(-50, -50, -1), new(50, 50, 0), SurfaceKind.Floor);
        var scene = new RenderSceneBuilder().AddBuilding(mesh,
            _ => new RenderMaterial { BaseColor = new Vector3(0.18f), Roughness = 1, Pattern = SurfacePattern.None }).Build();
        var settings = new RenderSettings
        {
            SunDirection = Vector3.UnitZ, Exposure = PhotographicCamera.Exposure(16, 0.01, 100), FireflyClamp = 0
        };
        var tracer = new PathTracer(scene, 16, 16, new RenderCamera(new(0, 0, 10), new(0, 0, 0), 20), settings);
        for (var i = 0; i < 64; i++) tracer.RenderPass();
        var linear = tracer.LinearImage(false).Average(p => p.Y) * settings.Exposure;
        Console.WriteLine($"INFO: Sunny 16 on an 18 % grey card at noon reads {linear:0.000} (mid-grey is 0.18)");
        check(linear is > 0.13f and < 0.26f, "Sunny 16 renders a sunlit 18 % grey card within half a stop of mid-grey");

        // Depth of field: a wide aperture softens a distant edge, a pinhole keeps it crisp.
        var edge = new SceneMesh();
        BenchmarkScenes.AddBox(edge, new(-40, 12, -5), new(0, 12.5f, 6), SurfaceKind.Wall);       // bright slab far away, edge at x = 0
        BenchmarkScenes.AddBox(edge, new(-40, 13, -5), new(40, 14, 6), SurfaceKind.Structure);   // dark backdrop behind it
        var edgeScene = new RenderSceneBuilder().AddBuilding(edge,
            kind => new RenderMaterial { BaseColor = kind == SurfaceKind.Wall ? new Vector3(0.9f) : new Vector3(0.02f), Roughness = 1, Pattern = SurfacePattern.None }).Build();
        double Sharpness(RenderCamera camera)
        {
            var t = new PathTracer(edgeScene, 96, 24, camera, new RenderSettings { SunIntensity = 0, SkyIntensity = 1, FireflyClamp = 0, Seed = 2 });
            for (var i = 0; i < 160; i++) t.RenderPass();
            var image = t.LinearImage(false);
            double best = 0;
            for (var y = 0; y < 24; y++)
                for (var x = 1; x < 96; x++) best = Math.Max(best, Math.Abs(image[y * 96 + x].Y - image[y * 96 + x - 1].Y));
            return best;
        }
        var pinhole = new RenderCamera(new(0, 0, 1.5f), new(0, 12, 1.5f), 30);
        var wide = PhotographicCamera.Create(new(0, 0, 1.5f), new(0, 12, 1.5f), 135, 1.0, focusDistanceMeters: 2, correctVerticals: false) with { VerticalFieldOfViewDegrees = 30 };
        var crisp = Sharpness(pinhole);
        var soft = Sharpness(wide);
        Console.WriteLine($"INFO: edge sharpness pinhole {crisp:0.0000}, wide aperture focused near {soft:0.0000}");
        // 135 mm at f/1 focused at 2 m: a 0.68 m blur circle at 12 m, about 2.5 pixels here, so the steepest step falls to roughly 0.6 of the crisp one.
        check(soft < crisp * 0.7, "A wide aperture focused up close blurs a distant edge that a pinhole camera keeps sharp");
    }

    private sealed class DarkDenoiser : IDenoiser
    {
        public int Calls;
        public int Pixels;
        public string Name => "test double";
        public Vector3[] Denoise(Vector3[] indirect, float[] varianceOfMean, FeatureBuffer features, float relativeNoise)
        {
            Calls++;
            Pixels = features.Width * features.Height;
            return new Vector3[features.Width * features.Height]; // discards all indirect light
        }
    }

    /// <summary>A different denoiser can be plugged in; the renderer adds the sharp direct light back to whatever it returns.</summary>
    private static void DenoiserSeam(Action<bool, string> check)
    {
        var room = BenchmarkScenes.DaylitRoom();
        var tracer = new PathTracer(room.Mesh, 48, 27, room.Camera, room.Settings);
        for (var i = 0; i < 6; i++) tracer.RenderPass();
        var builtIn = new VarianceGuidedDenoiser();
        check(builtIn.Name.Contains("à-trous") && tracer.Denoiser is null, "The built-in denoiser is the default and identifies itself");
        var stub = new DarkDenoiser();
        tracer.Denoiser = stub;
        var image = tracer.LinearImage(denoise: true);
        var direct = tracer.LinearComponents().Direct;
        check(stub.Calls == 1 && stub.Pixels == 48 * 27 && image.SequenceEqual(direct),
            "A plugged-in denoiser is used once per image and the direct light is added back to its result");
    }
}
