namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// A linear 8-bit scalar texture for physically based roughness and metalness.
/// Unlike color images, scalar values are never transformed from sRGB.
/// Filtered mip levels avoid moire and material flicker on distant surfaces.
/// </summary>
public sealed class RenderScalarMap
{
    private readonly byte[][] _levels;
    private readonly int[] _widths;
    private readonly int[] _heights;

    private RenderScalarMap(byte[][] levels, int[] widths, int[] heights)
    {
        _levels = levels;
        _widths = widths;
        _heights = heights;
    }

    public int Width => _widths[0];
    public int Height => _heights[0];
    public int LevelCount => _levels.Length;

    public static RenderScalarMap FromGray(int width, int height, byte[] gray)
    {
        ArgumentNullException.ThrowIfNull(gray);
        if (width < 1 || height < 1 || (long)width * height > gray.Length)
            throw new ArgumentException("Scalar texture pixels do not match its dimensions.");
        var levels = new List<byte[]> { gray.AsSpan(0, width * height).ToArray() };
        var widths = new List<int> { width };
        var heights = new List<int> { height };
        while (width > 1 || height > 1)
        {
            var nextWidth = (width + 1) / 2;
            var nextHeight = (height + 1) / 2;
            var down = new byte[nextWidth * nextHeight];
            var source = levels[^1];
            // A 3x3 texture becomes 2x2 before 1x1. Averaging 2x2 source
            // blocks gives the corner texel four times the weight of others
            // at the final mip, making distant roughness/metalness drift.
            // Filter the exact source area represented by each output texel.
            // Even dimensions retain the fast, bit-identical 2x2 box filter.
            if ((width & 1) == 0 && (height & 1) == 0)
            {
                for (var y = 0; y < nextHeight; y++)
                    for (var x = 0; x < nextWidth; x++)
                    {
                        var offset = (2 * y) * width + 2 * x;
                        var sum = source[offset] + source[offset + 1] +
                                  source[offset + width] + source[offset + width + 1];
                        down[y * nextWidth + x] = (byte)((sum + 2) / 4);
                    }
            }
            else
            {
                for (var y = 0; y < nextHeight; y++)
                    for (var x = 0; x < nextWidth; x++)
                    {
                        var left = (double)x * width / nextWidth;
                        var right = (double)(x + 1) * width / nextWidth;
                        var top = (double)y * height / nextHeight;
                        var bottom = (double)(y + 1) * height / nextHeight;
                        double weightedSum = 0;
                        for (var yy = (int)top; yy < Math.Ceiling(bottom); yy++)
                            for (var xx = (int)left; xx < Math.Ceiling(right); xx++)
                            {
                                var area = (Math.Min(right, xx + 1) - Math.Max(left, xx)) *
                                           (Math.Min(bottom, yy + 1) - Math.Max(top, yy));
                                weightedSum += source[yy * width + xx] * area;
                            }
                        var average = weightedSum / ((right - left) * (bottom - top));
                        down[y * nextWidth + x] = (byte)Math.Clamp(
                            Math.Round(average, MidpointRounding.AwayFromZero), 0, 255);
                    }
            }
            levels.Add(down);
            widths.Add(nextWidth);
            heights.Add(nextHeight);
            width = nextWidth;
            height = nextHeight;
        }
        return new RenderScalarMap([..levels], [..widths], [..heights]);
    }

    /// <summary>Samples a wrapping UV tile (v increases upward) with pixel footprint in tiles.</summary>
    public float Sample(float u, float v, float footprint)
    {
        if (!float.IsFinite(u) || !float.IsFinite(v)) return 0.5f;
        var level = float.IsFinite(footprint) && footprint > 0
            ? MathF.Log2(MathF.Max(footprint * Math.Max(Width, Height), 1e-6f)) : 0f;
        if (level <= 0) return Bilinear(0, u, v);
        var last = _levels.Length - 1;
        if (level >= last) return Bilinear(last, u, v);
        var low = (int)level;
        var from = Bilinear(low, u, v);
        return from + (Bilinear(low + 1, u, v) - from) * (level - low);
    }

    /// <summary>UV span of one texel at the sampled mip level, for stable finite-difference bump normals.</summary>
    public float TexelSize(float footprint)
    {
        var density = Math.Max(Width, Height);
        // Add logarithms rather than multiplying: a finite footprint can overflow
        // when scaled by texture resolution, and casting Log2(+Infinity) to int
        // would incorrectly select the sharpest mip instead of the coarsest.
        var level = float.IsFinite(footprint) && footprint > 0
            ? Math.Clamp((int)MathF.Floor(MathF.Log2(footprint) + MathF.Log2(density)), 0, _levels.Length - 1)
            : 0;
        return 1f / Math.Max(_widths[level], _heights[level]);
    }

    private float Bilinear(int level, float u, float v)
    {
        var width = _widths[level];
        var height = _heights[level];
        var x = (u - MathF.Floor(u)) * width - .5f;
        var y = (1 - (v - MathF.Floor(v))) * height - .5f;
        var left = (int)MathF.Floor(x);
        var top = (int)MathF.Floor(y);
        var fx = x - left;
        var fy = y - top;
        var a = Texel(level, left, top);
        var b = Texel(level, left + 1, top);
        var c = Texel(level, left, top + 1);
        var d = Texel(level, left + 1, top + 1);
        return ((a + (b - a) * fx) + ((c + (d - c) * fx) - (a + (b - a) * fx)) * fy);
    }

    private float Texel(int level, int x, int y)
    {
        var width = _widths[level];
        var height = _heights[level];
        x = (x % width + width) % width;
        y = (y % height + height) % height;
        return _levels[level][y * width + x] / 255f;
    }
}
