using System.Numerics;
using Guanacaste3D.Core.Rendering;

/// <summary>
/// Visual regression tests. Deterministic scenes are rendered and compared with stored reference images using PSNR, SSIM,
/// color difference and the share of pixels outside tolerance, so an unintended change to the look of a render is caught
/// even when no number in the code changed. References live in tests/Guanacaste3D.Checks/golden and are refreshed on
/// purpose with the environment variable G3_UPDATE_GOLDEN=1 after a change that is meant to alter the picture.
/// </summary>
static class RenderRegressionChecks
{
    private sealed record Case(string Name, Func<BenchmarkScene> Scene, Func<RenderSettings, RenderSettings> Configure,
        int Samples, double MinimumPsnr, double MinimumSsim, double MaximumOutside);

    private const int Width = 192, Height = 108;
    private const int Reduction = 4;

    /// <summary>
    /// Compares two images after averaging 4×4 blocks. Independent noise realizations differ pixel by pixel but agree in
    /// each block, whereas a change of lighting, material or geometry moves the block averages.
    /// </summary>
    private static ImageQuality Compare(RasterImage test, RasterImage reference)
    {
        Vector3[] Reduce(RasterImage image)
        {
            var source = image.ToDisplay();
            var reduced = new Vector3[(Width / Reduction) * (Height / Reduction)];
            for (var y = 0; y < Height / Reduction; y++)
                for (var x = 0; x < Width / Reduction; x++)
                {
                    var sum = Vector3.Zero;
                    for (var j = 0; j < Reduction; j++)
                        for (var i = 0; i < Reduction; i++) sum += source[(y * Reduction + j) * Width + x * Reduction + i];
                    reduced[y * (Width / Reduction) + x] = sum / (Reduction * Reduction);
                }
            return reduced;
        }
        return ImageMetrics.Compare(Reduce(test), Reduce(reference), Width / Reduction, Height / Reduction, tolerance: 8f / 255f);
    }

    // Enough samples that a different noise realization stays inside the tolerance, while a real change of light,
    // material or geometry does not (see the controls at the end of Run).
    private static readonly Case[] Cases =
    [
        new("house-exterior", BenchmarkScenes.HouseExterior, s => s, 48, 36, 0.985, 0.03),
        new("daylit-room", BenchmarkScenes.DaylitRoom, s => s, 64, 36, 0.985, 0.03),
        new("daylit-room-lowbounce-cache", BenchmarkScenes.DaylitRoom, s => s with { UseRadianceCache = true }, 64, 32, 0.97, 0.06),
        new("many-lights", BenchmarkScenes.ManyLights, s => s, 96, 34, 0.97, 0.06),
    ];

    public static void Run(Action<bool, string> check)
    {
        var folder = FindGoldenFolder();
        if (folder is null)
        {
            Console.WriteLine("INFO: repository sources not found; render regression checks skipped.");
            return;
        }
        var update = Environment.GetEnvironmentVariable("G3_UPDATE_GOLDEN") == "1";
        foreach (var test in Cases)
        {
            var image = Render(test, null);
            var path = Path.Combine(folder, test.Name + ".png");
            if (update || !File.Exists(path))
            {
                if (!update)
                {
                    check(false, $"Reference image missing: {test.Name}.png (run once with G3_UPDATE_GOLDEN=1 to create it)");
                    continue;
                }
                PngCodec.Save(path, image);
                Console.WriteLine($"INFO: wrote reference {path}");
                continue;
            }
            var golden = PngCodec.Load(path);
            check(golden.Width == Width && golden.Height == Height, $"Reference {test.Name}.png has the expected size");
            var quality = Compare(image, golden);
            Console.WriteLine($"INFO: {test.Name} (4x reduced): PSNR {quality.Psnr:0.0} dB, SSIM {quality.Ssim:0.0000}, dE {quality.MeanDeltaE:0.00}, outside tolerance {quality.OutOfTolerance:P2}");
            check(quality.Psnr >= test.MinimumPsnr && quality.Ssim >= test.MinimumSsim && quality.OutOfTolerance <= test.MaximumOutside,
                $"Render of {test.Name} matches its reference (PSNR ≥ {test.MinimumPsnr} dB, SSIM ≥ {test.MinimumSsim}, ≤ {test.MaximumOutside:P0} pixels out of tolerance)");
        }
        if (update) return;

        // Positive control: the same scene with another noise realization must still match, or the tolerance is too tight.
        var roomReference = Path.Combine(folder, "daylit-room.png");
        if (File.Exists(roomReference))
        {
            var reseeded = RenderWithSeed(Cases[1], 999);
            var same = Compare(reseeded, PngCodec.Load(roomReference));
            Console.WriteLine($"INFO: daylit-room with another seed (4x reduced): PSNR {same.Psnr:0.0} dB, SSIM {same.Ssim:0.0000}, outside {same.OutOfTolerance:P1}");
            check(same.Psnr >= Cases[1].MinimumPsnr && same.Ssim >= Cases[1].MinimumSsim && same.OutOfTolerance <= Cases[1].MaximumOutside,
                "A different noise seed still matches the reference");
        }

        // Negative control: the comparison must notice a real change of lighting, otherwise it protects nothing.
        var roomPath = Path.Combine(folder, "daylit-room.png");
        if (File.Exists(roomPath))
        {
            var room = Cases[1];
            var dimmer = Render(room, s => s with { SunIntensity = s.SunIntensity * 0.5f });
            var differs = Compare(dimmer, PngCodec.Load(roomPath));
            Console.WriteLine($"INFO: half sunlight (4x reduced): PSNR {differs.Psnr:0.0} dB, SSIM {differs.Ssim:0.0000}, outside {differs.OutOfTolerance:P1}");
            check(differs.Ssim < room.MinimumSsim || differs.Psnr < room.MinimumPsnr || differs.OutOfTolerance > room.MaximumOutside,
                "The regression comparison detects half the sunlight in the room");
            var noPortals = Render(room, s => s with { SkyPortals = false, MaxBounces = 1 });
            var shallow = Compare(noPortals, PngCodec.Load(roomPath));
            Console.WriteLine($"INFO: one bounce, no portals (4x reduced): PSNR {shallow.Psnr:0.0} dB, SSIM {shallow.Ssim:0.0000}");
            check(shallow.Ssim < room.MinimumSsim || shallow.Psnr < room.MinimumPsnr, "The regression comparison detects a change of light transport (one bounce, no portals)");
        }
    }

    private static RasterImage Render(Case test, Func<RenderSettings, RenderSettings>? extra)
    {
        var scene = test.Scene();
        var builder = new RenderSceneBuilder().AddBuilding(scene.Mesh);
        foreach (var light in scene.Lights) builder.AddLight(light);
        var settings = test.Configure(scene.Settings with { Seed = 12345 });
        if (extra is not null) settings = extra(settings);
        var tracer = new PathTracer(builder.Build(), Width, Height, scene.Camera, settings);
        for (var i = 0; i < test.Samples; i++) tracer.RenderPass();
        return RasterImage.FromBgra32(Width, Height, tracer.ToBgra32(denoise: true));
    }

    private static RasterImage RenderWithSeed(Case test, uint seed)
    {
        var scene = test.Scene();
        var builder = new RenderSceneBuilder().AddBuilding(scene.Mesh);
        foreach (var light in scene.Lights) builder.AddLight(light);
        var tracer = new PathTracer(builder.Build(), Width, Height, scene.Camera, test.Configure(scene.Settings with { Seed = seed }));
        for (var i = 0; i < test.Samples; i++) tracer.RenderPass();
        return RasterImage.FromBgra32(Width, Height, tracer.ToBgra32(denoise: true));
    }

    private static string? FindGoldenFolder()
    {
        if (Environment.GetEnvironmentVariable("G3_GOLDEN_DIR") is { Length: > 0 } explicitFolder)
        {
            Directory.CreateDirectory(explicitFolder);
            return explicitFolder;
        }
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Guanacaste3D.slnx")))
            {
                var folder = Path.Combine(directory.FullName, "tests", "Guanacaste3D.Checks", "golden");
                Directory.CreateDirectory(folder);
                return folder;
            }
        return null;
    }
}
