using System.Buffers;
using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// Removes noise from the indirect light of a render. The renderer talks to this interface only, so a vendor or learned
/// denoiser (for example Open Image Denoise) can replace the built-in one without touching the renderer.
/// </summary>
/// <remarks>
/// An implementation must only redistribute light between pixels that the geometry buffers say belong to the same
/// surface; it must not add detail the model does not contain.
/// </remarks>
public interface IDenoiser
{
    string Name { get; }

    /// <summary>Returns a new, filtered copy of <paramref name="indirect"/> (linear radiance, same layout).</summary>
    /// <param name="indirect">Noisy indirect radiance per pixel; it may be longer than the image.</param>
    /// <param name="varianceOfMean">Variance of the mean luminance of each pixel; large where it is unreliable.</param>
    /// <param name="features">First-hit normal, albedo and depth at the image resolution.</param>
    /// <param name="relativeNoise">Average noise of the whole image: standard error over brightness (0 clean, 2 hopeless).</param>
    Vector3[] Denoise(Vector3[] indirect, float[] varianceOfMean, FeatureBuffer features, float relativeNoise);
}

/// <summary>
/// The built-in denoiser: the variance-guided à-trous filter whose tolerance falls from <see cref="SigmaNoisy"/> to
/// <see cref="SigmaClean"/> as the measured noise of the image falls from <see cref="NoisyAbove"/> to <see cref="CleanBelow"/>.
/// </summary>
public sealed record VarianceGuidedDenoiser(float SigmaNoisy = 6f, float SigmaClean = 2f, float CleanBelow = 0.05f,
    float NoisyAbove = 0.25f, int Iterations = 4) : IDenoiser
{
    public string Name => "Variance-guided à-trous (CPU)";

    public Vector3[] Denoise(Vector3[] indirect, float[] varianceOfMean, FeatureBuffer features, float relativeNoise)
    {
        // Variance from a few samples underestimates heavy-tailed noise (rare bright paths), so the tolerance follows the
        // image's measured relative noise: wide while it is noisy, narrow only once it is clean.
        var clean = Math.Clamp((NoisyAbove - relativeNoise) / Math.Max(NoisyAbove - CleanBelow, 1e-4f), 0f, 1f);
        var sigma = SigmaNoisy + (SigmaClean - SigmaNoisy) * clean;
        return AtrousDenoiser.ApplyGuided(indirect, varianceOfMean, features, Iterations, sigma);
    }
}

/// <summary>Cheap stand-ins for the transcendental functions that dominate the filter's inner loop.</summary>
internal static class FastMath
{
    private const int TableSize = 2048;
    private const float Range = 16f;
    private const float Scale = TableSize / Range;
    private static readonly float[] ExpTable = BuildTable();

    private static float[] BuildTable()
    {
        var table = new float[TableSize + 2];
        for (var i = 0; i < table.Length; i++) table[i] = MathF.Exp(-i / Scale);
        return table;
    }

    /// <summary>exp(−a) for a ≥ 0 by table interpolation (error under 1e-5); zero beyond 16.</summary>
    public static float ExpNegative(float a)
    {
        if (a >= Range) return 0;
        var position = a * Scale;
        var index = (int)position;
        var fraction = position - index;
        return ExpTable[index] + (ExpTable[index + 1] - ExpTable[index]) * fraction;
    }

    /// <summary>x raised to 64 by repeated squaring, for x in [0, 1].</summary>
    public static float Pow64(float x)
    {
        x *= x; x *= x; x *= x; x *= x; x *= x; x *= x;
        return x;
    }

    /// <summary>x raised to 32.</summary>
    public static float Pow32(float x)
    {
        x *= x; x *= x; x *= x; x *= x; x *= x;
        return x;
    }
}

/// <summary>Per-pixel first-hit features used to keep edges sharp while filtering noise.</summary>
public sealed class FeatureBuffer(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public Vector3[] Normal { get; } = new Vector3[width * height];
    public Vector3[] Albedo { get; } = new Vector3[width * height];
    /// <summary>Camera distance of the first hit; +∞ for sky.</summary>
    public float[] Depth { get; } = new float[width * height];
    /// <summary>Index into <see cref="MaterialNames"/> of the surface at each pixel; <see cref="NoMaterial"/> for sky.</summary>
    public ushort[] Material { get; } = Filled(width * height);
    /// <summary>Name of each material of the scene, so an effect can pick a surface by the name the person sees.</summary>
    public string[] MaterialNames { get; init; } = [];
    public const ushort NoMaterial = ushort.MaxValue;

    private static ushort[] Filled(int count)
    {
        var values = new ushort[count];
        Array.Fill(values, NoMaterial);
        return values;
    }
}

/// <summary>
/// Edge-avoiding à-trous wavelet filter (Dammertz et al. 2010) on albedo-demodulated radiance.
/// Each iteration doubles the B3-spline tap spacing, so three passes cover a 29×29 footprint
/// at the cost of 25 taps per pixel per pass.
/// </summary>
public static class AtrousDenoiser
{
    private static readonly float[] Kernel = [1f / 16, 1f / 4, 3f / 8, 1f / 4, 1f / 16];

    /// <summary>Returns a filtered copy of <paramref name="radiance"/> (linear, same layout).</summary>
    /// <remarks>Defaults were tuned against 256-spp references of the sample house (indirect error −47 % at 4 spp).</remarks>
    public static Vector3[] Apply(Vector3[] radiance, FeatureBuffer features, int iterations = 3,
        float colorSigma = 0.2f, float normalPower = 64, float depthSigma = 0.04f)
    {
        ArgumentNullException.ThrowIfNull(radiance);
        ArgumentNullException.ThrowIfNull(features);
        var width = features.Width;
        var height = features.Height;
        if (radiance.Length != width * height) throw new ArgumentException("Buffer sizes differ.");

        // Divide out surface colour so the filter only smooths lighting, preserving texture and edges.
        var current = new Vector3[radiance.Length];
        for (var i = 0; i < radiance.Length; i++)
            current[i] = float.IsInfinity(features.Depth[i]) ? radiance[i] : radiance[i] / Demodulator(features.Albedo[i]);
        var next = new Vector3[radiance.Length];

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var step = 1 << iteration;
            var sigma = colorSigma * MathF.Pow(2, -iteration * 0.5f);
            var inverseColorVariance = 1 / (2 * sigma * sigma);
            var source = current;
            var target = next;
            Parallel.For(0, height, y =>
            {
                for (var x = 0; x < width; x++)
                {
                    var p = y * width + x;
                    var depthP = features.Depth[p];
                    if (float.IsInfinity(depthP))
                    {
                        target[p] = source[p];
                        continue;
                    }
                    var colorP = source[p];
                    var normalP = features.Normal[p];
                    var luminanceP = Luminance(colorP);
                    var sum = Vector3.Zero;
                    var weightSum = 0f;
                    for (var ky = -2; ky <= 2; ky++)
                    {
                        var qy = y + ky * step;
                        if ((uint)qy >= (uint)height) continue;
                        for (var kx = -2; kx <= 2; kx++)
                        {
                            var qx = x + kx * step;
                            if ((uint)qx >= (uint)width) continue;
                            var q = qy * width + qx;
                            var depthQ = features.Depth[q];
                            if (float.IsInfinity(depthQ)) continue;
                            var cosine = MathF.Max(0, Vector3.Dot(normalP, features.Normal[q]));
                            var normalWeight = normalPower == 64 ? FastMath.Pow64(cosine) : MathF.Pow(cosine, normalPower);
                            var depthWeight = FastMath.ExpNegative(MathF.Abs(depthP - depthQ) / (depthSigma * depthP * step + 1e-4f));
                            var luminanceDelta = Luminance(source[q]) - luminanceP;
                            var colorWeight = FastMath.ExpNegative(luminanceDelta * luminanceDelta * inverseColorVariance /
                                                                   (1 + luminanceP));
                            var weight = Kernel[kx + 2] * Kernel[ky + 2] * normalWeight * depthWeight * colorWeight;
                            sum += source[q] * weight;
                            weightSum += weight;
                        }
                    }
                    target[p] = weightSum > 1e-8f ? sum / weightSum : colorP;
                }
            });
            (current, next) = (next, current);
        }

        for (var i = 0; i < current.Length; i++)
            if (!float.IsInfinity(features.Depth[i])) current[i] *= Demodulator(features.Albedo[i]);
        return current;
    }

    /// <summary>
    /// Variance-guided à-trous filter (after Schied et al. 2017, single frame). The luminance edge-stopping tolerance of
    /// each pixel comes from its own sample variance, so a noisy pixel is blended widely with similar neighbors while a
    /// well-converged one is left alone. That removes the bias floor of a fixed-strength filter: more samples simply
    /// mean less filtering, and the result approaches the raw estimate.
    /// </summary>
    /// <param name="radiance">Linear radiance to filter (the indirect component).</param>
    /// <param name="varianceOfMean">Variance of the per-pixel mean of <paramref name="radiance"/>'s luminance.</param>
    public static Vector3[] ApplyGuided(Vector3[] radiance, float[] varianceOfMean, FeatureBuffer features, int iterations = 4,
        float lumaSigma = 4f, float normalPower = 64, float depthSigma = 0.04f)
    {
        ArgumentNullException.ThrowIfNull(radiance);
        ArgumentNullException.ThrowIfNull(varianceOfMean);
        ArgumentNullException.ThrowIfNull(features);
        var width = features.Width;
        var height = features.Height;
        var count = width * height;
        // The inputs may be longer than the image (they can come from a buffer pool); they must not be shorter.
        if (radiance.Length < count || varianceOfMean.Length < count) throw new ArgumentException("Buffer sizes differ.");

        // Working buffers are rented: a 1080p frame would otherwise allocate over 100 MB per call, nearly all of it on
        // the large object heap. Everything rented is fully written before it is read.
        var pool = ArrayPool<Vector3>.Shared;
        var floatPool = ArrayPool<float>.Shared;
        var current = pool.Rent(count);
        var next = pool.Rent(count);
        var variance = floatPool.Rent(count);
        var nextVariance = floatPool.Rent(count);
        var luminance = floatPool.Rent(count);
        float[]? smoothed = null;
        try
        {
        for (var i = 0; i < count; i++)
        {
            if (float.IsInfinity(features.Depth[i])) { current[i] = radiance[i]; variance[i] = 0; continue; }
            var demodulator = Demodulator(features.Albedo[i]);
            current[i] = radiance[i] / demodulator;
            var luminanceScale = MathF.Max(Luminance(demodulator), 0.02f);
            variance[i] = varianceOfMean[i] / (luminanceScale * luminanceScale);
        }
        smoothed = SmoothVariance(variance, features, floatPool.Rent(count));
        (variance, smoothed) = (smoothed, variance);   // the unsmoothed buffer is now free to be returned
        var fastNormal = normalPower == 64;

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var step = 1 << iteration;
            var source = current;
            var sourceVariance = variance;
            var target = next;
            var targetVariance = nextVariance;
            for (var i = 0; i < count; i++) luminance[i] = Luminance(source[i]);
            Parallel.For(0, height, y =>
            {
                for (var x = 0; x < width; x++)
                {
                    var p = y * width + x;
                    var depthP = features.Depth[p];
                    if (float.IsInfinity(depthP))
                    {
                        target[p] = source[p];
                        targetVariance[p] = sourceVariance[p];
                        continue;
                    }
                    var normalP = features.Normal[p];
                    var luminanceP = luminance[p];
                    var inverseSigma = 1 / (lumaSigma * MathF.Sqrt(MathF.Max(sourceVariance[p], 0)) + 1e-4f);
                    var inverseDepth = 1 / (depthSigma * depthP * step + 1e-4f);
                    var sum = Vector3.Zero;
                    var varianceSum = 0f;
                    var weightSum = 0f;
                    for (var ky = -2; ky <= 2; ky++)
                    {
                        var qy = y + ky * step;
                        if ((uint)qy >= (uint)height) continue;
                        for (var kx = -2; kx <= 2; kx++)
                        {
                            var qx = x + kx * step;
                            if ((uint)qx >= (uint)width) continue;
                            var q = qy * width + qx;
                            var depthQ = features.Depth[q];
                            if (float.IsInfinity(depthQ)) continue;
                            var cosine = MathF.Max(0, Vector3.Dot(normalP, features.Normal[q]));
                            var normalWeight = fastNormal ? FastMath.Pow64(cosine) : MathF.Pow(cosine, normalPower);
                            if (normalWeight < 1e-3f) continue;
                            var depthWeight = FastMath.ExpNegative(MathF.Abs(depthP - depthQ) * inverseDepth);
                            if (depthWeight < 1e-3f) continue;
                            var lumaWeight = FastMath.ExpNegative(MathF.Abs(luminance[q] - luminanceP) * inverseSigma);
                            var weight = Kernel[kx + 2] * Kernel[ky + 2] * normalWeight * depthWeight * lumaWeight;
                            sum += source[q] * weight;
                            varianceSum += sourceVariance[q] * weight * weight;
                            weightSum += weight;
                        }
                    }
                    if (weightSum > 1e-8f)
                    {
                        target[p] = sum / weightSum;
                        targetVariance[p] = varianceSum / (weightSum * weightSum);
                    }
                    else
                    {
                        target[p] = source[p];
                        targetVariance[p] = sourceVariance[p];
                    }
                }
            });
            (current, next) = (next, current);
            (variance, nextVariance) = (nextVariance, variance);
        }

        var output = new Vector3[count];
        for (var i = 0; i < count; i++)
            output[i] = float.IsInfinity(features.Depth[i]) ? current[i] : current[i] * Demodulator(features.Albedo[i]);
        return output;
        }
        finally
        {
            pool.Return(current);
            pool.Return(next);
            floatPool.Return(variance);
            floatPool.Return(nextVariance);
            floatPool.Return(luminance);
            if (smoothed is not null) floatPool.Return(smoothed);
        }
    }

    /// <summary>3×3 geometry-aware average of the variance map: single-pixel variance estimates are themselves very noisy.</summary>
    private static float[] SmoothVariance(float[] variance, FeatureBuffer features, float[] result)
    {
        var width = features.Width;
        var height = features.Height;
        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var p = y * width + x;
                if (float.IsInfinity(features.Depth[p])) { result[p] = 0; continue; }
                var sum = 0f;
                var weightSum = 0f;
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var qx = x + dx;
                    var qy = y + dy;
                    if ((uint)qx >= (uint)width || (uint)qy >= (uint)height) continue;
                    var q = qy * width + qx;
                    if (float.IsInfinity(features.Depth[q])) continue;
                    var weight = FastMath.Pow32(MathF.Max(0, Vector3.Dot(features.Normal[p], features.Normal[q]))) *
                                 FastMath.ExpNegative(MathF.Abs(features.Depth[p] - features.Depth[q]) / (0.04f * features.Depth[p] + 1e-4f));
                    sum += variance[q] * weight;
                    weightSum += weight;
                }
                result[p] = weightSum > 1e-6f ? sum / weightSum : variance[p];
            }
        });
        return result;
    }

    private static Vector3 Demodulator(Vector3 albedo) => Vector3.Max(albedo, new Vector3(0.02f));

    private static float Luminance(Vector3 color) => 0.2126f * color.X + 0.7152f * color.Y + 0.0722f * color.Z;
}
