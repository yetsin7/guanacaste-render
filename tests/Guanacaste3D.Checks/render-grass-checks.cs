using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

/// <summary>Checks the blades of grass: what a view ray finds in the lawn, that far grass matches near grass, and what the render shows.</summary>
static class RenderGrassChecks
{
    public static void Run(Action<bool, string> check)
    {
        // Seen from a slant, a point of lawn is a blade (light toward the tip, dark at the root) or the dark soil between blades.
        var random = new Random(20261105);
        var direction = Vector3.Normalize(new Vector3(0.3f, 0.8f, -0.45f));
        var samples = Enumerable.Range(0, 40000).Select(_ => SurfacePatterns.GrassBlades(
            new Vector3((float)random.NextDouble() * 20, (float)random.NextDouble() * 20, 0), Vector3.UnitZ, direction, 0.2f, 0.022f)).ToArray();
        var blades = samples.Count(sample => sample.Blade) / (double)samples.Length;
        var mean = samples.Average(sample => Sampling.Luminance(sample.Tone));
        Console.WriteLine($"GRASS blades={blades:0.000} mean={mean:0.000}");
        check(blades > 0.6 && blades < 0.99 && samples.Any(sample => !sample.Blade) && Math.Abs(mean - SurfacePatterns.GrassMeanTone) / mean < 0.1,
            $"Grass from a slant is {blades:0%} blades over soil, and its average tone ({mean:0.00}) is what far, unresolved grass is multiplied by");
        var flat = SurfacePatterns.GrassBlades(new Vector3(1, 1, 0), Vector3.UnitZ, new Vector3(1, 0, 0), 0.2f, 0.022f);
        check(!flat.Blade && SurfacePatterns.GrassBlades(new Vector3(1, 1, 0), Vector3.UnitZ, direction, 0, 0.022f).Blade == false &&
              SurfacePatterns.GrassBlades(new Vector3(1, 1, 0), Vector3.UnitZ, direction, 0.2f, 0.022f) == SurfacePatterns.GrassBlades(new Vector3(1, 1, 0), Vector3.UnitZ, direction, 0.2f, 0.022f),
            "A ray along the ground, a lawn of no height, and the same point twice give soil, soil and the same answer");

        // The render: a lawn grows blades near the camera and is the same as before with the grass off.
        var ground = new SceneMesh();
        ground.AddQuad(new(-200, -200, 0), new(200, -200, 0), new(200, 200, 0), new(-200, 200, 0), SurfaceKind.Ground);
        var scene = new RenderSceneBuilder().AddBuilding(ground).Build();
        const int width = 240, height = 150;
        var camera = new RenderCamera(new Vector3(0, -0.9f, 0.5f), new Vector3(0, 1, 0), 55);
        byte[] Shot(float grass)
        {
            var tracer = new PathTracer(scene, width, height, camera, new RenderSettings { Seed = 4, GrassHeight = grass });
            for (var pass = 0; pass < 4; pass++) tracer.RenderPass();
            return tracer.ToBgra32(finish: ImageProcessor.Plain);
        }
        var dry = Shot(0);
        var grown = Shot(0.25f);
        double Detail(byte[] picture, int y0, int y1)
        {
            // Pixel-to-pixel change over a band of rows: blades make the near lawn busy.
            double sum = 0;
            var count = 0;
            for (var y = y0; y < y1; y++)
                for (var x = 1; x < width; x++)
                {
                    sum += Math.Abs(picture[(y * width + x) * 4 + 1] - picture[(y * width + x - 1) * 4 + 1]);
                    count++;
                }
            return sum / count;
        }
        double Brightness(byte[] picture, int y0, int y1)
        {
            double sum = 0;
            for (var y = y0; y < y1; y++)
                for (var x = 0; x < width; x++) sum += picture[(y * width + x) * 4 + 1];
            return sum / ((y1 - y0) * width);
        }
        check(Shot(0).SequenceEqual(dry) && !grown.SequenceEqual(dry) && Detail(grown, 130, 148) > Detail(dry, 130, 148) * 2,
            $"With the grass off the picture is exactly the old one; with it on the near lawn is far busier ({Detail(grown, 130, 148):0.0} against {Detail(dry, 130, 148):0.0})");
        check(Math.Abs(Brightness(grown, 45, 55) - Brightness(dry, 45, 55)) / Brightness(dry, 45, 55) < 0.25,
            $"Far lawn keeps about the brightness it had, so the blades do not show as a seam ({Brightness(grown, 45, 55):0} against {Brightness(dry, 45, 55):0})");

        // Wind: calm grass is the grass as before at any second; windy grass leans and ripples, and the gusts move with the time.
        var spot = new Vector3(3.1f, 4.7f, 0);
        var calm = SurfacePatterns.GrassBlades(spot, Vector3.UnitZ, direction, 0.2f, 0.022f);
        check(SurfacePatterns.GrassBlades(spot, Vector3.UnitZ, direction, 0.2f, 0.022f, 0, 5.3f, 1f) == calm &&
              samples.Zip(Enumerable.Range(0, samples.Length)).Take(2000).Any(pair => pair.First != SurfacePatterns.GrassBlades(
                  new Vector3(pair.Second % 50 * 0.37f, pair.Second / 50 * 0.29f, 0), Vector3.UnitZ, direction, 0.2f, 0.022f, 0.8f, 1f, 0.5f)),
            "Calm grass is the same at any second, and wind changes the blades");
        byte[] Windy(float strength, float seconds)
        {
            var tracer = new PathTracer(scene, width, height, camera, new RenderSettings { Seed = 4, GrassHeight = 0.25f, WindStrength = strength, WindSeconds = seconds });
            for (var pass = 0; pass < 4; pass++) tracer.RenderPass();
            return tracer.ToBgra32(finish: ImageProcessor.Plain);
        }
        var still = Windy(0, 0);
        check(Windy(0, 3).SequenceEqual(still) && still.SequenceEqual(grown) && !Windy(0.8f, 0).SequenceEqual(still) && !Windy(0.8f, 1.3f).SequenceEqual(Windy(0.8f, 0)),
            "With no wind the picture does not depend on the time; with wind the lawn leans, and the gusts have moved a moment later");

        // Rendering must not wait for the thread pool to grow: once a tracer exists the pool starts with room for every core.
        ThreadPool.GetMinThreads(out var poolThreads, out _);
        check(poolThreads >= Math.Max(4, Environment.ProcessorCount * 2),
            $"A path tracer leaves the thread pool room to start with twice as many threads as cores ({poolThreads}), so no core waits for it to grow");

        // Saved with the photo's look, and refused out of range.
        var setup = new RenderSetup { Grass = 0.6, Wind = 0.35 };
        var rejected = new[] { new RenderSetup { Grass = 1.4 }, new RenderSetup { Grass = -0.1 }, new RenderSetup { Grass = double.NaN }, new RenderSetup { Wind = 2 } }.Count(bad =>
        {
            try { bad.Validate(); return false; } catch (InvalidDataException) { return true; }
        });
        check(new RenderSetup().WithEffectsFrom(setup).Grass == 0.6 && new RenderSetup().WithEffectsFrom(setup).Wind == 0.35 && rejected == 4 && RenderFx.Find("grass") is { Planned: false, Removable: true } effect && effect.Controls[0].Key == "GrassSlider" && RenderFx.Find("wind")?.Controls[0].Key == "WindSlider",
            "The grass height is kept with the photo's look, bad values are refused, and the effect picker offers it");
    }
}
