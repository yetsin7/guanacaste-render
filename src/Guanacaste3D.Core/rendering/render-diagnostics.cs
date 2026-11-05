using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// Engineering views of what the renderer sees and how much work each pixel receives. They are measurements for finding
/// problems, not pictures. Views that make no sense for a progressive CPU path tracer (overdraw, motion vectors, level of
/// detail, texture residency) are deliberately absent rather than faked.
/// </summary>
public enum DiagnosticView
{
    /// <summary>First-hit surface normal as color (x, y, z → red, green, blue).</summary>
    Normals,
    /// <summary>First-hit surface color before lighting.</summary>
    Albedo,
    /// <summary>Distance to the first surface: near is bright, far is dark (logarithmic).</summary>
    Depth,
    /// <summary>First-hit world position, repeating every meter so scale and placement are readable.</summary>
    WorldPosition,
    /// <summary>A stable color per material, to check material assignment.</summary>
    MaterialId,
    /// <summary>First-hit microfacet roughness as grey (white is matte).</summary>
    Roughness,
    /// <summary>First-hit metalness as grey.</summary>
    Metallic,
    /// <summary>Samples per pixel as a heat map: shows where adaptive sampling is spending effort.</summary>
    SampleCount,
    /// <summary>Relative noise (standard error over brightness) as a heat map.</summary>
    Noise,
    /// <summary>How much the denoiser can trust each pixel: bright where the estimate is clean.</summary>
    DenoiserConfidence,
    /// <summary>Number of explicit lights that can reach each first hit, as a heat map.</summary>
    LightComplexity,
    /// <summary>Whether the radiance cache has an entry for each first hit (white) or not (black).</summary>
    CacheResidency,
    /// <summary>Where sky light enters through windows: brightness of the portal selection weights at each first hit.</summary>
    PortalReach,
    /// <summary>Only the light that arrives before the first diffuse bounce (sun, sky, lights).</summary>
    DirectLight,
    /// <summary>Only the light that has bounced at least once.</summary>
    IndirectLight
}

/// <summary>A snapshot of the renderer's state and costs, for the developer panel.</summary>
public sealed record RenderTelemetry(
    int Width, int Height, int Triangles, int Lights, int EmissiveTriangles, int SkyPortals,
    double AverageSamplesPerPixel, float ActiveFraction, long Rays, double MegaRaysPerSecond,
    double LastPassMilliseconds, double LastDenoiseMilliseconds, double AccelerationBuildMilliseconds,
    long ImageBufferBytes, long SceneBytes, long CacheBytes, double CacheHitRate, int CacheEntries,
    long ManagedMemoryBytes, double RelativeNoise)
{
    /// <summary>Estimated milliseconds one more pass of the current size takes, from the last measured pass.</summary>
    public double EstimatedMillisecondsPerSample => LastPassMilliseconds;
}

/// <summary>Color ramps shared by the diagnostic views.</summary>
internal static class DiagnosticColors
{
    /// <summary>Blue → cyan → green → yellow → red for a value in [0, 1].</summary>
    public static Vector3 Heat(float t)
    {
        t = Math.Clamp(t, 0f, 1f) * 4;
        return t switch
        {
            < 1 => new Vector3(0, t, 1),
            < 2 => new Vector3(0, 1, 2 - t),
            < 3 => new Vector3(t - 2, 1, 0),
            _ => new Vector3(1, 4 - t, 0)
        };
    }

    /// <summary>A bright, well-separated color derived from an integer id.</summary>
    public static Vector3 Id(int id)
    {
        var h = (uint)id * 2654435761u;
        h ^= h >> 15;
        h *= 2246822519u;
        var hue = (h & 0xFFFF) / 65536f;
        return Hsv(hue, 0.65f, 0.95f);
    }

    private static Vector3 Hsv(float hue, float saturation, float value)
    {
        var h = hue * 6;
        var sector = (int)MathF.Floor(h);
        var f = h - sector;
        var p = value * (1 - saturation);
        var q = value * (1 - saturation * f);
        var t = value * (1 - saturation * (1 - f));
        return (sector % 6) switch
        {
            0 => new Vector3(value, t, p),
            1 => new Vector3(q, value, p),
            2 => new Vector3(p, value, t),
            3 => new Vector3(p, q, value),
            4 => new Vector3(t, p, value),
            _ => new Vector3(value, p, q)
        };
    }
}
