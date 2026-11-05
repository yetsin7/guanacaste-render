using System.Diagnostics;
using System.Numerics;
using Guanacaste3D.Core.Rendering;

/// <summary>One renderer configuration under test.</summary>
sealed record Variant(string Name, Func<RenderSettings, RenderSettings> Configure, bool Denoise = true);

/// <summary>
/// Equal-time A/B comparison. Every variant renders the same scene until each time budget is reached and is then
/// scored against one converged reference, so "faster" and "better" can be judged together. Only render time is
/// charged; scoring and the reference run are excluded.
/// </summary>
static class AbExperiment
{
    public static void Run(BenchmarkScene scene, int width, int referencePasses, Variant reference, Variant[] variants,
        double[] seconds, string? outputFolder)
    {
        var height = width * 9 / 16;
        var builder = new RenderSceneBuilder().AddBuilding(scene.Mesh);
        foreach (var light in scene.Lights) builder.AddLight(light);
        var renderScene = builder.Build();
        Console.WriteLine($"\n== A/B on {scene.Name} · {width}x{height} · reference: {reference.Name} {referencePasses} spp");
        var truthTracer = new PathTracer(renderScene, width, height, scene.Camera, reference.Configure(scene.Settings with { Seed = 99 }));
        for (var i = 0; i < referencePasses; i++) truthTracer.RenderPass();
        var truth = Display(truthTracer, denoise: false);
        if (outputFolder is not null) Save(Path.Combine(outputFolder, $"{scene.Name}-reference.png"), truthTracer, false);

        Console.WriteLine("  variant                      time s |  spp   rays/px | raw: PSNR  SSIM   dE | denoised: PSNR  SSIM   dE");
        foreach (var variant in variants)
        {
            var tracer = new PathTracer(renderScene, width, height, scene.Camera, variant.Configure(scene.Settings with { Seed = 7 }));
            tracer.RenderPass(); // warm-up pass is discarded below by resetting
            tracer.Reset();
            var render = new Stopwatch();
            var rays0 = tracer.RayCount;
            foreach (var budget in seconds)
            {
                render.Start();
                while (render.Elapsed.TotalSeconds < budget) tracer.RenderPass();
                render.Stop();
                var raw = ImageMetrics.Compare(Display(tracer, false), truth, width, height);
                var clean = ImageMetrics.Compare(Display(tracer, true), truth, width, height);
                Console.WriteLine($"  {variant.Name,-28} {render.Elapsed.TotalSeconds,5:0.00} | {tracer.AverageSamplesPerPixel,5:0.0} {(double)(tracer.RayCount - rays0) / (width * height) / Math.Max(1, tracer.AverageSamplesPerPixel),6:0.0}  | " +
                                  $"{raw.Psnr,10:0.0} {raw.Ssim,6:0.000} {raw.MeanDeltaE,4:0.0} | {clean.Psnr,14:0.0} {clean.Ssim,6:0.000} {clean.MeanDeltaE,4:0.0}");
            }
            if (outputFolder is not null) Save(Path.Combine(outputFolder, $"{scene.Name}-{Slug(variant.Name)}-{seconds[^1]:0}s.png"), tracer, true);
        }
    }

    /// <summary>
    /// Paired comparison at equal sample counts. Sampling is deterministic per pixel, so every variant sees exactly the
    /// same samples and any difference is caused by the variant alone, independent of how busy the machine is. Use it
    /// for post-processing such as denoising; use <see cref="Run"/> when the variants change the cost of a sample.
    /// </summary>
    public static void RunEqualSamples(BenchmarkScene scene, int width, int referencePasses, Variant reference,
        Variant[] variants, int[] samples)
    {
        var height = width * 9 / 16;
        var builder = new RenderSceneBuilder().AddBuilding(scene.Mesh);
        foreach (var light in scene.Lights) builder.AddLight(light);
        var renderScene = builder.Build();
        Console.WriteLine($"\n== Equal samples on {scene.Name} · {width}x{height} · reference {referencePasses} spp");
        var truthTracer = new PathTracer(renderScene, width, height, scene.Camera, reference.Configure(scene.Settings with { Seed = 99 }));
        for (var i = 0; i < referencePasses; i++) truthTracer.RenderPass();
        var truth = Display(truthTracer, denoise: false);
        Console.WriteLine("  variant                          " + string.Join(" | ", samples.Select(n => $"{n,3} spp PSNR  SSIM")));
        var tracers = variants.Select(v => new PathTracer(renderScene, width, height, scene.Camera, v.Configure(scene.Settings with { Seed = 7 }))).ToArray();
        var rows = variants.Select(_ => new List<string>()).ToArray();
        var rawRow = new List<string>();
        var done = 0;
        foreach (var target in samples)
        {
            for (var v = 0; v < tracers.Length; v++)
            {
                while (tracers[v].SampleCount < target) tracers[v].RenderPass();
                var q = ImageMetrics.Compare(Display(tracers[v], true), truth, width, height);
                rows[v].Add($"{q.Psnr,13:0.00} {q.Ssim,6:0.000}");
            }
            var raw = ImageMetrics.Compare(Display(tracers[0], false), truth, width, height);
            rawRow.Add($"{raw.Psnr,13:0.00} {raw.Ssim,6:0.000}");
            done++;
        }
        Console.WriteLine($"  {"raw (no filter)",-32} " + string.Join(" | ", rawRow));
        for (var v = 0; v < variants.Length; v++)
            Console.WriteLine($"  {variants[v].Name,-32} " + string.Join(" | ", rows[v]));
    }

    /// <summary>
    /// Compares variants at equal numbers of traced rays per pixel, which tracks work done rather than wall-clock time
    /// and so is stable on a busy machine. Training work (for example filling a cache) is included in the count.
    /// </summary>
    public static void RunEqualRays(BenchmarkScene scene, int width, int referencePasses, Variant reference, Variant[] variants,
        double[] raysPerPixel)
    {
        var height = width * 9 / 16;
        var builder = new RenderSceneBuilder().AddBuilding(scene.Mesh);
        foreach (var light in scene.Lights) builder.AddLight(light);
        var renderScene = builder.Build();
        Console.WriteLine($"\n== Equal rays on {scene.Name} · {width}x{height} · reference {referencePasses} spp ({reference.Name})");
        var truthTracer = new PathTracer(renderScene, width, height, scene.Camera, reference.Configure(scene.Settings with { Seed = 99 }));
        for (var i = 0; i < referencePasses; i++) truthTracer.RenderPass();
        var truth = Display(truthTracer, denoise: false);
        Console.WriteLine("  variant                      " + string.Join(" | ", raysPerPixel.Select(r => $"{r,5:0} rays/px PSNR  SSIM")));
        foreach (var variant in variants)
        {
            var tracer = new PathTracer(renderScene, width, height, scene.Camera, variant.Configure(scene.Settings with { Seed = 7 }));
            var cells = new List<string>();
            foreach (var budget in raysPerPixel)
            {
                while ((double)tracer.RayCount / (width * height) < budget) tracer.RenderPass();
                var q = ImageMetrics.Compare(Display(tracer, variant.Denoise), truth, width, height);
                cells.Add($"{tracer.AverageSamplesPerPixel,5:0}spp {q.Psnr,6:0.00} {q.Ssim,6:0.000}");
            }
            Console.WriteLine($"  {variant.Name,-28} " + string.Join(" | ", cells) + (tracer.CacheHitRate > 0 ? $"   [cache hit {tracer.CacheHitRate:P0}]" : ""));
        }
    }

    private static string Slug(string name) => new(name.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());

    private static Vector3[] Display(PathTracer tracer, bool denoise) =>
        RasterImage.FromBgra32(tracer.Width, tracer.Height, tracer.ToBgra32(denoise)).ToDisplay();

    private static void Save(string path, PathTracer tracer, bool denoise) =>
        PngCodec.Save(path, RasterImage.FromBgra32(tracer.Width, tracer.Height, tracer.ToBgra32(denoise)));
}
