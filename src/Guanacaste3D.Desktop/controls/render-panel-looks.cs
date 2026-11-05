using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>
/// Skies, cloud types, picture styles and effects of the Render panel. A sky sets the light, the hour, the clouds and a
/// fitting style in one choice; a style sets the effect sliders, which can then be refined one by one. Neither needs
/// a new render: skies change the lighting of the live preview, styles only the finish.
/// </summary>
public partial class RenderPanel
{
    private bool _applyingLook;

    /// <summary>Fills the sky, cloud and style pickers; called once from the constructor.</summary>
    private void InitializeLooks()
    {
        SkyPicker.Items.Add(new ComboBoxItem { Content = Text("RenderSky.Custom"), Tag = string.Empty });
        foreach (var sky in RenderLooks.Skies) SkyPicker.Items.Add(new ComboBoxItem { Content = LookEntry(Text(sky.NameKey), () => RenderLookThumbnail.Sky(sky)), Tag = sky.Id });
        SkyPicker.SelectedIndex = 0;
        foreach (var clouds in Enum.GetValues<CloudStyle>())
            CloudStylePicker.Items.Add(new ComboBoxItem { Content = Text($"CloudStyle.{clouds}"), Tag = clouds.ToString() });
        CloudStylePicker.SelectedIndex = 0;
        foreach (var style in RenderLooks.Styles) StylePicker.Items.Add(new ComboBoxItem { Content = LookEntry(Text(style.NameKey), () => RenderLookThumbnail.Style(style)), Tag = style.Id });
        StylePicker.SelectedIndex = 0;
        foreach (var lut in ColorLutPresets.All)
            LutPicker.Items.Add(new ComboBoxItem { Content = lut.Name, Tag = lut.Id });
        LutPicker.Items.Add(new ComboBoxItem { Content = "LUT personalizada (.cube)", Tag = "custom" });
        LutPicker.SelectedIndex = 0;
    }

    /// <summary>Load the example off the UI thread, only when its picker item is shown.</summary>
    private static FrameworkElement LookEntry(string name, Func<BitmapSource?> make)
    {
        var image = new Image { Width = 64, Height = 36, Stretch = Stretch.UniformToFill, Margin = new Thickness(0, 0, 8, 0) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(image);
        row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        bool started = false;
        image.Loaded += async (_, _) =>
        {
            if (started) return;
            started = true;
            try { image.Source = await Task.Run(make); }
            catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or
                                               InvalidOperationException or NotSupportedException or ArgumentException) { }
        };
        return row;
    }

    private CloudStyle SelectedClouds() => Enum.TryParse<CloudStyle>(Mode(CloudStylePicker), out var clouds) ? clouds : CloudStyle.Cumulus;

    private string SelectedStyle() => Mode(StylePicker) is { Length: > 0 } id ? id : "none";

    /// <summary>Applies a sky: scenario, date, hour, clouds, haze and the style that suits it.</summary>
    private void Sky_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingLook || RenderLooks.Skies.FirstOrDefault(sky => sky.Id == Mode(SkyPicker)) is not { } preset) return;
        _applyingLook = true;
        try
        {
            Select(ScenarioPicker, preset.Scenario.ToString());
            SunDatePicker.SelectedDate = new DateTime(DateTime.Today.Year, 1, 1).AddDays(preset.DayOfYear - 1);
            HourSlider.Value = Math.Clamp(preset.SolarHour, HourSlider.Minimum, HourSlider.Maximum);
            CloudSlider.Value = preset.CloudCover * 100;
            Select(CloudStylePicker, preset.Clouds.ToString());
            HazeCheck.IsChecked = preset.Haze;
            TurbiditySlider.Value = preset.Turbidity;
            CloudSeedSlider.Value = preset.CloudSeed;
            CloudDirectionSlider.Value = preset.CloudDirectionDegrees;
            SunStrengthSlider.Value = preset.SunStrength;
            SkyStrengthSlider.Value = preset.SkyStrength;
            // Picking a built-in procedural sky deliberately replaces any custom HDRI, not just its label.
            if (_hdriEnvironment is not null) ClearHdriForDocument();
        }
        finally
        {
            _applyingLook = false;
        }
        Select(StylePicker, preset.StyleId);
        Lighting_Changed(sender, e);
    }

    /// <summary>Changes the deterministic pattern without fetching data or using external assets.</summary>
    private void NewClouds_Click(object sender, RoutedEventArgs e)
    {
        CloudSeedSlider.Value = ((uint)CloudSeedSlider.Value * 1664525u + 1013904223u) % 1024u;
        Lighting_Changed(sender, e);
    }

    /// <summary>Applies a picture style: its tone curve, bloom, vignette, white balance and effect sliders.</summary>
    private void Style_Changed(object sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged fires while InitializeLooks populates the picker.
        // Do not apply a partial look before the panel and its sliders are ready.
        if (!_ready || StylePicker.SelectedIndex < 0) return;
        var styled = RenderLooks.Style(SelectedStyle()).Apply(new ImageFinish
        {
            Bloom = 0, Vignette = 0, Saturation = 1, Contrast = 1, ToneMapper = CurrentLook()
        });
        _applyingLook = true;
        try
        {
            Select(LookPicker, styled.ToneMapper switch { ToneMapper.Aces => "Aces", ToneMapper.Soft => "Soft", _ => "AgX" });
            TemperatureSlider.Value = Math.Clamp(styled.Temperature, -1, 1);
            // Neutral is a real reset, not a style that inherits bloom and vignette
            // from whichever photographic filter was selected previously.
            BloomCheck.IsChecked = styled.Bloom > 0;
            VignetteCheck.IsChecked = styled.Vignette > 0;
            ContrastSlider.Value = 0;
            SaturationSlider.Value = 0;
            HighlightsSlider.Value = styled.Highlights;
            ShadowsSlider.Value = styled.Shadows;
            VibranceSlider.Value = styled.Vibrance;
            TintSlider.Value = styled.Tint;
            GlareSlider.Value = styled.Glare;
            FogSlider.Value = styled.Fog;
            SharpenSlider.Value = styled.Sharpen;
            AberrationSlider.Value = styled.ChromaticAberration;
            GrainSlider.Value = styled.FilmGrain;
            MonochromeCheck.IsChecked = styled.Monochrome;
        }
        finally
        {
            _applyingLook = false;
        }
        Finish_Changed(sender, e);
        SyncFxFromControls();
    }

    private ToneMapper CurrentLook() => ((LookPicker.SelectedItem as ComboBoxItem)?.Tag as string) switch
    {
        "Aces" => ToneMapper.Aces,
        "Soft" => ToneMapper.Soft,
        _ => ToneMapper.AgX
    };

    /// <summary>The style's colour grading with the panel's effect sliders on top.</summary>
    private ImageFinish WithEffects(ImageFinish finish)
    {
        var styled = RenderLooks.Style(SelectedStyle()).Apply(finish);
        return styled with
    {
        Contrast = Math.Max(0.2f, styled.Contrast * (1 + 0.5f * (float)ContrastSlider.Value)),
        Saturation = Math.Max(0f, styled.Saturation * (1 + (float)SaturationSlider.Value)),
        Highlights = (float)HighlightsSlider.Value,
        Shadows = (float)ShadowsSlider.Value,
        Vibrance = (float)VibranceSlider.Value,
        Tint = (float)TintSlider.Value,
        Glare = (float)GlareSlider.Value,
        Fog = (float)FogSlider.Value,
        Sharpen = (float)SharpenSlider.Value,
        ChromaticAberration = (float)AberrationSlider.Value,
        FilmGrain = (float)GrainSlider.Value,
        Monochrome = MonochromeCheck.IsChecked == true,
        HighlightMaterial = SelectedHighlight(),
        HighlightDim = (float)HighlightDimSlider.Value,
        Rain = (float)RainSlider.Value,
        Snow = (float)SnowSlider.Value,
        Fires = FiresForFinish(),
        FireSeconds = (float)_playTime,
        LensFlare = (float)LensFlareSlider.Value,
        LightRays = (float)LightRaysSlider.Value,
        TiltShift = (float)TiltShiftSlider.Value,
        TiltShiftPosition = (float)TiltShiftPositionSlider.Value,
        Lut = SelectedLut(),
        LutStrength = (float)LutStrengthSlider.Value
    };
    }
}
