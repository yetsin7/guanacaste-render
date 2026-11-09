using System.Numerics;
using System.Runtime.CompilerServices;

namespace Guanacaste3D.Core.Rendering;

/// <summary>The kind of cloud a sky shows.</summary>
public enum CloudStyle
{
    /// <summary>Fair-weather cumulus: white heaps with bright tops, about 1.5 km up.</summary>
    Cumulus,
    /// <summary>Cirrus: thin, wind-combed streaks high up (about 8 km) that barely dim the sun.</summary>
    Cirrus,
    /// <summary>Stratocumulus: broad grey-and-white rolls close together, a low layer under a soft sun.</summary>
    Stratocumulus,
    /// <summary>A storm front: heavy dark masses with bright breaks, for dramatic skies.</summary>
    Storm,
    /// <summary>High, speckled cloudlets: small individual towers grouped in fields.</summary>
    Altocumulus,
    /// <summary>Broad translucent mid-altitude veil, diffuse and low-contrast.</summary>
    Altostratus,
    /// <summary>Tall anvil-shaped thunderclouds with deep shadowed bodies.</summary>
    Cumulonimbus,
    /// <summary>Long smooth lens clouds formed in mountainous airflows.</summary>
    Lenticular
}

/// <summary>
/// A procedural layer of fair-weather cumulus about 1.5 km up (or another <see cref="CloudStyle"/>). A sky ray is followed to the layer, where warped fractal
/// noise decides how much cloud it meets; coverage moves the threshold, so 0 is a clear sky and 1 nearly overcast.
/// The clouds are lit by the sun (bright tops, a silver lining toward the sun, greyer where they are thick) and by the
/// sky, and they thin out toward the horizon, where the layer would be too far away to resolve.
/// </summary>
public sealed class CloudLayer
{
    private readonly float _coverage;
    private readonly Vector2 _offset;
    private readonly CloudStyle _style;
    private readonly float _altitudeKm;
    private readonly float _cosWind;
    private readonly float _sinWind;

    /// <param name="coverage">Share of the sky covered, 0–1.</param>
    /// <param name="seed">Chooses the cloud pattern; the same seed always gives the same sky.</param>
    /// <param name="style">The kind of cloud.</param>
    public CloudLayer(float coverage, uint seed = 0, CloudStyle style = CloudStyle.Cumulus,
        float rotationDegrees = 0f)
    {
        _coverage = float.IsFinite(coverage) ? Math.Clamp(coverage, 0f, 1f) : 0f;
        _offset = new Vector2(seed % 977 * 3.17f, seed % 613 * 5.31f);
        rotationDegrees = float.IsFinite(rotationDegrees) ? Math.Clamp(rotationDegrees, -360f, 360f) : 0f;
        var radians = rotationDegrees * MathF.PI / 180f;
        _cosWind = MathF.Cos(radians);
        _sinWind = MathF.Sin(radians);
        _style = Enum.IsDefined(style) ? style : CloudStyle.Cumulus;
        _altitudeKm = _style switch
        {
            CloudStyle.Cirrus => 8f,
            CloudStyle.Altocumulus => 4.5f,
            CloudStyle.Altostratus => 5f,
            CloudStyle.Lenticular => 4f,
            CloudStyle.Stratocumulus => 1.2f,
            CloudStyle.Storm or CloudStyle.Cumulonimbus => 1f,
            _ => 1.5f
        };
    }

    /// <summary>Whether there are any clouds at all.</summary>
    public bool IsClear => _coverage <= 0.001f;

    /// <summary>
    /// Returns how much a sky ray in <paramref name="direction"/> is covered (0–1) and the thickness of the cloud it
    /// meets (0–1); zero for rays at or below the horizon.
    /// </summary>
    public (float Opacity, float Thickness) Sample(Vector3 direction)
    {
        // Reject invalid ray directions before projection/noise: NaN and infinity otherwise
        // propagate through the cloud opacity into the path tracer's radiance buffer.
        if (IsClear || !float.IsFinite(direction.X) || !float.IsFinite(direction.Y) ||
            !float.IsFinite(direction.Z) || direction.Z <= 0.015f) return (0, 0);
        var distance = _altitudeKm / direction.Z;
        var plane = new Vector2(direction.X, direction.Y) * distance;
        // Finite input components can still overflow during projection. A real unit sky ray
        // intersects this layer within about 534 km; farther coordinates are not meaningful
        // for the float-based noise grid and can produce NaN during integer-cell lookup.
        if (!float.IsFinite(plane.X) || !float.IsFinite(plane.Y) ||
            MathF.Abs(plane.X) > 1_000_000f || MathF.Abs(plane.Y) > 1_000_000f)
            return (0, 0);
        var point = new Vector2(plane.X * _cosWind - plane.Y * _sinWind,
            plane.X * _sinWind + plane.Y * _cosWind) + _offset;
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) ||
            MathF.Abs(point.X) > 1_000_000f || MathF.Abs(point.Y) > 1_000_000f)
            return (0, 0);
        var horizon = SmoothStep(0.015f, 0.16f, direction.Z);
        switch (_style)
        {
            case CloudStyle.Altocumulus:
            {
                var cells = point * 1.6f;
                var drift = new Vector2(Fbm(point * 0.25f, 3), Fbm(point * 0.25f + new Vector2(4, 9), 3));
                var structure = Fbm(cells + drift * 3f, 5);
                var cut = 0.71f - 0.46f * _coverage;
                var body = Math.Clamp((structure - cut) / 0.17f, 0f, 1f);
                return (body * (2 - body) * horizon * 0.93f, body * 0.50f);
            }
            case CloudStyle.Altostratus:
            {
                var veil = Fbm(point * 0.12f, 5);
                var fine = Fbm(point * 0.48f, 3);
                var body = Math.Clamp((veil * 0.82f + fine * 0.18f - (0.66f - 0.36f * _coverage)) / 0.18f, 0f, 1f);
                return (body * 0.85f * horizon, body * 0.47f);
            }
            case CloudStyle.Lenticular:
            {
                var bands = point * 0.34f;
                var wave = MathF.Cos(bands.Y * 3.8f + 0.5f * Fbm(point * 0.16f, 3));
                var envelope = Fbm(new Vector2(bands.X * 0.5f, bands.Y * 1.3f), 4);
                var body = Math.Clamp((wave * 0.25f + envelope - (0.66f - 0.34f * _coverage)) / 0.14f, 0f, 1f);
                return (body * 0.9f * horizon, body * 0.58f);
            }
            case CloudStyle.Cumulonimbus:
            {
                var thunderWarp = new Vector2(Fbm(point * 0.32f, 4), Fbm(point * 0.32f + new Vector2(12, 5), 4));
                var tower = Fbm(point * 0.25f + thunderWarp * 3.5f, 6);
                var thunderThreshold = 0.65f - 0.34f * _coverage;
                var body = Math.Clamp((tower - thunderThreshold) / 0.13f, 0f, 1f);
                var thunderDepth = Math.Clamp((tower - thunderThreshold) / 0.23f, 0f, 1f);
                return (body * (2 - body) * horizon, thunderDepth);
            }
            case CloudStyle.Cirrus:
            {
                // Stretched noise along the wind gives combed filaments; they stay thin and translucent.
                var combed = new Vector2(point.X * 0.05f, point.Y * 0.6f);
                var streak = Fbm(combed + new Vector2(Fbm(point * 0.08f, 2) * 2.5f, 0), 5);
                var cut = 0.66f - 0.32f * _coverage;
                var wisp = Math.Clamp((streak - cut) / 0.18f, 0f, 1f);
                return (wisp * 0.65f * horizon, wisp * 0.15f);
            }
            case CloudStyle.Stratocumulus:
            {
                var rolls = Fbm(point * 0.35f + new Vector2(0, Fbm(point * 0.12f, 2) * 3f), 5);
                var cut = 0.70f - 0.50f * _coverage;
                var body = Math.Clamp((rolls - cut) / 0.16f, 0f, 1f);
                return (body * (2 - body) * horizon, Math.Clamp(body * 0.8f + 0.2f, 0f, 1f) * body);
            }
            case CloudStyle.Storm:
            {
                var bank = Fbm(point * 0.22f + new Vector2(Fbm(point * 0.5f, 3), Fbm(point * 0.5f + new Vector2(4, 2), 3)) * 2f, 6);
                var cut = 0.60f - 0.28f * _coverage;
                var mass = Math.Clamp((bank - cut) / 0.10f, 0f, 1f);
                // Storm cloud is deep: thickness grows into the cores, so the shading darkens them to slate grey while
                // thinner edges and the breaks between masses stay bright.
                var depth = Math.Clamp((bank - cut) / 0.30f, 0f, 1f);
                return (mass * (2 - mass) * horizon, depth);
            }
        }
        // Domain warping turns round noise blobs into the cauliflower outlines of cumulus.
        var warp = new Vector2(Fbm(point * 0.35f + new Vector2(5.2f, 1.3f), 3), Fbm(point * 0.35f + new Vector2(-3.7f, 8.1f), 3));
        var density = Fbm(point * 0.55f + warp * 1.6f, 5);
        var threshold = 0.78f - 0.48f * _coverage;
        var thickness = Math.Clamp((density - threshold) / 0.22f, 0f, 1f);
        var opacity = thickness * (2 - thickness);   // soft edges, solid cores
        // Far away the layer is seen edge-on and its detail is smaller than a pixel: let it fade into the haze.
        return (opacity * horizon, thickness);
    }

    /// <summary>
    /// Attenuates the sun at a world-space point by intersecting the same procedural cloud plane that
    /// is visible in the sky. Model coordinates are metres; cloud noise coordinates are kilometres.
    /// This is a lightweight cloud-layer shadow, not a volumetric march or a geometric shadow ray.
    /// Clear skies and points above the layer always transmit all sunlight.
    /// </summary>
    public float SunTransmittance(Vector3 surfacePosition, Vector3 sunDirection)
    {
        if (IsClear || sunDirection.Z <= 0.015f ||
            !float.IsFinite(surfacePosition.X) || !float.IsFinite(surfacePosition.Y) ||
            !float.IsFinite(surfacePosition.Z) || !float.IsFinite(sunDirection.X) ||
            !float.IsFinite(sunDirection.Y) || !float.IsFinite(sunDirection.Z))
            return 1f;

        var distance = (_altitudeKm * 1000f - surfacePosition.Z) / sunDirection.Z;
        if (!float.IsFinite(distance) || distance <= 0f) return 1f;
        var cloudHit = surfacePosition + sunDirection * distance;
        if (!float.IsFinite(cloudHit.X) || !float.IsFinite(cloudHit.Y)) return 1f;
        // Normalizing the point on the layer gives Sample() exactly the same planar coordinates
        // as a sky ray from world origin. Moving across a site shifts the projected cloud shadow.
        var skyRay = Vector3.Normalize(new Vector3(cloudHit.X / 1000f, cloudHit.Y / 1000f, _altitudeKm));
        var (opacity, thickness) = Sample(skyRay);
        var depth = _style switch
        {
            CloudStyle.Cirrus => 0.15f + 0.12f * thickness,
            CloudStyle.Altostratus => 0.38f + 0.30f * thickness,
            CloudStyle.Storm or CloudStyle.Cumulonimbus => 0.65f + 0.30f * thickness,
            CloudStyle.Stratocumulus => 0.38f + 0.40f * thickness,
            _ => 0.35f + 0.47f * thickness
        };
        return Math.Clamp(1f - opacity * depth, 0.05f, 1f);
    }

    /// <summary>
    /// Radiance of the cloud seen in <paramref name="direction"/>, given the light that reaches it: the sun's
    /// irradiance over π (<paramref name="sunlight"/>), the direction to the sun and the sky's zenith radiance.
    /// </summary>
    public static Vector3 Shade(Vector3 direction, float thickness, Vector3 sunDirection, Vector3 sunlight, Vector3 skylight)
    {
        var towardSun = Math.Clamp(Vector3.Dot(direction, sunDirection), -1f, 1f);
        // Thin edges glow when the sun is behind them; thick bodies scatter light every way and darken underneath.
        var silver = MathF.Pow(MathF.Max(0, towardSun), 8) * (1 - thickness) * 1.2f;
        var body = 0.62f + 0.18f * (towardSun * 0.5f + 0.5f);
        var selfShadow = 1 - 0.42f * thickness;
        return sunlight * (0.78f * (body * selfShadow + silver)) + skylight * (1.6f + 0.6f * (1 - thickness));
    }

    /// <summary>
    /// <see cref="Shade"/> for this layer's kind of cloud: stratocumulus rolls are greyer where they are deep, and storm
    /// masses go slate-dark underneath while their thin edges still catch the light.
    /// </summary>
    public Vector3 ShadeFor(Vector3 direction, float thickness, Vector3 sunDirection, Vector3 sunlight, Vector3 skylight)
    {
        var radiance = Shade(direction, thickness, sunDirection, sunlight, skylight);
        return _style switch
        {
            CloudStyle.Storm or CloudStyle.Cumulonimbus =>
                radiance * (0.22f + 0.78f * (1 - thickness) * (1 - thickness)),
            CloudStyle.Stratocumulus or CloudStyle.Altostratus => radiance * (1 - 0.40f * thickness),
            _ => radiance
        };
    }

    private static float Fbm(Vector2 point, int octaves)
    {
        var sum = 0f;
        var amplitude = 0.5f;
        var total = 0f;
        for (var octave = 0; octave < octaves; octave++)
        {
            sum += amplitude * Noise(point);
            total += amplitude;
            // Rotate between octaves so the grid of the value noise never lines up.
            point = new Vector2(point.X * 1.6f - point.Y * 1.2f, point.X * 1.2f + point.Y * 1.6f) + new Vector2(1.7f, 9.2f);
            amplitude *= 0.5f;
        }
        return sum / total;
    }

    private static float Noise(Vector2 point)
    {
        var ix = (int)MathF.Floor(point.X);
        var iy = (int)MathF.Floor(point.Y);
        var fx = point.X - ix;
        var fy = point.Y - iy;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        var a = Hash(ix, iy);
        var b = Hash(ix + 1, iy);
        var c = Hash(ix, iy + 1);
        var d = Hash(ix + 1, iy + 1);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Hash(int x, int y)
    {
        var h = (uint)x * 374761393u + (uint)y * 668265263u;
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) * (1f / 16777216f);
    }

    private static float SmoothStep(float low, float high, float value)
    {
        var t = Math.Clamp((value - low) / (high - low), 0f, 1f);
        return t * t * (3 - 2 * t);
    }
}
