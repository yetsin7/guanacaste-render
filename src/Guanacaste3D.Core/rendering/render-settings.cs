using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>A pinhole or thin-lens camera in Z-up model coordinates.</summary>
public readonly record struct RenderCamera(Vector3 Position, Vector3 Target, float VerticalFieldOfViewDegrees)
{
    /// <summary>Lens radius in meters; 0 keeps everything in focus, larger values blur what is off the focus plane.</summary>
    public float ApertureRadius { get; init; }

    /// <summary>Distance to the plane of sharp focus; 0 focuses on <see cref="Target"/>.</summary>
    public float FocusDistance { get; init; }

    /// <summary>Keeps vertical edges vertical when the camera tilts (a shift lens), as architectural photography does.</summary>
    public bool CorrectVerticals { get; init; }

    /// <summary>Rotation of the sensor about the view axis in degrees (a tilted horizon); positive turns the picture clockwise.</summary>
    public float RollDegrees { get; init; }

    /// <summary>Lens shift as a fraction of the frame height: positive moves the view right.</summary>
    public float ShiftX { get; init; }

    /// <summary>Lens shift as a fraction of the frame height: positive moves the view up, keeping verticals parallel.</summary>
    public float ShiftY { get; init; }

    /// <summary>Renders a full 360° × 180° equirectangular panorama from the camera position (use a 2:1 image).</summary>
    public bool Panoramic { get; init; }

    /// <summary>Frames the mesh bounds from a south-east, elevated three-quarter viewpoint.</summary>
    public static RenderCamera FrameBounds(Vector3 min, Vector3 max, float azimuthDegrees = -50,
        float elevationDegrees = 28, float distanceScale = 1)
    {
        var center = (min + max) / 2;
        var radius = MathF.Max(3, (max - min).Length() / 2);
        var azimuth = azimuthDegrees * MathF.PI / 180;
        var elevation = elevationDegrees * MathF.PI / 180;
        var direction = new Vector3(
            MathF.Cos(elevation) * MathF.Cos(azimuth),
            MathF.Cos(elevation) * MathF.Sin(azimuth),
            MathF.Sin(elevation));
        const float fieldOfView = 45;
        // Bounding-sphere fit is conservative for box-like buildings; 0.8 frames them more tightly.
        var distance = radius / MathF.Sin(fieldOfView * MathF.PI / 360) * 0.8f * Math.Clamp(distanceScale, 0.05f, 20);
        return new RenderCamera(center + direction * distance, center, fieldOfView);
    }

    /// <summary>Distance from the lens to the plane in focus.</summary>
    public float EffectiveFocusDistance =>
        FocusDistance > 0 ? FocusDistance : Math.Max(0.1f, Vector3.Distance(Position, Target));
}

/// <summary>Lighting and quality controls for the path tracer.</summary>
public sealed record RenderSettings
{
    /// <summary>Direction pointing from the scene toward the sun (normalized internally).</summary>
    public Vector3 SunDirection { get; init; } = new(-0.45f, -0.55f, 0.70f);
    /// <summary>Sun strength as irradiance divided by π on a surface facing it; 0 turns the sun off.</summary>
    public float SunIntensity { get; init; } = 2.0f;
    /// <summary>Tests direct-light visibility against geometry; disable for an unshadowed material study.</summary>
    public bool CastShadows { get; init; } = true;
    /// <summary>Angular radius of the sun disk in degrees; larger values give softer shadows.</summary>
    public float SunAngularRadiusDegrees { get; init; } = 0.8f;
    public float SkyIntensity { get; init; } = 1.2f;
    public SkyKind Sky { get; init; } = SkyKind.Clear;
    /// <summary>Optional Radiance RGBE panorama for physically based sky/environment illumination (not a flat backdrop).</summary>
    public HdriEnvironment? Environment { get; init; }
    /// <summary>Rotate the environment around the scene's vertical axis; degrees.</summary>
    public float EnvironmentYawDegrees { get; init; }
    /// <summary>Atmospheric haze for <see cref="SkyKind.Clear"/>: 2 is crystal clear, 6 is hazy, 10 is smoggy.</summary>
    public float Turbidity { get; init; } = 2.5f;
    /// <summary>Number of diffuse bounces after the first hit.</summary>
    public int MaxBounces { get; init; } = 3;
    /// <summary>Calibrated so a 0.3-albedo sunlit ground lands near middle grey after tone mapping.</summary>
    public float Exposure { get; init; } = 0.4f;
    /// <summary>Worker threads per pass; zero or less uses every core.</summary>
    public int MaxThreads { get; init; }
    /// <summary>Random sequence selector; equal seeds reproduce identical images.</summary>
    public uint Seed { get; init; }
    /// <summary>Skips pixels whose estimate has stopped changing, spending samples where noise remains.</summary>
    public bool AdaptiveSampling { get; init; }
    /// <summary>Relative standard error under which a pixel counts as converged (adaptive sampling only).</summary>
    public float AdaptiveTolerance { get; init; } = 0.02f;
    /// <summary>Samples every pixel always receives before adaptive sampling may stop it.</summary>
    public int AdaptiveMinimumSamples { get; init; } = 16;
    /// <summary>Caps the radiance of one noisy indirect sample (0 disables), trading a little energy for no white speckles.</summary>
    public float FireflyClamp { get; init; } = 10f;
    /// <summary>Wet ground after rain, 0–1 (0 is dry): level surfaces that see the sky darken, turn glossy and collect puddles with raindrop rings.</summary>
    public float Wetness { get; init; }
    /// <summary>Height of the tallest blade of the lawn, in metres (0 is off): lawn surfaces grow blades of grass with depth instead of a flat green.</summary>
    public float GrassHeight { get; init; }
    /// <summary>Wind on the grass, 0–1 (0 is calm): blades lean downwind (the clouds' direction) in gusts that travel across the lawn.</summary>
    public float WindStrength { get; init; }
    /// <summary>The second of the movie the wind is at, so the gusts move from frame to frame.</summary>
    public float WindSeconds { get; init; }
    /// <summary>
    /// Candidate lights examined per shading point. Where more lights than this can reach a point, one is chosen by
    /// resampled importance sampling, so cost stays flat as luminaires multiply; 0 evaluates every light every time.
    /// </summary>
    public int LightCandidates { get; init; } = 16;
    /// <summary>Irradiance below which a light is ignored at a place; keeps hundreds of luminaires from all counting everywhere.</summary>
    public float LightCullThreshold { get; init; } = 0.0005f;
    /// <summary>Filters the indirect light with a tolerance taken from each pixel's measured noise (instead of fixed strength).</summary>
    public bool VarianceGuidedDenoising { get; init; } = true;
    /// <summary>How many standard deviations of a pixel's own noise count as "the same light" when filtering (larger = smoother).</summary>
    public float DenoiserLumaSigma { get; init; } = 2f;
    /// <summary>The tolerance used with very few samples, from which it falls to <see cref="DenoiserLumaSigma"/> as the image converges.</summary>
    public float DenoiserLumaSigmaFewSamples { get; init; } = 6f;
    /// <summary>À-trous passes of the guided filter; each doubles the reach.</summary>
    public int DenoiserIterations { get; init; } = 4;
    /// <summary>Average relative noise (standard error over brightness) at or below which the image counts as clean and the filter tolerance is at its converged value.</summary>
    public float DenoiserCleanNoise { get; init; } = 0.05f;
    /// <summary>Average relative noise at or above which the filter keeps its widest tolerance.</summary>
    public float DenoiserNoisyNoise { get; init; } = 0.25f;
    /// <summary>
    /// Reuses light bounced between surfaces through a world-space cache instead of tracing every path to its end. Much
    /// cheaper per sample and kept across camera moves, but the result is blurred by up to one cache cell, so it suits
    /// previews and drafts; leave it off for the final, unbiased image.
    /// </summary>
    public bool UseRadianceCache { get; init; }
    /// <summary>Samples a cache entry needs before it may replace tracing a path.</summary>
    public int RadianceCacheMinimumSamples { get; init; } = 8;
    /// <summary>Samples a cache entry averages with equal weight before older ones fade.</summary>
    public int RadianceCacheWindow { get; init; } = 128;
    /// <summary>Edge of a radiance cache cell in meters: smaller is sharper and needs more samples to fill. Zero chooses it from the size of the scene.</summary>
    public float RadianceCacheCellSize { get; init; }
    /// <summary>Samples windows as light portals (with multiple importance sampling) so interiors lit by the sky converge quickly.</summary>
    public bool SkyPortals { get; init; } = true;
    /// <summary>
    /// Strength of aerial perspective: distant surfaces fade toward the color of the horizon, as air does over
    /// hundreds of meters, so the ground meets the sky softly. 1 is clear air (scaled by turbidity), 0 turns it off.
    /// </summary>
    public float AerialPerspective { get; init; } = 1f;
    /// <summary>
    /// Share of a clear sky covered by procedural cumulus, 0–1. Clouds brighten the sky light around them and keep clear
    /// of the sun's disk, so sunlight and shadows are unchanged. Ignored by the overcast sky, which is already cloud.
    /// </summary>
    public float CloudCover { get; init; } = 0.3f;
    /// <summary>The kind of cloud that <see cref="CloudCover"/> draws.</summary>
    public CloudStyle Clouds { get; init; } = CloudStyle.Cumulus;
    /// <summary>Fixed cloud shape seed; stable for screenshots, saved render setups and animation frames.</summary>
    public uint CloudSeed { get; init; } = 7;
    /// <summary>Rotation of the cloud pattern around the zenith (degrees).</summary>
    public float CloudDirectionDegrees { get; init; }
    /// <summary>Reflectance of the ground seen below the horizon when the scene has none.</summary>
    public Vector3 GroundColor { get; init; } = new(0.30f, 0.29f, 0.27f);

    /// <summary>Estimated bytes of per-pixel buffers for an image of this size (accumulators, features, counters).</summary>
    public static long EstimateImageBytes(int width, int height) => (long)width * height * 66;
}

/// <summary>Curve that compresses scene radiance into the displayable range.</summary>
public enum ToneMapper
{
    /// <summary>ACES filmic approximation (Narkowicz): rich contrast, desaturated highlights.</summary>
    Aces,
    /// <summary>
    /// AgX (Blender's photographic view transform): bright light desaturates toward white the way film does instead of
    /// skewing to saturated cyan or yellow, so sunlit plaster stays white and skies stay blue.
    /// </summary>
    AgX,
    /// <summary>Gentle Reinhard roll-off that keeps colors more neutral.</summary>
    Soft,
    /// <summary>No curve; values above 1 clip. For measuring, not for pictures.</summary>
    Linear
}

/// <summary>Camera-like finishing applied to the linear image after rendering and denoising.</summary>
public sealed record ImageFinish
{
    /// <summary>Exposure compensation in photographic stops.</summary>
    public float ExposureStops { get; init; }
    /// <summary>Measures the image and scales it toward middle grey before <see cref="ExposureStops"/> is applied.</summary>
    public bool AutoExposure { get; init; }
    public ToneMapper ToneMapper { get; init; } = ToneMapper.AgX;
    public float Saturation { get; init; } = 1.05f;
    /// <summary>White balance from −1 (cool, bluish) to 1 (warm, golden); 0 is neutral daylight.</summary>
    public float Temperature { get; init; }
    public float Contrast { get; init; } = 1.0f;
    /// <summary>Glow around bright areas, 0–1.</summary>
    public float Bloom { get; init; } = 0.12f;
    /// <summary>Edge darkening, 0–1.</summary>
    public float Vignette { get; init; } = 0.12f;
    /// <summary>Adds sub-quantization noise so smooth skies do not show 8-bit banding.</summary>
    public bool Dither { get; init; } = true;

    // Effects, as an architectural visualisation package offers them. Every default leaves the picture unchanged.

    /// <summary>Brightens (positive) or recovers (negative) the brightest tones, −1–1.</summary>
    public float Highlights { get; init; }
    /// <summary>Opens (positive) or deepens (negative) the darkest tones, −1–1.</summary>
    public float Shadows { get; init; }
    /// <summary>Green (negative) to magenta (positive) colour balance, −1–1, on top of <see cref="Temperature"/>.</summary>
    public float Tint { get; init; }
    /// <summary>Saturates muted colours more than vivid ones, 0–1, so skin, sky and foliage stay natural.</summary>
    public float Vibrance { get; init; }
    /// <summary>Colour given to the shadows (split toning); white leaves them neutral.</summary>
    public Vector3 ShadowTint { get; init; } = Vector3.One;
    /// <summary>Colour given to the highlights (split toning); white leaves them neutral.</summary>
    public Vector3 HighlightTint { get; init; } = Vector3.One;
    /// <summary>Four-point star streaks from very bright points such as the sun on glass, 0–1.</summary>
    public float Glare { get; init; }
    /// <summary>Depth fog that thickens with distance, 0–1 (1 is about 50 % at 60 m).</summary>
    public float Fog { get; init; }
    /// <summary>Colour of <see cref="Fog"/>, a light that the haze scatters toward the camera.</summary>
    public Vector3 FogColor { get; init; } = new(0.78f, 0.83f, 0.90f);
    /// <summary>Crispens fine detail (unsharp mask), 0–1.</summary>
    public float Sharpen { get; init; }
    /// <summary>Colour fringes toward the frame edges, as a real lens shows them, 0–1.</summary>
    public float ChromaticAberration { get; init; }
    /// <summary>Photographic grain, strongest in the mid tones, 0–1.</summary>
    public float FilmGrain { get; init; }
    /// <summary>Renders in black and white, keeping the tonal balance of the colour image.</summary>
    public bool Monochrome { get; init; }
    /// <summary>Name of the material the Highlight effect picks out; null or empty leaves every surface alone.</summary>
    public string? HighlightMaterial { get; init; }
    /// <summary>How far the rest of the picture is muted so <see cref="HighlightMaterial"/> stands out, 0–1 (0 is off).</summary>
    public float HighlightDim { get; init; }
    /// <summary>Slanted rain streaks and a greyer, dimmer light, 0–1 (0 is off).</summary>
    public float Rain { get; init; }
    /// <summary>Falling snow, a cold cast and a white cover on surfaces that face up, 0–1 (0 is off).</summary>
    public float Snow { get; init; }
    /// <summary>Ghosts and rings of the brightest light, as a lens makes them, 0–1 (0 is off); needs a bright light in view.</summary>
    public float LensFlare { get; init; }
    /// <summary>Rays of light streaming from the brightest light, 0–1 (0 is off); needs a bright light in view.</summary>
    public float LightRays { get; init; }
    /// <summary>The miniature look: a sharp band with the rest blurred, 0–1 (0 is off).</summary>
    public float TiltShift { get; init; }
    /// <summary>Where the sharp band of <see cref="TiltShift"/> sits, −1 (high) to 1 (low); 0 is the middle.</summary>
    public float TiltShiftPosition { get; init; }
    /// <summary>Fires and smoke to draw (null for none), at <see cref="FireSeconds"/> of the movie; the tracer works out where they fall.</summary>
    public IReadOnlyList<FireEmitter>? Fires { get; init; }
    public float FireSeconds { get; init; }
    /// <summary>The particles of <see cref="Fires"/> already placed on this picture by the tracer that made it; not set by hand.</summary>
    internal IReadOnlyList<FireSprite>? FireSprites { get; init; }
    /// <summary>Optional 3D color lookup table applied after tone mapping and prior to lens effects.</summary>
    public ColorLookupTable? Lut { get; init; }
    /// <summary>Blend from unmodified color to LUT-graded color, between zero and one.</summary>
    public float LutStrength { get; init; }
}
