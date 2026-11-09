using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// The Highlight effect: one material keeps its colour and gets a thin warm outline while everything else is muted, as
/// Lumion's Highlight does to point a client at a finish. It reads the material of each pixel from the feature buffer, so
/// it costs one lookup per pixel and no extra rays, and it can be changed without rendering again.
/// </summary>
public static partial class ImageProcessor
{
    private static readonly Vector3 HighlightColor = new(1f, 0.80f, 0.32f);

    /// <summary>Which pixels show the highlighted material, and how many pixels outside it each one is (0 = not on the outline).</summary>
    private sealed class HighlightMap(bool[] mask, byte[] ring, int reach)
    {
        public bool[] Mask { get; } = mask;
        public byte[] Ring { get; } = ring;
        public int Reach { get; } = reach;
    }

    /// <summary>The highlighted pixels and their outline, or null when the effect is off or the material is not in view.</summary>
    private static HighlightMap? BuildHighlight(FeatureBuffer? features, ImageFinish finish, int width, int height)
    {
        if (features is null || finish.HighlightDim <= 0.001f || string.IsNullOrWhiteSpace(finish.HighlightMaterial)) return null;
        var wanted = new bool[features.MaterialNames.Length];
        var found = false;
        for (var id = 0; id < wanted.Length; id++)
            if (string.Equals(features.MaterialNames[id], finish.HighlightMaterial, StringComparison.Ordinal)) found = wanted[id] = true;
        if (!found) return null;

        var mask = new bool[features.Material.Length];
        var frontier = new List<int>();
        var any = false;
        for (var index = 0; index < mask.Length; index++)
        {
            var id = features.Material[index];
            if (id < wanted.Length && wanted[id]) any = mask[index] = true;
        }
        if (!any) return null;

        // The outline is about 0.15 % of the picture width wide, never less than one pixel. It grows outward from the
        // material one pixel per step, so the work follows the length of the edge and not the size of the picture.
        var reach = Math.Clamp((int)MathF.Round(width * 0.0015f), 1, 12);
        var ring = new byte[mask.Length];
        for (var index = 0; index < mask.Length; index++)
            if (mask[index] && TouchesOutside(mask, index, width, height)) frontier.Add(index);
        for (var step = 1; step <= reach && frontier.Count > 0; step++)
        {
            var next = new List<int>();
            foreach (var index in frontier)
            {
                int column = index % width, row = index / width;
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    int x = column + dx, y = row + dy;
                    if (x < 0 || y < 0 || x >= width || y >= height) continue;
                    var neighbour = y * width + x;
                    if (mask[neighbour] || ring[neighbour] != 0) continue;
                    ring[neighbour] = (byte)step;
                    next.Add(neighbour);
                }
            }
            frontier = next;
        }
        return new HighlightMap(mask, ring, reach);
    }

    private static bool TouchesOutside(bool[] mask, int index, int width, int height)
    {
        int column = index % width, row = index / width;
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            int x = column + dx, y = row + dy;
            if (x >= 0 && y >= 0 && x < width && y < height && !mask[y * width + x]) return true;
        }
        return false;
    }

    /// <summary>
    /// Mutes a pixel that is not the highlighted material (less colour, less light) and paints a soft outline on the ring of
    /// pixels around it: full strength where it touches the material, fading to nothing at the edge of the reach.
    /// </summary>
    private static Vector3 ApplyHighlight(Vector3 color, HighlightMap map, int index, float dim)
    {
        if (map.Mask[index]) return color;
        dim = Math.Clamp(dim, 0f, 1f);
        var luminance = Sampling.Luminance(color);
        var muted = Vector3.Lerp(color, new Vector3(luminance), 0.8f * dim) * (1 - 0.62f * dim);
        var step = map.Ring[index];
        if (step == 0) return muted;
        var strength = 1f - (step - 1f) / map.Reach;
        return Vector3.Lerp(muted, HighlightColor, Math.Clamp(strength * (0.55f + 0.4f * dim), 0f, 0.95f));
    }
}
