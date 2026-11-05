using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// Turns the renderer's linear radiance into a display image: exposure (manual or measured), bloom, tone mapping,
/// color grading, vignette, sRGB encoding and dithering. Runs on the denoised image only, so it can be repeated
/// cheaply while the user moves the sliders, without re-rendering anything.
/// </summary>
public static partial class ImageProcessor
{
    /// <summary>A neutral finish with no bloom, vignette, grading or dithering, matching the original renderer output.</summary>
    public static ImageFinish Plain { get; } = new()
    {
        Saturation = 1, Contrast = 1, Bloom = 0, Vignette = 0, Dither = false, AutoExposure = false,
        ToneMapper = ToneMapper.Aces
    };

    /// <summary>Returns BGRA32 bytes for display or saving.</summary>
    public static byte[] ToBgra32(Vector3[] linear, int width, int height, float baseExposure, ImageFinish finish,
        Func<FeatureBuffer>? features = null)
    {
        ArgumentNullException.ThrowIfNull(linear);
        ArgumentNullException.ThrowIfNull(finish);
        if (linear.Length != width * height) throw new ArgumentException("Buffer size does not match the image size.");
        var exposure = baseExposure * MathF.Pow(2, Math.Clamp(finish.ExposureStops, -8f, 8f));
        // Auto exposure and fog both read the depth buffer; it is captured once, and only when one of them needs it.
        FeatureBuffer? captured = null;
        FeatureBuffer? Features() => captured ??= features?.Invoke();
        if (finish.AutoExposure) exposure *= MeasureAutoExposure(linear, baseExposure, Features());

        var exposed = new Vector3[linear.Length];
        var balance = WhiteBalance(finish.Temperature) * TintBalance(finish.Tint);
        var depth = finish.Fog > 0.001f ? Features()?.Depth : null;
        // Snow lies on what faces up, so it reads the normals; rain and falling flakes need no buffer at all.
        var snowFeatures = finish.Snow > 0.001f ? Features() : null;
        var normals = snowFeatures?.Normal;
        var surfaceDepth = snowFeatures?.Depth;
        var weatherUnit = WeatherUnit(height);
        var highlight = finish.HighlightDim > 0.001f && !string.IsNullOrWhiteSpace(finish.HighlightMaterial)
            ? BuildHighlight(Features(), finish, width, height) : null;
        Parallel.For(0, height, row =>
        {
            for (var column = 0; column < width; column++)
            {
                var index = row * width + column;
                var value = linear[index] * balance * exposure;
                value = float.IsFinite(value.X + value.Y + value.Z) ? Vector3.Max(value, Vector3.Zero) : Vector3.Zero;
                if (depth is not null) value = ApplyFog(value, depth[index], finish);
                exposed[index] = value;
            }
        });

        // Fire and smoke are painted into the exposed picture, so bloom and glare make the flames glow.
        if (finish.FireSprites is { Count: > 0 } fireSprites) DrawFire(exposed, width, height, fireSprites, Features()?.Depth);

        var glow = finish.Bloom > 0.001f ? Bloom(exposed, width, height) : null;
        var streaks = finish.Glare > 0.001f ? Glare(exposed, width, height) : null;
        // Flare and rays come from the brightest light in view; with none in view they add nothing.
        var spot = finish.LensFlare > 0.001f || finish.LightRays > 0.001f ? FindLightSpot(exposed, width, height) : null;
        var ghosts = spot is not null && finish.LensFlare > 0.001f ? FlareGhosts(spot, width, height, finish.LensFlare) : null;
        var lightRays = spot is not null && finish.LightRays > 0.001f ? LightRays(exposed, width, height, spot, finish.LightRays) : ((Vector3[] Rays, int Width, int Height)?)null;
        var display = new Vector3[width * height];
        var bloomStrength = Math.Clamp(finish.Bloom, 0f, 1f) * 0.6f;
        var vignette = Math.Clamp(finish.Vignette, 0f, 1f);
        Parallel.For(0, height, row =>
        {
            for (var column = 0; column < width; column++)
            {
                var index = row * width + column;
                var color = exposed[index];
                if (glow is not null) color += glow[index] * bloomStrength;
                if (streaks is not null) color += streaks[index] * Math.Clamp(finish.Glare, 0f, 1f);
                if (ghosts is not null) color += FlareAt(ghosts, column, row);
                if (lightRays is { } rays) color += ReadScaled(rays.Rays, rays.Width, rays.Height, RayScale, column, row);
                color = ToneMap(color, finish.ToneMapper);
                color = Grade(color, finish);
                if (finish.Contrast != 1)
                {
                    var contrast = Math.Clamp(finish.Contrast, 0.5f, 2f);
                    color = new Vector3(Pivot(color.X, contrast), Pivot(color.Y, contrast), Pivot(color.Z, contrast));
                }
                if (finish.Saturation != 1)
                {
                    var luminance = Sampling.Luminance(color);
                    color = Vector3.Max(Vector3.Zero, new Vector3(luminance) + (color - new Vector3(luminance)) * finish.Saturation);
                }
                if (finish.Lut is { } lut && finish.LutStrength > 0.001f)
                    color = Vector3.Lerp(color, lut.Sample(color), Math.Clamp(finish.LutStrength, 0f, 1f));
                if (highlight is not null) color = ApplyHighlight(color, highlight, index, finish.HighlightDim);
                if (finish.Rain > 0.001f) color = ApplyRain(color, column, row, weatherUnit, finish.Rain);
                if (finish.Snow > 0.001f)
                {
                    var onSurface = surfaceDepth is not null && !float.IsInfinity(surfaceDepth[index]);
                    color = ApplySnow(color, column, row, weatherUnit, finish.Snow, onSurface ? normals![index] : null, onSurface);
                }
                if (vignette > 0)
                {
                    var dx = (column + 0.5f) / width * 2 - 1;
                    var dy = (row + 0.5f) / height * 2 - 1;
                    color *= 1 - vignette * 0.55f * (dx * dx + dy * dy) * 0.5f;
                }
                display[index] = color;
            }
        });
        if (finish.TiltShift > 0.001f) display = TiltShift(display, width, height, finish.TiltShift, finish.TiltShiftPosition);
        display = Lens(display, width, height, finish);
        var pixels = new byte[width * height * 4];
        Parallel.For(0, height, row =>
        {
            for (var column = 0; column < width; column++)
            {
                var index = row * width + column;
                var color = display[index];
                var noise = finish.Dither ? DitherNoise(index) : 0f;
                pixels[index * 4] = Encode(color.Z, noise);
                pixels[index * 4 + 1] = Encode(color.Y, noise);
                pixels[index * 4 + 2] = Encode(color.X, noise);
                pixels[index * 4 + 3] = 255;
            }
        });
        return pixels;
    }

    /// <summary>
    /// Returns the multiplier that brings the scene's geometric-mean luminance to a pleasant mid-grey. Sky pixels are
    /// ignored so a bright sky does not darken the building, and the correction is limited to a sane range.
    /// </summary>
    public static float MeasureAutoExposure(Vector3[] linear, float baseExposure, FeatureBuffer? features)
    {
        double sum = 0;
        var count = 0;
        for (var index = 0; index < linear.Length; index += 3)
        {
            if (features is not null && float.IsInfinity(features.Depth[index])) continue;
            var luminance = Sampling.Luminance(linear[index]) * baseExposure;
            if (!float.IsFinite(luminance)) continue;
            sum += Math.Log(Math.Clamp(luminance, 0.004f, 40f));
            count++;
        }
        if (count < 16) return 1;
        var geometricMean = Math.Exp(sum / count);
        return (float)Math.Clamp(0.19 / geometricMean, 0.4, 2.5);
    }

    private static float Pivot(float value, float contrast) => 0.4f * MathF.Pow(MathF.Max(value, 0) / 0.4f, contrast);

    private static Vector3 ToneMap(Vector3 color, ToneMapper mapper) => mapper switch
    {
        ToneMapper.Linear => Vector3.Clamp(color, Vector3.Zero, Vector3.One),
        ToneMapper.Soft => new Vector3(Soft(color.X), Soft(color.Y), Soft(color.Z)),
        ToneMapper.AgX => AgX(color),
        _ => new Vector3(Aces(color.X), Aces(color.Y), Aces(color.Z))
    };

    /// <summary>
    /// Channel gains for a white balance shift that keeps luminance: warm raises red and lowers blue, cool the reverse.
    /// The range is about ±1500 K around daylight.
    /// </summary>
    public static Vector3 WhiteBalance(float temperature)
    {
        var t = Math.Clamp(float.IsFinite(temperature) ? temperature : 0, -1f, 1f);
        if (t == 0) return Vector3.One;
        var gains = new Vector3(1 + 0.16f * t, 1 + 0.02f * t, 1 - 0.20f * t);
        return gains / Sampling.Luminance(gains);
    }

    /// <summary>
    /// AgX view transform (Troy Sobotka's design, with Wrensch's polynomial fit of the default contrast curve). Light is
    /// first mixed slightly toward the other primaries, so over-bright colors bleach to white instead of clipping one
    /// channel; the log-encoded result is shaped by the curve and mixed back. The "punchy" finish (30 % more color and a
    /// 1.25 power on the display values) restores the depth that the plain curve lifts, and a gain of 2 brings mid grey to
    /// where the previous ACES curve put it, so automatic exposure keeps its calibration.
    /// </summary>
    private static Vector3 AgX(Vector3 color)
    {
        const float minimumEv = -12.47393f, maximumEv = 4.026069f, punch = 2.2f * 1.25f;
        color = Vector3.Max(color * 2.0f, Vector3.Zero);
        var mixed = new Vector3(
            0.842479062f * color.X + 0.0784336f * color.Y + 0.0792237451f * color.Z,
            0.0423282423f * color.X + 0.878468636f * color.Y + 0.0791661275f * color.Z,
            0.0423756549f * color.X + 0.0784336f * color.Y + 0.879142974f * color.Z);
        static float Curve(float value)
        {
            var log = Math.Clamp(MathF.Log2(MathF.Max(value, 1e-10f)), minimumEv, maximumEv);
            var x = (log - minimumEv) / (maximumEv - minimumEv);
            var x2 = x * x;
            var x4 = x2 * x2;
            return 15.5f * x4 * x2 - 40.14f * x4 * x + 31.96f * x4 - 6.868f * x2 * x + 0.4298f * x2 + 0.1191f * x - 0.00232f;
        }
        var shaped = new Vector3(Curve(mixed.X), Curve(mixed.Y), Curve(mixed.Z));
        // Part of the punchy look: the log encoding mutes color, so it is given back here, before mixing out.
        var grey = Sampling.Luminance(shaped);
        shaped = Vector3.Max(Vector3.Zero, new Vector3(grey) + (shaped - new Vector3(grey)) * 1.3f);
        var display = new Vector3(
            1.19687901f * shaped.X - 0.0980208811f * shaped.Y - 0.099029744f * shaped.Z,
            -0.0528968518f * shaped.X + 1.15190313f * shaped.Y - 0.0989611768f * shaped.Z,
            -0.0529716355f * shaped.X - 0.0980434501f * shaped.Y + 1.15107367f * shaped.Z);
        display = Vector3.Clamp(display, Vector3.Zero, Vector3.One);
        // The curve's output is display-encoded; return linear light, which the sRGB encoder expects.
        return new Vector3(MathF.Pow(display.X, punch), MathF.Pow(display.Y, punch), MathF.Pow(display.Z, punch));
    }

    /// <summary>ACES filmic approximation (Narkowicz 2015).</summary>
    private static float Aces(float x)
    {
        x = MathF.Max(0, x);
        return Math.Clamp(x * (2.51f * x + 0.03f) / (x * (2.43f * x + 0.59f) + 0.14f), 0f, 1f);
    }

    /// <summary>Extended Reinhard with a white point at 6, so highlights roll off instead of clipping.</summary>
    private static float Soft(float x)
    {
        x = MathF.Max(0, x);
        return Math.Clamp(x * (1 + x / 36f) / (1 + x), 0f, 1f);
    }

    private static byte Encode(float linear, float dither)
    {
        var clamped = Math.Clamp(linear, 0f, 1f);
        var srgb = clamped <= 0.0031308f ? 12.92f * clamped : 1.055f * MathF.Pow(clamped, 1 / 2.4f) - 0.055f;
        return (byte)Math.Clamp(srgb * 255 + 0.5f + dither, 0f, 255f);
    }

    /// <summary>Triangular-distribution noise of ±1 code value, from a hash of the pixel index.</summary>
    private static float DitherNoise(int index)
    {
        var h = (uint)index * 2654435761u;
        h ^= h >> 15;
        h *= 2246822519u;
        h ^= h >> 13;
        var a = (h & 0xFFFF) / 65536f;
        var b = (h >> 16) / 65536f;
        return a + b - 1f;
    }

    /// <summary>Soft glow of the parts brighter than 1: two blur scales at reduced resolution, upsampled bilinearly.</summary>
    private static Vector3[] Bloom(Vector3[] image, int width, int height)
    {
        var small = Math.Max(1, width / 4);
        var smallHeight = Math.Max(1, height / 4);
        var bright = Downsample(image, width, height, small, smallHeight, threshold: true);
        var near = BoxBlur(bright, small, smallHeight, Math.Max(2, small / 120), passes: 2);
        var tiny = Math.Max(1, small / 4);
        var tinyHeight = Math.Max(1, smallHeight / 4);
        var far = BoxBlur(Downsample(near, small, smallHeight, tiny, tinyHeight, threshold: false), tiny, tinyHeight,
            Math.Max(2, tiny / 40), passes: 2);
        var result = new Vector3[width * height];
        Parallel.For(0, height, row =>
        {
            for (var column = 0; column < width; column++)
            {
                var nearValue = Sample(near, small, smallHeight, (column + 0.5f) / width, (row + 0.5f) / height);
                var farValue = Sample(far, tiny, tinyHeight, (column + 0.5f) / width, (row + 0.5f) / height);
                result[row * width + column] = nearValue * 0.6f + farValue * 0.9f;
            }
        });
        return result;
    }

    private static Vector3[] Downsample(Vector3[] source, int width, int height, int targetWidth, int targetHeight,
        bool threshold)
    {
        var target = new Vector3[targetWidth * targetHeight];
        Parallel.For(0, targetHeight, y =>
        {
            var y0 = y * height / targetHeight;
            var y1 = Math.Max(y0 + 1, (y + 1) * height / targetHeight);
            for (var x = 0; x < targetWidth; x++)
            {
                var x0 = x * width / targetWidth;
                var x1 = Math.Max(x0 + 1, (x + 1) * width / targetWidth);
                var sum = Vector3.Zero;
                for (var yy = y0; yy < y1; yy++)
                for (var xx = x0; xx < x1; xx++)
                {
                    var value = source[yy * width + xx];
                    // Only energy above the displayable range glows; ordinary surfaces stay crisp.
                    sum += threshold ? Vector3.Max(value - Vector3.One, Vector3.Zero) : value;
                }
                target[y * targetWidth + x] = sum / ((y1 - y0) * (x1 - x0));
            }
        });
        return target;
    }

    private static Vector3[] BoxBlur(Vector3[] source, int width, int height, int radius, int passes)
    {
        var current = source;
        for (var pass = 0; pass < passes; pass++)
        {
            var horizontal = new Vector3[current.Length];
            var input = current;
            Parallel.For(0, height, y =>
            {
                for (var x = 0; x < width; x++)
                {
                    var sum = Vector3.Zero;
                    var count = 0;
                    for (var k = -radius; k <= radius; k++)
                    {
                        var xx = x + k;
                        if ((uint)xx >= (uint)width) continue;
                        sum += input[y * width + xx];
                        count++;
                    }
                    horizontal[y * width + x] = sum / count;
                }
            });
            var vertical = new Vector3[current.Length];
            Parallel.For(0, height, y =>
            {
                for (var x = 0; x < width; x++)
                {
                    var sum = Vector3.Zero;
                    var count = 0;
                    for (var k = -radius; k <= radius; k++)
                    {
                        var yy = y + k;
                        if ((uint)yy >= (uint)height) continue;
                        sum += horizontal[yy * width + x];
                        count++;
                    }
                    vertical[y * width + x] = sum / count;
                }
            });
            current = vertical;
        }
        return current;
    }

    private static Vector3 Sample(Vector3[] image, int width, int height, float u, float v)
    {
        var x = Math.Clamp(u * width - 0.5f, 0, width - 1);
        var y = Math.Clamp(v * height - 0.5f, 0, height - 1);
        var x0 = (int)x;
        var y0 = (int)y;
        var x1 = Math.Min(x0 + 1, width - 1);
        var y1 = Math.Min(y0 + 1, height - 1);
        var fx = x - x0;
        var fy = y - y0;
        var top = Vector3.Lerp(image[y0 * width + x0], image[y0 * width + x1], fx);
        var bottom = Vector3.Lerp(image[y1 * width + x0], image[y1 * width + x1], fx);
        return Vector3.Lerp(top, bottom, fy);
    }
}
