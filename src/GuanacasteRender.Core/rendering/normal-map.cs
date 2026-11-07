using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// An OpenGL-convention tangent-space normal map (green points toward increasing v).
/// Directions are sampled in linear vector space, never decoded as sRGB colours. A pyramid of
/// filtered directions preserves close-up relief while suppressing distant leaf/bark shimmer.
/// </summary>
public sealed class NormalMap
{
    private readonly byte[][] _levels;
    private readonly int[] _widths;
    private readonly int[] _heights;

    private NormalMap(byte[][] levels, int[] widths, int[] heights)
    {
        _levels = levels;
        _widths = widths;
        _heights = heights;
    }

    public int Width => _widths[0];
    public int Height => _heights[0];
    public int LevelCount => _levels.Length;

    /// <summary>Builds and owns a normal map from packed RGB pixels (rows top to bottom).</summary>
    public static NormalMap FromRgb(int width, int height, byte[] rgb)
    {
        ArgumentNullException.ThrowIfNull(rgb);
        if (width < 1 || height < 1 || (long)width * height > rgb.Length / 3)
            throw new ArgumentException("The normal map pixels do not match its size.");

        // Do not retain the caller's mutable image buffer; subsequent edits must not alter an active render.
        var levels = new List<byte[]> { rgb.AsSpan(0, width * height * 3).ToArray() };
        var widths = new List<int> { width };
        var heights = new List<int> { height };
        while (width > 1 || height > 1)
        {
            var nextWidth = (width + 1) / 2;
            var nextHeight = (height + 1) / 2;
            var downsampled = new byte[nextWidth * nextHeight * 3];
            var previous = levels[^1];
            for (var y = 0; y < nextHeight; y++)
                for (var x = 0; x < nextWidth; x++)
                {
                    var sum = Vector3.Zero;
                    Vector3 average;
                    if ((width & 1) == 0 && (height & 1) == 0)
                    {
                        // Preserve the original 2x2 filter for even-sized levels.
                        for (var yy = y * 2; yy < y * 2 + 2; yy++)
                            for (var xx = x * 2; xx < x * 2 + 2; xx++)
                                sum += Decode(previous, (yy * width + xx) * 3);
                        average = sum / 4f;
                    }
                    else
                    {
                        // Odd-sized textures need area coverage, not equal-weight
                        // 2x2 blocks: otherwise corner slopes become exaggerated
                        // or disappear as successive mip levels shrink.
                        var left = (double)x * width / nextWidth;
                        var right = (double)(x + 1) * width / nextWidth;
                        var top = (double)y * height / nextHeight;
                        var bottom = (double)(y + 1) * height / nextHeight;
                        for (var yy = (int)top; yy < Math.Ceiling(bottom); yy++)
                            for (var xx = (int)left; xx < Math.Ceiling(right); xx++)
                            {
                                var area = (Math.Min(right, xx + 1) - Math.Max(left, xx)) *
                                           (Math.Min(bottom, yy + 1) - Math.Max(top, yy));
                                sum += Decode(previous, (yy * width + xx) * 3) * (float)area;
                            }
                        average = sum / (float)((right - left) * (bottom - top));
                    }
                    average = average.LengthSquared() > 1e-8f ? Vector3.Normalize(average) : Vector3.UnitZ;
                    var index = (y * nextWidth + x) * 3;
                    downsampled[index] = Encode(average.X);
                    downsampled[index + 1] = Encode(average.Y);
                    downsampled[index + 2] = Encode(average.Z);
                }
            levels.Add(downsampled);
            widths.Add(nextWidth);
            heights.Add(nextHeight);
            width = nextWidth;
            height = nextHeight;
        }
        return new NormalMap([.. levels], [.. widths], [.. heights]);
    }

    /// <summary>Full-resolution sampling; compatible with callers that do not know the pixel footprint.</summary>
    public Vector3 Sample(float u, float v) => Sample(u, v, 0);

    /// <summary>
    /// A direction at wrapping UV coordinates (v up) with pixel footprint measured in texture tiles,
    /// using bilinear/trilinear filtering of decoded vectors rather than interpolating encoded RGB.
    /// </summary>
    public Vector3 Sample(float u, float v, float footprint)
    {
        if (!float.IsFinite(u) || !float.IsFinite(v)) return Vector3.UnitZ;
        var level = float.IsFinite(footprint) && footprint > 0
            ? MathF.Log2(MathF.Max(footprint * Math.Max(Width, Height), 1e-6f))
            : 0;
        if (level <= 0) return Bilinear(0, u, v);
        var last = _levels.Length - 1;
        if (level >= last) return Bilinear(last, u, v);
        var low = (int)level;
        var blended = Vector3.Lerp(Bilinear(low, u, v), Bilinear(low + 1, u, v), level - low);
        return blended.LengthSquared() > 1e-8f ? Vector3.Normalize(blended) : Vector3.UnitZ;
    }

    private Vector3 Bilinear(int level, float u, float v)
    {
        var width = _widths[level];
        var height = _heights[level];
        var x = (u - MathF.Floor(u)) * width - 0.5f;
        var y = (1 - (v - MathF.Floor(v))) * height - 0.5f;
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var dx = x - x0;
        var dy = y - y0;
        var top = Vector3.Lerp(Texel(level, x0, y0), Texel(level, x0 + 1, y0), dx);
        var bottom = Vector3.Lerp(Texel(level, x0, y0 + 1), Texel(level, x0 + 1, y0 + 1), dx);
        var normal = Vector3.Lerp(top, bottom, dy);
        return normal.LengthSquared() > 1e-8f ? Vector3.Normalize(normal) : Vector3.UnitZ;
    }

    private Vector3 Texel(int level, int x, int y)
    {
        var width = _widths[level];
        var height = _heights[level];
        x = (x % width + width) % width;
        y = (y % height + height) % height;
        return Decode(_levels[level], (y * width + x) * 3);
    }

    private static Vector3 Decode(byte[] pixels, int offset) => new(
        pixels[offset] * (2f / 255f) - 1f,
        pixels[offset + 1] * (2f / 255f) - 1f,
        pixels[offset + 2] * (2f / 255f) - 1f);

    private static byte Encode(float value) =>
        (byte)Math.Clamp((int)MathF.Round((Math.Clamp(value, -1f, 1f) + 1f) * 127.5f), 0, 255);

    /// <summary>
    /// Bends a shading normal by the map, given the triangle's corners and their texture coordinates (v up): the
    /// tangent follows increasing u and the bitangent increasing v, made perpendicular to the normal.
    /// </summary>
    public static Vector3 Perturb(Vector3 normal, Vector3 sample, Vector3 p0, Vector3 p1, Vector3 p2, Vector2 t0, Vector2 t1, Vector2 t2,
        float strength = 1)
    {
        var (edge1, edge2) = (p1 - p0, p2 - p0);
        var (duv1, duv2) = (t1 - t0, t2 - t0);
        var determinant = duv1.X * duv2.Y - duv2.X * duv1.Y;
        if (MathF.Abs(determinant) < 1e-12f) return normal;
        var tangent = (edge1 * duv2.Y - edge2 * duv1.Y) / determinant;
        var bitangent = (edge2 * duv1.X - edge1 * duv2.X) / determinant;
        tangent -= normal * Vector3.Dot(normal, tangent);
        if (tangent.LengthSquared() < 1e-12f) return normal;
        tangent = Vector3.Normalize(tangent);
        var side = Vector3.Cross(normal, tangent);
        // Mirrored texture coordinates flip the bitangent; follow them.
        if (Vector3.Dot(side, bitangent) < 0) side = -side;
        var bent = tangent * (sample.X * strength) + side * (sample.Y * strength) + normal * sample.Z;
        return bent.LengthSquared() > 1e-12f ? Vector3.Normalize(bent) : normal;
    }
}
