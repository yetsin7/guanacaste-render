using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

public sealed partial class RenderTexture
{
    /// <summary>Builds paired color and coverage levels while keeping invisible pixels from tinting visible foliage.</summary>
    private static RenderTexture FromMaskedRgb(int width, int height, byte[] rgb, byte[] alpha, int maximumSize)
    {
        maximumSize = Math.Clamp(maximumSize, 1, 8192);
        while (width > maximumSize || height > maximumSize)
            (rgb, alpha, width, height) = ReduceMasked(rgb, alpha, width, height);
        var colors = new List<byte[]>();
        var masks = new List<byte[]>();
        var widths = new List<int>();
        var heights = new List<int>();
        do
        {
            colors.Add(rgb); masks.Add(alpha); widths.Add(width); heights.Add(height);
            if (width == 1 && height == 1) break;
            (rgb, alpha, width, height) = ReduceMasked(rgb, alpha, width, height);
        } while (true);
        return new RenderTexture([.. colors], [.. widths], [.. heights]) { _alpha = masks[0], _alphaLevels = [.. masks] };
    }

    /// <summary>Filters linear color by coverage and area, including fractional source pixels in odd-sized images.</summary>
    private static (byte[] Rgb, byte[] Alpha, int Width, int Height) ReduceMasked(byte[] rgb, byte[] alpha, int width, int height)
    {
        var nextWidth = Math.Max(1, (width + 1) / 2);
        var nextHeight = Math.Max(1, (height + 1) / 2);
        var nextRgb = new byte[nextWidth * nextHeight * 3];
        var nextAlpha = new byte[nextWidth * nextHeight];
        for (var y = 0; y < nextHeight; y++)
        for (var x = 0; x < nextWidth; x++)
        {
            var left = (double)x * width / nextWidth;
            var right = (double)(x + 1) * width / nextWidth;
            var top = (double)y * height / nextHeight;
            var bottom = (double)(y + 1) * height / nextHeight;
            var color = Vector3.Zero;
            double coverage = 0;
            for (var sourceY = (int)top; sourceY < Math.Ceiling(bottom); sourceY++)
            for (var sourceX = (int)left; sourceX < Math.Ceiling(right); sourceX++)
            {
                var area = (Math.Min(right, sourceX + 1) - Math.Max(left, sourceX)) *
                    (Math.Min(bottom, sourceY + 1) - Math.Max(top, sourceY));
                var index = sourceY * width + sourceX;
                var weight = area * alpha[index] / 255.0;
                coverage += weight;
                color += new Vector3(Linear[rgb[index * 3]], Linear[rgb[index * 3 + 1]], Linear[rgb[index * 3 + 2]]) * (float)weight;
            }
            var target = y * nextWidth + x;
            nextAlpha[target] = (byte)Math.Clamp(Math.Round(255 * coverage / ((right - left) * (bottom - top))), 0, 255);
            if (coverage <= 0) continue;
            color /= (float)coverage;
            nextRgb[target * 3] = Encode(color.X);
            nextRgb[target * 3 + 1] = Encode(color.Y);
            nextRgb[target * 3 + 2] = Encode(color.Z);
        }
        return (nextRgb, nextAlpha, nextWidth, nextHeight);
    }

    /// <summary>Interpolates color in linear light weighted by alpha, avoiding dark and colored fringes at cutouts.</summary>
    private static Vector3 MaskedBilinear(byte[] rgb, byte[] alpha, int width, int height, int left, int top, float fx, float fy)
    {
        var result = Vector3.Zero;
        var coverage = 0f;
        for (var y = 0; y < 2; y++)
        for (var x = 0; x < 2; x++)
        {
            var sampleX = left + x;
            var sampleY = top + y;
            var weight = (x == 0 ? 1 - fx : fx) * (y == 0 ? 1 - fy : fy) *
                MaskTexel(alpha, width, height, sampleX, sampleY) / 255f;
            result += Texel(rgb, width, height, sampleX, sampleY) * weight;
            coverage += weight;
        }
        return coverage > 0 ? result / coverage : Vector3.Zero;
    }
}
