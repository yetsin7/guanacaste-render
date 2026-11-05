using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

/// <summary>
/// Checks the physically based path tracer: sky and sun, glass shadows, light bounds, adaptive sampling, tone
/// mapping, presets and the background render session. Each check builds a tiny scene whose answer is known.
/// </summary>
static class RenderEngineChecks
{
    public static void Run(Action<bool, string> check)
    {
        Materials(check);
        PbrScalarMaps(check);
        CastConcrete(check);
        NaturalStone(check);
        NormalMapFiltering(check);
        TextureInputValidation(check);
        Sky(check);
        Hdri(check);
        BvhOnLargeScene(check);
        GlassShadows(check);
        EmissiveLight(check);
        Determinism(check);
        Adaptive(check);
        Finish(check);
        Presets(check);
        Session(check).GetAwaiter().GetResult();
    }

    private static SceneMesh FloorWithRoof(SurfaceKind roof)
    {
        var mesh = new SceneMesh();
        mesh.AddQuad(new(-10, -10, 0), new(10, -10, 0), new(10, 10, 0), new(-10, 10, 0), SurfaceKind.Ground);
        mesh.AddQuad(new(-3, -3, 2), new(-3, 3, 2), new(3, 3, 2), new(3, -3, 2), roof); // faces downward
        mesh.AddQuad(new(-3, -3, 2), new(3, -3, 2), new(3, 3, 2), new(-3, 3, 2), roof);
        return mesh;
    }

    private static float MeanLuminance(PathTracer tracer)
    {
        var image = tracer.LinearImage(denoise: false);
        return image.Average(pixel => 0.2126f * pixel.X + 0.7152f * pixel.Y + 0.0722f * pixel.Z);
    }

    private static void Materials(Action<bool, string> check)
    {
        var broken = new RenderMaterial { BaseColor = new(float.NaN, 5, -1), Roughness = float.PositiveInfinity, Ior = 0, Transmission = 9 }.Sanitized();
        check(float.IsFinite(broken.BaseColor.X) && broken.BaseColor.Y == 1 && broken.BaseColor.Z == 0 && broken.Roughness <= 1 &&
              broken.Ior >= 1 && broken.Transmission == 1, "Materials with NaN or out-of-range values are sanitized before rendering");
        check(Math.Abs(new RenderMaterial { Ior = 1.5f }.DielectricF0 - 0.04f) < 1e-4f, "A glass index of 1.5 reflects 4 % at normal incidence");
    }

    private static void PbrScalarMaps(Action<bool, string> check)
    {
        var source = new byte[] { 0, 255, 255, 0 };
        var map = RenderScalarMap.FromGray(2, 2, source);
        source[0] = 255;
        check(map.LevelCount == 2 && map.Sample(.25f, .75f, 0) < .01f &&
              map.Sample(.75f, .75f, 0) > .99f &&
              MathF.Abs(map.Sample(.25f, .75f, 2) - .5f) < .01f,
            "Linear roughness/metallic maps preserve texels, filter distant values and own their buffers");
        bool Invalid(Func<RenderScalarMap> create)
        {
            try { create(); return false; } catch (ArgumentException) { return true; }
        }
        var odd = RenderScalarMap.FromGray(3, 1, [0, 255, 0]);
        check(Invalid(() => RenderScalarMap.FromGray(65536, 65536, [])) &&
              Invalid(() => RenderScalarMap.FromGray(2, 2, [1, 2])) &&
              odd.LevelCount == 3 && float.IsFinite(odd.Sample(.2f, .3f, 10)),
            "PBR scalar maps reject malformed images and downsample odd dimensions safely");
    }

    /// <summary>Checks that exposed concrete varies across its surface while painted walls keep their own finish.</summary>
    private static void CastConcrete(Action<bool, string> check)
    {
        var exposed = MaterialLibrary.ForKind(SurfaceKind.WallConcrete);
        var painted = MaterialLibrary.ForKind(SurfaceKind.Wall);
        var values = Enumerable.Range(0, 64).Select(index =>
            SurfacePatterns.Modulate(exposed.Pattern, exposed.PatternScale, new Vector3(index * 0.037f, 0, index * 0.021f), -Vector3.UnitY).X).ToArray();
        check(exposed.Pattern == SurfacePattern.PouredConcrete && painted.Pattern != exposed.Pattern &&
              values.All(value => float.IsFinite(value) && value is > 0.5f and < 1.3f) && values.Max() - values.Min() > 0.05f,
            "Exposed concrete has bounded surface variation without changing painted walls");
    }

    /// <summary>Natural-stone patterns have irregular joints and a stone-specific relief without changing concrete blocks.</summary>
    private static void NaturalStone(Action<bool, string> check)
    {
        var stone = MaterialLibrary.ForKind(SurfaceKind.WallStone);
        var blocks = MaterialLibrary.ForKind(SurfaceKind.WallBlock);
        var samples = (from y in Enumerable.Range(0, 64)
                       from x in Enumerable.Range(0, 64)
                       select SurfacePatterns.Modulate(stone.Pattern, stone.PatternScale,
                           new Vector3(x * 0.017f, 0, y * 0.017f), Vector3.UnitY).X).ToArray();
        check(stone.Pattern == SurfacePattern.StoneMasonry && blocks.Pattern == SurfacePattern.Blocks &&
              samples.All(value => float.IsFinite(value) && value > 0.15f && value < 1.6f) &&
              samples.Max() - samples.Min() > 0.35f,
            "Natural-stone walls have distinct recessed joints and varied stones without affecting concrete blocks");

        var p = new Vector3(-0.19f, 0, 0.31f);
        var a = SurfacePatterns.Modulate(stone.Pattern, stone.PatternScale, p, Vector3.UnitY);
        var b = SurfacePatterns.Modulate(stone.Pattern, stone.PatternScale, p, -Vector3.UnitY);
        var details = Enumerable.Range(0, 80)
            .Select(i => SurfacePatterns.Relief(stone.Pattern, stone.PatternScale,
                new Vector3(i * 0.013f, 0, 0.16f), Vector3.UnitY, 2f)).ToArray();
        check(a == b && details.All(n => float.IsFinite(n.X) && float.IsFinite(n.Y) && float.IsFinite(n.Z) &&
              MathF.Abs(n.Length() - 1) < 0.001f) && details.Any(n => Vector3.Dot(n, Vector3.UnitY) < 0.99f),
            "Stone relief is deterministic, unit length and continuous across opposite wall faces");
    }

    /// <summary>Distant normal maps converge toward their area-average direction instead of shimmering.</summary>
    private static void NormalMapFiltering(Action<bool, string> check)
    {
        byte[] texels = [255, 128, 255, 0, 128, 255,
                         0, 128, 255, 255, 128, 255];
        var map = NormalMap.FromRgb(2, 2, texels);
        var near = map.Sample(0.25f, 0.75f, 0);
        var far = map.Sample(0.25f, 0.75f, 2);
        texels[0] = 0;
        var retained = map.Sample(0.25f, 0.75f);
        check(map.LevelCount == 2 && near.X > 0.6f && far.Z > 0.99f &&
              MathF.Abs(far.X) < 0.03f && retained.X > 0.6f,
            "Normal maps retain near detail, filter distant opposing slopes and own the source buffer");

        bool Rejects(Func<NormalMap> create)
        {
            try { create(); return false; }
            catch (ArgumentException) { return true; }
        }
        var odd = NormalMap.FromRgb(3, 1,
            [128, 128, 255, 255, 128, 255, 128, 128, 255]);
        var sample = odd.Sample(0.2f, 0.4f, 4);
        check(Rejects(() => NormalMap.FromRgb(65_536, 65_536, [])) &&
              Rejects(() => NormalMap.FromRgb(2, 2, new byte[11])) &&
              odd.LevelCount == 3 && float.IsFinite(sample.X) &&
              MathF.Abs(sample.Length() - 1f) < 1e-4f,
            "Normal maps reject malformed dimensions and filter odd-sized maps safely");

        // On a 3x3 map, a single tilted corner represents 1/9 of the
        // texture regardless of which corner it occupies. Repeated unweighted
        // 2x2 reductions used to make opposite corners contribute 1/16 vs 1/4.
        var neutral = new byte[3 * 3 * 3];
        for (var pixel = 0; pixel < 9; pixel++)
        {
            neutral[pixel * 3] = 128;
            neutral[pixel * 3 + 1] = 128;
            neutral[pixel * 3 + 2] = 255;
        }
        var firstCorner = (byte[])neutral.Clone();
        firstCorner[0] = 255;
        firstCorner[2] = 128;
        var lastCorner = (byte[])neutral.Clone();
        lastCorner[8 * 3] = 255;
        lastCorner[8 * 3 + 2] = 128;
        var firstDistant = NormalMap.FromRgb(3, 3, firstCorner).Sample(.4f, .6f, 10f);
        var lastDistant = NormalMap.FromRgb(3, 3, lastCorner).Sample(.4f, .6f, 10f);
        check(MathF.Abs(firstDistant.X - lastDistant.X) < .02f &&
              firstDistant.X is > .12f and < .23f &&
              firstDistant.Z > .96f && lastDistant.Z > .96f,
            "Odd-sized normal-map mips give equally sized corner details equal distant coverage");
    }

    private static void TextureInputValidation(Action<bool, string> check)
    {
        static bool RejectsInvalidSize(Func<RenderTexture> create)
        {
            try { create(); return false; }
            catch (ArgumentException) { return true; }
        }

        // 65,536² pixels used to overflow the int-sized byte count to zero, passing
        // validation and then failing deep in the mip-map generator.
        check(RejectsInvalidSize(() => RenderTexture.FromBgra32(65_536, 65_536, Array.Empty<byte>())) &&
              RejectsInvalidSize(() => RenderTexture.FromRgba(65_536, 65_536, Array.Empty<byte>())),
            "Malformed giant BGRA/RGBA dimensions are rejected before allocation");
        check(RejectsInvalidSize(() => RenderTexture.FromBgra32(2, 2, new byte[15])) &&
              RejectsInvalidSize(() => RenderTexture.FromRgba(2, 2, new byte[15])) &&
              RejectsInvalidSize(() => RenderTexture.FromBgra32(0, 1, Array.Empty<byte>())),
            "Texture constructors reject incomplete buffers and non-positive dimensions");

        var rgba = RenderTexture.FromRgba(2, 1,
            new byte[] { 255, 0, 0, 255, 0, 255, 0, 0 });
        check(rgba.Width == 2 && rgba.Height == 1 && rgba.HasAlpha &&
              rgba.Alpha(0.25f, 0.5f) > 0.99f && rgba.Alpha(0.75f, 0.5f) < 0.01f,
            "Valid RGBA cut-out textures retain their dimensions and alpha coverage");

        // Color maps must make the same safe mip choice as roughness/height maps:
        // extreme finite footprints are fully blurred, not accidentally sharp.
        var color = RenderTexture.FromBgra32(2, 2,
            [0, 0, 255, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255]);
        var near = color.Sample(.25f, .75f, 0f);
        var far = color.Sample(.25f, .75f, float.MaxValue);
        check(near.X > .99f && far.X > .22f && far.X < .28f &&
              MathF.Abs(color.TexelSize(float.MaxValue) - 1f) < 1e-6f,
            "Extreme finite color texture footprints use the coarsest mip and texel size");
        var invalidUv = color.Sample(float.NaN, .75f, 0f);
        var invalidFootprint = color.Sample(.25f, .75f, float.NaN);
        check(float.IsFinite(invalidUv.X) && float.IsFinite(invalidUv.Y) &&
              float.IsFinite(invalidUv.Z) &&
              Vector3.Distance(invalidUv, color.Mean) < 1e-6f &&
              Vector3.Distance(invalidFootprint, near) < 1e-6f &&
              MathF.Abs(color.TexelSize(float.NaN) - .5f) < 1e-6f,
            "Malformed color UVs and footprints never propagate NaN into PBR shading");
    }

    private static void Sky(Action<bool, string> check)
    {
        var noon = new SkyModel(SkyKind.Clear, new Vector3(0.3f, 0.2f, 0.9f));
        var up = noon.Radiance(Vector3.UnitZ);
        check(up.Z > up.X && up.Z > up.Y && up.Y > up.X, "A clear daytime sky is blue at the zenith");
        var horizon = noon.Radiance(Vector3.Normalize(new Vector3(0.2f, -1f, 0.03f)));
        check(Sum(horizon) > Sum(up) * 0.5f, "The horizon is not darker than half of the zenith");

        var high = new SkyModel(SkyKind.Clear, Vector3.Normalize(new Vector3(0.3f, 0, 1))).SunColor;
        var low = new SkyModel(SkyKind.Clear, Vector3.Normalize(new Vector3(1f, 0, 0.1f))).SunColor;
        check(low.X / Math.Max(low.Z, 1e-6f) > high.X / high.Z * 2 && Sum(low) < Sum(high), "A low sun is redder and dimmer than a high one");
        check(new SkyModel(SkyKind.Clear, new Vector3(1, 0, -0.3f)).SunColor == Vector3.Zero, "The sun below the horizon casts no light");
        var night = new SkyModel(SkyKind.Clear, Vector3.Normalize(new Vector3(1, 0, -0.5f))).Radiance(Vector3.UnitZ);
        check(Sum(night) < 0.1f, "The night sky is dark");
        var cloudy = new SkyModel(SkyKind.Overcast, Vector3.UnitZ);
        check(cloudy.SunColor == Vector3.Zero && cloudy.Radiance(Vector3.UnitZ).Y > cloudy.Radiance(new Vector3(1, 0, 0.1f)).Y,
            "An overcast sky has no sun and is brightest overhead");
        var hazy = new SkyModel(SkyKind.Clear, Vector3.Normalize(new Vector3(0.3f, 0, 1)), float.NaN);
        check(float.IsFinite(hazy.Turbidity) && Sum(hazy.Radiance(Vector3.UnitX)) > 0, "A NaN turbidity falls back to a clear sky");
    }

    private static void Hdri(Action<bool, string> check)
    {
        var header = System.Text.Encoding.ASCII.GetBytes("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 2 +X 2\n");
        // Encode 1.0 in the red or blue channel as 128 * 2^(129 - 136).
        var values = new byte[]
        {
            128, 0, 0, 129, 128, 0, 0, 129,
            0, 0, 128, 129, 0, 0, 128, 129
        };
        var panorama = HdriEnvironment.FromRgbe([.. header, .. values]);
        var top = panorama.Sample(Vector3.UnitZ);
        var bottom = panorama.Sample(-Vector3.UnitZ);
        check(panorama.Width == 2 && panorama.Height == 2 &&
              top.X > 0.99f && top.Z < 0.01f && bottom.Z > 0.99f && bottom.X < 0.01f,
            "Radiance HDRI renders distinct top/bottom hemispheres in linear radiance");
        bool Reject(byte[] bytes)
        {
            try { _ = HdriEnvironment.FromRgbe(bytes); return false; }
            catch (InvalidDataException) { return true; }
        }
        check(Reject([.. header, .. values, 0]) &&
              Reject(System.Text.Encoding.ASCII.GetBytes("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 99999 +X 2\n")),
            "HDRI rejects unexpected trailing bytes and extreme dimensions");

        // Width >= 8 selects Radiance's four-channel scanline RLE.
        var rleHeader = System.Text.Encoding.ASCII.GetBytes("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 1 +X 8\n");
        var rle = HdriEnvironment.FromRgbe([.. rleHeader, 2, 2, 0, 8,
            136, 128, 136, 64, 136, 32, 136, 129]);
        var light = rle.Sample(Vector3.UnitX);
        check(rle.Width == 8 && light.X > light.Y && light.Y > light.Z && light.Z > 0,
            "RGBE per-channel RLE scanlines decode colors and exponent correctly");

        // Very large but finite camera/environment yaw values must not overflow
        // degrees-to-radians conversion and turn HDRI samples into NaN pixels.
        var hugeYaw = panorama.Sample(Vector3.UnitX, float.MaxValue);
        var negativeHugeYaw = panorama.Sample(Vector3.UnitX, -float.MaxValue);
        var reducedYaw = (float)((double)float.MaxValue % 360.0);
        check(float.IsFinite(hugeYaw.X + hugeYaw.Y + hugeYaw.Z) &&
              float.IsFinite(negativeHugeYaw.X + negativeHugeYaw.Y + negativeHugeYaw.Z) &&
              Vector3.Distance(hugeYaw, panorama.Sample(Vector3.UnitX, reducedYaw)) < 1e-4f &&
              Vector3.Distance(panorama.Sample(Vector3.UnitX, float.NaN),
                               panorama.Sample(Vector3.UnitX)) < 1e-4f,
            "HDRI sampling wraps extreme finite yaw and ignores nonfinite yaw without NaN pixels");
    }

    private static float Sum(Vector3 v) => v.X + v.Y + v.Z;

    /// <summary>Compares the parallel-built hierarchy with brute force on a scene large enough to take the parallel path.</summary>
    private static void BvhOnLargeScene(Action<bool, string> check)
    {
        var random = new Random(11);
        const int count = 120_000;
        var positions = new Vector3[count * 3];
        var indices = new int[count * 3];
        for (var t = 0; t < count; t++)
        {
            var center = new Vector3(random.NextSingle() * 100, random.NextSingle() * 100, random.NextSingle() * 100);
            for (var k = 0; k < 3; k++)
            {
                positions[t * 3 + k] = center + new Vector3(random.NextSingle(), random.NextSingle(), random.NextSingle()) * 1.5f;
                indices[t * 3 + k] = t * 3 + k;
            }
        }
        var bvh = new Bvh(positions, indices, null);
        var wrong = 0;
        for (var sample = 0; sample < 150; sample++)
        {
            var origin = new Vector3(random.NextSingle() * 100, random.NextSingle() * 100, -5);
            var direction = Vector3.Normalize(new Vector3(random.NextSingle() - 0.5f, random.NextSingle() - 0.5f, 1));
            var hit = new RayHit { Distance = float.MaxValue };
            var found = bvh.Intersect(origin, direction, 1e-4f, ref hit);
            var best = float.MaxValue;
            for (var t = 0; t < count; t++)
            {
                var a = positions[t * 3];
                var e1 = positions[t * 3 + 1] - a;
                var e2 = positions[t * 3 + 2] - a;
                var p = Vector3.Cross(direction, e2);
                var determinant = Vector3.Dot(e1, p);
                if (MathF.Abs(determinant) < 1e-12f) continue;
                var s = origin - a;
                var u = Vector3.Dot(s, p) / determinant;
                if (u < 0 || u > 1) continue;
                var q = Vector3.Cross(s, e1);
                var v = Vector3.Dot(direction, q) / determinant;
                if (v < 0 || u + v > 1) continue;
                var distance = Vector3.Dot(e2, q) / determinant;
                if (distance > 1e-4f && distance < best) best = distance;
            }
            var occluded = bvh.Occluded(origin, direction, 1e-4f, float.MaxValue);
            if (found != (best < float.MaxValue) || (found && MathF.Abs(best - hit.Distance) > 1e-3f) || occluded != found) wrong++;
        }
        check(wrong == 0 && bvh.TriangleCount == count, "A 120 000-triangle parallel BVH matches brute force for closest and any hit");
    }

    private static void GlassShadows(Action<bool, string> check)
    {
        var camera = new RenderCamera(new Vector3(0, 0, 1), new Vector3(0, 0, 0), 40);
        var settings = new RenderSettings { SunDirection = Vector3.UnitZ, SkyIntensity = 0, MaxBounces = 0, SunAngularRadiusDegrees = 0.5f };
        float Measure(SceneMesh mesh)
        {
            var tracer = new PathTracer(mesh, 8, 8, camera, settings);
            for (var pass = 0; pass < 24; pass++) tracer.RenderPass();
            return MeanLuminance(tracer);
        }
        var open = new SceneMesh();
        open.AddQuad(new(-10, -10, 0), new(10, -10, 0), new(10, 10, 0), new(-10, 10, 0), SurfaceKind.Ground);
        var lit = Measure(open);
        var glass = Measure(FloorWithRoof(SurfaceKind.Glass));
        var opaque = Measure(FloorWithRoof(SurfaceKind.Wall));
        Console.WriteLine($"INFO: sun on floor, open {lit:0.000}, under glass {glass:0.000}, under an opaque roof {opaque:0.000}");
        check(lit > 0.1f, "Direct sun lights an open floor");
        check(glass > lit * 0.5f && glass < lit, "Sunlight passes through glass, tinted and slightly reduced");
        check(opaque < lit * 0.05f, "An opaque roof blocks direct sun completely");
    }

    private static void EmissiveLight(Action<bool, string> check)
    {
        var mesh = new SceneMesh();
        mesh.AddQuad(new(-5, -5, 0), new(5, -5, 0), new(5, 5, 0), new(-5, 5, 0), SurfaceKind.Floor);
        var scene = new RenderSceneBuilder().AddBuilding(mesh).Build();
        var lamp = new RenderMaterial { Name = "Lamp", BaseColor = Vector3.Zero, Emission = new Vector3(20) };
        var panel = new SceneMesh();
        panel.AddQuad(new(-0.5f, -0.5f, 2), new(-0.5f, 0.5f, 2), new(0.5f, 0.5f, 2), new(0.5f, -0.5f, 2), SurfaceKind.Wall);
        var builder = new RenderSceneBuilder().AddBuilding(mesh);
        var withLamp = builder.AddBuilding(panel, kind => kind == SurfaceKind.Wall ? lamp : MaterialLibrary.ForKind(kind)).Build();
        var settings = new RenderSettings { SunIntensity = 0, SkyIntensity = 0, MaxBounces = 1 };
        var camera = new RenderCamera(new Vector3(0, -4, 1.5f), new Vector3(0, 0, 0.5f), 50);
        var dark = new PathTracer(scene, 16, 16, camera, settings);
        var bright = new PathTracer(withLamp, 16, 16, camera, settings);
        for (var pass = 0; pass < 48; pass++)
        {
            dark.RenderPass();
            bright.RenderPass();
        }
        check(MeanLuminance(dark) < 0.02f && MeanLuminance(bright) > 0.2f, "An emissive panel lights the floor with no sun or sky");
    }

    private static void Determinism(Action<bool, string> check)
    {
        var mesh = FloorWithRoof(SurfaceKind.Wall);
        var camera = new RenderCamera(new Vector3(6, -6, 4), new Vector3(0, 0, 1), 45);
        PathTracer Make() => new(mesh, 24, 16, camera, new RenderSettings { Seed = 5, MaxThreads = 1 });
        var a = Make();
        var b = Make();
        for (var pass = 0; pass < 6; pass++)
        {
            a.RenderPass();
            b.RenderPass();
        }
        check(a.LinearImage(false).SequenceEqual(b.LinearImage(false)), "Equal seeds reproduce identical images");
        var c = new PathTracer(mesh, 24, 16, camera, new RenderSettings { Seed = 6, MaxThreads = 1 });
        for (var pass = 0; pass < 6; pass++) c.RenderPass();
        check(!a.LinearImage(false).SequenceEqual(c.LinearImage(false)), "A different seed produces a different noise pattern");
        check(a.RayCount > 24 * 16 * 6, "The tracer counts the rays it casts");
    }

    private static void Adaptive(Action<bool, string> check)
    {
        var mesh = FloorWithRoof(SurfaceKind.Wall);
        var camera = new RenderCamera(new Vector3(6, -6, 4), new Vector3(0, 0, 1), 45);
        var fixedSamples = new PathTracer(mesh, 40, 28, camera, new RenderSettings { Seed = 2 });
        var adaptive = new PathTracer(mesh, 40, 28, camera, new RenderSettings { Seed = 2, AdaptiveSampling = true, AdaptiveTolerance = 0.05f });
        for (var pass = 0; pass < 96; pass++)
        {
            fixedSamples.RenderPass();
            adaptive.RenderPass();
        }
        Console.WriteLine($"INFO: adaptive sampling kept {adaptive.ActiveFraction:P0} of pixels active, {adaptive.AverageSamplesPerPixel:0.0} spp average");
        check(adaptive.ActiveFraction < 0.9f && adaptive.AverageSamplesPerPixel < 96, "Adaptive sampling retires converged pixels and saves samples");
        var difference = Math.Abs(MeanLuminance(adaptive) - MeanLuminance(fixedSamples)) / MeanLuminance(fixedSamples);
        check(difference < 0.05f, "Adaptive and fixed sampling agree on mean brightness within 5 %");
    }

    private static void Finish(Action<bool, string> check)
    {
        var black = new Vector3[16];
        var bytes = ImageProcessor.ToBgra32(black, 4, 4, 1, ImageProcessor.Plain);
        check(Enumerable.Range(0, 16).All(i => bytes[i * 4] == 0 && bytes[i * 4 + 3] == 255), "A black image stays black and opaque");
        var hot = Enumerable.Repeat(new Vector3(500), 16).ToArray();
        check(ImageProcessor.ToBgra32(hot, 4, 4, 1, ImageProcessor.Plain).Where((_, i) => i % 4 != 3).All(b => b >= 250),
            "Very bright values tone-map to white without overflow");
        var dim = Enumerable.Repeat(new Vector3(0.02f), 64).ToArray();
        var measured = ImageProcessor.MeasureAutoExposure(dim, 1, null);
        check(measured > 2 && measured <= 2.5f, "Auto exposure brightens a dim image, within its limit");
        var nan = Enumerable.Repeat(new Vector3(float.NaN), 16).ToArray();
        check(ImageProcessor.ToBgra32(nan, 4, 4, 1, new ImageFinish { Bloom = 0.5f, Dither = false, Vignette = 0 }).All(b => b is 0 or 255),
            "NaN pixels render as black instead of corrupting the image");
        var withBloom = new Vector3[64 * 64];
        withBloom[32 * 64 + 32] = new Vector3(200);
        var plain = ImageProcessor.ToBgra32(withBloom, 64, 64, 1, new ImageFinish { Bloom = 0, Vignette = 0, Dither = false });
        var glow = ImageProcessor.ToBgra32(withBloom, 64, 64, 1, new ImageFinish { Bloom = 1, Vignette = 0, Dither = false });
        check(glow[(32 * 64 + 40) * 4] > plain[(32 * 64 + 40) * 4], "Bloom spreads light from a bright pixel to its neighbors");
    }

    private static void Presets(Action<bool, string> check)
    {
        var golden = RenderPresets.HourForAltitude(12.1, 120, 6);
        var altitude = SolarPosition.AltitudeDegrees(SolarPosition.Direction(12.1, 120, golden));
        check(Math.Abs(altitude - 6) < 0.05 && golden > 12, "The golden-hour preset finds the hour of a 6° afternoon sun");
        var dusk = RenderPresets.Lighting(LightingScenario.Dusk, 12.1, 120, 10);
        check(dusk.SunIntensity == 0, "Dusk lighting has the sun below the horizon and switched off");
        check(RenderPresets.Lighting(LightingScenario.Overcast, 12.1, 120, 10).SunIntensity == 0 &&
              RenderPresets.Lighting(LightingScenario.Overcast, 12.1, 120, 10).Sky == SkyKind.Overcast, "Overcast lighting has no sun");
        check(RenderPresets.Lighting(LightingScenario.Sunny, 12.1, 120, 12).SunIntensity > 1.5f, "A sunny noon has a strong sun");
        check(RenderPresets.Profile(RenderQuality.Draft).TargetSamples < RenderPresets.Profile(RenderQuality.Standard).TargetSamples &&
              RenderPresets.Profile(RenderQuality.High).TargetSamples < RenderPresets.Profile(RenderQuality.Final).TargetSamples,
            "Quality levels take more samples as they rise");
        check(RenderPresets.Threads(ResourceMode.Eco) <= RenderPresets.Threads(ResourceMode.Balanced) &&
              RenderPresets.Threads(ResourceMode.Balanced) <= RenderPresets.Threads(ResourceMode.Maximum), "Resource modes order the thread counts");
        check(RenderPresets.OutputSize(1920, 1080, RenderQuality.Draft) == (960, 540), "Draft quality renders at half resolution");
        check(RenderPresets.HourForAltitude(80, 355, 6) is >= 12 and <= 24, "Polar latitudes still yield an hour instead of failing");
    }

    private static async Task Session(Action<bool, string> check)
    {
        var mesh = FloorWithRoof(SurfaceKind.Wall);
        var scene = new RenderSceneBuilder().AddBuilding(mesh).Build();
        var camera = new RenderCamera(new Vector3(6, -6, 4), new Vector3(0, 0, 1), 45);
        var frames = new List<RenderFrame>();
        var phases = new List<string>();
        using (var session = new RenderSession(scene, camera, new RenderSettings(), new RenderJob(48, 32, 8)))
        {
            session.FrameReady += frame => { lock (frames) frames.Add(frame); };
            session.PhaseChanged += phases.Add;
            var result = await session.RunAsync(CancellationToken.None);
            check(result.Completed && result.Samples == 8 && frames.Count >= 2 && frames[^1].IsFinal && frames[^1].Bgra.Length == 48 * 32 * 4,
                "A render session produces a preview frame early and a final frame at the sample target");
            check(phases.SequenceEqual(["Render.Preparing", "Render.Rendering"]) && result.Rays > 0, "A render session reports its phases and ray count");
        }

        var rejectedInvalidBucket = false;
        try
        {
            using var invalid = new RenderSession(scene, camera, new RenderSettings(),
                new RenderJob(64, 64, 2, Buckets: true) { BucketSize = 7 });
        }
        catch (ArgumentOutOfRangeException) { rejectedInvalidBucket = true; }
        check(rejectedInvalidBucket && new RenderSetup { BucketSize = 8 } is { BucketSize: 8 },
            "Bucket size is validated before rendering and the selection is stored in RenderSetup");
        var rejectedSetup = false;
        try { (new RenderSetup { BucketSize = 7 }).Validate(); }
        catch (InvalidDataException) { rejectedSetup = true; }
        check(rejectedSetup, "Project render setups reject invalid bucket sizes");

        // Partial buckets at the image edge can be 1px wide or high.
        // Verify corner markers neither escape the tile nor write past the BGRA buffer.
        var bracketMethod = typeof(RenderSession).GetMethod("Brackets",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var cornerFrame = new byte[17 * 17 * 4];
        var stripFrame = new byte[17 * 17 * 4];
        var clippedBrackets = bracketMethod is not null;
        try
        {
            bracketMethod?.Invoke(null, new object[] { cornerFrame, 17, 16, 16, 1, 1 });
            bracketMethod?.Invoke(null, new object[] { stripFrame, 17, 16, 0, 1, 16 });
        }
        catch (System.Reflection.TargetInvocationException) { clippedBrackets = false; }
        check(clippedBrackets && cornerFrame.Count(value => value != 0) == 3 &&
              Enumerable.Range(0, stripFrame.Length).All(i => (i / 4) % 17 == 16 || stripFrame[i] == 0),
            "Bucket corner markers stay inside one-pixel edge tiles and the image buffer");

        var bucketFrames = new List<RenderFrame>();
        using (var bucketed = new RenderSession(scene, camera, new RenderSettings(), new RenderJob(150, 100, 4, Buckets: true) { BucketSize = 16 }))
        {
            bucketed.FrameReady += frame => { lock (bucketFrames) bucketFrames.Add(frame); };
            var result = await bucketed.RunAsync(CancellationToken.None);
            var first = bucketFrames[0].Bgra;
            var darkAtStart = first.Where((_, i) => i % 4 != 3).All(value => value <= 40);
            check(result.Completed && result.Samples == 4 && bucketFrames[^1].IsFinal && darkAtStart &&
                  bucketFrames[0].Progress == 0 && bucketFrames[^1].Progress == 1,
                "A 16px bucket render begins empty, fills its exact completion bar and ends with an image");
            check(bucketFrames.All(frame => frame.Progress is >= 0 and <= 1) &&
                  bucketFrames.Zip(bucketFrames.Skip(1), (a, b) => a.Progress <= b.Progress).All(value => value),
                "Bucket progress never decreases even when many workers complete squares concurrently");
            check(bucketed.RenderElapsed >= TimeSpan.Zero && bucketed.RenderElapsed <= result.Elapsed + TimeSpan.FromMilliseconds(50),
                "The monotonic elapsed timer freezes when the render completes");
        }

        using (var cancelling = new RenderSession(scene, camera, new RenderSettings(), new RenderJob(48, 32, 100000)))
        {
            using var cancellation = new CancellationTokenSource();
            cancelling.FrameReady += _ => cancellation.Cancel();
            var result = await cancelling.RunAsync(cancellation.Token);
            check(result.Cancelled && result.LastFrame is { IsFinal: true } && result.Samples > 0, "Cancelling a render keeps the image rendered so far");
        }

        using (var paused = new RenderSession(scene, camera, new RenderSettings(), new RenderJob(48, 32, 400)))
        {
            paused.Pause();
            var running = paused.RunAsync(CancellationToken.None);
            await Task.Delay(400);
            var heldAt = paused.Tracer?.SampleCount ?? 0;
            await Task.Delay(300);
            var stillHeld = (paused.Tracer?.SampleCount ?? 0) == heldAt;
            paused.Resume();
            await running;
            check(stillHeld && paused.Tracer!.SampleCount == 400, "A paused render waits and resumes to completion");
        }

        var limited = new RenderSession(scene, camera, new RenderSettings(), new RenderJob(48, 32, 1_000_000, TimeSpan.FromMilliseconds(300)));
        var limitedResult = await limited.RunAsync(CancellationToken.None);
        check(limitedResult.Completed && limitedResult.Samples < 1_000_000 && limitedResult.Elapsed < TimeSpan.FromSeconds(20),
            "A time limit stops the render long before the sample target");
        limited.Dispose();

        var refinish = new RenderSession(scene, camera, new RenderSettings(), new RenderJob(32, 24, 3));
        check(refinish.Refinish(new ImageFinish(), TimeSpan.Zero) is null, "Re-finishing before the first sample returns nothing");
        await refinish.RunAsync(CancellationToken.None);
        var bright = refinish.Refinish(new ImageFinish { ExposureStops = 2, AutoExposure = false, Bloom = 0 }, TimeSpan.Zero)!;
        var darker = refinish.Refinish(new ImageFinish { ExposureStops = -2, AutoExposure = false, Bloom = 0 }, TimeSpan.Zero)!;
        check(bright.Bgra.Sum(b => (long)b) > darker.Bgra.Sum(b => (long)b), "Exposure changes re-finish the image without re-rendering");
        refinish.Dispose();
        _ = Task.CompletedTask;
    }
}
