using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>How the sky and sun are modeled.</summary>
public enum SkyKind
{
    /// <summary>Original analytic gradient with a fixed warm sun; kept for reproducible comparisons.</summary>
    Simple,
    /// <summary>Clear sky from the Perez daylight distribution (Preetham et al. 1999) with a sun colored by air mass.</summary>
    Clear,
    /// <summary>CIE standard overcast sky: soft, shadowless light that is brightest at the zenith.</summary>
    Overcast
}

/// <summary>
/// Evaluates sky radiance for a direction and the sun's color for the current sun height. All values
/// are linear RGB in the renderer's relative units; <see cref="RenderSettings.SkyIntensity"/> scales them.
/// </summary>
public sealed class SkyModel
{
    /// <summary>Luminance of the legacy gradient at the zenith, which the physical model is calibrated to match.</summary>
    private const float ReferenceZenithLuminance = 0.434f;

    private readonly SkyKind _kind;
    private readonly Vector3 _sun;
    private readonly float _sunAltitude;   // radians
    private readonly float _daylight;      // 0 at night, 1 in full day
    private readonly float[] _y = new float[5];
    private readonly float[] _x = new float[5];
    private readonly float[] _z = new float[5];
    private readonly float _zenithLuminanceScale;
    private readonly float _zenithX;
    private readonly float _zenithY;
    private readonly float _zenithShape;   // F(0, θs): normalizes the luminance distribution
    private static readonly Vector3 BaseSunColor = new(1.0f, 0.93f, 0.82f);

    public SkyModel(SkyKind kind, Vector3 sunDirection, float turbidity = 2.5f)
    {
        _kind = kind;
        _sun = sunDirection.LengthSquared() > 1e-8f ? Vector3.Normalize(sunDirection) : Vector3.UnitZ;
        _sunAltitude = MathF.Asin(Math.Clamp(_sun.Z, -1f, 1f));
        var degrees = _sunAltitude * 180 / MathF.PI;
        _daylight = SmoothStep(-9f, 3f, degrees);
        turbidity = float.IsFinite(turbidity) ? Math.Clamp(turbidity, 1.7f, 10f) : 2.5f;
        Turbidity = turbidity;

        // Perez coefficients as linear functions of turbidity (Preetham, Shirley and Smits).
        Fill(_y, turbidity, [0.1787f, -1.4630f], [-0.3554f, 0.4275f], [-0.0227f, 5.3251f], [0.1206f, -2.5771f], [-0.0670f, 0.3703f]);
        Fill(_x, turbidity, [-0.0193f, -0.2592f], [-0.0665f, 0.0008f], [-0.0004f, 0.2125f], [-0.0641f, -0.8989f], [-0.0033f, 0.0452f]);
        Fill(_z, turbidity, [-0.0167f, -0.2608f], [-0.0950f, 0.0092f], [-0.0079f, 0.2102f], [-0.0441f, -1.6537f], [-0.0109f, 0.0529f]);

        // The model is valid only with the sun above the horizon; below it the sky is faded, not extrapolated.
        var thetaSun = MathF.PI / 2 - MathF.Max(_sunAltitude, 1.0f * MathF.PI / 180);
        _zenithX = ZenithChromaticity(turbidity, thetaSun, xChannel: true);
        _zenithY = ZenithChromaticity(turbidity, thetaSun, xChannel: false);
        _zenithShape = Perez(_y, 1f, thetaSun);
        _zenithLuminanceScale = ZenithScale(turbidity, thetaSun);
    }

    public float Turbidity { get; }

    /// <summary>Sun height above the horizon in degrees; negative when it has set.</summary>
    public float SunAltitudeDegrees => _sunAltitude * 180 / MathF.PI;

    /// <summary>
    /// Color of direct sunlight. Longer air paths at low sun remove blue first, so sunrise and sunset turn
    /// orange and dim without any special casing. Returns zero once the sun is below the horizon.
    /// </summary>
    public Vector3 SunColor
    {
        get
        {
            if (_kind == SkyKind.Overcast || _sunAltitude <= 0) return Vector3.Zero;
            if (_kind == SkyKind.Simple) return BaseSunColor;
            var altitude = Math.Max(_sunAltitude, 0.5f * MathF.PI / 180);
            // Relative optical air mass (Kasten and Young); 1 at the zenith, ~38 at the horizon.
            var degrees = altitude * 180 / MathF.PI;
            var airMass = 1 / (MathF.Sin(altitude) + 0.50572f * MathF.Pow(degrees + 6.07995f, -1.6364f));
            var haze = 0.7f + 0.12f * Turbidity;
            var optical = new Vector3(0.095f, 0.17f, 0.33f) * haze;
            var extinction = new Vector3(MathF.Exp(-(airMass - 1) * optical.X),
                MathF.Exp(-(airMass - 1) * optical.Y), MathF.Exp(-(airMass - 1) * optical.Z));
            return BaseSunColor * extinction * SmoothStep(0f, 2.5f, degrees);
        }
    }

    /// <summary>Relative strength of the sky at this sun height, 0 at night and 1 by day, for tuning lights.</summary>
    public float Daylight => _daylight;

    /// <summary>Returns sky radiance in a direction (Z up), excluding the sun disk.</summary>
    public Vector3 Radiance(Vector3 direction)
    {
        return _kind switch
        {
            SkyKind.Simple => SimpleSky(direction),
            SkyKind.Overcast => OvercastSky(direction),
            _ => ClearSky(direction)
        };
    }

    private Vector3 SimpleSky(Vector3 direction)
    {
        var altitude = MathF.Max(direction.Z, -0.2f);
        var zenith = new Vector3(0.24f, 0.45f, 0.85f);
        var horizon = new Vector3(0.78f, 0.84f, 0.90f);
        var ground = new Vector3(0.32f, 0.30f, 0.27f);
        var sky = altitude >= 0
            ? Vector3.Lerp(horizon, zenith, MathF.Pow(altitude, 0.55f))
            : Vector3.Lerp(horizon, ground, MathF.Min(1, -altitude * 5));
        var sunAlignment = MathF.Max(0, Vector3.Dot(direction, _sun));
        sky += BaseSunColor * (0.35f * MathF.Pow(sunAlignment, 24)); // forward-scattering glow
        return sky;
    }

    private Vector3 OvercastSky(Vector3 direction)
    {
        var altitude = Math.Clamp(direction.Z, 0f, 1f);
        // CIE overcast: L(θ) = Lz (1 + 2 sin h) / 3. Dim it with the sun so overcast dusk still goes dark.
        var luminance = 0.62f * (1 + 2 * altitude) / 3 * (0.12f + 0.88f * _daylight);
        var below = direction.Z < 0 ? Vector3.Lerp(new Vector3(1f), new Vector3(0.45f), MathF.Min(1, -direction.Z * 4)) : Vector3.One;
        return new Vector3(0.88f, 0.93f, 1.0f) * luminance * below + NightFloor(direction);
    }

    private Vector3 ClearSky(Vector3 direction)
    {
        var cosTheta = Math.Clamp(direction.Z, -1f, 1f);
        var below = cosTheta < 0;
        // Below the horizon blend toward a ground-colored haze instead of evaluating the formula out of range.
        var viewCos = MathF.Max(cosTheta, 0.012f);
        var theta = MathF.Acos(viewCos);
        var cosGamma = Math.Clamp(Vector3.Dot(direction.Z >= 0 ? direction : new Vector3(direction.X, direction.Y, 0), _sun), -1f, 1f);
        var gamma = MathF.Acos(cosGamma);

        var luminanceShape = Perez(_y, viewCos, gamma, cosGamma) / _zenithShape;
        var x = _zenithX * Perez(_x, viewCos, gamma, cosGamma) / PerezAtZenith(_x, ThetaSun());
        var y = _zenithY * Perez(_z, viewCos, gamma, cosGamma) / PerezAtZenith(_z, ThetaSun());
        var luminance = ReferenceZenithLuminance * _zenithLuminanceScale * luminanceShape;
        var color = ToLinearRgb(x, y, luminance);

        // The Perez fit turns the horizon pink at low turbidity; a real daytime horizon is a pale, almost white blue
        // (light scattered through a long column of air). The band is whitened by day and left alone near sunset,
        // where its warmth is real.
        var daytime = 1 - SmoothStep(25f, 2f, Math.Clamp(SunAltitudeDegrees, 0f, 25f));
        var whitening = MathF.Exp(-MathF.Max(direction.Z, 0) * 5f) * 0.75f * daytime;
        if (whitening > 0.001f)
        {
            var paleBlue = new Vector3(0.90f, 0.95f, 1.0f);
            color = Vector3.Lerp(color, paleBlue * (Sampling.Luminance(color) / Sampling.Luminance(paleBlue)), whitening);
        }

        // Sunset warmth: low sun tints the horizon near its azimuth with the extincted sun color.
        var degrees = SunAltitudeDegrees;
        if (degrees < 25)
        {
            var low = SmoothStep(25f, 2f, MathF.Max(degrees, 0));
            var horizonBand = MathF.Exp(-MathF.Max(direction.Z, 0) * 7f);
            var near = MathF.Exp(-gamma * gamma / 0.8f);
            var glow = SunColorForGlow() * (0.9f * low * horizonBand * near);
            color += glow;
        }
        color *= _daylight;
        color += NightFloor(direction);
        if (!below) return color;
        var haze = Vector3.Lerp(color, new Vector3(0.30f, 0.29f, 0.27f) * (0.15f + 0.85f * _daylight), MathF.Min(1, -cosTheta * 5));
        return haze;
    }

    private Vector3 SunColorForGlow()
    {
        // Same extinction as the sun itself but never zero, so the afterglow persists just below the horizon.
        var altitude = Math.Max(_sunAltitude, 0.5f * MathF.PI / 180);
        var degrees = altitude * 180 / MathF.PI;
        var airMass = 1 / (MathF.Sin(altitude) + 0.50572f * MathF.Pow(degrees + 6.07995f, -1.6364f));
        return new Vector3(1.0f, 0.55f, 0.22f) * MathF.Min(1, 0.4f + 0.6f * MathF.Exp(-(airMass - 1) * 0.04f));
    }

    private static Vector3 NightFloor(Vector3 direction) =>
        new Vector3(0.0025f, 0.0035f, 0.007f) * (0.6f + 0.4f * MathF.Max(direction.Z, 0));

    private float ThetaSun() => MathF.PI / 2 - MathF.Max(_sunAltitude, 1.0f * MathF.PI / 180);

    private static void Fill(float[] target, float turbidity, float[] a, float[] b, float[] c, float[] d, float[] e)
    {
        target[0] = a[0] * turbidity + a[1];
        target[1] = b[0] * turbidity + b[1];
        target[2] = c[0] * turbidity + c[1];
        target[3] = d[0] * turbidity + d[1];
        target[4] = e[0] * turbidity + e[1];
    }

    /// <summary>Perez distribution F(θ, γ) = (1 + A e^(B/cosθ)) (1 + C e^(Dγ) + E cos²γ).</summary>
    private static float Perez(float[] k, float cosTheta, float gamma, float cosGamma) =>
        (1 + k[0] * MathF.Exp(k[1] / cosTheta)) * (1 + k[2] * MathF.Exp(k[3] * gamma) + k[4] * cosGamma * cosGamma);

    /// <summary>F evaluated looking at the zenith (cosθ = 1) with the sun at <paramref name="thetaSun"/>.</summary>
    private static float Perez(float[] k, float cosTheta, float thetaSun) =>
        Perez(k, cosTheta, thetaSun, MathF.Cos(thetaSun));

    private static float PerezAtZenith(float[] k, float thetaSun) => Perez(k, 1f, thetaSun, MathF.Cos(thetaSun));

    private static float ZenithChromaticity(float t, float thetaSun, bool xChannel)
    {
        var s = thetaSun;
        var s2 = s * s;
        var s3 = s2 * s;
        var t2 = t * t;
        return xChannel
            ? t2 * (0.00166f * s3 - 0.00375f * s2 + 0.00209f * s) +
              t * (-0.02903f * s3 + 0.06377f * s2 - 0.03202f * s + 0.00394f) +
              (0.11693f * s3 - 0.21196f * s2 + 0.06052f * s + 0.25886f)
            : t2 * (0.00275f * s3 - 0.00610f * s2 + 0.00317f * s) +
              t * (-0.04214f * s3 + 0.08970f * s2 - 0.04153f * s + 0.00516f) +
              (0.15346f * s3 - 0.26756f * s2 + 0.06670f * s + 0.26688f);
    }

    /// <summary>
    /// Zenith luminance relative to a 45° sun at the same turbidity. The physical ratio between noon and low sun is
    /// about 10×; a square-root compression keeps both ends usable before exposure adapts.
    /// </summary>
    private static float ZenithScale(float turbidity, float thetaSun)
    {
        static float Zenith(float t, float theta)
        {
            var chi = (4f / 9f - t / 120f) * (MathF.PI - 2 * theta);
            return (4.0453f * t - 4.9710f) * MathF.Tan(chi) - 0.2155f * t + 2.4192f;
        }
        var ratio = MathF.Max(Zenith(turbidity, thetaSun), 0.05f) / MathF.Max(Zenith(turbidity, MathF.PI / 4), 0.05f);
        return Math.Clamp(MathF.Sqrt(ratio), 0.18f, 1.6f);
    }

    /// <summary>Converts CIE xyY (luminance Y) to linear sRGB, clipping out-of-gamut negatives.</summary>
    private static Vector3 ToLinearRgb(float x, float y, float luminance)
    {
        if (y < 1e-4f) return Vector3.Zero;
        var bigX = x / y * luminance;
        var bigZ = (1 - x - y) / y * luminance;
        return Vector3.Max(Vector3.Zero, new Vector3(
            3.2406f * bigX - 1.5372f * luminance - 0.4986f * bigZ,
            -0.9689f * bigX + 1.8758f * luminance + 0.0415f * bigZ,
            0.0557f * bigX - 0.2040f * luminance + 1.0570f * bigZ));
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        var t = Math.Clamp((value - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3 - 2 * t);
    }
}
