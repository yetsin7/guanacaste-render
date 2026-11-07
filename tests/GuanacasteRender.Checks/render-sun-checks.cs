using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Rendering;

/// <summary>Checks that the render's sun follows the project's turn from true north.</summary>
static class RenderSunChecks
{
    public static void Run(Action<bool, string> check)
    {
        // Zero degrees is the old behaviour, exactly.
        var plain = RenderPresets.Lighting(LightingScenario.Sunny, 40, 80, 12).SunDirection;
        check(RenderPresets.Lighting(LightingScenario.Sunny, 40, 80, 12, trueNorthDegrees: 0).SunDirection == plain &&
              RenderPresets.Lighting(LightingScenario.Sunny, 40, 80, 12, trueNorthDegrees: 360).SunDirection == plain,
            "A project that faces true north lights exactly as before");

        // At noon in the northern middle latitudes the sun is due south: project -Y when facing north.
        check(plain.Y < -0.3f && Math.Abs(plain.X) < 0.05f && plain.Z > 0.5f, $"At noon at 40°N the sun is due south ({plain.X:0.00}, {plain.Y:0.00})");
        // With true north along the project's -X (a quarter turn), due south is the project's +X.
        var turned = RenderPresets.Lighting(LightingScenario.Sunny, 40, 80, 12, trueNorthDegrees: 90).SunDirection;
        check(turned.X > 0.3f && Math.Abs(turned.Y) < 0.05f && Math.Abs(turned.Z - plain.Z) < 1e-4f,
            $"Turning the project a quarter from north moves the noon sun to the other axis and keeps its height ({turned.X:0.00}, {turned.Y:0.00})");

        // It agrees with the site's own conversion for any place, day, hour and turn.
        var random = new Random(20261104);
        var agree = true;
        for (var trial = 0; trial < 40; trial++)
        {
            var latitude = random.NextDouble() * 120 - 60;
            var day = random.Next(1, 366);
            var hour = 8 + random.NextDouble() * 8;
            var angle = random.NextDouble() * 720 - 360;
            var raw = RenderPresets.Lighting(LightingScenario.Sunny, latitude, day, hour).SunDirection;
            var sun = RenderPresets.Lighting(LightingScenario.Sunny, latitude, day, hour, trueNorthDegrees: angle).SunDirection;
            var (x, y) = new ProjectSite(latitude, 0, 0, angle, 0, 0).TrueToProject(raw.X, raw.Y);
            if (Math.Abs(sun.X - x) > 1e-4 || Math.Abs(sun.Y - y) > 1e-4 || Math.Abs(sun.Z - raw.Z) > 1e-6 ||
                Math.Abs(sun.Length() - 1) > 1e-4) agree = false;
        }
        check(agree, "The render's sun matches the project site's own true-north conversion for 40 random places, days, hours and turns");
        check(RenderPresets.Lighting(LightingScenario.Sunny, 40, 80, 12, trueNorthDegrees: double.NaN).SunDirection == plain,
            "A turn that is not a number is ignored instead of breaking the sun");
    }
}
