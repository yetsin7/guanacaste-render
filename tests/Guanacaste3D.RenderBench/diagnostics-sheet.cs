using System.Numerics;
using Guanacaste3D.Core.Rendering;

/// <summary>Writes every diagnostic view of both benchmark scenes as PNG files, for looking at them.</summary>
static class DiagnosticsSheet
{
    public static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var scene in new[] { BenchmarkScenes.HouseExterior(), BenchmarkScenes.DaylitRoom(), BenchmarkScenes.ManyLights() })
        {
            var builder = new RenderSceneBuilder().AddBuilding(scene.Mesh);
            foreach (var light in scene.Lights) builder.AddLight(light);
            var tracer = new PathTracer(builder.Build(), 320, 180, scene.Camera,
                scene.Settings with { UseRadianceCache = true, AdaptiveSampling = true, AdaptiveTolerance = 0.03f });
            for (var i = 0; i < 40; i++) tracer.RenderPass();
            foreach (var view in Enum.GetValues<DiagnosticView>())
                PngCodec.Save(Path.Combine(folder, $"diag-{scene.Name}-{view}.png"), RasterImage.FromBgra32(320, 180, tracer.DiagnosticBgra32(view)));
            PngCodec.Save(Path.Combine(folder, $"diag-{scene.Name}-final.png"), RasterImage.FromBgra32(320, 180, tracer.ToBgra32(true)));
            var t = tracer.Telemetry();
            Console.WriteLine($"{scene.Name}: {t.Triangles} tris, {t.Lights} lights, {t.SkyPortals} portals, {t.AverageSamplesPerPixel:0.0} spp, {t.MegaRaysPerSecond:0.0} Mray/s, " +
                              $"pass {t.LastPassMilliseconds:0} ms, denoise {t.LastDenoiseMilliseconds:0} ms, build {t.AccelerationBuildMilliseconds:0} ms, cache {t.CacheBytes / 1048576.0:0.0} MB hit {t.CacheHitRate:P0}, noise {t.RelativeNoise:0.000}");
        }
    }
}
