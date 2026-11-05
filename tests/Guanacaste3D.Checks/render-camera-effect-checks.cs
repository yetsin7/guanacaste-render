using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

/// <summary>Checks lens flare, light rays and tilt-shift, and that depth of field is an effect of the picker.</summary>
static class RenderCameraEffectChecks
{
    public static void Run(Action<bool, string> check)
    {
        const int width = 96, height = 60;
        var plain = new ImageFinish { Dither = false, Bloom = 0, Vignette = 0 };
        int Light(byte[] pixels, int x, int y) => pixels[(y * width + x) * 4] + pixels[(y * width + x) * 4 + 1] + pixels[(y * width + x) * 4 + 2];

        // A dim picture with a sun in the upper right.
        var sky = new Vector3[width * height];
        Array.Fill(sky, new Vector3(0.15f));
        for (var y = 14; y <= 22; y++)
            for (var x = 66; x <= 74; x++)
                if ((x - 70) * (x - 70) + (y - 18) * (y - 18) <= 16) sky[y * width + x] = new Vector3(80f);
        var neutral = ImageProcessor.ToBgra32(sky, width, height, 1, plain);

        // Flare: the first ghost sits 35 % of the way from the sun to the centre of the picture.
        var flare = ImageProcessor.ToBgra32(sky, width, height, 1, plain with { LensFlare = 1 });
        var (ghostX, ghostY) = ((int)Math.Round(70 + (48 - 70) * 0.35), (int)Math.Round(18 + (30 - 18) * 0.35));
        check(Light(flare, ghostX, ghostY) > Light(neutral, ghostX, ghostY) + 8 && Light(flare, 3, 55) == Light(neutral, 3, 55),
            $"Lens flare puts a ghost of the sun on the line through the centre and leaves the far corner alone ({Light(neutral, ghostX, ghostY)} -> {Light(flare, ghostX, ghostY)})");

        // Rays: pixels on the way out from the sun brighten, more than a far corner does.
        var rays = ImageProcessor.ToBgra32(sky, width, height, 1, plain with { LightRays = 1 });
        int Gain(int x, int y) => Light(rays, x, y) - Light(neutral, x, y);
        check(Gain(58, 26) > 8 && Gain(58, 26) > Gain(3, 55) + 4, $"Light rays brighten what lies between the viewer and the sun ({Gain(58, 26)} near, {Gain(3, 55)} far)");

        // No bright light in view: flare and rays do nothing, as a real lens would show nothing.
        var dull = new Vector3[width * height];
        Array.Fill(dull, new Vector3(0.4f));
        var dullNeutral = ImageProcessor.ToBgra32(dull, width, height, 1, plain);
        check(ImageProcessor.ToBgra32(dull, width, height, 1, plain with { LensFlare = 1, LightRays = 1 }).SequenceEqual(dullNeutral) &&
              ImageProcessor.ToBgra32(sky, width, height, 1, plain with { LensFlare = 0, LightRays = 0, TiltShift = 0 }).SequenceEqual(neutral),
            "Without a bright light in view, or at zero, flare and rays leave the picture unchanged");

        // Tilt-shift: fine detail survives in the band and is blurred above and below it.
        var detail = new Vector3[width * height];
        for (var index = 0; index < detail.Length; index++) detail[index] = new Vector3(((index % width) + (index / width)) % 2 == 0 ? 0.1f : 0.4f);
        var sharp = ImageProcessor.ToBgra32(detail, width, height, 1, plain);
        var tilted = ImageProcessor.ToBgra32(detail, width, height, 1, plain with { TiltShift = 1 });
        int Contrast(byte[] pixels, int y) => Enumerable.Range(0, width - 1).Sum(x => Math.Abs(Light(pixels, x, y) - Light(pixels, x + 1, y)));
        check(Contrast(tilted, 30) > Contrast(sharp, 30) * 0.9 && Contrast(tilted, 2) < Contrast(sharp, 2) * 0.35 && Contrast(tilted, 57) < Contrast(sharp, 57) * 0.35,
            $"Tilt-shift keeps its band sharp and blurs above and below it (centre {Contrast(tilted, 30)}/{Contrast(sharp, 30)}, top {Contrast(tilted, 2)}/{Contrast(sharp, 2)})");
        var moved = ImageProcessor.ToBgra32(detail, width, height, 1, plain with { TiltShift = 1, TiltShiftPosition = -1 });
        check(Contrast(moved, 30 - 12) > Contrast(moved, 30 + 22),
            "Moving the band up keeps the upper rows sharp and blurs the lower ones");

        // The cobogó house: the same effects on a real picture.
        var house = BreezeBlockHouseShowcase.Create();
        var (min, max) = BuildingMesher.Build(house).Bounds();
        var tracer = new PathTracer(BuildingMesher.Build(house, includeGround: true), 128, 80, RenderCamera.FrameBounds(min, max));
        tracer.RenderPass();
        var baseline = tracer.ToBgra32(finish: plain);
        var miniature = tracer.ToBgra32(finish: plain with { TiltShift = 1 });
        int Busy(byte[] pixels, int from, int to) => Enumerable.Range(from, to - from).Sum(y =>
            Enumerable.Range(0, 127).Sum(x => Math.Abs(pixels[(y * 128 + x) * 4 + 1] - pixels[(y * 128 + x + 1) * 4 + 1])));
        check(Busy(miniature, 0, 10) <= Busy(baseline, 0, 10) && Busy(miniature, 36, 44) >= Busy(baseline, 36, 44) * 0.8,
            "On the cobogó house tilt-shift blurs the top of the picture and keeps the middle");
        var everything = tracer.ToBgra32(finish: plain with { LensFlare = 1, LightRays = 1, TiltShift = 1, TiltShiftPosition = 0.5f });
        check(everything.Length == baseline.Length && Enumerable.Range(0, 128 * 80).All(pixel => everything[pixel * 4 + 3] == 255),
            "All three camera effects together give a valid picture");

        // The choices are part of the project, of a photo's effects, and of the picker.
        var setup = new RenderSetup { LensFlare = 0.4, LightRays = 0.5, TiltShift = 0.6, TiltShiftPosition = -0.3 };
        setup.Validate();
        var copied = new RenderSetup().WithEffectsFrom(setup);
        check(copied.LensFlare == 0.4 && copied.LightRays == 0.5 && copied.TiltShift == 0.6 && copied.TiltShiftPosition == -0.3,
            "A photo's effects carry the camera effects");
        var rejected = 0;
        foreach (var bad in new[] { new RenderSetup { LensFlare = 2 }, new RenderSetup { TiltShiftPosition = 1.5 }, new RenderSetup { LightRays = double.NaN } })
            try { bad.Validate(); } catch (InvalidDataException) { rejected++; }
        check(rejected == 3, "Camera effects outside their range are rejected when a setup is validated");
        check(new[] { "dof", "tiltshift", "lensflare", "lightrays" }.All(id => RenderFx.Find(id) is { Planned: false, Removable: true }) &&
              RenderFx.Find("dof")!.Controls.Select(control => control.Key).SequenceEqual(["DepthOfFieldSlider", "FocusSlider"]) &&
              RenderFx.Find("lightrays")!.Category == FxCategory.Sun,
            "Depth of field, tilt-shift, lens flare and light rays are usable effects in the picker");
    }
}
