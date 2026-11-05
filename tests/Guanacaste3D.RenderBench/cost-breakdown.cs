using System.Diagnostics;
using Guanacaste3D.Core.Rendering;

/// <summary>Measures how the cost of one pass splits between feature groups, by switching them off one at a time.</summary>
static class CostBreakdown
{
    public static void Run()
    {
        foreach (var scene in new[] { BenchmarkScenes.HouseExterior(), BenchmarkScenes.DaylitRoom() })
        {
            Console.WriteLine($"\n== {scene.Name}");
            var stock = new RenderSceneBuilder().AddBuilding(scene.Mesh).Build();
            var plain = new RenderSceneBuilder().AddBuilding(scene.Mesh,
                kind => MaterialLibrary.ForKind(kind) with { Pattern = SurfacePattern.None }).Build();
            var matte = new RenderSceneBuilder().AddBuilding(scene.Mesh,
                kind => MaterialLibrary.ForKind(kind) with { Pattern = SurfacePattern.None, Roughness = 1f, Metallic = 0 }).Build();
            Measure("default                        ", stock, scene, scene.Settings);
            Measure("no procedural patterns         ", plain, scene, scene.Settings);
            Measure("no patterns, fully rough       ", matte, scene, scene.Settings);
            Measure("sky portals off                ", stock, scene, scene.Settings with { SkyPortals = false });
            Measure("bounces 0 (direct light only)  ", stock, scene, scene.Settings with { MaxBounces = 0 });
            Measure("bounces 1                      ", stock, scene, scene.Settings with { MaxBounces = 1 });
            Measure("sun off                        ", stock, scene, scene.Settings with { SunIntensity = 0 });
            Measure("1 thread                       ", stock, scene, scene.Settings with { MaxThreads = 1 }, width: 160);
            Measure("16 threads (same 160px)        ", stock, scene, scene.Settings with { MaxThreads = 16 }, width: 160);
        }
    }

    private static void Measure(string name, RenderScene scene, BenchmarkScene source, RenderSettings settings, int width = 480)
    {
        var tracer = new PathTracer(scene, width, width * 9 / 16, source.Camera, settings);
        if (name.StartsWith("default")) Console.WriteLine($"  (sky portals found: {tracer.SkyPortalCount})");
        tracer.RenderPass(); // warm up the JIT and caches
        // The minimum over several short batches is immune to bursts of load from other programs.
        var best = double.MaxValue;
        var rayDelta = 0L;
        for (var batch = 0; batch < 7; batch++)
        {
            var before = tracer.RayCount;
            var timer = Stopwatch.StartNew();
            for (var i = 0; i < 3; i++) tracer.RenderPass();
            var ms = timer.Elapsed.TotalMilliseconds / 3;
            if (ms < best) { best = ms; rayDelta = (tracer.RayCount - before) / 3; }
        }
        var ms2 = best;
        var perPass = (double)rayDelta;
        Console.WriteLine($"  {name} {ms2,7:0.0} ms/pass · {perPass / ms2 / 1000:0.0} Mray/s · {perPass / (tracer.Width * tracer.Height):0.00} rays/pixel");
    }
}
