using System.Globalization;
using System.IO;
using System.Windows.Controls;
using Guanacaste3D.Core.Navigation;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop.Controls;

public partial class RenderPanel
{
    /// <summary>Loads an existing setup without an associated file (e.g., an in-memory project).</summary>
    public void ApplySetup(RenderSetup setup) => ApplySetupForProject(setup, null);

    /// <summary>Restores project render controls, including a saved and safely resolved HDRI reference.</summary>
    public void ApplySetupForProject(RenderSetup setup, string? projectFile)
    {
        ClearHdriForDocument();
        ClearLutForDocument();
        // First the style, which sets the look, white balance, bloom, vignette and effects; the saved values follow.
        Select(StylePicker, setup.StyleId);
        Select(ScenarioPicker, setup.Scenario.ToString());
        SunDatePicker.SelectedDate = new DateTime(DateTime.Today.Year, 1, 1).AddDays(setup.DayOfYear - 1);
        HourSlider.Value = Math.Clamp(setup.SolarHour, HourSlider.Minimum, HourSlider.Maximum);
        CloudSlider.Value = setup.CloudCover * 100;
        LatitudeInput.Text = setup.Latitude.ToString("0.###", CultureInfo.InvariantCulture);
        HazeCheck.IsChecked = setup.Haze;
        TurbiditySlider.Value = setup.Turbidity;
        CloudSeedSlider.Value = setup.CloudSeed;
        CloudDirectionSlider.Value = setup.CloudDirectionDegrees;
        SunStrengthSlider.Value = setup.SunStrength;
        SkyStrengthSlider.Value = setup.SkyStrength;
        Select(QualityPicker, setup.Quality?.ToString() ?? "Auto");
        Select(SizePicker, $"{setup.Width}x{setup.Height}");
        Select(BucketSizePicker, setup.BucketSize.ToString(CultureInfo.InvariantCulture));
        Select(LookPicker, setup.Look switch { ToneMapper.Aces => "Aces", ToneMapper.Soft => "Soft", _ => "AgX" });
        ExposureSlider.Value = setup.ExposureStops;
        TemperatureSlider.Value = setup.Temperature;
        AutoExposureCheck.IsChecked = setup.AutoExposure;
        BloomCheck.IsChecked = setup.Bloom;
        VignetteCheck.IsChecked = setup.Vignette;
        VerticalsCheck.IsChecked = setup.CorrectVerticals;
        DepthOfFieldSlider.Value = setup.DepthOfField;
        RollSlider.Value = setup.RollDegrees;
        ShiftXSlider.Value = setup.ShiftX * 100;
        ShiftYSlider.Value = setup.ShiftY * 100;
        FocusSlider.Value = Math.Min(FocusSlider.Maximum, setup.FocusDistance);
        ApertureCheck.IsChecked = setup.FNumber > 0;
        if (setup.FNumber > 0) ApertureSlider.Value = Math.Clamp(setup.FNumber, ApertureSlider.Minimum, ApertureSlider.Maximum);
        AperturePanel.Visibility = setup.FNumber > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        ContrastSlider.Value = setup.ContrastAdjust;
        SaturationSlider.Value = setup.SaturationAdjust;
        ArtificialLightsCheck.IsChecked = setup.ArtificialLights;
        StoneDetailCheck.IsChecked = setup.StoneMicrogeometry;
        HdriYawSlider.Value = setup.EnvironmentYawDegrees;
        HdriExposureSlider.Value = setup.EnvironmentExposureStops;
        Select(CloudStylePicker, setup.Clouds.ToString());
        // The saved sliders win over the style's own values, set when the style was selected first.
        HighlightsSlider.Value = setup.Highlights;
        ShadowsSlider.Value = setup.Shadows;
        VibranceSlider.Value = setup.Vibrance;
        TintSlider.Value = setup.Tint;
        GlareSlider.Value = setup.Glare;
        FogSlider.Value = setup.Fog;
        SharpenSlider.Value = setup.Sharpen;
        AberrationSlider.Value = setup.ChromaticAberration;
        GrainSlider.Value = setup.FilmGrain;
        MonochromeCheck.IsChecked = setup.Monochrome;
        HighlightDimSlider.Value = setup.HighlightDim;
        RainSlider.Value = setup.Rain;
        WetnessSlider.Value = setup.Wetness;
        GrassSlider.Value = setup.Grass;
        WindSlider.Value = setup.Wind;
        SnowSlider.Value = setup.Snow;
        LensFlareSlider.Value = setup.LensFlare;
        LightRaysSlider.Value = setup.LightRays;
        TiltShiftSlider.Value = setup.TiltShift;
        TiltShiftPositionSlider.Value = setup.TiltShiftPosition;
        SelectHighlight(setup.HighlightMaterial);
        Select(LutPicker, setup.LutPresetId);
        LutStrengthSlider.Value = setup.LutStrength;
        RestoreLutForProject(RenderSetup.SelectedCustomLutPath(setup.LutPresetId, setup.LutPath), projectFile);
        // Selecting a saved library ID is a label restore, not a reapplication of its defaults.
        _applyingLook = true;
        try { Select(SkyPicker, setup.SkyPresetId ?? string.Empty); }
        finally { _applyingLook = false; }
        RestoreHdriForProject(setup.EnvironmentPath, projectFile);
        _fxActive.Clear();
        _fxMuted.Clear();
        _fxOpen.Clear();
        SyncFxFromControls();
        // A saved setup keeps its own latitude: the site only moves it when the site itself changes afterwards.
        RememberProjectSite();
        OnCameraChanged();
    }

    /// <summary>The panel's current settings and the given camera, as a setup the project can keep.</summary>
    public RenderSetup CaptureSetup(NavigationCamera camera)
    {
        var size = Mode(SizePicker).Split('x');
        var width = size.Length == 2 && int.TryParse(size[0], out var w) ? w : 0;
        var height = size.Length == 2 && int.TryParse(size[1], out var h) ? h : 0;
        return new RenderSetup
        {
            CameraX = camera.Position.X, CameraY = camera.Position.Y, CameraZ = camera.Position.Z,
            YawDegrees = camera.YawDegrees, PitchDegrees = camera.PitchDegrees,
            FieldOfViewDegrees = Math.Clamp(camera.FieldOfViewDegrees, 5, 150),
            Scenario = Scenario(), DayOfYear = (SunDatePicker.SelectedDate ?? DateTime.Today).DayOfYear,
            SolarHour = HourSlider.Value, Latitude = Latitude(), CloudCover = CloudSlider.Value / 100,
            Haze = HazeCheck.IsChecked == true, Quality = RequestedQuality(),
            SkyPresetId = string.IsNullOrEmpty(Mode(SkyPicker)) ? null : Mode(SkyPicker),
            Turbidity = TurbiditySlider.Value, CloudSeed = (uint)CloudSeedSlider.Value,
            CloudDirectionDegrees = CloudDirectionSlider.Value,
            SunStrength = SunStrengthSlider.Value, SkyStrength = SkyStrengthSlider.Value,
            Width = width > 0 && height > 0 ? width : 0, Height = width > 0 && height > 0 ? height : 0,
            BucketSize = int.TryParse(Mode(BucketSizePicker), out var bucketSize) ? bucketSize : 0,
            Look = CurrentFinish().ToneMapper, ExposureStops = ExposureSlider.Value, Temperature = TemperatureSlider.Value,
            AutoExposure = AutoExposureCheck.IsChecked == true, Bloom = BloomCheck.IsChecked == true,
            Vignette = VignetteCheck.IsChecked == true, CorrectVerticals = VerticalsCheck.IsChecked == true,
            DepthOfField = DepthOfFieldSlider.Value,
            RollDegrees = RollSlider.Value, ShiftX = ShiftXSlider.Value / 100, ShiftY = ShiftYSlider.Value / 100,
            FocusDistance = FocusSlider.Value, FNumber = ApertureCheck.IsChecked == true ? ApertureSlider.Value : 0,
            ContrastAdjust = ContrastSlider.Value, SaturationAdjust = SaturationSlider.Value, ArtificialLights = ArtificialLightsCheck.IsChecked == true,
            StoneMicrogeometry = StoneDetailCheck.IsChecked == true,
            EnvironmentPath = _hdriFilePath,
            EnvironmentYawDegrees = HdriYawSlider.Value,
            EnvironmentExposureStops = HdriExposureSlider.Value,
            Clouds = SelectedClouds(), StyleId = SelectedStyle(), Highlights = HighlightsSlider.Value, Shadows = ShadowsSlider.Value,
            Vibrance = VibranceSlider.Value, Tint = TintSlider.Value, Glare = GlareSlider.Value, Fog = FogSlider.Value,
            Sharpen = SharpenSlider.Value, ChromaticAberration = AberrationSlider.Value, FilmGrain = GrainSlider.Value,
            Monochrome = MonochromeCheck.IsChecked == true,
            HighlightMaterial = SelectedHighlight(), HighlightDim = HighlightDimSlider.Value,
            Rain = RainSlider.Value, Snow = SnowSlider.Value, Wetness = WetnessSlider.Value, Grass = GrassSlider.Value, Wind = WindSlider.Value,
            LensFlare = LensFlareSlider.Value, LightRays = LightRaysSlider.Value,
            TiltShift = TiltShiftSlider.Value, TiltShiftPosition = TiltShiftPositionSlider.Value,
            LutPresetId = Mode(LutPicker) is { Length: > 0 } lutId ? lutId : "none",
            LutPath = RenderSetup.SelectedCustomLutPath(Mode(LutPicker), _lutFilePath),
            LutStrength = LutStrengthSlider.Value
        };
    }

    /// <summary>Save As uses the target filename, keeping HDRIs inside the project folder portable.</summary>
    public RenderSetup CaptureSetupForProject(NavigationCamera camera, string projectFile)
    {
        var setup = CaptureSetup(camera);
        var absolute = setup.EnvironmentPath;
        if (absolute is null && setup.LutPath is null) return setup;
        try
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(projectFile));
            if (folder is null) return setup;
            string? Portable(string? resource)
            {
                if (resource is null || !Path.IsPathRooted(resource)) return resource;
                var local = Path.GetRelativePath(folder, resource);
                return !Path.IsPathRooted(local) && local != ".." &&
                    !local.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                    !IsNetworkHdriPath(local) ? local : resource;
            }
            return setup with
            {
                EnvironmentPath = Portable(setup.EnvironmentPath),
                LutPath = Portable(setup.LutPath)
            };
        }
        catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException) { }
        return setup;
    }

    /// <summary>Selects the entry whose tag matches, leaving the picker unchanged when none does.</summary>
    private static void Select(ComboBox picker, string tag)
    {
        var item = picker.Items.OfType<ComboBoxItem>().FirstOrDefault(entry => entry.Tag as string == tag);
        if (item is not null) picker.SelectedItem = item;
    }
}
