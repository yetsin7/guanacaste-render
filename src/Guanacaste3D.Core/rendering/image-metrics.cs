using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>Quality of a test image measured against a reference of the same size.</summary>
/// <param name="Psnr">Peak signal-to-noise ratio in dB over all channels (higher is better; ∞ when identical).</param>
/// <param name="Ssim">Mean structural similarity of luma in [−1, 1] (1 is identical).</param>
/// <param name="MeanDeltaE">Mean CIELAB ΔE*76 between corresponding pixels (about 2.3 is a just-noticeable difference).</param>
/// <param name="OutOfTolerance">Fraction of pixels whose largest channel difference exceeds the tolerance.</param>
public readonly record struct ImageQuality(double Psnr, double Ssim, double MeanDeltaE, double OutOfTolerance);

/// <summary>
/// Full-reference image metrics for regression tests and renderer A/B experiments. Inputs are
/// display-referred pixels in [0, 1] (tone-mapped and gamma-encoded), so errors reflect what a person sees
/// rather than raw radiance dominated by a few bright pixels.
/// </summary>
public static class ImageMetrics
{
    private const int SsimWindow = 8;
    private const int SsimStride = 4;

    /// <summary>Compares <paramref name="test"/> with <paramref name="reference"/>.</summary>
    public static ImageQuality Compare(ReadOnlySpan<Vector3> test, ReadOnlySpan<Vector3> reference, int width, int height,
        float tolerance = 4f / 255f)
    {
        if (test.Length != width * height || reference.Length != width * height)
            throw new ArgumentException("Both images must contain width × height pixels.");
        double squared = 0, deltaE = 0;
        var outside = 0;
        for (var index = 0; index < test.Length; index++)
        {
            var difference = test[index] - reference[index];
            squared += difference.X * difference.X + difference.Y * difference.Y + difference.Z * difference.Z;
            if (MathF.Max(MathF.Abs(difference.X), MathF.Max(MathF.Abs(difference.Y), MathF.Abs(difference.Z))) > tolerance)
                outside++;
            deltaE += Vector3.Distance(ToLab(test[index]), ToLab(reference[index]));
        }
        var mse = squared / (test.Length * 3.0);
        var psnr = mse <= 1e-12 ? double.PositiveInfinity : 10 * Math.Log10(1 / mse);
        return new ImageQuality(psnr, Ssim(test, reference, width, height), deltaE / test.Length,
            outside / (double)test.Length);
    }

    /// <summary>Mean structural similarity (Wang et al. 2004) of luma over 8×8 windows with stride 4.</summary>
    public static double Ssim(ReadOnlySpan<Vector3> test, ReadOnlySpan<Vector3> reference, int width, int height)
    {
        if (width < SsimWindow || height < SsimWindow) return 1;
        var a = new float[test.Length];
        var b = new float[test.Length];
        for (var i = 0; i < a.Length; i++)
        {
            a[i] = Luma(test[i]);
            b[i] = Luma(reference[i]);
        }
        const double c1 = 0.01 * 0.01, c2 = 0.03 * 0.03;
        double total = 0;
        var windows = 0;
        for (var y = 0; y + SsimWindow <= height; y += SsimStride)
        {
            for (var x = 0; x + SsimWindow <= width; x += SsimStride)
            {
                double meanA = 0, meanB = 0;
                for (var j = 0; j < SsimWindow; j++)
                    for (var i = 0; i < SsimWindow; i++)
                    {
                        var index = (y + j) * width + x + i;
                        meanA += a[index];
                        meanB += b[index];
                    }
                const double n = SsimWindow * SsimWindow;
                meanA /= n;
                meanB /= n;
                double varianceA = 0, varianceB = 0, covariance = 0;
                for (var j = 0; j < SsimWindow; j++)
                    for (var i = 0; i < SsimWindow; i++)
                    {
                        var index = (y + j) * width + x + i;
                        var da = a[index] - meanA;
                        var db = b[index] - meanB;
                        varianceA += da * da;
                        varianceB += db * db;
                        covariance += da * db;
                    }
                varianceA /= n - 1;
                varianceB /= n - 1;
                covariance /= n - 1;
                total += (2 * meanA * meanB + c1) * (2 * covariance + c2) /
                         ((meanA * meanA + meanB * meanB + c1) * (varianceA + varianceB + c2));
                windows++;
            }
        }
        return windows == 0 ? 1 : total / windows;
    }

    private static float Luma(Vector3 color) => 0.2126f * color.X + 0.7152f * color.Y + 0.0722f * color.Z;

    /// <summary>Converts gamma-encoded sRGB in [0, 1] to CIELAB (D65).</summary>
    private static Vector3 ToLab(Vector3 srgb)
    {
        var r = Linear(srgb.X);
        var g = Linear(srgb.Y);
        var b = Linear(srgb.Z);
        var x = (0.4124564f * r + 0.3575761f * g + 0.1804375f * b) / 0.95047f;
        var y = 0.2126729f * r + 0.7151522f * g + 0.0721750f * b;
        var z = (0.0193339f * r + 0.1191920f * g + 0.9503041f * b) / 1.08883f;
        var fx = LabF(x);
        var fy = LabF(y);
        var fz = LabF(z);
        return new Vector3(116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    private static float Linear(float value) =>
        value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

    private static float LabF(float t) => t > 0.008856f ? MathF.Cbrt(t) : 7.787f * t + 16f / 116;
}
