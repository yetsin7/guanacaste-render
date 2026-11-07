using System.Numerics;
using System.Runtime.CompilerServices;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// Per-sample random stream. Every (pixel, sample) pair seeds its own PCG32 stream, so results do not depend
/// on thread scheduling and adaptive sampling can give pixels different sample counts. The sub-pixel jitter
/// comes from a rotated R2 low-discrepancy sequence, which converges edges noticeably faster than white noise.
/// </summary>
internal struct Sampler
{
    private const ulong Multiplier = 6364136223846793005UL;
    private const ulong Increment = 1442695040888963407UL;
    // Plastic-constant R2 sequence generators (Roberts 2018).
    private const float R2A = 0.7548776662466927f;
    private const float R2B = 0.5698402909980532f;

    private ulong _state;
    private readonly float _shiftX;
    private readonly float _shiftY;
    private readonly uint _sampleIndex;

    public Sampler(uint pixel, uint sampleIndex, uint seed)
    {
        var mixed = SplitMix((ulong)pixel << 32 | seed);
        _shiftX = (uint)(mixed >> 40) * (1f / 16777216f);
        _shiftY = (uint)(mixed >> 16 & 0xFFFFFF) * (1f / 16777216f);
        _sampleIndex = sampleIndex;
        _state = SplitMix(mixed ^ ((ulong)sampleIndex * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL));
        NextUInt();
    }

    /// <summary>Returns the sub-pixel offset pair for this sample, stratified across the sample sequence.</summary>
    public Vector2 JitterPair()
    {
        var n = (float)_sampleIndex;
        return new Vector2(Fraction(_shiftX + n * R2A), Fraction(_shiftY + n * R2B));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint NextUInt()
    {
        var old = _state;
        _state = old * Multiplier + Increment;
        var xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        var rotation = (int)(old >> 59);
        return (xorShifted >> rotation) | (xorShifted << (-rotation & 31));
    }

    /// <summary>Uniform float in [0, 1).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Fraction(float value) => value - MathF.Floor(value);

    private static ulong SplitMix(ulong value)
    {
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}

/// <summary>Geometry helpers for sampling directions.</summary>
internal static class Sampling
{
    /// <summary>Builds an orthonormal basis (Duff et al. 2017, branchless).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (Vector3 Tangent, Vector3 Bitangent) Basis(Vector3 n)
    {
        var sign = MathF.CopySign(1, n.Z);
        var a = -1 / (sign + n.Z);
        var b = n.X * n.Y * a;
        return (new Vector3(1 + sign * n.X * n.X * a, sign * b, -sign * n.X),
                new Vector3(b, sign + n.Y * n.Y * a, -n.Y));
    }

    /// <summary>Cosine-weighted direction around a normal (pdf = cosθ/π).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 CosineHemisphere(Vector3 normal, float r1, float r2)
    {
        var phi = 2 * MathF.PI * r1;
        var radius = MathF.Sqrt(r2);
        var (tangent, bitangent) = Basis(normal);
        return Vector3.Normalize(tangent * (radius * MathF.Cos(phi)) + bitangent * (radius * MathF.Sin(phi)) +
                                 normal * MathF.Sqrt(MathF.Max(0, 1 - r2)));
    }

    /// <summary>Uniformly samples a direction inside a cone around an axis.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Cone(Vector3 axis, float cosineMax, float r1, float r2)
    {
        var cosine = 1 - r1 * (1 - cosineMax);
        var sine = MathF.Sqrt(MathF.Max(0, 1 - cosine * cosine));
        var phi = 2 * MathF.PI * r2;
        var (tangent, bitangent) = Basis(axis);
        return tangent * (sine * MathF.Cos(phi)) + bitangent * (sine * MathF.Sin(phi)) + axis * cosine;
    }

    /// <summary>Uniform point on a unit disk (concentric mapping), used for the camera lens.</summary>
    public static Vector2 Disk(float u1, float u2)
    {
        var a = 2 * u1 - 1;
        var b = 2 * u2 - 1;
        if (a == 0 && b == 0) return Vector2.Zero;
        float radius, angle;
        if (MathF.Abs(a) > MathF.Abs(b))
        {
            radius = a;
            angle = MathF.PI / 4 * (b / a);
        }
        else
        {
            radius = b;
            angle = MathF.PI / 2 - MathF.PI / 4 * (a / b);
        }
        return new Vector2(radius * MathF.Cos(angle), radius * MathF.Sin(angle));
    }

    /// <summary>Uniform point on a triangle given its corners (barycentric square-root mapping).</summary>
    public static Vector3 TrianglePoint(Vector3 a, Vector3 b, Vector3 c, float u1, float u2)
    {
        var s = MathF.Sqrt(u1);
        return a * (1 - s) + b * (s * (1 - u2)) + c * (s * u2);
    }

    public static float Luminance(Vector3 color) => 0.2126f * color.X + 0.7152f * color.Y + 0.0722f * color.Z;

    /// <summary>Power-heuristic weight (β = 2) of strategy <paramref name="a"/> against <paramref name="b"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float PowerHeuristic(float a, float b)
    {
        var a2 = a * a;
        var b2 = b * b;
        var sum = a2 + b2;
        return sum > 0 && float.IsFinite(sum) ? a2 / sum : (a > b ? 1f : 0f);
    }
}

/// <summary>GGX microfacet reflection with height-correlated Smith masking and visible-normal sampling.</summary>
internal static class Microfacet
{
    /// <summary>Trowbridge–Reitz normal distribution; <paramref name="alpha"/> is roughness squared.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Distribution(float alpha, float nDotH)
    {
        var a2 = alpha * alpha;
        var d = nDotH * nDotH * (a2 - 1) + 1;
        return a2 / (MathF.PI * d * d + 1e-12f);
    }

    /// <summary>Smith masking term for one direction.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float G1(float alpha, float nDotV)
    {
        var a2 = alpha * alpha;
        return 2 * nDotV / (nDotV + MathF.Sqrt(a2 + (1 - a2) * nDotV * nDotV) + 1e-12f);
    }

    /// <summary>Height-correlated masking-shadowing, divided by the 4·NL·NV denominator (the "visibility" term).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Visibility(float alpha, float nDotL, float nDotV)
    {
        var a2 = alpha * alpha;
        var lambdaV = nDotL * MathF.Sqrt(a2 + (1 - a2) * nDotV * nDotV);
        var lambdaL = nDotV * MathF.Sqrt(a2 + (1 - a2) * nDotL * nDotL);
        return 0.5f / (lambdaV + lambdaL + 1e-12f);
    }

    /// <summary>Schlick Fresnel for a colored normal-incidence reflectance.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Fresnel(Vector3 f0, float cosine)
    {
        var m = 1 - Math.Clamp(cosine, 0f, 1f);
        var m2 = m * m;
        return f0 + (Vector3.One - f0) * (m2 * m2 * m);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Fresnel(float f0, float cosine)
    {
        var m = 1 - Math.Clamp(cosine, 0f, 1f);
        var m2 = m * m;
        return f0 + (1 - f0) * (m2 * m2 * m);
    }

    /// <summary>
    /// Samples a microfacet normal visible from <paramref name="view"/> (Heitz 2018), in the local frame where
    /// the surface normal is +Z. Returns the half vector.
    /// </summary>
    public static Vector3 SampleVisibleNormal(Vector3 view, float alpha, float u1, float u2)
    {
        var stretched = Vector3.Normalize(new Vector3(alpha * view.X, alpha * view.Y, view.Z));
        var lengthSquared = stretched.X * stretched.X + stretched.Y * stretched.Y;
        var t1 = lengthSquared > 1e-12f
            ? new Vector3(-stretched.Y, stretched.X, 0) / MathF.Sqrt(lengthSquared)
            : Vector3.UnitX;
        var t2 = Vector3.Cross(stretched, t1);
        var r = MathF.Sqrt(u1);
        var phi = 2 * MathF.PI * u2;
        var p1 = r * MathF.Cos(phi);
        var p2 = r * MathF.Sin(phi);
        var s = 0.5f * (1 + stretched.Z);
        p2 = (1 - s) * MathF.Sqrt(MathF.Max(0, 1 - p1 * p1)) + s * p2;
        var n = t1 * p1 + t2 * p2 + stretched * MathF.Sqrt(MathF.Max(0, 1 - p1 * p1 - p2 * p2));
        return Vector3.Normalize(new Vector3(alpha * n.X, alpha * n.Y, MathF.Max(1e-6f, n.Z)));
    }

    /// <summary>Probability density (per solid angle of the reflected direction) of visible-normal sampling.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Pdf(float alpha, float nDotV, float nDotH, float vDotH) =>
        G1(alpha, nDotV) * Distribution(alpha, nDotH) * MathF.Max(vDotH, 0) / (MathF.Max(nDotV, 1e-6f) * 4 * MathF.Max(vDotH, 1e-6f));
}
