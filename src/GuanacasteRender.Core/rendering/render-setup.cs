namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// The render view a project keeps: the camera, the sun and sky, the output size and quality, and the image filters
/// (look, exposure, white balance, bloom, vignette). Opening the project restores it, so a saved render can be
/// repeated exactly; saving the project stores the Render panel's current setup.
/// </summary>
public sealed record RenderSetup
{
    public double CameraX { get; init; }
    public double CameraY { get; init; }
    public double CameraZ { get; init; }

    /// <summary>Heading in degrees, 0 looking along +X and increasing toward +Y.</summary>
    public double YawDegrees { get; init; }

    /// <summary>Elevation of the view in degrees, positive looking up.</summary>
    public double PitchDegrees { get; init; }

    /// <summary>Vertical field of view in degrees.</summary>
    public double FieldOfViewDegrees { get; init; } = 45;

    /// <summary>Tilt of the horizon in degrees (−45 to 45); 0 keeps the picture level.</summary>
    public double RollDegrees { get; init; }

    /// <summary>Lens shift sideways as a fraction of the frame height (−0.5 to 0.5), as a shift lens does.</summary>
    public double ShiftX { get; init; }

    /// <summary>Lens shift up or down as a fraction of the frame height (−0.5 to 0.5); keeps buildings from leaning.</summary>
    public double ShiftY { get; init; }

    /// <summary>Distance in meters to the plane of sharp focus; 0 focuses on whatever is at the centre of the view.</summary>
    public double FocusDistance { get; init; }

    /// <summary>
    /// Lens opening as an f-number (1.2 to 32). Zero keeps everything in focus unless the older
    /// <see cref="DepthOfField"/> amount is used; a small number blurs what is far from the focus distance.
    /// </summary>
    public double FNumber { get; init; }

    /// <summary>Contrast on top of the style, −1 (flat) to 1 (punchy); 0 leaves the style alone.</summary>
    public double ContrastAdjust { get; init; }

    /// <summary>Saturation on top of the style, −1 (grey) to 1 (doubled); 0 leaves the style alone.</summary>
    public double SaturationAdjust { get; init; }

    public LightingScenario Scenario { get; init; } = LightingScenario.Sunny;

    /// <summary>Day of the year for the sun (1 = January 1).</summary>
    public int DayOfYear { get; init; } = 172;

    /// <summary>Local solar time in hours.</summary>
    public double SolarHour { get; init; } = 10;

    public double Latitude { get; init; } = 12.1;

    /// <summary>Cloud cover, 0–1.</summary>
    public double CloudCover { get; init; } = 0.3;

    public bool Haze { get; init; } = true;
    /// <summary>Sky library selection, stable ID; null for hand-adjusted atmospheric settings.</summary>
    public string? SkyPresetId { get; init; }
    /// <summary>Preetham/Perez turbidity, values 1.7–10.</summary>
    public double Turbidity { get; init; } = 2.8;
    public uint CloudSeed { get; init; } = 7;
    public double CloudDirectionDegrees { get; init; }
    public double SunStrength { get; init; } = 1;
    public double SkyStrength { get; init; } = 1;


    /// <summary>Null lets the panel choose the quality for the machine (AUTO).</summary>
    public RenderQuality? Quality { get; init; }

    /// <summary>Output size in pixels; zero renders at the size of the view.</summary>
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>Bucket edge in pixels; 0 selects AUTO based on the computer. Saved with the render view.</summary>
    public int BucketSize { get; init; }

    public ToneMapper Look { get; init; } = ToneMapper.AgX;

    /// <summary>Exposure correction in stops, on top of the scenario's own.</summary>
    public double ExposureStops { get; init; }

    /// <summary>White balance, −1 (cool) to 1 (warm).</summary>
    public double Temperature { get; init; }

    public bool AutoExposure { get; init; } = true;
    public bool Bloom { get; init; } = true;
    public bool Vignette { get; init; }
    public bool CorrectVerticals { get; init; } = true;

    /// <summary>Depth of field, 0 (none) to 100.</summary>
    public double DepthOfField { get; init; }

    public bool ArtificialLights { get; init; } = true;
    /// <summary>Optional budget-limited render-only stone geometry, disabled for legacy projects.</summary>
    public bool StoneMicrogeometry { get; init; }

    /// <summary>Optional HDRI source. Relative paths are interpreted from the project file folder.</summary>
    public string? EnvironmentPath { get; init; }
    /// <summary>Panorama rotation around the scene's vertical axis, 0–360 degrees.</summary>
    public double EnvironmentYawDegrees { get; init; }
    /// <summary>HDRI light intensity adjustment in photographic stops, −4 to +4.</summary>
    public double EnvironmentExposureStops { get; init; }

    /// <summary>The kind of cloud that <see cref="CloudCover"/> draws.</summary>
    public CloudStyle Clouds { get; init; } = CloudStyle.Cumulus;

    /// <summary>The picture style (<see cref="RenderLooks.Styles"/>) under the effects; "none" for no style.</summary>
    public string StyleId { get; init; } = "none";

    // The effect sliders, as in <see cref="ImageFinish"/>.
    public double Highlights { get; init; }
    public double Shadows { get; init; }
    public double Vibrance { get; init; }
    public double Tint { get; init; }
    public double Glare { get; init; }
    public double Fog { get; init; }
    public double Sharpen { get; init; }
    public double ChromaticAberration { get; init; }
    public double FilmGrain { get; init; }
    public bool Monochrome { get; init; }
    /// <summary>Material picked out by the Highlight effect (null for none) and how far the rest is muted, 0–1.</summary>
    public string? HighlightMaterial { get; init; }
    public double HighlightDim { get; init; }
    /// <summary>Weather laid over the picture, 0–1: rain streaks and falling snow with a white cover.</summary>
    public double Rain { get; init; }
    /// <summary>Wet ground, 0–1: a lighting setting (the ground is darker, glossier and has puddles), so it is rendered, not painted over the picture.</summary>
    public double Wetness { get; init; }
    /// <summary>Grass, 0–1 (0 is off): how tall the blades of every lawn grow, from a mown lawn to a meadow; a lighting setting, so it is rendered.</summary>
    public double Grass { get; init; }
    /// <summary>Wind, 0–1 (0 is calm): grass leans and ripples in gusts; a lighting setting, so it is rendered.</summary>
    public double Wind { get; init; }
    public double Snow { get; init; }
    /// <summary>Camera effects laid over the picture: lens flare, light rays and tilt-shift (0–1), and the band's place (−1 to 1).</summary>
    public double LensFlare { get; init; }
    public double LightRays { get; init; }
    public double TiltShift { get; init; }
    public double TiltShiftPosition { get; init; }
    /// <summary>Built-in photo LUT or custom to load a .cube file (none is neutral).</summary>
    public string LutPresetId { get; init; } = "none";
    /// <summary>Optional custom .cube path, relative to the .g3d when bundled with the project.</summary>
    public string? LutPath { get; init; }
    public double LutStrength { get; init; } = 0.7;

    /// <summary>
    /// Only a selected custom .cube may be loaded or serialized as an active LUT reference.
    /// Older projects can still contain a stale path alongside a built-in filter; ignore it.
    /// </summary>
    public static string? SelectedCustomLutPath(string? presetId, string? path) =>
        presetId == "custom" ? path : null;

    /// <summary>Rejects values the Render panel cannot show.</summary>
    public void Validate()
    {
        double[] finite = [CameraX, CameraY, CameraZ, YawDegrees, PitchDegrees, FieldOfViewDegrees, SolarHour, Latitude,
            CloudCover, ExposureStops, Temperature, DepthOfField, Highlights, Shadows, Vibrance, Tint, Glare, Fog, Sharpen,
            ChromaticAberration, FilmGrain, EnvironmentYawDegrees, EnvironmentExposureStops, LutStrength,
            Turbidity, CloudDirectionDegrees, SunStrength, SkyStrength, RollDegrees, ShiftX, ShiftY, FocusDistance, FNumber, ContrastAdjust, SaturationAdjust];
        if (finite.Any(value => !double.IsFinite(value)) || Math.Abs(CameraX) > 1e6 || Math.Abs(CameraY) > 1e6 ||
            Math.Abs(CameraZ) > 1e6 || PitchDegrees is < -90 or > 90 || FieldOfViewDegrees is < 5 or > 150 ||
            !Enum.IsDefined(Scenario) || DayOfYear is < 1 or > 366 || SolarHour is < 0 or > 24 || Latitude is < -90 or > 90 ||
            CloudCover is < 0 or > 1 || (Quality is { } quality && !Enum.IsDefined(quality)) ||
            Width is < 0 or > 16384 || Height is < 0 or > 16384 || (Width == 0) != (Height == 0) ||
            (BucketSize != 0 && (BucketSize < 8 || BucketSize > 256 || (BucketSize & (BucketSize - 1)) != 0)) ||
            !Enum.IsDefined(Look) ||
            ExposureStops is < -3 or > 3 || Temperature is < -1 or > 1 || DepthOfField is < 0 or > 100 ||
            EnvironmentYawDegrees is < 0 or > 360 || EnvironmentExposureStops is < -4 or > 4 ||
            Turbidity is < 1.7 or > 10 || CloudSeed > 1023 || CloudDirectionDegrees is < -180 or > 180 ||
            RollDegrees is < -45 or > 45 || ShiftX is < -0.5 or > 0.5 || ShiftY is < -0.5 or > 0.5 ||
            FocusDistance is < 0 or > 1000 || (FNumber != 0 && FNumber is < 1 or > 32) ||
            ContrastAdjust is < -1 or > 1 || SaturationAdjust is < -1 or > 1 ||
            SunStrength is < 0 or > 2 || SkyStrength is < 0 or > 2 ||
            (SkyPresetId is not null && RenderLooks.Skies.All(sky => sky.Id != SkyPresetId)) ||
            LutStrength is < 0 or > 1 || HighlightDim is < 0 or > 1 || !double.IsFinite(HighlightDim) ||
            Rain is < 0 or > 1 || Snow is < 0 or > 1 || Wetness is < 0 or > 1 || !double.IsFinite(Wetness) || Grass is < 0 or > 1 || !double.IsFinite(Grass) || Wind is < 0 or > 1 || !double.IsFinite(Wind) || !double.IsFinite(Rain) || !double.IsFinite(Snow) ||
            LensFlare is < 0 or > 1 || LightRays is < 0 or > 1 || TiltShift is < 0 or > 1 || TiltShiftPosition is < -1 or > 1 ||
            !double.IsFinite(LensFlare) || !double.IsFinite(LightRays) || !double.IsFinite(TiltShift) || !double.IsFinite(TiltShiftPosition) ||
            (HighlightMaterial is { Length: > 120 }) ||
            (LutPresetId != "custom" && ColorLutPresets.All.All(preset => preset.Id != LutPresetId)) ||
            (LutPath is not null && (string.IsNullOrWhiteSpace(LutPath) || LutPath.Length > 1024)) ||
            (EnvironmentPath is not null && (string.IsNullOrWhiteSpace(EnvironmentPath) || EnvironmentPath.Length > 1024)) ||
            !Enum.IsDefined(Clouds) || StyleId is null || RenderLooks.Styles.All(style => style.Id != StyleId) ||
            new[] { Highlights, Shadows, Tint }.Any(value => value is < -1 or > 1) ||
            new[] { Vibrance, Glare, Fog, Sharpen, ChromaticAberration, FilmGrain }.Any(value => value is < 0 or > 1))
            throw new InvalidDataException("The saved render setup is invalid.");
    }
}
