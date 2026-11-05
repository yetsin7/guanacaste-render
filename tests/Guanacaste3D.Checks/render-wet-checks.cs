using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

/// <summary>Checks wet ground: where puddles are and how their rings lean, what stays dry, and what the render shows.</summary>
static class RenderWetChecks
{
    public static void Run(Action<bool, string> check)
    {
        // Puddles grow with the wetness and never exist when it is dry.
        var random = new Random(20261104);
        var points = Enumerable.Range(0, 4000).Select(_ => new Vector3((float)(random.NextDouble() * 200), (float)(random.NextDouble() * 200), 0)).ToArray();
        double Flooded(float wetness) => points.Count(point => SurfacePatterns.Puddles(point, wetness) > 0.5f) / (double)points.Length;
        var (light, heavy, soaked) = (Flooded(0.3f), Flooded(0.7f), Flooded(1f));
        check(Flooded(0) == 0 && light > 0 && light < heavy && heavy < soaked && soaked is > 0.2 and < 0.9 &&
              points.All(point => SurfacePatterns.Puddles(point, 1f) is >= 0 and <= 1),
            $"Puddles cover more of the ground as the wetness grows ({light:0%} at 30 %, {heavy:0%} at 70 %, {soaked:0%} at 100 %) and none when dry");

        // The rings: slopes stay gentle, exist near a drop and vanish between drops and far away.
        var slopes = points.Select(point => SurfacePatterns.RainRings(point)).ToArray();
        var steepest = slopes.Max(slope => Math.Sqrt(slope.SlopeU * slope.SlopeU + slope.SlopeV * slope.SlopeV));
        check(steepest is > 0.05 and < 0.5 && slopes.Count(slope => slope == (0, 0)) > slopes.Length / 4 && slopes.Any(slope => slope != (0, 0)) &&
              points.Take(500).All(point => Math.Abs(SurfacePatterns.RainRings(point, 200).SlopeU) < Math.Abs(SurfacePatterns.RainRings(point, 0).SlopeU) + 1e-9),
            $"Raindrop rings are gentle ({steepest:0.00} at the steepest), leave room between drops and fade with distance");

        // The scene: open ground gets wet, ground under a roof stays dry.
        var mesh = new SceneMesh();
        mesh.AddQuad(new(-60, -60, 0), new(60, -60, 0), new(60, 60, 0), new(-60, 60, 0), SurfaceKind.Ground);
        mesh.AddQuad(new(-3, -5, 3), new(0, -5, 3), new(0, 5, 3), new(-3, 5, 3), SurfaceKind.Floor);
        var scene = new RenderSceneBuilder().AddBuilding(mesh).Build();
        const int width = 160, height = 100;
        var camera = new RenderCamera(new Vector3(0, -12, 1.2f), new Vector3(0, 0, 0.3f), 50);
        byte[] Shot(float wetness)
        {
            var tracer = new PathTracer(scene, width, height, camera, new RenderSettings { Seed = 9, Wetness = wetness });
            for (var pass = 0; pass < 4; pass++) tracer.RenderPass();
            return tracer.ToBgra32(finish: ImageProcessor.Plain);
        }
        var dry = Shot(0);
        var wet = Shot(0.9f);
        check(Shot(0).SequenceEqual(dry) && Shot(0).SequenceEqual(Shot(0.0f)), "With no wetness the picture is exactly the dry one");
        var view = new PathTracer(scene, width, height, camera).Projection()!;
        view.TryProject(new Vector3(-1.5f, 0, 0), out var coveredX, out var coveredY, out _, out _);
        view.TryProject(new Vector3(4f, 0, 0), out var openX, out var openY, out _, out _);
        long Change(float x, float y)
        {
            long total = 0;
            for (var dy = -3; dy <= 3; dy++)
                for (var dx = -6; dx <= 6; dx++)
                {
                    var index = ((int)y + dy) * width + (int)x + dx;
                    for (var channel = 0; channel < 3; channel++) total += Math.Abs(wet[index * 4 + channel] - dry[index * 4 + channel]);
                }
            return total;
        }
        var (covered, open) = (Change(coveredX, coveredY), Change(openX, openY));
        check(open > 400 && covered * 3 < open, $"Open ground changes with the rain and the ground under a roof stays dry ({open} against {covered})");
        long Brightness(byte[] image) => Enumerable.Range(0, width * height).Sum(pixel => (long)image[pixel * 4] + image[pixel * 4 + 1] + image[pixel * 4 + 2]);
        check(Brightness(wet) != Brightness(dry), "The wet picture is not the dry one");

        // Glass, metal, lights and water are left as they are.
        var glassScene = new RenderSceneBuilder().AddBuilding(mesh, kind => kind == SurfaceKind.Ground ? MaterialLibrary.ForKind(SurfaceKind.Mirror) : MaterialLibrary.ForKind(kind)).Build();
        byte[] MirrorShot(float wetness)
        {
            var tracer = new PathTracer(glassScene, 64, 40, camera, new RenderSettings { Seed = 9, Wetness = wetness });
            tracer.RenderPass();
            return tracer.ToBgra32(finish: ImageProcessor.Plain);
        }
        check(MirrorShot(0).Zip(MirrorShot(0.9f)).Count(pair => Math.Abs(pair.First - pair.Second) > 25) < 40,
            "A mirror, like glass, metal, lights and water, does not get wet");

        // The choice is part of the project and of a photo's look, and rejects nonsense.
        // Rain wets the ground by itself; the wetness slider can only add to it.
        check(RainWetness.Effective(0, 0) == 0 && RainWetness.Effective(0, 0.5f) > 0.4f && RainWetness.Effective(0, 1) == RainWetness.AtFullRain &&
              RainWetness.Effective(0.95f, 0.3f) == 0.95f && RainWetness.Effective(float.NaN, float.NaN) == 0,
              "Rain leaves the ground wet on its own, a wetter slider wins, and no rain with no slider stays dry");
        var setup = new RenderSetup { Wetness = 0.7 };
        setup.Validate();
        var rejected = 0;
        foreach (var bad in new[] { new RenderSetup { Wetness = 1.5 }, new RenderSetup { Wetness = -0.1 }, new RenderSetup { Wetness = double.NaN } })
            try { bad.Validate(); } catch (InvalidDataException) { rejected++; }
        check(new RenderSetup().WithEffectsFrom(setup).Wetness == 0.7 && rejected == 3 &&
              RenderFx.Find("wet") is { Planned: false, Category: FxCategory.Weather, Removable: true } effect && effect.Controls[0].Key == "WetnessSlider",
            "Wetness is saved with the photo, rejected out of range, and an effect of the Weather group");

        // The cobogó house under rain: the grass turns darker and the picture stays valid.
        var house = BreezeBlockHouseShowcase.Create();
        var houseMesh = BuildingMesher.Build(house, includeGround: true);
        var (min, max) = BuildingMesher.Build(house).Bounds();
        var houseScene = new RenderSceneBuilder().AddBuilding(houseMesh).Build();
        byte[] HouseShot(float wetness)
        {
            var tracer = new PathTracer(houseScene, 128, 80, RenderCamera.FrameBounds(min, max), new RenderSettings { Seed = 4, Wetness = wetness });
            tracer.RenderPass();
            return tracer.ToBgra32(finish: ImageProcessor.Plain);
        }
        var houseDry = HouseShot(0);
        var houseWet = HouseShot(0.8f);
        check(Enumerable.Range(0, 128 * 80).All(pixel => houseWet[pixel * 4 + 3] == 255) && Brightness2(houseWet) != Brightness2(houseDry),
            "The cobogó house renders under rain with a valid picture that differs from the dry one");
        static long Brightness2(byte[] image) => Enumerable.Range(0, image.Length / 4).Sum(pixel => (long)image[pixel * 4] + image[pixel * 4 + 1] + image[pixel * 4 + 2]);
    }
}
