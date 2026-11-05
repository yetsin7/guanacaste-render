using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>The world-space projection textures use, shared by the renderer and the interactive 3D view.</summary>
public static class TextureProjection
{
    /// <summary>Plane coordinates in meters of a point on a face: (x, y) on floors, (along, height) on walls.</summary>
    public static (float U, float V) PlaneUv(Vector3 position, Vector3 normal) => SurfacePatterns.PlaneUv(position, normal);
}

/// <summary>
/// An image texture for the renderer: 8-bit sRGB texels in a mip chain, so a wall seen from afar averages its texels
/// instead of sparkling. Sampling is trilinear in linear light and wraps in both directions, because textures are
/// projected in world space and repeat every <see cref="RenderMaterial.TextureSize"/> meters.
/// </summary>
public sealed partial class RenderTexture
{
    private static readonly float[] Linear = Enumerable.Range(0, 256)
        .Select(value => MaterialLibrary.SrgbToLinear(value / 255f)).ToArray();

    private readonly byte[][] _levels;
    private readonly int[] _widths;
    private readonly int[] _heights;
    /// <summary>Coverage of the top level, one byte per texel, or null for an opaque image.</summary>
    private byte[]? _alpha;
    private byte[][]? _alphaLevels;

    private RenderTexture(byte[][] levels, int[] widths, int[] heights)
    {
        _levels = levels;
        _widths = widths;
        _heights = heights;
        var top = levels[^1];
        Mean = new Vector3(Linear[top[0]], Linear[top[1]], Linear[top[2]]);
    }

    /// <summary>Name shown in the interface, usually the file name.</summary>
    public string Name { get; init; } = "Textura";

    public int Width => _widths[0];
    public int Height => _heights[0];
    public int LevelCount => _levels.Length;

    /// <summary>Average color in linear light; used where the surface is only seen blurred, after a diffuse bounce.</summary>
    public Vector3 Mean { get; }

    /// <summary>Approximate memory held by all levels, in bytes.</summary>
    public long Bytes => _levels.Sum(level => (long)level.Length) + (_alphaLevels?.Sum(level => (long)level.Length) ?? 0);

    // Validate before allocating: int width * height * 4 can overflow for large or malformed image dimensions.
    // A valid 4-byte-per-pixel buffer also guarantees that pixelCount * 3 fits in an int.
    private static int ValidatePixelCount(int width, int height, int bufferLength)
    {
        if (width < 1 || height < 1 || (long)width * height > bufferLength / 4)
            throw new ArgumentException("The texture pixels do not match its size.");
        return width * height;
    }

    /// <summary>Builds a texture from BGRA32 pixels (rows top to bottom), reduced to at most <paramref name="maximumSize"/> texels a side.</summary>
    public static RenderTexture FromBgra32(int width, int height, ReadOnlySpan<byte> bgra, int maximumSize = 2048)
    {
        var pixelCount = ValidatePixelCount(width, height, bgra.Length);
        var rgb = new byte[pixelCount * 3];
        for (var index = 0; index < pixelCount; index++)
        {
            rgb[index * 3] = bgra[index * 4 + 2];
            rgb[index * 3 + 1] = bgra[index * 4 + 1];
            rgb[index * 3 + 2] = bgra[index * 4];
        }
        return FromRgb(width, height, rgb, maximumSize);
    }

    /// <summary>
    /// Builds a texture from RGBA pixels (rows top to bottom). The alpha channel becomes a cut-out mask, as leaf and
    /// grass cards use: where it is below a material's cutoff the surface is a hole that rays and shadows pass through.
    /// </summary>
    public static RenderTexture FromRgba(int width, int height, ReadOnlySpan<byte> rgba, int maximumSize = 2048)
    {
        var pixelCount = ValidatePixelCount(width, height, rgba.Length);
        var rgb = new byte[pixelCount * 3];
        var alpha = new byte[pixelCount];
        for (var index = 0; index < pixelCount; index++)
        {
            rgb[index * 3] = rgba[index * 4];
            rgb[index * 3 + 1] = rgba[index * 4 + 1];
            rgb[index * 3 + 2] = rgba[index * 4 + 2];
            alpha[index] = rgba[index * 4 + 3];
        }
        return FromMaskedRgb(width, height, rgb, alpha, maximumSize);
    }

    /// <summary>Whether the image carries a cut-out mask.</summary>
    public bool HasAlpha => _alpha is not null;

    /// <summary>Coverage 0–1 at texture coordinates (wrapping, v up as in <see cref="Sample"/>); 1 without a mask.</summary>
    public float Alpha(float u, float v)
    {
        if (_alpha is not { } mask) return 1;
        var width = _widths[0];
        var height = _heights[0];
        if (!float.IsFinite(u) || !float.IsFinite(v)) return 1;
        var x = (u - MathF.Floor(u)) * width - 0.5f;
        var y = (1 - (v - MathF.Floor(v))) * height - 0.5f;
        var left = (int)MathF.Floor(x);
        var top = (int)MathF.Floor(y);
        var horizontal = x - left;
        var vertical = y - top;
        var upper = MaskTexel(mask, width, height, left, top) * (1 - horizontal) +
            MaskTexel(mask, width, height, left + 1, top) * horizontal;
        var lower = MaskTexel(mask, width, height, left, top + 1) * (1 - horizontal) +
            MaskTexel(mask, width, height, left + 1, top + 1) * horizontal;
        return (upper * (1 - vertical) + lower * vertical) / 255f;
    }

    /// <summary>Wraps coverage samples at the same texel centers as the color texture.</summary>
    private static byte MaskTexel(byte[] mask, int width, int height, int x, int y) =>
        mask[((y % height + height) % height) * width + (x % width + width) % width];

    /// <summary>Builds a texture from a decoded PNG image.</summary>
    public static RenderTexture FromRaster(RasterImage image, int maximumSize = 2048) =>
        FromRgb(image.Width, image.Height, image.Rgb, maximumSize);

    private static RenderTexture FromRgb(int width, int height, byte[] rgb, int maximumSize)
    {
        maximumSize = Math.Clamp(maximumSize, 1, 8192);
        var levels = new List<byte[]>();
        var widths = new List<int>();
        var heights = new List<int>();
        while (width > maximumSize || height > maximumSize)
            (rgb, width, height) = Halve(rgb, width, height);
        levels.Add(rgb);
        widths.Add(width);
        heights.Add(height);
        while (width > 1 || height > 1)
        {
            (rgb, width, height) = Halve(rgb, width, height);
            levels.Add(rgb);
            widths.Add(width);
            heights.Add(height);
        }
        return new RenderTexture([.. levels], [.. widths], [.. heights]);
    }

    /// <summary>
    /// Color at texture coordinates measured in tiles (1 = one copy of the image), with <paramref name="footprint"/> the
    /// size of a pixel on the surface, also in tiles, which chooses the detail level.
    /// </summary>
    public Vector3 Sample(float u, float v, float footprint)
    {
        // Invalid UVs must never inject NaN into the lighting integrator.
        // A heavily blurred texel is a stable fallback for a malformed projection.
        if (!float.IsFinite(u) || !float.IsFinite(v)) return Mean;
        // Adding logarithms avoids overflow when a very large but finite
        // footprint is multiplied by the texture resolution.
        var level = float.IsFinite(footprint) && footprint > 0
            ? MathF.Log2(footprint) + MathF.Log2(Math.Max(_widths[0], _heights[0]))
            : 0f;
        if (level <= 0) return Bilinear(0, u, v);
        var last = _levels.Length - 1;
        if (level >= last) return Bilinear(last, u, v);
        var lower = (int)level;
        return Vector3.Lerp(Bilinear(lower, u, v), Bilinear(lower + 1, u, v), level - lower);
    }

    /// <summary>Luminance at the detail level chosen for <paramref name="footprint"/>, for relief.</summary>
    public float Luminance(float u, float v, float footprint) => Sampling.Luminance(Sample(u, v, footprint));

    /// <summary>Size in tiles of one texel at the detail level a footprint selects; the step for relief slopes.</summary>
    public float TexelSize(float footprint)
    {
        var level = float.IsFinite(footprint) && footprint > 0
            ? Math.Clamp((int)MathF.Floor(MathF.Log2(footprint) +
                MathF.Log2(Math.Max(_widths[0], _heights[0]))), 0, _levels.Length - 1)
            : 0;
        return 1f / Math.Max(_widths[level], _heights[level]);
    }

    private Vector3 Bilinear(int level, float u, float v)
    {
        var width = _widths[level];
        var height = _heights[level];
        var data = _levels[level];
        // Rows run top to bottom in the image while v grows upward on a wall, so v is flipped.
        var x = (u - MathF.Floor(u)) * width - 0.5f;
        var y = (1 - (v - MathF.Floor(v))) * height - 0.5f;
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var fx = x - x0;
        var fy = y - y0;
        if (_alphaLevels is { } masks) return MaskedBilinear(data, masks[level], width, height, x0, y0, fx, fy);
        var a = Texel(data, width, height, x0, y0);
        var b = Texel(data, width, height, x0 + 1, y0);
        var c = Texel(data, width, height, x0, y0 + 1);
        var d = Texel(data, width, height, x0 + 1, y0 + 1);
        return Vector3.Lerp(Vector3.Lerp(a, b, fx), Vector3.Lerp(c, d, fx), fy);
    }

    private static Vector3 Texel(byte[] data, int width, int height, int x, int y)
    {
        x = ((x % width) + width) % width;
        y = ((y % height) + height) % height;
        var index = (y * width + x) * 3;
        return new Vector3(Linear[data[index]], Linear[data[index + 1]], Linear[data[index + 2]]);
    }

    /// <summary>Area-filters in linear light while preserving the average of odd-sized images.</summary>
    private static (byte[] Rgb, int Width, int Height) Halve(byte[] rgb, int width, int height)
    {
        var halfWidth = Math.Max(1, (width + 1) / 2);
        var halfHeight = Math.Max(1, (height + 1) / 2);
        var result = new byte[halfWidth * halfHeight * 3];
        for (var y = 0; y < halfHeight; y++)
            for (var x = 0; x < halfWidth; x++)
            {
                var sum = Vector3.Zero;
                var left = (double)x * width / halfWidth;
                var right = (double)(x + 1) * width / halfWidth;
                var top = (double)y * height / halfHeight;
                var bottom = (double)(y + 1) * height / halfHeight;
                var weightSum = 0.0;
                for (var sy = (int)top; sy < Math.Ceiling(bottom); sy++)
                    for (var sx = (int)left; sx < Math.Ceiling(right); sx++)
                    {
                        var weight = (Math.Min(right, sx + 1) - Math.Max(left, sx)) *
                            (Math.Min(bottom, sy + 1) - Math.Max(top, sy));
                        var index = (sy * width + sx) * 3;
                        sum += new Vector3(Linear[rgb[index]], Linear[rgb[index + 1]], Linear[rgb[index + 2]]) * (float)weight;
                        weightSum += weight;
                    }
                var mean = sum / (float)weightSum;
                var target = (y * halfWidth + x) * 3;
                result[target] = Encode(mean.X);
                result[target + 1] = Encode(mean.Y);
                result[target + 2] = Encode(mean.Z);
            }
        return (result, halfWidth, halfHeight);
    }

    private static byte Encode(float linear)
    {
        var clamped = Math.Clamp(linear, 0f, 1f);
        var srgb = clamped <= 0.0031308f ? 12.92f * clamped : 1.055f * MathF.Pow(clamped, 1 / 2.4f) - 0.055f;
        return (byte)Math.Clamp(srgb * 255 + 0.5f, 0f, 255f);
    }
}
