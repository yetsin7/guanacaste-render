using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>How much effort a render takes. Each level trades time for a cleaner, more accurate image.</summary>
public enum RenderQuality
{
    /// <summary>A fast look at composition and light, at half the output resolution.</summary>
    Draft,
    /// <summary>Good for reviews: clean at screen size within about a minute.</summary>
    Standard,
    /// <summary>Presentation quality with deeper light bounces.</summary>
    High,
    /// <summary>Print quality: the most samples and bounces, stopping early only where the image has converged.</summary>
    Final
}

/// <summary>Lighting situations the user can pick without understanding sun angles.</summary>
public enum LightingScenario
{
    /// <summary>Clear sky with the sun at the chosen date and hour.</summary>
    Sunny,
    /// <summary>Soft, shadowless light under a full cloud layer.</summary>
    Overcast,
    /// <summary>Low warm sun a little before sunset.</summary>
    GoldenHour,
    /// <summary>Just after sunset: deep blue sky and a glowing horizon.</summary>
    Dusk
}

/// <summary>How many processor cores a render may use.</summary>
public enum ResourceMode
{
    /// <summary>Half of the cores, leaving the computer comfortable for other work.</summary>
    Eco,
    /// <summary>All cores but one, which keeps the interface responsive.</summary>
    Balanced,
    /// <summary>Every core.</summary>
    Maximum
}

/// <summary>The effort settings behind a <see cref="RenderQuality"/> level.</summary>
public sealed record QualityProfile(RenderQuality Quality, double ResolutionScale, int TargetSamples, int MaxBounces,
    bool Adaptive, float AdaptiveTolerance, float FireflyClamp);

/// <summary>Picks sensible defaults so the render works well without tuning.</summary>
public static class RenderPresets
{
    /// <summary>Returns the effort profile for a quality level.</summary>
    public static QualityProfile Profile(RenderQuality quality) => quality switch
    {
        RenderQuality.Draft => new(quality, 0.5, 24, 2, false, 0.05f, 8f),
        RenderQuality.Standard => new(quality, 1.0, 96, 4, true, 0.03f, 10f),
        RenderQuality.High => new(quality, 1.0, 320, 5, true, 0.02f, 12f),
        _ => new(quality, 1.0, 1024, 6, true, 0.012f, 16f)
    };

    /// <summary>Number of worker threads for a resource mode on this computer.</summary>
    public static int Threads(ResourceMode mode)
    {
        var cores = Math.Max(1, Environment.ProcessorCount);
        return mode switch
        {
            ResourceMode.Eco => Math.Max(1, cores / 2),
            ResourceMode.Balanced => Math.Max(1, cores - 1),
            _ => cores
        };
    }

    /// <summary>
    /// Builds the lighting for a place, date, hour and scenario. Below the horizon the sun is switched off and the
    /// twilight sky takes over; within the last 3° above it the sun fades in so sunrise and sunset do not pop.
    /// </summary>
    /// <param name="latitude">Site latitude in degrees, north positive.</param>
    /// <param name="dayOfYear">1–365.</param>
    /// <param name="solarHour">Local solar time; used for <see cref="LightingScenario.Sunny"/> only.</param>
    /// <param name="trueNorthDegrees">How far the project's +Y axis is from true north (counter-clockwise), so the sun
    /// lights the side of the building it really would; 0 for a project that faces north.</param>
    public static RenderSettings Lighting(LightingScenario scenario, double latitude, int dayOfYear, double solarHour,
        RenderQuality quality = RenderQuality.Standard, ResourceMode resources = ResourceMode.Balanced, double trueNorthDegrees = 0)
    {
        var hour = scenario switch
        {
            LightingScenario.GoldenHour => HourForAltitude(latitude, dayOfYear, 6),
            LightingScenario.Dusk => HourForAltitude(latitude, dayOfYear, -4),
            LightingScenario.Overcast => Math.Clamp(solarHour, 8, 16),
            _ => solarHour
        };
        var sun = SolarPosition.Direction(Math.Clamp(latitude, -90, 90), dayOfYear, hour);
        var altitude = SolarPosition.AltitudeDegrees(sun);
        var profile = Profile(quality);
        var defaults = new RenderSettings();
        var sunFactor = (float)Math.Clamp(altitude / 3, 0, 1);
        var daylit = altitude > 0;
        return defaults with
        {
            Sky = scenario == LightingScenario.Overcast ? SkyKind.Overcast : SkyKind.Clear,
            // Below the horizon only the twilight sky lights the scene; the sun direction stays valid for the sky model.
            SunDirection = SolarPosition.ToProject(daylit ? sun : Vector3.Normalize(new Vector3(sun.X, sun.Y, sun.Z)), trueNorthDegrees),
            SunIntensity = scenario == LightingScenario.Overcast ? 0 : defaults.SunIntensity * sunFactor,
            SkyIntensity = defaults.SkyIntensity,
            MaxBounces = profile.MaxBounces,
            AdaptiveSampling = profile.Adaptive,
            AdaptiveTolerance = profile.AdaptiveTolerance,
            FireflyClamp = profile.FireflyClamp,
            MaxThreads = Threads(resources),
            Turbidity = scenario == LightingScenario.Overcast ? 4f : 2.8f
        };
    }

    /// <summary>
    /// Finds the afternoon solar hour at which the sun sits at <paramref name="altitudeDegrees"/>, by bisection between
    /// solar noon and midnight. Where the sun never reaches that height (polar seasons) it returns the nearest hour.
    /// </summary>
    public static double HourForAltitude(double latitude, int dayOfYear, double altitudeDegrees)
    {
        latitude = Math.Clamp(latitude, -90, 90);
        double Altitude(double hour) => SolarPosition.AltitudeDegrees(SolarPosition.Direction(latitude, dayOfYear, hour));
        double low = 12, high = 24;
        if (Altitude(low) <= altitudeDegrees) return low;
        if (Altitude(high) >= altitudeDegrees) return high;
        for (var i = 0; i < 40; i++)
        {
            var middle = (low + high) / 2;
            if (Altitude(middle) > altitudeDegrees) low = middle; else high = middle;
        }
        return (low + high) / 2;
    }

    /// <summary>Picks output pixels from a base size and quality, never exceeding the limits that keep memory sane.</summary>
    public static (int Width, int Height) OutputSize(int baseWidth, int baseHeight, RenderQuality quality)
    {
        var scale = Profile(quality).ResolutionScale;
        return (Math.Clamp((int)Math.Round(baseWidth * scale), 64, 8192),
            Math.Clamp((int)Math.Round(baseHeight * scale), 48, 8192));
    }

    /// <summary>Picture finish that suits a scenario: night scenes lift exposure, daylight stays neutral.</summary>
    public static ImageFinish Finish(LightingScenario scenario) => scenario switch
    {
        LightingScenario.Dusk => new ImageFinish { AutoExposure = true, ExposureStops = 0.3f, Bloom = 0.2f },
        LightingScenario.GoldenHour => new ImageFinish { AutoExposure = true, ExposureStops = 0.1f, Bloom = 0.15f },
        LightingScenario.Overcast => new ImageFinish { AutoExposure = true, Saturation = 1.0f },
        _ => new ImageFinish { AutoExposure = true }
    };
}
