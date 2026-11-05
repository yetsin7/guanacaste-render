using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// The effects of <see cref="ImageFinish"/> beyond exposure and tone mapping: colour balance, depth fog, glare streaks,
/// tone and colour grading, and the lens stage (chromatic aberration, sharpening, grain). Like the rest of the finish
/// they run on the finished image, so they can be changed freely without rendering again.
/// </summary>
public static partial class ImageProcessor
{
    /// <summary>Green–magenta gains that keep luminance, the second axis of white balance.</summary>
    public static Vector3 TintBalance(float tint)
    {
        var t = Math.Clamp(float.IsFinite(tint) ? tint : 0, -1f, 1f);
        if (t == 0) return Vector3.One;
        var gains = new Vector3(1 + 0.08f * t, 1 - 0.10f * t, 1 + 0.08f * t);
        return gains / Sampling.Luminance(gains);
    }

    /// <summary>
    /// Mixes a pixel toward the fog colour by its distance: exponential extinction, so near things stay clear and the
    /// background melts into the haze. The sky (infinite depth) takes the fog fully only at the horizon's strength.
    /// </summary>
    private static Vector3 ApplyFog(Vector3 color, float depth, ImageFinish finish)
    {
        var density = Math.Clamp(finish.Fog, 0f, 1f) * 0.0115f;
        var distance = float.IsFinite(depth) ? depth : 400f;
        var amount = 1 - MathF.Exp(-density * distance);
        // The fog is lit like a mid-tone surface, so it reads as haze rather than a white wall.
        return Vector3.Lerp(color, finish.FogColor * 0.55f, Math.Clamp(amount, 0f, 0.92f));
    }

    /// <summary>
    /// Star glare: the energy above a threshold is smeared along rows and columns with an exponential fall-off in both
    /// directions (two recursive passes each), which draws thin four-point streaks from the sun's glints in O(pixels).
    /// </summary>
    private static Vector3[] Glare(Vector3[] image, int width, int height)
    {
        const float threshold = 2.0f;
        var bright = new Vector3[image.Length];
        for (var index = 0; index < image.Length; index++)
        {
            var luminance = Sampling.Luminance(image[index]);
            if (luminance > threshold) bright[index] = image[index] * ((luminance - threshold) / luminance);
        }
        var streaks = new Vector3[image.Length];
        var decay = MathF.Exp(-1f / Math.Max(4f, width * 0.03f));
        Parallel.For(0, height, row =>
        {
            Vector3 forward = default, backward = default;
            for (var column = 0; column < width; column++)
            {
                var index = row * width + column;
                forward = forward * decay + bright[index];
                streaks[index] += forward;
            }
            for (var column = width - 1; column >= 0; column--)
            {
                var index = row * width + column;
                backward = backward * decay + bright[index];
                streaks[index] += backward;
            }
        });
        Parallel.For(0, width, column =>
        {
            Vector3 down = default, up = default;
            for (var row = 0; row < height; row++)
            {
                var index = row * width + column;
                down = down * decay + bright[index];
                streaks[index] += down * 0.7f;
            }
            for (var row = height - 1; row >= 0; row--)
            {
                var index = row * width + column;
                up = up * decay + bright[index];
                streaks[index] += up * 0.7f;
            }
        });
        // Each streak carries the whole bright energy on every pixel it passes; this scale keeps it a fine line.
        var scale = (1 - decay) * 0.35f;
        for (var index = 0; index < streaks.Length; index++) streaks[index] *= scale;
        return streaks;
    }

    /// <summary>
    /// Grading on display values: highlight and shadow recovery, split toning, vibrance and black and white. Weights
    /// follow luminance, so each control acts where a photographer expects it.
    /// </summary>
    private static Vector3 Grade(Vector3 color, ImageFinish finish)
    {
        var luminance = Math.Clamp(Sampling.Luminance(color), 0f, 1f);
        if (finish.Highlights != 0)
            color *= 1 + Math.Clamp(finish.Highlights, -1f, 1f) * 0.45f * luminance * luminance;
        if (finish.Shadows != 0)
        {
            var shadow = (1 - luminance) * (1 - luminance) * (1 - luminance);
            color *= 1 + Math.Clamp(finish.Shadows, -1f, 1f) * 0.9f * shadow;
        }
        if (finish.ShadowTint != Vector3.One || finish.HighlightTint != Vector3.One)
        {
            static Vector3 Keep(Vector3 tint) => tint / Math.Max(1e-3f, Sampling.Luminance(tint));
            var tone = Vector3.Lerp(Keep(finish.ShadowTint), Keep(finish.HighlightTint), MathF.Sqrt(luminance));
            color *= tone;
        }
        if (finish.Vibrance > 0)
        {
            var max = MathF.Max(color.X, MathF.Max(color.Y, color.Z));
            var min = MathF.Min(color.X, MathF.Min(color.Y, color.Z));
            var chroma = max > 1e-4f ? (max - min) / max : 0;
            var grey = new Vector3(Sampling.Luminance(color));
            color = Vector3.Max(Vector3.Zero, grey + (color - grey) * (1 + Math.Clamp(finish.Vibrance, 0f, 1f) * 0.8f * (1 - chroma)));
        }
        if (finish.Monochrome) color = new Vector3(Sampling.Luminance(color));
        return color;
    }

    /// <summary>The lens stage on display values: chromatic aberration, then sharpening, then grain.</summary>
    private static Vector3[] Lens(Vector3[] image, int width, int height, ImageFinish finish)
    {
        var aberration = Math.Clamp(finish.ChromaticAberration, 0f, 1f);
        var sharpen = Math.Clamp(finish.Sharpen, 0f, 1f);
        var grain = Math.Clamp(finish.FilmGrain, 0f, 1f);
        if (aberration <= 0.001f && sharpen <= 0.001f && grain <= 0.001f) return image;
        var source = image;
        if (aberration > 0.001f)
        {
            var fringed = new Vector3[image.Length];
            Parallel.For(0, height, row =>
            {
                for (var column = 0; column < width; column++)
                {
                    // Red is magnified and blue shrunk about the centre, more toward the corners, as a simple lens does.
                    var u = (column + 0.5f) / width;
                    var v = (row + 0.5f) / height;
                    var (du, dv) = (u - 0.5f, v - 0.5f);
                    var shift = aberration * 0.006f;
                    var red = Sample(source, width, height, 0.5f + du * (1 - shift), 0.5f + dv * (1 - shift)).X;
                    var blue = Sample(source, width, height, 0.5f + du * (1 + shift), 0.5f + dv * (1 + shift)).Z;
                    var index = row * width + column;
                    fringed[index] = new Vector3(red, source[index].Y, blue);
                }
            });
            source = fringed;
        }
        var output = new Vector3[image.Length];
        Parallel.For(0, height, row =>
        {
            for (var column = 0; column < width; column++)
            {
                var index = row * width + column;
                var color = source[index];
                if (sharpen > 0.001f && row > 0 && row < height - 1 && column > 0 && column < width - 1)
                {
                    var blur = (source[index - 1] + source[index + 1] + source[index - width] + source[index + width]) * 0.25f;
                    color += (color - blur) * sharpen * 1.2f;
                }
                if (grain > 0.001f)
                {
                    var luminance = Math.Clamp(Sampling.Luminance(color), 0f, 1f);
                    var noise = (Hash(index) - 0.5f) * grain * 0.09f * 4 * luminance * (1 - luminance);
                    color += new Vector3(noise);
                }
                output[index] = Vector3.Max(Vector3.Zero, color);
            }
        });
        return output;
    }

    /// <summary>A repeatable pseudo-random value in [0, 1) from a pixel index.</summary>
    private static float Hash(int index)
    {
        var x = (uint)index * 747796405u + 2891336453u;
        x = ((x >> (int)((x >> 28) + 4)) ^ x) * 277803737u;
        return ((x >> 22) ^ x) / 4294967296f;
    }
}
