namespace Guanacaste3D.Core.Rendering;

/// <summary>Why a plan differs from what the person asked for. The interface turns each code into a sentence.</summary>
public enum PlanNote
{
    /// <summary>The quality was chosen automatically from the hardware.</summary>
    ChosenAutomatically,
    /// <summary>Running on battery: fewer cores and a lower quality level to save energy and heat.</summary>
    BatteryLowered,
    /// <summary>The image would not fit the memory budget, so it is rendered at a smaller size.</summary>
    OutputResolutionReduced,
    /// <summary>The scene geometry alone is close to the memory budget.</summary>
    SceneNearBudget
}

/// <summary>
/// Hardware-aware choices behind the AUTO option: which quality level and how many cores to use, and the largest image
/// that fits in memory. <see cref="Configure"/> applies them to the render settings.
/// </summary>
public sealed record QualityPlan
{
    public RenderQuality Quality { get; init; }
    public ResourceMode Resources { get; init; }
    public int OutputWidth { get; init; }
    public int OutputHeight { get; init; }
    public int TargetSamples { get; init; }
    public int Threads { get; init; }
    /// <summary>Run workers below normal priority so the interface and other programs stay responsive.</summary>
    public bool LowPriorityThreads { get; init; } = true;
    /// <summary>Planned memory use of the image buffers plus the scene.</summary>
    public long EstimatedBytes { get; init; }
    public IReadOnlyList<PlanNote> Notes { get; init; } = [];

    /// <summary>
    /// Whether this level reuses bounced light through the radiance cache. The cache blurs light by up to one cell, which
    /// measurably helps interiors and previews and costs a little accuracy outdoors at high budgets, so it is used for
    /// drafts and standard renders and left off where the image must be unbiased (see docs/render/ADR-006).
    /// </summary>
    public bool UsesRadianceCache => Quality is RenderQuality.Draft or RenderQuality.Standard;

    /// <summary>Applies the plan's decisions to settings built by <see cref="RenderPresets.Lighting"/>.</summary>
    public RenderSettings Configure(RenderSettings settings) => settings with
    {
        MaxThreads = Threads,
        UseRadianceCache = UsesRadianceCache
    };
}

/// <summary>
/// Turns a requested quality (or AUTO), the hardware and the scene size into a plan that fits in memory and leaves the
/// machine usable. It is a starting point: the render session still reports what it actually measures.
/// </summary>
public static class QualityPlanner
{
    /// <summary>Approximate bytes per triangle for the hierarchy plus its triangle copy.</summary>
    public const int BytesPerSceneTriangle = 120;

    /// <summary>Largest share of available memory a render may plan to use.</summary>
    public const double MemoryShare = 0.5;

    /// <summary>Smallest image the planner will shrink to before giving up.</summary>
    private const int MinimumWidth = 160;

    /// <param name="requested">The quality the person chose, or null for AUTO.</param>
    public static QualityPlan Plan(RenderQuality? requested, HardwareProfile hardware, int outputWidth, int outputHeight,
        long sceneTriangles = 0, long sceneBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(hardware);
        if (outputWidth < 1 || outputHeight < 1) throw new ArgumentOutOfRangeException(nameof(outputWidth));
        var notes = new List<PlanNote>();
        RenderQuality quality;
        if (requested is null)
        {
            quality = AutomaticQuality(hardware);
            notes.Add(PlanNote.ChosenAutomatically);
        }
        else
        {
            quality = requested.Value;
        }

        var resources = ResourceMode.Balanced;
        if (hardware.IsOnBattery)
        {
            resources = ResourceMode.Eco;
            if (quality > RenderQuality.Standard) quality = RenderQuality.Standard;
            notes.Add(PlanNote.BatteryLowered);
        }

        if (sceneTriangles < 0) throw new ArgumentOutOfRangeException(nameof(sceneTriangles));
        if (sceneBytes < 0) throw new ArgumentOutOfRangeException(nameof(sceneBytes));
        var budget = (long)(Math.Max(0, hardware.AvailableMemoryBytes) * MemoryShare);
        var sceneCost = sceneTriangles > (long.MaxValue - sceneBytes) / BytesPerSceneTriangle
            ? long.MaxValue : sceneBytes + sceneTriangles * BytesPerSceneTriangle;
        if (sceneCost > budget * 0.6) notes.Add(PlanNote.SceneNearBudget);

        var width = outputWidth;
        var height = outputHeight;
        // 66 bytes of render accumulators + 4 bytes of output per pixel.
        // Saturation prevents an enormous scene or requested image from appearing to fit.
        long Cost(int w, int h)
        {
            var pixels = (long)w * h;
            return pixels > (long.MaxValue - sceneCost) / 70
                ? long.MaxValue : sceneCost + pixels * 70;
        }
        var reduced = false;
        while (Cost(width, height) > budget && width > MinimumWidth && height > MinimumWidth * 9 / 16)
        {
            width = (int)(width * 0.85);
            height = (int)(height * 0.85);
            reduced = true;
        }
        if (reduced) notes.Add(PlanNote.OutputResolutionReduced);

        var cores = Math.Max(1, hardware.LogicalCores);
        // Plan against the supplied hardware, not the current process's CPU quota.
        // RenderPresets.Threads uses Environment.ProcessorCount for live render defaults,
        // which is unrelated to simulated profiles and can vary across CI runners.
        var threads = resources switch
        {
            ResourceMode.Eco => Math.Max(1, cores / 2),
            ResourceMode.Balanced => Math.Max(1, cores - 1),
            _ => cores
        };
        return new QualityPlan
        {
            Quality = quality,
            Resources = resources,
            OutputWidth = width,
            OutputHeight = height,
            TargetSamples = RenderPresets.Profile(quality).TargetSamples,
            Threads = threads,
            LowPriorityThreads = quality != RenderQuality.Final,
            EstimatedBytes = Cost(width, height),
            Notes = notes
        };
    }

    /// <summary>
    /// Release-build benchmark of the development machine (i7-12650H, 16 threads, mains power), the point where the
    /// hand-tuned preview sizes were chosen. Debug builds measure about 30× less, which is why calibrations are kept apart.
    /// </summary>
    public const double ReferenceMegaTests = 1900;

    /// <summary>
    /// Height in pixels of the live preview. Pixels per second scale with throughput, and height with its square root,
    /// so a machine twice as fast gets about 1.4× the height at the same frame rate. Without a measurement it falls back
    /// to the fixed sizes the panel used before calibration existed.
    /// </summary>
    public static int PreviewHeight(HardwareProfile hardware, ResourceMode resources)
    {
        ArgumentNullException.ThrowIfNull(hardware);
        var baseHeight = resources switch { ResourceMode.Eco => 216.0, ResourceMode.Maximum => 360.0, _ => 288.0 };
        if (hardware.MicrobenchmarkMegaTests <= 0) return (int)baseHeight;
        var speed = Math.Sqrt(hardware.MicrobenchmarkMegaTests / ReferenceMegaTests);
        if (hardware.IsOnBattery) speed *= 0.75;
        // Rounded to a multiple of 8 so the preview's tile grid stays even.
        return (int)Math.Clamp(Math.Round(baseHeight * speed / 8) * 8, 144, 540);
    }

    /// <summary>
    /// Standard is the sweet spot for most machines; strong ones start at High, and weak ones at Draft so the first image
    /// arrives quickly. AUTO never picks Final, which is the person's decision.
    /// </summary>
    public static RenderQuality AutomaticQuality(HardwareProfile hardware)
    {
        var gigabytes = hardware.AvailableMemoryBytes / 1073741824.0;

        var throughput = hardware.MicrobenchmarkMegaTests;
        if (double.IsFinite(throughput) && throughput > 0)
        {
            if (throughput >= ReferenceMegaTests && gigabytes >= 8) return RenderQuality.High;
            if (throughput >= ReferenceMegaTests / 3 && gigabytes >= 4) return RenderQuality.Standard;
            return RenderQuality.Draft;
        }

        // Fall back to hardware proxies until a usable measurement is available.
        if (hardware.LogicalCores >= 12 && gigabytes >= 8) return RenderQuality.High;
        if (hardware.LogicalCores >= 6 && gigabytes >= 4) return RenderQuality.Standard;
        return RenderQuality.Draft;
    }
}
