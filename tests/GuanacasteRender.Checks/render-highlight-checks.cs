using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

/// <summary>Checks the Highlight effect: a chosen material keeps its colour, gets an outline, and the rest is muted.</summary>
static class RenderHighlightChecks
{
    public static void Run(Action<bool, string> check)
    {
        // A synthetic picture: left half is "Pared", right half is "Piso". Highlighting "Pared" must leave it untouched.
        const int width = 64, height = 32;
        var image = new Vector3[width * height];
        Array.Fill(image, new Vector3(0.35f, 0.25f, 0.15f));
        var features = new FeatureBuffer(width, height) { MaterialNames = ["Pared", "Piso"] };
        for (var index = 0; index < features.Material.Length; index++) features.Material[index] = (ushort)(index % width < width / 2 ? 0 : 1);
        var plain = new ImageFinish { Dither = false, Bloom = 0, Vignette = 0 };
        var neutral = ImageProcessor.ToBgra32(image, width, height, 1, plain, () => features);
        var lit = ImageProcessor.ToBgra32(image, width, height, 1, plain with { HighlightMaterial = "Pared", HighlightDim = 1 }, () => features);
        int Channel(byte[] pixels, int x, int y, int channel) => pixels[(y * width + x) * 4 + channel];
        int Light(byte[] pixels, int x, int y) => Channel(pixels, x, y, 0) + Channel(pixels, x, y, 1) + Channel(pixels, x, y, 2);

        check(Enumerable.Range(0, height).All(y => Enumerable.Range(0, width / 2).All(x => Light(lit, x, y) == Light(neutral, x, y))),
            "Highlight leaves every pixel of the chosen material exactly as it was");
        check(Light(lit, width - 4, 10) < Light(neutral, width - 4, 10) * 0.8,
            "Highlight mutes the surfaces that are not the chosen material");
        check(Channel(lit, width / 2, 10, 2) > Channel(lit, width - 4, 10, 2) + 40,
            "Highlight paints a bright outline right next to the chosen material");
        check(ImageProcessor.ToBgra32(image, width, height, 1, plain with { HighlightMaterial = "No existe", HighlightDim = 1 }, () => features)
                  .SequenceEqual(neutral) &&
              ImageProcessor.ToBgra32(image, width, height, 1, plain with { HighlightMaterial = "Pared", HighlightDim = 0 }, () => features)
                  .SequenceEqual(neutral),
            "Highlight does nothing for an unknown material or with the dimming at zero");

        // The same effect on the real picture of the cobogó house.
        var house = BreezeBlockHouseShowcase.Create();
        var scene = BuildingMesher.Build(house, includeGround: true);
        var (min, max) = BuildingMesher.Build(house).Bounds();
        var tracer = new PathTracer(scene, 96, 60, RenderCamera.FrameBounds(min, max));
        tracer.RenderPass();
        var captured = tracer.CaptureFeatures();
        var counts = new Dictionary<string, int>();
        foreach (var id in captured.Material)
            if (id != FeatureBuffer.NoMaterial) counts[captured.MaterialNames[id]] = counts.GetValueOrDefault(captured.MaterialNames[id]) + 1;
        check(counts.Count >= 2, $"The cobogó house shows at least two materials to the camera ({counts.Count})");
        if (counts.Count < 2) return;

        var target = counts.OrderByDescending(pair => pair.Value).First().Key;
        var baseline = tracer.ToBgra32(finish: plain);
        var picked = tracer.ToBgra32(finish: plain with { HighlightMaterial = target, HighlightDim = 0.8f });
        long insideDelta = 0, outsideBefore = 0, outsideAfter = 0;
        // The outline brightens the ring around the material, so the muting is measured away from it.
        bool TouchesTarget(int pixel)
        {
            for (var dy = -2; dy <= 2; dy++)
                for (var dx = -2; dx <= 2; dx++)
                {
                    int x = pixel % 96 + dx, y = pixel / 96 + dy;
                    if (x < 0 || y < 0 || x >= 96 || y >= 60) continue;
                    var id = captured.Material[y * 96 + x];
                    if (id != FeatureBuffer.NoMaterial && captured.MaterialNames[id] == target) return true;
                }
            return false;
        }
        for (var pixel = 0; pixel < 96 * 60; pixel++)
        {
            var id = captured.Material[pixel];
            var same = id != FeatureBuffer.NoMaterial && captured.MaterialNames[id] == target;
            long before = baseline[pixel * 4] + baseline[pixel * 4 + 1] + baseline[pixel * 4 + 2];
            long after = picked[pixel * 4] + picked[pixel * 4 + 1] + picked[pixel * 4 + 2];
            if (same) insideDelta += Math.Abs(after - before);
            else if (!TouchesTarget(pixel)) { outsideBefore += before; outsideAfter += after; }
        }
        check(insideDelta == 0 && outsideAfter < outsideBefore * 0.8,
            $"On the cobogó house, highlighting \"{target}\" leaves it alone and mutes the rest ({outsideAfter} < {outsideBefore})");

        // The choice is part of the project and of a photo's effects.
        var setup = new RenderSetup { HighlightMaterial = target, HighlightDim = 0.6 };
        setup.Validate();
        var copied = new RenderSetup().WithEffectsFrom(setup);
        check(copied.HighlightMaterial == target && Math.Abs(copied.HighlightDim - 0.6) < 1e-9,
            "A photo's effects carry the highlighted material");
        var rejected = false;
        try { new RenderSetup { HighlightDim = 2 }.Validate(); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "A highlight dimming outside 0 to 1 is rejected when a setup is validated");
        var effect = RenderFx.Find("highlight");
        check(effect is { Planned: false } && effect.Controls.Any(control => control.Kind == FxControlKind.Choice),
            "The Highlight effect is usable from the effect picker");
    }
}
