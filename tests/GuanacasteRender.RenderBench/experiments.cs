using Guanacaste3D.Core.Rendering;

/// <summary>The named A/B experiments that drive renderer design decisions (see docs/render/ADR-*.md).</summary>
static class Experiments
{
    private static readonly double[] Budgets = [0.25, 0.5, 1, 2, 4];

    public static void Run(string name, int width, int referencePasses, string? output)
    {
        var scenes = new[] { BenchmarkScenes.DaylitRoom(), BenchmarkScenes.HouseExterior() };
        switch (name)
        {
            case "portals":
                foreach (var scene in scenes)
                    AbExperiment.Run(scene, width, referencePasses,
                        new Variant("reference (portals off)", s => s with { SkyPortals = false }),
                        [new Variant("portals off", s => s with { SkyPortals = false }),
                         new Variant("portals on", s => s with { SkyPortals = true })],
                        Budgets, output);
                break;
            case "denoiser":
                foreach (var scene in scenes)
                    AbExperiment.Run(scene, width, referencePasses,
                        new Variant("reference", s => s with { VarianceGuidedDenoising = false }),
                        [new Variant("fixed-strength filter", s => s with { VarianceGuidedDenoising = false }),
                         new Variant("variance-guided filter", s => s with { VarianceGuidedDenoising = true })],
                        Budgets, output);
                break;
            case "denoiser-sweep":
                foreach (var scene in scenes)
                    AbExperiment.RunEqualSamples(scene, width, referencePasses,
                        new Variant("reference", s => s with { VarianceGuidedDenoising = false }),
                        [new Variant("fixed-strength filter", s => s with { VarianceGuidedDenoising = false }),
                         new Variant("noise 0.05..0.25", s => s with { DenoiserCleanNoise = 0.05f, DenoiserNoisyNoise = 0.25f }),
                         new Variant("noise 0.03..0.15", s => s with { DenoiserCleanNoise = 0.03f, DenoiserNoisyNoise = 0.15f }),
                         new Variant("noise 0.02..0.30", s => s with { DenoiserCleanNoise = 0.02f, DenoiserNoisyNoise = 0.30f }),
                         new Variant("noise 0.05..0.5", s => s with { DenoiserCleanNoise = 0.05f, DenoiserNoisyNoise = 0.5f }),
                         new Variant("noise 0.1..0.5", s => s with { DenoiserCleanNoise = 0.10f, DenoiserNoisyNoise = 0.5f })],
                        [4, 8, 16, 32, 64, 128, 256]);
                break;
            case "cache":
                foreach (var scene in scenes)
                    AbExperiment.RunEqualRays(scene, width, referencePasses,
                        new Variant("reference (16 bounces, no cache)", s => s with { UseRadianceCache = false, MaxBounces = 16 }),
                        [new Variant("no cache, 6 bounces", s => s with { UseRadianceCache = false, MaxBounces = 6 }),
                         new Variant("cache auto cell (a)", s => s with { UseRadianceCache = true }),
                         new Variant("cache auto cell (b)", s => s with { UseRadianceCache = true }),
                         new Variant("cache auto cell (c)", s => s with { UseRadianceCache = true })],
                        [30, 60, 120, 240]);
                break;
            case "cache-move":
                CacheMoveExperiment.Run(width, referencePasses);
                break;
            case "lights":
                AbExperiment.Run(BenchmarkScenes.ManyLights(), width, referencePasses,
                    new Variant("reference (RIS 8, converged)", s => s with { LightCandidates = 8 }),
                    [new Variant("all 360 lights, no cull", s => s with { LightCandidates = 0 }),
                     new Variant("RIS 4 candidates", s => s with { LightCandidates = 4 }),
                     new Variant("RIS 8 candidates", s => s with { LightCandidates = 8 }),
                     new Variant("RIS 16 candidates", s => s with { LightCandidates = 16 })],
                    [1, 2, 4, 8], output);
                break;
            default:
                Console.WriteLine("Unknown experiment: " + name);
                break;
        }
    }
}
