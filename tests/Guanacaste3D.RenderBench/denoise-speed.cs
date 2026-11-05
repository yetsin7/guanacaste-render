using System.Diagnostics;
using Guanacaste3D.Core.Rendering;

/// <summary>Times the denoiser and the whole "get a displayable frame" step at several image sizes.</summary>
static class DenoiseSpeed
{
    public static void Run()
    {
        var scene = BenchmarkScenes.DaylitRoom();
        var render = new RenderSceneBuilder().AddBuilding(scene.Mesh).Build();
        foreach (var (width, height) in new[] { (320, 180), (960, 540), (1920, 1080) })
        {
            var tracer = new PathTracer(render, width, height, scene.Camera, scene.Settings);
            for (var i = 0; i < 3; i++) tracer.RenderPass();
            tracer.LinearImage(true); // features and JIT
            var best = double.MaxValue;
            for (var repeat = 0; repeat < 5; repeat++)
            {
                var timer = Stopwatch.StartNew();
                tracer.LinearImage(true);
                best = Math.Min(best, timer.Elapsed.TotalMilliseconds);
            }
            var toneBest = double.MaxValue;
            for (var repeat = 0; repeat < 3; repeat++)
            {
                var timer = Stopwatch.StartNew();
                tracer.ToBgra32(true, new ImageFinish { AutoExposure = true });
                toneBest = Math.Min(toneBest, timer.Elapsed.TotalMilliseconds);
            }
            var allocated = GC.GetTotalAllocatedBytes(precise: true);
            tracer.LinearImage(true);
            allocated = GC.GetTotalAllocatedBytes(precise: true) - allocated;
            Console.WriteLine($"{width}x{height}: allocates {allocated / 1048576.0,7:0.0} MB per denoised image · denoise {best,6:0.0} ms ({best * 1000 / (width * height):0.00} us/px) · full displayable frame {toneBest,6:0.0} ms");
        }
    }
}
