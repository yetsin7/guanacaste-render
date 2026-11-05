using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// The camera effects Lumion offers beyond blur and grain: lens flare, light rays and tilt-shift. All three are drawn on the
/// finished picture, so they cost no extra rays and can be changed without rendering again. Flare and rays need a very bright
/// light in view (the sun, a lamp); when there is none they do nothing, as a real lens would show nothing.
/// </summary>
public static partial class ImageProcessor
{
    /// <summary>The brightest light of the picture: where it is in pixels, its colour and how strong it is, 0–1.</summary>
    private sealed record LightSpot(float X, float Y, Vector3 Color, float Strength);

    private const float LightThreshold = 3f;

    /// <summary>
    /// Finds the brightest light by scanning every other pixel, then takes the centre of everything above half its
    /// brightness, so a sun disk with a glow reads as one spot. Null when nothing in the picture is bright enough.
    /// </summary>
    private static LightSpot? FindLightSpot(Vector3[] image, int width, int height)
    {
        var peak = 0f;
        for (var row = 0; row < height; row += 2)
            for (var column = 0; column < width; column += 2)
            {
                var luminance = Sampling.Luminance(image[row * width + column]);
                if (float.IsFinite(luminance) && luminance > peak) peak = luminance;
            }
        if (peak < LightThreshold) return null;
        double sumX = 0, sumY = 0, weight = 0;
        var color = Vector3.Zero;
        var floor = peak * 0.5f;
        for (var row = 0; row < height; row += 2)
            for (var column = 0; column < width; column += 2)
            {
                var value = image[row * width + column];
                var luminance = Sampling.Luminance(value);
                if (!float.IsFinite(luminance) || luminance < floor) continue;
                sumX += column * luminance;
                sumY += row * luminance;
                weight += luminance;
                color += value;
            }
        if (weight <= 0) return null;
        var strength = Math.Clamp(MathF.Log2(peak / LightThreshold) / 4f + 0.25f, 0f, 1f);
        var hue = Vector3.Max(color, new Vector3(1e-3f));
        return new LightSpot((float)(sumX / weight), (float)(sumY / weight), hue / MathF.Max(hue.X, MathF.Max(hue.Y, hue.Z)), strength);
    }

    // ---------------------------------------------------------------- lens flare

    private readonly record struct Ghost(float X, float Y, float Radius, Vector3 Tint, float Strength);

    /// <summary>
    /// Ghosts of the light along the line through the centre of the picture, as the glass elements of a lens produce them:
    /// soft discs and thin rings, each in its own tint, fainter as the light nears the edge of the frame.
    /// </summary>
    private static Ghost[] FlareGhosts(LightSpot spot, int width, int height, float amount)
    {
        float centreX = width / 2f, centreY = height / 2f;
        var edge = Math.Clamp(MathF.Sqrt((spot.X - centreX) * (spot.X - centreX) + (spot.Y - centreY) * (spot.Y - centreY)) /
                              (0.75f * MathF.Sqrt(centreX * centreX + centreY * centreY)), 0f, 1f);
        var strength = spot.Strength * amount * (1 - 0.6f * edge) * 0.35f;
        (float Along, float Radius, Vector3 Tint)[] plan =
        [
            (0.35f, 0.05f, new Vector3(0.5f, 0.8f, 1.0f)), (0.7f, 0.09f, new Vector3(1.0f, 0.7f, 0.4f)),
            (1.25f, 0.035f, new Vector3(0.6f, 1.0f, 0.7f)), (1.65f, 0.07f, new Vector3(0.9f, 0.6f, 1.0f)),
            (2.1f, 0.12f, new Vector3(1.0f, 0.9f, 0.7f))
        ];
        return [.. plan.Select(item => new Ghost(spot.X + (centreX - spot.X) * item.Along, spot.Y + (centreY - spot.Y) * item.Along,
            item.Radius * height, item.Tint * spot.Color, strength))];
    }

    /// <summary>The light the ghosts add at one pixel.</summary>
    private static Vector3 FlareAt(Ghost[] ghosts, int column, int row)
    {
        var total = Vector3.Zero;
        foreach (var ghost in ghosts)
        {
            float dx = column - ghost.X, dy = row - ghost.Y;
            if (MathF.Abs(dx) > ghost.Radius * 1.1f || MathF.Abs(dy) > ghost.Radius * 1.1f) continue;
            var distance = MathF.Sqrt(dx * dx + dy * dy) / ghost.Radius;
            if (distance >= 1.1f) continue;
            var disc = Math.Clamp(1 - distance, 0f, 1f);
            var ring = MathF.Exp(-MathF.Pow((distance - 0.8f) / 0.12f, 2));
            total += ghost.Tint * (disc * disc * 0.35f + ring * 0.6f) * ghost.Strength;
        }
        return total;
    }

    // ---------------------------------------------------------------- light rays

    private const int RayScale = 4;
    private const int RaySteps = 32;

    /// <summary>
    /// Rays of light streaming from the light: the highlights are gathered at a quarter of the picture size, and each point
    /// there sums what lies on the way to the light, fading with distance. Only what is much brighter than the sky counts, and
    /// each highlight is capped, so an open sky gives a soft glow and the dark shapes against the light (a roof, branches)
    /// cut it into rays. At that size it costs about as much as one pass over the picture.
    /// </summary>
    private static (Vector3[] Rays, int Width, int Height) LightRays(Vector3[] image, int width, int height, LightSpot spot, float amount)
    {
        int lowWidth = Math.Max(1, width / RayScale), lowHeight = Math.Max(1, height / RayScale);
        var bright = new Vector3[lowWidth * lowHeight];
        var peak = 0f;
        foreach (var value in image.Where((_, index) => index % 7 == 0)) peak = MathF.Max(peak, Sampling.Luminance(value));
        // A sun far brighter than the sky must still read as a wide source, not as a few pixels: the cut-off follows the peak
        // only a little, and each highlight is capped below.
        var threshold = Math.Clamp(peak * 0.01f, 1.5f, 8f);
        Parallel.For(0, lowHeight, row =>
        {
            for (var column = 0; column < lowWidth; column++)
            {
                // The mean of the four corners of the block keeps thin dark shapes from vanishing between samples.
                var sum = Vector3.Zero;
                for (var corner = 0; corner < 4; corner++)
                {
                    int x = Math.Min(width - 1, column * RayScale + (corner & 1) * (RayScale - 1)), y = Math.Min(height - 1, row * RayScale + (corner >> 1) * (RayScale - 1));
                    var value = image[y * width + x];
                    var luminance = Sampling.Luminance(value);
                    if (float.IsFinite(luminance) && luminance > threshold) sum += Vector3.Min(value * ((luminance - threshold) / luminance), new Vector3(6f));
                }
                bright[row * lowWidth + column] = sum * 0.25f;
            }
        });
        var rays = new Vector3[bright.Length];
        float lightX = spot.X / RayScale, lightY = spot.Y / RayScale;
        var reach = 0.55f * MathF.Sqrt((float)lowWidth * lowWidth + (float)lowHeight * lowHeight);
        var gain = amount * spot.Strength * 1.6f / RaySteps;
        Parallel.For(0, lowHeight, row =>
        {
            for (var column = 0; column < lowWidth; column++)
            {
                float toX = lightX - column, toY = lightY - row;
                var distance = MathF.Sqrt(toX * toX + toY * toY);
                var near = Math.Clamp(1 - distance / reach, 0f, 1f);
                if (near <= 0) continue;
                var total = Vector3.Zero;
                var fade = 1f;
                for (var step = 0; step < RaySteps; step++)
                {
                    var along = step / (float)RaySteps * 0.95f;
                    total += ReadScaled(bright, lowWidth, lowHeight, 1f, (int)(column + toX * along + 0.5f), (int)(row + toY * along + 0.5f)) * fade;
                    fade *= 0.955f;
                }
                rays[row * lowWidth + column] = total * (gain * near * near);
            }
        });
        return (SoftenRays(rays, lowWidth, lowHeight), lowWidth, lowHeight);
    }

    /// <summary>Two passes of a five-tap box blur, which hide the coarse grid the rays are computed on.</summary>
    private static Vector3[] SoftenRays(Vector3[] rays, int width, int height)
    {
        var across = new Vector3[rays.Length];
        Parallel.For(0, height, row =>
        {
            for (var column = 0; column < width; column++)
            {
                var total = Vector3.Zero;
                for (var offset = -2; offset <= 2; offset++) total += rays[row * width + Math.Clamp(column + offset, 0, width - 1)];
                across[row * width + column] = total * 0.2f;
            }
        });
        var result = new Vector3[rays.Length];
        Parallel.For(0, height, row =>
        {
            for (var column = 0; column < width; column++)
            {
                var total = Vector3.Zero;
                for (var offset = -2; offset <= 2; offset++) total += across[Math.Clamp(row + offset, 0, height - 1) * width + column];
                result[row * width + column] = total * 0.2f;
            }
        });
        return result;
    }

    /// <summary>A bilinear read of a small picture at full-size pixel coordinates.</summary>
    private static Vector3 ReadScaled(Vector3[] small, int smallWidth, int smallHeight, float scale, int column, int row)
    {
        float fx = Math.Clamp(column / scale - 0.5f, 0, smallWidth - 1), fy = Math.Clamp(row / scale - 0.5f, 0, smallHeight - 1);
        int x0 = (int)fx, y0 = (int)fy, x1 = Math.Min(smallWidth - 1, x0 + 1), y1 = Math.Min(smallHeight - 1, y0 + 1);
        float tx = fx - x0, ty = fy - y0;
        var top = Vector3.Lerp(small[y0 * smallWidth + x0], small[y0 * smallWidth + x1], tx);
        var bottom = Vector3.Lerp(small[y1 * smallWidth + x0], small[y1 * smallWidth + x1], tx);
        return Vector3.Lerp(top, bottom, ty);
    }

    // ---------------------------------------------------------------- tilt-shift

    /// <summary>
    /// The miniature look: a horizontal band stays sharp and the picture blurs smoothly above and below it, with a touch
    /// more colour. Blur comes from a pyramid of half-size copies, so it costs a few reads per pixel at any strength.
    /// </summary>
    private static Vector3[] TiltShift(Vector3[] image, int width, int height, float amount, float position)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        const int levels = 4;
        var pictures = new Vector3[levels][];
        var widths = new int[levels];
        var heights = new int[levels];
        pictures[0] = image;
        widths[0] = width;
        heights[0] = height;
        for (var level = 1; level < levels; level++)
        {
            widths[level] = Math.Max(1, widths[level - 1] / 2);
            heights[level] = Math.Max(1, heights[level - 1] / 2);
            var source = pictures[level - 1];
            var sourceWidth = widths[level - 1];
            var sourceHeight = heights[level - 1];
            var target = new Vector3[widths[level] * heights[level]];
            for (var row = 0; row < heights[level]; row++)
                for (var column = 0; column < widths[level]; column++)
                {
                    int x = Math.Min(sourceWidth - 1, column * 2), y = Math.Min(sourceHeight - 1, row * 2);
                    int x1 = Math.Min(sourceWidth - 1, x + 1), y1 = Math.Min(sourceHeight - 1, y + 1);
                    target[row * widths[level] + column] = (source[y * sourceWidth + x] + source[y * sourceWidth + x1] +
                                                           source[y1 * sourceWidth + x] + source[y1 * sourceWidth + x1]) * 0.25f;
                }
            pictures[level] = target;
        }

        var result = new Vector3[image.Length];
        var centre = (0.5f + Math.Clamp(position, -1f, 1f) * 0.35f) * height;
        float band = 0.1f * height, fall = 0.32f * height;
        Parallel.For(0, height, row =>
        {
            var away = Math.Clamp((MathF.Abs(row + 0.5f - centre) - band) / fall, 0f, 1f);
            var blur = away * away * (3 - 2 * away) * amount;
            var level = blur * (levels - 1);
            int low = (int)level, high = Math.Min(levels - 1, low + 1);
            var mix = level - low;
            for (var column = 0; column < width; column++)
            {
                var index = row * width + column;
                if (blur <= 0) { result[index] = image[index]; continue; }
                var value = low == 0 ? image[index] : ReadScaled(pictures[low], widths[low], heights[low], 1 << low, column, row);
                if (mix > 0)
                    value = Vector3.Lerp(value, ReadScaled(pictures[high], widths[high], heights[high], 1 << high, column, row), mix);
                result[index] = value;
            }
        });
        // The miniature look is also a little more saturated, mostly where it is sharp.
        var boost = 1 + 0.18f * amount;
        Parallel.For(0, image.Length, index =>
        {
            var luminance = Sampling.Luminance(result[index]);
            result[index] = Vector3.Max(Vector3.Zero, new Vector3(luminance) + (result[index] - new Vector3(luminance)) * boost);
        });
        return result;
    }
}
