using System.Numerics;
using Guanacaste3D.Core.Rendering;

/// <summary>
/// The case a world-space cache exists for: the camera moves to a new view. With a warm cache the first rays after the
/// move already see light that was computed from the old viewpoint; an image-space accumulation starts from nothing.
/// Quality is measured against a converged deep reference rendered from the new viewpoint, at equal rays since the move.
/// </summary>
static class CacheMoveExperiment
{
    public static void Run(int width, int referencePasses)
    {
        var scene = BenchmarkScenes.DaylitRoom();
        var height = width * 9 / 16;
        var render = new RenderSceneBuilder().AddBuilding(scene.Mesh).Build();
        var first = scene.Camera;
        var second = new RenderCamera(new(0.8f, 3.4f, 1.5f), new(3.8f, 0.4f, 1.0f), 75);

        var reference = new PathTracer(render, width, height, second, scene.Settings with { MaxBounces = 16, Seed = 99 });
        for (var i = 0; i < referencePasses; i++) reference.RenderPass();
        var truth = Display(reference, false);

        Console.WriteLine($"\n== Camera move on {scene.Name} · {width}x{height}; quality at the new view after N rays/px since the move");
        Console.WriteLine("  variant                                     " + string.Join(" | ", new[] { 8, 15, 30, 60, 120 }.Select(r => $"{r,4} rays/px PSNR  SSIM")));
        foreach (var (name, warmRays, cache) in new[]
        {
            ("no cache (6 bounces), fresh start", 0, false),
            ("cache, cold start", 0, true),
            ("cache warmed 60 rays/px at the old view", 60, true),
            ("cache warmed 240 rays/px at the old view", 240, true),
        })
        {
            var settings = scene.Settings with { UseRadianceCache = cache, MaxBounces = cache ? 3 : 6, Seed = 7 };
            var tracer = new PathTracer(render, width, height, first, settings);
            while ((double)tracer.RayCount / (width * height) < warmRays) tracer.RenderPass();
            tracer.SetCamera(second);
            var start = tracer.RayCount;
            var cells = new List<string>();
            foreach (var budget in new[] { 8, 15, 30, 60, 120 })
            {
                while ((double)(tracer.RayCount - start) / (width * height) < budget) tracer.RenderPass();
                var q = ImageMetrics.Compare(Display(tracer, true), truth, width, height);
                cells.Add($"{tracer.AverageSamplesPerPixel,4:0}spp {q.Psnr,6:0.00} {q.Ssim,6:0.000}");
            }
            Console.WriteLine($"  {name,-43} " + string.Join(" | ", cells));
        }
    }

    private static Vector3[] Display(PathTracer tracer, bool denoise) =>
        RasterImage.FromBgra32(tracer.Width, tracer.Height, tracer.ToBgra32(denoise)).ToDisplay();
}
