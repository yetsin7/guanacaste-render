using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

/// <summary>Checks the Rain and Snow effects: they draw streaks and flakes, snow covers what faces up, and nothing changes at zero.</summary>
static class RenderWeatherChecks
{
    public static void Run(Action<bool, string> check)
    {
        const int width = 160, height = 100;
        var image = new Vector3[width * height];
        Array.Fill(image, new Vector3(0.3f));
        var plain = new ImageFinish { Dither = false, Bloom = 0, Vignette = 0 };
        var neutral = ImageProcessor.ToBgra32(image, width, height, 1, plain);
        int Light(byte[] pixels, int index) => pixels[index * 4] + pixels[index * 4 + 1] + pixels[index * 4 + 2];

        var rain = ImageProcessor.ToBgra32(image, width, height, 1, plain with { Rain = 1 });
        var brighter = Enumerable.Range(0, width * height).Count(index => Light(rain, index) > Light(neutral, index) + 12);
        check(brighter > width * height / 200 && brighter < width * height / 4,
            $"Rain draws streaks over part of the picture, not all of it ({brighter} of {width * height} pixels)");
        check(rain.SequenceEqual(ImageProcessor.ToBgra32(image, width, height, 1, plain with { Rain = 1 })),
            "The same picture gets the same rain every time");
        var drizzle = ImageProcessor.ToBgra32(image, width, height, 1, plain with { Rain = 0.2f });
        check(Enumerable.Range(0, width * height).Count(index => Light(drizzle, index) > Light(neutral, index) + 12) < brighter,
            "Light rain has fewer streaks than heavy rain");
        check(ImageProcessor.ToBgra32(image, width, height, 1, plain with { Rain = 0, Snow = 0 }).SequenceEqual(neutral),
            "Rain and snow at zero leave the picture unchanged");

        // Snow: the left half faces up (cover), the right half stands upright (flakes only).
        var features = new FeatureBuffer(width, height);
        for (var index = 0; index < image.Length; index++)
        {
            features.Depth[index] = 5f;
            features.Normal[index] = index % width < width / 2 ? Vector3.UnitZ : Vector3.UnitX;
        }
        var snow = ImageProcessor.ToBgra32(image, width, height, 1, plain with { Snow = 1 }, () => features);
        double Mean(byte[] pixels, byte[] reference, int from, int to)
        {
            double total = 0;
            var count = 0;
            for (var y = 0; y < height; y++)
                for (var x = from; x < to; x++) { total += Light(pixels, y * width + x) - Light(reference, y * width + x); count++; }
            return total / count;
        }
        var covered = Mean(snow, neutral, 0, width / 2);
        var standing = Mean(snow, neutral, width / 2, width);
        check(covered > 120 && covered > standing + 80,
            $"Snow whitens surfaces that face up far more than upright ones ({covered:0} against {standing:0})");

        // The same effects on the real picture of the cobogó house.
        var house = BreezeBlockHouseShowcase.Create();
        var scene = BuildingMesher.Build(house, includeGround: true);
        var (min, max) = BuildingMesher.Build(house).Bounds();
        var tracer = new PathTracer(scene, 120, 75, RenderCamera.FrameBounds(min, max));
        tracer.RenderPass();
        var captured = tracer.CaptureFeatures();
        var baseline = tracer.ToBgra32(finish: plain);
        var snowy = tracer.ToBgra32(finish: plain with { Snow = 1 });
        double upGain = 0, sideGain = 0;
        int upCount = 0, sideCount = 0;
        for (var pixel = 0; pixel < 120 * 75; pixel++)
        {
            if (float.IsInfinity(captured.Depth[pixel])) continue;
            var gain = Light(snowy, pixel) - Light(baseline, pixel);
            if (captured.Normal[pixel].Z > 0.9f) { upGain += gain; upCount++; }
            else if (MathF.Abs(captured.Normal[pixel].Z) < 0.2f) { sideGain += gain; sideCount++; }
        }
        check(upCount > 0 && sideCount > 0 && upGain / upCount > sideGain / sideCount + 60,
            $"On the cobogó house, snow whitens the ground and roofs more than the walls ({upGain / Math.Max(1, upCount):0} against {sideGain / Math.Max(1, sideCount):0})");
        var rainy = tracer.ToBgra32(finish: plain with { Rain = 0.8f });
        long baseSum = 0, rainSum = 0;
        for (var pixel = 0; pixel < 120 * 75; pixel++) { baseSum += Light(baseline, pixel); rainSum += Light(rainy, pixel); }
        check(rainSum < baseSum, "Rain makes the house's light greyer and dimmer");

        // The choice is part of the project and of a photo's effects.
        var setup = new RenderSetup { Rain = 0.4, Snow = 0.7 };
        setup.Validate();
        var copied = new RenderSetup().WithEffectsFrom(setup);
        check(Math.Abs(copied.Rain - 0.4) < 1e-9 && Math.Abs(copied.Snow - 0.7) < 1e-9, "A photo's effects carry the rain and snow");
        var rejected = false;
        try { new RenderSetup { Snow = 1.5 }.Validate(); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "Weather outside 0 to 1 is rejected when a setup is validated");
        check(new[] { "rain", "snow" }.All(id => RenderFx.Find(id) is { Planned: false, Category: FxCategory.Weather, Removable: true }),
            "Rain and snow are usable effects of the Weather group");
        check(FxStyle.TryParse("""{"Version":1,"Name":"x","Effects":["rain","snow"],"Values":{"RainSlider":0.5,"SnowSlider":0.4}}""") is not null,
            "A shared style can carry rain and snow");
    }
}
