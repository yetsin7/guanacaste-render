using System.Numerics;
using Guanacaste3D.Core.Rendering;

/// <summary>Writes reference, cached render and amplified difference images, to see where a bias lives.</summary>
static class CacheView
{
    public static void Run(string folder, float cell)
    {
        Directory.CreateDirectory(folder);
        var scene = BenchmarkScenes.DaylitRoom();
        const int width = 320, height = 180;
        var render = new RenderSceneBuilder().AddBuilding(scene.Mesh).Build();
        var reference = new PathTracer(render, width, height, scene.Camera, scene.Settings with { Seed = 99 });
        for (var i = 0; i < 1024; i++) reference.RenderPass();
        var cached = new PathTracer(render, width, height, scene.Camera, scene.Settings with { UseRadianceCache = true, RadianceCacheCellSize = cell, Seed = 5 });
        for (var i = 0; i < 128; i++) cached.RenderPass();
        var a = reference.LinearImage(false);
        var b = cached.LinearImage(false);
        double meanA = a.Average(p => p.Y), meanB = b.Average(p => p.Y);
        Console.WriteLine($"mean luminance reference {meanA:0.00000}, cached {meanB:0.00000}, ratio {meanB / meanA:0.000}");
        var diff = new Vector3[a.Length];
        for (var i = 0; i < diff.Length; i++) diff[i] = new Vector3(Math.Max(0, b[i].Y - a[i].Y), Math.Max(0, a[i].Y - b[i].Y), 0) * 6;  // red: cache too bright, green: too dark
        byte[] Bytes(Vector3[] image) => ImageProcessor.ToBgra32(image, width, height, scene.Settings.Exposure, ImageProcessor.Plain, reference.CaptureFeatures);
        PngCodec.Save(Path.Combine(folder, "cache-reference.png"), RasterImage.FromBgra32(width, height, Bytes(a)));
        PngCodec.Save(Path.Combine(folder, "cache-render.png"), RasterImage.FromBgra32(width, height, Bytes(b)));
        PngCodec.Save(Path.Combine(folder, "cache-diff.png"), RasterImage.FromBgra32(width, height, Bytes(diff)));
        var (ra, rb) = (reference.LinearComponents().Indirect, cached.LinearComponents().Indirect);
        Console.WriteLine($"mean indirect luminance reference {ra.Average(p => p.Y):0.00000}, cached {rb.Average(p => p.Y):0.00000}");
    }
}
