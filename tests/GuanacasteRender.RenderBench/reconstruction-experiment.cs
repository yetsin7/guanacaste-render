using System.Diagnostics;
using System.Numerics;
using Guanacaste3D.Core.Rendering;

/// <summary>
/// Internal-resolution A/B. The baseline traces every pixel with the full path and denoises. The decoupled variant
/// computes direct light (sun, portals, lights, visible sky) at full resolution with no bounces, traces the indirect
/// light at a fraction of the resolution, denoises it there, and reconstructs it with <see cref="GuidedUpsampler"/>.
/// Both are scored against the same converged reference at an equal number of traced rays, which is independent of
/// how busy the machine is.
/// </summary>
static class ReconstructionExperiment
{
    public static void Run(int width, int referencePasses)
    {
        foreach (var scene in new[] { BenchmarkScenes.DaylitRoom(), BenchmarkScenes.HouseExterior() })
        {
            var height = width * 9 / 16;
            var render = new RenderSceneBuilder().AddBuilding(scene.Mesh).Build();
            var reference = new PathTracer(render, width, height, scene.Camera, scene.Settings with { Seed = 99 });
            for (var i = 0; i < referencePasses; i++) reference.RenderPass();
            var truth = Display(reference.LinearImage(false), reference);
            Console.WriteLine($"\n== Reconstruction on {scene.Name} · {width}x{height} · reference {referencePasses} spp");
            Console.WriteLine("  variant                              rays/px      PSNR   SSIM   dE");

            // Oracle: take the converged indirect light, shrink it, and enlarge it again. Whatever error remains is the
            // reconstructor's own bias, with no sampling noise involved.
            foreach (var scale in new[] { 0.5f, 0.35f })
            {
                var lowWidth = Math.Max(8, (int)(width * scale));
                var lowHeight = Math.Max(8, (int)(height * scale));
                var low = new PathTracer(render, lowWidth, lowHeight, scene.Camera, scene.Settings with { MaxBounces = 0 });
                var lowFeatures = low.CaptureFeatures();
                var highFeatures = reference.CaptureFeatures();
                var (referenceDirect, referenceIndirect) = reference.LinearComponents();
                var shrunk = new Vector3[lowWidth * lowHeight];
                var counts = new int[lowWidth * lowHeight];
                for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                    {
                        var lx = Math.Min(lowWidth - 1, (int)(x * (float)lowWidth / width));
                        var ly = Math.Min(lowHeight - 1, (int)(y * (float)lowHeight / height));
                        shrunk[ly * lowWidth + lx] += referenceIndirect[y * width + x];
                        counts[ly * lowWidth + lx]++;
                    }
                for (var i = 0; i < shrunk.Length; i++) if (counts[i] > 0) shrunk[i] /= counts[i];
                var restored = new GuidedUpsampler().Upscale(shrunk, lowFeatures, highFeatures);
                var image = new Vector3[restored.Length];
                for (var i = 0; i < image.Length; i++) image[i] = referenceDirect[i] + restored[i];
                var q = ImageMetrics.Compare(Display(image, reference), truth, width, height);
                Console.WriteLine($"  oracle: converged indirect at x{scale:0.00} then upsampled   {q.Psnr,6:0.00}  {q.Ssim:0.000}  {q.MeanDeltaE,4:0.0}   <- reconstructor bias only");
            }

            // Baseline: full path tracing, guided denoiser.
            var baseline = new PathTracer(render, width, height, scene.Camera, scene.Settings with { Seed = 7 });
            foreach (var target in new[] { 4, 8, 16, 32, 64 })
            {
                while (baseline.SampleCount < target) baseline.RenderPass();
                var q = ImageMetrics.Compare(Display(baseline.LinearImage(true), baseline), truth, width, height);
                Console.WriteLine($"  full-res path + denoise ({target,3} spp)     {(double)baseline.RayCount / (width * height),7:0.0}   {q.Psnr,6:0.00}  {q.Ssim:0.000}  {q.MeanDeltaE,4:0.0}");
            }

            foreach (var scale in new[] { 0.5f, 0.35f })
            {
                var lowWidth = Math.Max(8, (int)(width * scale));
                var lowHeight = Math.Max(8, (int)(height * scale));
                var direct = new PathTracer(render, width, height, scene.Camera, scene.Settings with { Seed = 11, MaxBounces = 0 });
                var low = new PathTracer(render, lowWidth, lowHeight, scene.Camera, scene.Settings with { Seed = 13 });
                var upsampler = new GuidedUpsampler();
                var highFeatures = direct.CaptureFeatures();
                var lowFeatures = low.CaptureFeatures();
                foreach (var (directSamples, lowSamples) in new[] { (8, 8), (12, 16), (16, 32), (24, 64), (32, 128) })
                {
                    while (direct.SampleCount < directSamples) direct.RenderPass();
                    while (low.SampleCount < lowSamples) low.RenderPass();
                    var indirect = low.LinearComponents().Indirect;
                    var smooth = AtrousDenoiser.ApplyGuided(indirect, low.IndirectVarianceOfMean(), lowFeatures);
                    var up = upsampler.Upscale(smooth, lowFeatures, highFeatures);
                    var directLight = direct.LinearComponents().Direct;
                    var image = new Vector3[up.Length];
                    for (var i = 0; i < image.Length; i++) image[i] = directLight[i] + up[i];
                    var q = ImageMetrics.Compare(Display(image, direct), truth, width, height);
                    var rays = (double)(direct.RayCount + low.RayCount) / (width * height);
                    Console.WriteLine($"  decoupled x{scale:0.00} ({directSamples,2} direct / {lowSamples,3} low)  {rays,7:0.0}   {q.Psnr,6:0.00}  {q.Ssim:0.000}  {q.MeanDeltaE,4:0.0}");
                }
            }
        }
    }

    private static Vector3[] Display(Vector3[] linear, PathTracer tracer) =>
        RasterImage.FromBgra32(tracer.Width, tracer.Height,
            ImageProcessor.ToBgra32(linear, tracer.Width, tracer.Height, tracer.BaseExposure, ImageProcessor.Plain, tracer.CaptureFeatures)).ToDisplay();
}
