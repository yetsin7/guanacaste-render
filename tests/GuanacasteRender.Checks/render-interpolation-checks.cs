using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Navigation;
using Guanacaste3D.Core.Rendering;

/// <summary>Checks the frames made between rendered ones: they must look like rendered frames, with parallax, not like a blend.</summary>
static class RenderInterpolationChecks
{
    public static void Run(Action<bool, string> check)
    {
        // A street: ground, a near wall to the left and a far wall behind, so a camera that slides sideways shows parallax.
        var mesh = new SceneMesh();
        mesh.AddQuad(new(-60, -20, 0), new(60, -20, 0), new(60, 60, 0), new(-60, 60, 0), SurfaceKind.Ground);
        mesh.AddQuad(new(-3, 3, 0), new(0, 3, 0), new(0, 3, 4), new(-3, 3, 4), SurfaceKind.WallBrick);   // near, left of the axis
        mesh.AddQuad(new(-12, 30, 0), new(12, 30, 0), new(12, 30, 9), new(-12, 30, 9), SurfaceKind.WallBlock); // far
        mesh.AddQuad(new(1.5f, 9, 0), new(3.5f, 9, 0), new(3.5f, 9, 2.5f), new(1.5f, 9, 2.5f), SurfaceKind.Roof); // middle
        var scene = new RenderSceneBuilder().AddBuilding(mesh).Build();
        const int width = 128, height = 80, fps = 8;
        {
            // Moving a frame into its own camera must give the frame back: this is what checks the unprojection and the depth.
            var cameraA = new RenderCamera(new Vector3(-1.5f, -6, 1.7f), new Vector3(0, 12, 1.4f), 55) with { CorrectVerticals = false };
            var tracerA = new PathTracer(scene, width, height, cameraA, new RenderSettings { Seed = 3, MaxThreads = 0 });
            for (var pass = 0; pass < 16; pass++) tracerA.RenderPass();
            var keyA = new KeyFrame(tracerA.LinearImage(denoise: true), FrameInterpolator.Copy(tracerA.CaptureFeatures()), tracerA.Projection()!, tracerA.BaseExposure);
            var again = FrameInterpolator.Between(keyA, keyA, 0.5f, keyA.View, width, height);
            double sum = 0;
            for (var i = 0; i < again.Length; i++) sum += Vector3.Distance(again[i], keyA.Radiance[i]);
            check(sum / again.Length < 1e-4, $"A frame moved into its own camera comes back unchanged (mean difference {sum / again.Length:0.0000}), so the unprojection and the depth agree with the renderer");
        }
        var path = new CameraPath();
        path.Add(new CameraMark(new Vector3(-1.5f, -6, 1.7f), new Vector3(0, 12, 1.4f), 55, 0));
        path.Add(new CameraMark(new Vector3(1.5f, -6, 1.7f), new Vector3(0, 12, 1.4f), 55, 1));
        var settings = new RenderSettings { Seed = 3, MaxThreads = 0 };
        var folder = Path.Combine(Path.GetTempPath(), "guanacaste-interp-" + Guid.NewGuid().ToString("N"));
        try
        {
            TourRenderer.RenderAsync(scene, path, settings, width, height, fps, 24, new ImageFinish(), Path.Combine(folder, "all"), null, CancellationToken.None)
                .GetAwaiter().GetResult();
            TourRenderer.RenderAsync(scene, path, settings, width, height, fps, 24, new ImageFinish(), Path.Combine(folder, "keys"), null, CancellationToken.None,
                keyframesPerSecond: 2).GetAwaiter().GetResult();
            var all = Directory.GetFiles(Path.Combine(folder, "all"), "frame_*.png").Order().ToArray();
            var keys = Directory.GetFiles(Path.Combine(folder, "keys"), "frame_*.png").Order().ToArray();
            check(all.Length == 9 && keys.Length == 9, $"With two real frames a second a movie of {fps} frames a second still has every frame ({keys.Length} of {all.Length})");

            // The light traced at half size and brought to full size with the real surfaces: nearly the same picture, sky included, in a quarter of the paths.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            TourRenderer.RenderAsync(scene, path, settings, width, height, fps, 24, new ImageFinish(), Path.Combine(folder, "half"), null, CancellationToken.None,
                lightingDivisor: 2).GetAwaiter().GetResult();
            var half = Directory.GetFiles(Path.Combine(folder, "half"), "frame_*.png").Order().ToArray();
            double halfError = 0;
            double skyMean = 0;
            for (var frame = 0; frame < 9; frame++)
            {
                var a = PngCodec.Load(half[frame]).Rgb;
                var b = PngCodec.Load(all[frame]).Rgb;
                double sum = 0;
                for (var i = 0; i < a.Length; i++) sum += Math.Abs(a[i] - b[i]);
                halfError += sum / a.Length / 9;
                for (var i = 0; i < width * 3 * 4; i++) skyMean += a[i] / (double)(width * 3 * 4) / 9;   // the top rows are sky
            }
            check(halfError < 4 && skyMean > 60,
                $"Light traced at half size and upscaled with the real surfaces gives nearly the same movie (error {halfError:0.00} of 255) with the sky kept (top rows {skyMean:0})");

            byte[] Pixels(string file) => PngCodec.Load(file).Rgb;
            double Error(byte[] a, byte[] b)
            {
                double sum = 0;
                for (var i = 0; i < a.Length; i++) sum += Math.Abs(a[i] - b[i]);
                return sum / a.Length;
            }
            // Keys are frames 0, 4 and 8. Frame 2 is half way between the first two: compare it with the same frame rendered in full,
            // and with the plain blend of its neighbours that a cheap interpolation would give.
            var (first, last) = (Pixels(keys[0]), Pixels(keys[4]));
            var blend = new byte[first.Length];
            for (var i = 0; i < blend.Length; i++) blend[i] = (byte)((first[i] + last[i] + 1) / 2);
            double made = 0, blended = 0, copied = 0;
            foreach (var frame in new[] { 1, 2, 3 })
            {
                var truth = Pixels(all[frame]);
                made += Error(Pixels(keys[frame]), truth);
                var mix = new byte[first.Length];
                var along = frame / 4f;
                for (var i = 0; i < mix.Length; i++) mix[i] = (byte)Math.Round(first[i] * (1 - along) + last[i] * along);
                blended += Error(mix, truth);
                copied += Error(first, truth);
            }
            check(made < blended * 0.7 && made < copied * 0.6 && Error(Pixels(keys[0]), Pixels(all[0])) < 6 && Error(Pixels(keys[4]), Pixels(all[4])) < 6,
                $"Frames made between two rendered ones are closer to the real render than a blend of their neighbours (error {made / 3:0.00} against {blended / 3:0.00} for a blend and {copied / 3:0.00} for repeating a frame)");
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
