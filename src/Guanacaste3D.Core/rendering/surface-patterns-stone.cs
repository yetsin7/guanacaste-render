using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

internal static partial class SurfacePatterns
{
    /// <summary>
    /// Coursed rough-hewn masonry. Course edges wander gently and the vertical joints are
    /// individually staggered, so the wall does not look like a tiled concrete-block texture.
    /// World-space coordinates keep the pattern stable across BIM face triangulation.
    /// </summary>
    private static (float JointDistance, int Stone, int Course) StoneCell(float u, float v, float scale)
    {
        const float courseHeight = 0.22f;
        const float length = 0.39f;
        // Warp each course consistently from the same world-space function, not from the triangle ID.
        var warpedV = v + (ValueNoise(u * 1.55f + 4.1f, v * 1.25f - 7.3f) - 0.5f) * 0.050f;
        var row = warpedV / courseHeight;
        var course = (int)MathF.Floor(row);
        var fraction = row - MathF.Floor(row);
        var horizontal = MathF.Min(fraction, 1 - fraction) * courseHeight * scale;

        var x = u / length + ((course & 1) == 0 ? 0f : 0.5f);
        var stone = (int)MathF.Floor(x);
        float Border(int column) => column + (Hash01(column, course * 43 + 91) - 0.5f) * 0.28f;
        var left = Border(stone);
        if (x < left)
        {
            stone--;
            left = Border(stone);
        }
        var right = Border(stone + 1);
        if (x >= right)
        {
            stone++;
            left = right;
            right = Border(stone + 1);
        }
        var vertical = MathF.Min(x - left, right - x) * length * scale;
        var chipped = (ValueNoise(u * 34f + 3f, v * 34f - 7f) - 0.5f) * 0.004f * scale;
        return (MathF.Min(horizontal, vertical) + chipped, stone, course);
    }

    /// <summary>Warm/neutral mineral variation, with darker mortar and rounded chipped stone edges.</summary>
    private static Vector3 StoneMasonryColor(float u, float v, float scale)
    {
        var (edge, stone, course) = StoneCell(u, v, scale);
        var individual = Hash01(stone * 31 + 7, course * 19 - 5);
        var mottling = ValueNoise(u * 5.5f + 3, v * 5.5f - 2);
        var grain = ValueNoise(u * 38f, v * 38f);
        var face = 0.77f + 0.36f * individual + 0.16f * (mottling - 0.5f) + 0.09f * (grain - 0.5f);
        var mortar = 0.28f + 0.10f * ValueNoise(u * 9f, v * 9f);
        var solid = SmoothStep(0.002f * scale, 0.014f * scale, edge);
        var tone = mortar + (face - mortar) * solid;
        return new Vector3(tone * 1.05f, tone, tone * 0.93f);
    }

    /// <summary>Unfaded world-projected stone height, in meters, for optional render-only tessellation.</summary>
    internal static float StoneDisplacement(float worldU, float worldV) =>
        StoneMasonryHeight(worldU, worldV, 1f, 1f);

    /// <summary>
    /// Recesses the mortar about a centimetre, rounds protruding stone edges and adds centimetre-scale
    /// mineral undulations plus small chips. This is shading relief, not geometric displacement.
    /// </summary>
    private static float StoneMasonryHeight(float u, float v, float scale, float fade)
    {
        var (edge, stone, course) = StoneCell(u, v, scale);
        var individual = (Hash01(stone * 17 + 11, course * 13 + 3) - 0.5f) * 0.006f * scale;
        var rough = (ValueNoise(u * 8f + 12f, v * 8f - 4f) - 0.5f) * 0.010f * scale;
        var mineral = (ValueNoise(u * 42f - 5f, v * 42f + 3f) - 0.5f) * 0.003f * scale;
        var face = individual + (rough + mineral) * fade;
        var solid = SmoothStep(0.002f * scale, 0.014f * scale, edge);
        return (-0.010f * scale + (face + 0.010f * scale) * solid) * fade;
    }
}
