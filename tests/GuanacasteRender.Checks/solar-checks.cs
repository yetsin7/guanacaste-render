using System.Numerics;
using Guanacaste3D.Core.Rendering;

/// <summary>Solar geometry checks against analytically known positions.</summary>
static class SolarChecks
{
    public static void Run(Action<bool, string> check)
    {
        static bool Near(Vector3 a, Vector3 b, float tolerance) => Vector3.Distance(a, b) < tolerance;

        // Day 81 ≈ March equinox: declination ≈ 0, so the equator sees the sun at the zenith at noon.
        check(Near(SolarPosition.Direction(0, 81, 12), Vector3.UnitZ, 0.02f),
            "Sun is at the zenith at the equator on the equinox at noon");
        check(Near(SolarPosition.Direction(0, 81, 6), Vector3.UnitX, 0.02f) &&
              Near(SolarPosition.Direction(0, 81, 18), -Vector3.UnitX, 0.02f),
            "Equinox sun rises due east and sets due west");

        // Managua (12.1° N): in June the noon sun passes north of the zenith, in December well south.
        var june = SolarPosition.Direction(12.1, 172, 12);
        var december = SolarPosition.Direction(12.1, 355, 12);
        check(june.Y > 0 && Math.Abs(SolarPosition.AltitudeDegrees(june) - (90 - (23.44 - 12.1))) < 0.6,
            "Managua June noon sun stands north of the zenith at ~78.7°");
        check(december.Y < 0 && Math.Abs(SolarPosition.AltitudeDegrees(december) - (90 - 12.1 - 23.44)) < 0.6,
            "Managua December noon sun stands south at ~54.5°");
        check(SolarPosition.Direction(12.1, 172, 9).X > 0 && SolarPosition.Direction(12.1, 172, 15).X < 0,
            "Morning sun is in the east and afternoon sun in the west");
        check(SolarPosition.Direction(12.1, 172, 0).Z < 0, "Midnight sun is below the horizon");
        check(MathF.Abs(SolarPosition.Direction(-33.9, 1, 10.5).Length() - 1) < 1e-5f,
            "Sun direction is a unit vector in the southern hemisphere too");

        check(Near(SolarPosition.Direction(12.1, 172, 36), june, 1e-5f) &&
              Near(SolarPosition.Direction(12.1, 172, -12), june, 1e-5f),
            "Solar time wraps every 24 hours without changing the sun direction");
        var extremeHourSun = SolarPosition.Direction(12.1, 172, double.MaxValue);
        check(float.IsFinite(extremeHourSun.X) && float.IsFinite(extremeHourSun.Y) &&
              float.IsFinite(extremeHourSun.Z) && MathF.Abs(extremeHourSun.Length() - 1) < 1e-5f,
            "Extreme finite solar hours never propagate NaN into sky lighting");
    }
}
