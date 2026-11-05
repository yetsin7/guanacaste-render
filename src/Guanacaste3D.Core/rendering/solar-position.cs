using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// Computes where the sun is in the project sky for a site latitude, day of year and local
/// solar time, so renders show realistic shadows (e.g. Managua ≈ 12.1° N).
/// Model axes: +X = east, +Y = project north, +Z = up.
/// </summary>
public static class SolarPosition
{
    /// <summary>
    /// Returns the unit direction pointing from the scene toward the sun.
    /// </summary>
    /// <param name="latitudeDegrees">Site latitude, positive north (−90…90).</param>
    /// <param name="dayOfYear">1 = January 1 … 365 = December 31.</param>
    /// <param name="solarHour">Local solar time in hours; 12 = solar noon.</param>
    /// <returns>Normalized vector; Z &lt; 0 means the sun is below the horizon.</returns>
    public static Vector3 Direction(double latitudeDegrees, int dayOfYear, double solarHour)
    {
        if (!double.IsFinite(latitudeDegrees) || latitudeDegrees is < -90 or > 90)
            throw new ArgumentOutOfRangeException(nameof(latitudeDegrees));
        if (!double.IsFinite(solarHour)) throw new ArgumentOutOfRangeException(nameof(solarHour));
        var day = ((dayOfYear - 1) % 365 + 365) % 365 + 1;

        // Cooper's declination approximation (±0.5° accuracy is ample for shading studies).
        var declination = 23.44 * Math.PI / 180 * Math.Sin(2 * Math.PI * (284 + day) / 365);
        // Solar time repeats every 24 hours. Reduce before multiplying to avoid
        // overflow and NaN for large but finite values supplied by external callers.
        var hourOfDay = solarHour % 24.0;
        var hourAngle = (hourOfDay - 12.0) * Math.PI / 12.0; // negative in the morning
        var latitude = latitudeDegrees * Math.PI / 180;

        // Equatorial sun vector rotated into the local east-north-up frame.
        var east = -Math.Cos(declination) * Math.Sin(hourAngle);
        var north = Math.Cos(latitude) * Math.Sin(declination) -
                    Math.Sin(latitude) * Math.Cos(declination) * Math.Cos(hourAngle);
        var up = Math.Sin(latitude) * Math.Sin(declination) +
                 Math.Cos(latitude) * Math.Cos(declination) * Math.Cos(hourAngle);
        // Below-horizon results are returned as is; callers decide how to treat night.
        return Vector3.Normalize(new Vector3((float)east, (float)north, (float)up));
    }

    /// <summary>
    /// Turns a sun direction given against true north (X east, Y north) into the project's axes, for a project whose +Y is
    /// <paramref name="trueNorthDegrees"/> away from true north (counter-clockwise, as <c>ProjectSite</c> keeps it). The
    /// height is unchanged. Zero degrees leaves the direction as it is.
    /// </summary>
    public static Vector3 ToProject(Vector3 sun, double trueNorthDegrees)
    {
        if (!double.IsFinite(trueNorthDegrees) || trueNorthDegrees % 360 == 0) return sun;
        var angle = trueNorthDegrees * Math.PI / 180;
        float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
        return new Vector3(sun.X * cos - sun.Y * sin, sun.X * sin + sun.Y * cos, sun.Z);
    }

    /// <summary>Returns the sun altitude above the horizon in degrees.</summary>
    public static double AltitudeDegrees(Vector3 direction) =>
        Math.Asin(Math.Clamp(direction.Z, -1, 1)) * 180 / Math.PI;
}
