using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// Surface patterns of the material library that make one metal, plastic or rubber look different from another: brushed
/// lines, carbon-fibre weave, a perforated grill, rust blooming through paint, green patina and dirt on copper, and worn
/// rubber. Coordinates arrive already divided by the pattern scale, so one unit is one metre at scale 1; the features are
/// millimetres to centimetres, so the patterns read on a material ball and from a metre away.
/// </summary>
internal static partial class SurfacePatterns
{
    private static bool IsMaterialPattern(SurfacePattern pattern) =>
        pattern is SurfacePattern.Brushed or SurfacePattern.CarbonFiber or SurfacePattern.Perforated or SurfacePattern.RustPaint
            or SurfacePattern.Patina or SurfacePattern.WornRubber;

    private static float Blotches(float u, float v, float frequency) =>
        0.6f * ValueNoise(u * frequency + 3, v * frequency - 7) + 0.4f * ValueNoise(u * frequency * 3 - 1, v * frequency * 3 + 5);

    /// <summary>The multiplier of the base colour at a point of one of these patterns.</summary>
    private static Vector3 MaterialTone(SurfacePattern pattern, float u, float v)
    {
        switch (pattern)
        {
            case SurfacePattern.Brushed:
            {
                // Long lines along u, thin across v: what a wire brush leaves on steel.
                var line = ValueNoise(u * 5f, v * 450f);
                var fine = ValueNoise(u * 11f + 9, v * 1100f);
                return new Vector3(0.80f + 0.30f * line + 0.12f * (fine - 0.5f));
            }
            case SurfacePattern.CarbonFiber:
            {
                // A 2×2 twill: each 4 mm square carries fibres along u or along v, swapping every two squares.
                const float cell = 0.004f;
                float cu = MathF.Floor(u / cell), cv = MathF.Floor(v / cell);
                float fu = u / cell - cu, fv = v / cell - cv;
                var alongU = (((int)(cu + cv) >> 1) & 1) == 0;
                var sheen = alongU ? MathF.Sin(MathF.PI * fv) : MathF.Sin(MathF.PI * fu);
                return new Vector3(0.45f + 1.0f * sheen * sheen);
            }
            case SurfacePattern.Perforated:
            {
                // Round holes on a staggered 8 mm pitch, 5.4 mm across; the sheet between them is a little brighter.
                const float pitch = 0.008f;
                var row = MathF.Floor(v / (pitch * 0.866f));
                var shifted = u / pitch + ((int)row & 1) * 0.5f;
                float du = shifted - MathF.Round(shifted), dv = v / (pitch * 0.866f) - row - 0.5f;
                var distance = MathF.Sqrt(du * du + dv * dv * 0.75f) * pitch;
                return new Vector3(distance < 0.0027f ? 0.04f : 1.05f - 0.25f * SmoothStep(0.0027f, 0.0040f, distance));
            }
            case SurfacePattern.RustPaint:
            {
                // Blue paint (the base colour) flaking off in blotches that show orange-brown rust; tuned for a blue base.
                var rust = SmoothStep(0.50f, 0.60f, Blotches(u, v, 9f)) * (0.75f + 0.25f * ValueNoise(u * 60, v * 60));
                var flake = SmoothStep(0.44f, 0.50f, Blotches(u + 20, v, 9f)) * (1 - rust) * 0.5f;
                return Vector3.Lerp(Vector3.Lerp(Vector3.One, new Vector3(1.4f, 1.3f, 1.2f), flake), new Vector3(3.4f, 1.05f, 0.12f), rust);
            }
            case SurfacePattern.Patina:
            {
                // Dirty copper: streaks of green verdigris running down, and grime in the creases.
                var green = SmoothStep(0.5f, 0.8f, ValueNoise(u * 7f, v * 2.5f));
                var dirt = 0.62f + 0.55f * ValueNoise(u * 28f, v * 28f);
                return Vector3.Lerp(new Vector3(dirt), new Vector3(0.55f, 1.25f, 0.95f) * dirt, green * 0.8f);
            }
            case SurfacePattern.WornRubber:
            {
                // Black rubber faded and scuffed: grey-white worn patches and a fine speckle.
                var worn = SmoothStep(0.62f, 0.80f, Blotches(u, v, 14f));
                var speckle = 0.80f + 0.35f * ValueNoise(u * 90f, v * 90f);
                return Vector3.Lerp(new Vector3(speckle), new Vector3(2.6f), worn * 0.7f);
            }
            default:
                return Vector3.One;
        }
    }

    /// <summary>The height, in metres, of one of these patterns at a point (scaled coordinates).</summary>
    private static float MaterialHeight(SurfacePattern pattern, float u, float v, float fade)
    {
        switch (pattern)
        {
            case SurfacePattern.Brushed:
                return (ValueNoise(u * 5f, v * 450f) - 0.5f) * 0.00035f * fade;
            case SurfacePattern.CarbonFiber:
            {
                const float cell = 0.004f;
                float cu = MathF.Floor(u / cell), cv = MathF.Floor(v / cell);
                float fu = u / cell - cu, fv = v / cell - cv;
                var alongU = (((int)(cu + cv) >> 1) & 1) == 0;
                var bump = alongU ? MathF.Sin(MathF.PI * fv) : MathF.Sin(MathF.PI * fu);
                return bump * 0.0003f * fade;
            }
            case SurfacePattern.Perforated:
            {
                const float pitch = 0.008f;
                var row = MathF.Floor(v / (pitch * 0.866f));
                var shifted = u / pitch + ((int)row & 1) * 0.5f;
                float du = shifted - MathF.Round(shifted), dv = v / (pitch * 0.866f) - row - 0.5f;
                var distance = MathF.Sqrt(du * du + dv * dv * 0.75f) * pitch;
                return -0.0012f * (1 - SmoothStep(0.0024f, 0.0032f, distance));
            }
            case SurfacePattern.RustPaint:
                return -SmoothStep(0.50f, 0.60f, Blotches(u, v, 9f)) * 0.0015f + (ValueNoise(u * 60, v * 60) - 0.5f) * 0.0004f * fade;
            case SurfacePattern.Patina:
                return (ValueNoise(u * 28f, v * 28f) - 0.5f) * 0.0008f * fade;
            case SurfacePattern.WornRubber:
                return (ValueNoise(u * 90f, v * 90f) - 0.5f) * 0.0006f * fade;
            default:
                return 0;
        }
    }
}
