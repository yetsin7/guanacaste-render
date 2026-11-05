using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

/// <summary>Measures that denoising brings a low-sample render measurably closer to a converged one.</summary>
static class DenoiserChecks
{
    public static void Run(Action<bool, string> check)
    {
        var house = SampleProjects.CreateDemonstrationHouse();
        var scene = BuildingMesher.Build(house, includeGround: true);
        var (min, max) = BuildingMesher.Build(house).Bounds();
        var camera = RenderCamera.FrameBounds(min, max);
        const int width = 96, height = 60;

        var reference = new PathTracer(scene, width, height, camera);
        for (var pass = 0; pass < 256; pass++) reference.RenderPass();
        var truth = reference.LinearImage(denoise: false);

        // A different seed keeps the noisy samples independent of the reference.
        var noisy = new PathTracer(scene, width, height, camera, new RenderSettings { Seed = 7 });
        for (var pass = 0; pass < 4; pass++) noisy.RenderPass();
        // Only indirect light is filtered; the direct component's 4-spp error is edge aliasing,
        // which no feature-guided denoiser can remove, so each part is measured on its own.
        var (referenceDirect, referenceIndirect) = reference.LinearComponents();
        var (noisyDirect, noisyIndirect) = noisy.LinearComponents();
        var indirectRaw = Error(noisyIndirect, referenceIndirect);
        var indirectFiltered = Error(AtrousDenoiser.Apply(noisyIndirect, noisy.CaptureFeatures()), referenceIndirect);
        var rawError = Error(noisy.LinearImage(denoise: false), truth);
        var denoisedError = Error(noisy.LinearImage(denoise: true), truth);
        Console.WriteLine($"INFO: 4-spp indirect RMSE {indirectRaw:0.0000} → {indirectFiltered:0.0000}; " +
                          $"total {rawError:0.0000} → {denoisedError:0.0000}; direct (unfiltered) " +
                          $"{Error(noisyDirect, referenceDirect):0.0000}");
        check(indirectFiltered < indirectRaw * 0.65, "Denoiser cuts 4-spp indirect-light error by over 35 %");
        check(denoisedError < rawError, "Denoised 4-spp render is closer to the converged render overall");

        var features = noisy.CaptureFeatures();
        var sky = Enumerable.Range(0, width * height).Where(i => float.IsInfinity(features.Depth[i])).ToArray();
        var image = noisy.LinearImage(denoise: false);
        var filtered = noisy.LinearImage(denoise: true);
        check(sky.All(i => filtered[i] == image[i]) && features.Normal.Where((_, i) => !float.IsInfinity(features.Depth[i]))
                .All(n => MathF.Abs(n.Length() - 1) < 1e-3f),
            "Denoiser leaves sky pixels untouched and features carry unit normals");
    }

    /// <summary>Root-mean-square error after tone-mapping-like compression, so highlights don't dominate.</summary>
    private static double Error(Vector3[] image, Vector3[] truth)
    {
        double sum = 0;
        for (var i = 0; i < image.Length; i++)
        {
            var a = image[i] / (Vector3.One + image[i]);
            var b = truth[i] / (Vector3.One + truth[i]);
            sum += Vector3.DistanceSquared(a, b);
        }
        return Math.Sqrt(sum / image.Length);
    }
}
