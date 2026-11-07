using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Navigation;
using Guanacaste3D.Core.Rendering;
using Microsoft.Win32;

namespace Guanacaste3D.Desktop.Controls;

public partial class RenderPanel
{
    private HdriEnvironment? _hdriEnvironment;
    private string? _hdriFilePath;
    // Invalidate async reads when another project opens or a different HDRI is chosen.
    private int _hdriRequestVersion;

    // ---------------------------------------------------------------- control state

    /// <summary>Reads the stable enum identifier from a localized selector entry.</summary>
    private static string Mode(ComboBox picker) => (picker.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    /// <summary>Resolves the selected lighting scenario with a safe daylight fallback.</summary>
    private LightingScenario Scenario() => Enum.TryParse<LightingScenario>(Mode(ScenarioPicker), out var value) ? value : LightingScenario.Sunny;

    /// <summary>The level the person picked, or null for AUTO.</summary>
    private RenderQuality? RequestedQuality() =>
        Enum.TryParse<RenderQuality>(Mode(QualityPicker), out var value) ? value : null;

    /// <summary>Resolves the output quality preset; AUTO asks the planner using the measured hardware.</summary>
    private RenderQuality Quality() => RequestedQuality() ?? QualityPlanner.AutomaticQuality(_hardware);

    /// <summary>
    /// Loads the stored benchmark or measures one on a worker thread, then refreshes everything that depends on speed.
    /// </summary>
    private async Task CalibrateAsync(bool force)
    {
        RecalibrateButton.IsEnabled = false;
        HardwareText.Text = UiText.Get("RenderPanel.Calibrating");
        try
        {
            _hardware = await MachineCalibrationStore.LoadOrMeasureAsync(force: force);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Without a measurement AUTO keeps its core-count rule; the panel still works.
        }
        finally
        {
            RecalibrateButton.IsEnabled = true;
        }
        UpdateHardwareSummary();
        Quality_Changed(this, null!);
    }

    /// <summary>Tells the person what AUTO measured and decided, so the automatic choice is never a mystery.</summary>
    private void UpdateHardwareSummary()
    {
        var auto = QualityPlanner.AutomaticQuality(_hardware);
        var text = UiText.Format("RenderPanel.HardwareSummary", _hardware.LogicalCores, _hardware.MicrobenchmarkMegaTests,
            UiText.Get("RenderPanel." + auto));
        if (_hardware.IsOnBattery) text += " · " + UiText.Get("RenderPanel.HardwareBattery");
        HardwareText.Text = text;
        HardwareText.ToolTip = _hardware.BackendSummary;
    }

    /// <summary>Loads a bounded Radiance RGBE panorama on a worker without freezing the WPF workspace.</summary>
    private async void HdriLoad_Click(object sender, RoutedEventArgs e)
    {
        if (_rendering) return;
        var dialog = new OpenFileDialog
        {
            Title = "Elegir cielo HDRI (Radiance .hdr)",
            Filter = "HDRI Radiance (.hdr)|*.hdr|Todos los archivos|*.*"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        await LoadHdriAsync(dialog.FileName, ++_hdriRequestVersion, showWarning: true);
    }

    /// <summary>Reject network locations to avoid accidental UNC access from an untrusted project.</summary>
    private static bool IsNetworkHdriPath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal);

    /// <summary>Resolve saved local references without permitting relative traversal outside the project folder.</summary>
    private static string? ResolveSavedHdri(string path, string? projectFile)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || IsNetworkHdriPath(path) ||
                (projectFile is not null && IsNetworkHdriPath(projectFile))) return null;
            if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
            if (projectFile is null) return null;
            var folder = Path.GetDirectoryName(Path.GetFullPath(projectFile));
            if (folder is null) return null;
            var full = Path.GetFullPath(Path.Combine(folder, path));
            var prefix = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
            return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    /// <summary>Restore safely: a broken or missing asset leaves the physical procedural sky enabled.</summary>
    internal void RestoreHdriForProject(string? savedPath, string? projectFile)
    {
        if (savedPath is null) return;
        var resolved = ResolveSavedHdri(savedPath, projectFile);
        if (resolved is null)
        {
            HdriNameText.Text = "⚠ HDRI no disponible; cielo procedural activo";
            return;
        }
        _hdriFilePath = resolved;
        _ = LoadHdriAsync(resolved, ++_hdriRequestVersion, showWarning: false);
    }

    private async Task LoadHdriAsync(string file, int request, bool showWarning)
    {
        try
        {
            if (IsNetworkHdriPath(file))
                throw new InvalidDataException("No se cargan rutas HDRI de red automáticamente.");
            if (new FileInfo(file).Length > HdriEnvironment.MaximumFileBytes)
                throw new InvalidDataException("El HDRI excede el límite de 32 MiB.");
            var panorama = await Task.Run(() => HdriEnvironment.FromRgbe(File.ReadAllBytes(file)));
            if (request != _hdriRequestVersion || Dispatcher.HasShutdownStarted) return;
            _hdriEnvironment = panorama;
            _hdriFilePath = file;
            HdriNameText.Text = Path.GetFileName(file) + $" · {panorama.Width}×{panorama.Height}";
            _showingFinal = false;
            ShowInteractiveScene(true);
            Lighting_Changed(this, new RoutedEventArgs());
            if (PreviewToggle.IsChecked == true && IsVisible) ResumePreview();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            if (request != _hdriRequestVersion || Dispatcher.HasShutdownStarted) return;
            _hdriEnvironment = null;
            _hdriFilePath = file;
            HdriNameText.Text = "⚠ " + Path.GetFileName(file) + " no disponible; cielo procedural activo";
            Lighting_Changed(this, new RoutedEventArgs());
            if (showWarning)
            {
                var localized = Application.Current?.TryFindResource("Error." + ex.Message) as string ?? ex.Message;
                MessageBox.Show(Window.GetWindow(this), localized, "No se pudo cargar el HDRI",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    /// <summary>Clear sky textures and cancel older async reads whenever the user opens another project.</summary>
    internal void ClearHdriForDocument()
    {
        _hdriRequestVersion++;
        _hdriEnvironment = null;
        _hdriFilePath = null;
        if (HdriNameText is not null) HdriNameText.Text = "Cielo físico procedural activo";
        if (_ready) Lighting_Changed(this, new RoutedEventArgs());
    }

    private void HdriClear_Click(object sender, RoutedEventArgs e)
    {
        if (_rendering) return;
        ClearHdriForDocument();
        _showingFinal = false;
        ShowInteractiveScene(true);
        if (PreviewToggle.IsChecked == true && IsVisible) ResumePreview();
    }

    /// <summary>Runs the benchmark again, for instance after switching the Windows power plan.</summary>
    private async void Recalibrate_Click(object sender, RoutedEventArgs e) => await CalibrateAsync(force: true);

    /// <summary>Resolves the worker resource budget from the selected preset.</summary>
    private ResourceMode ResourceUse() => Enum.TryParse<ResourceMode>(Mode(ResourcePicker), out var value) ? value : ResourceMode.Balanced;

    /// <summary>Selects 16px tiles on low-resource machines unless the person overrides AUTO.</summary>
    private int SelectedBucketSize()
    {
        if (int.TryParse(Mode(BucketSizePicker), out var selected) && selected is 8 or 16 or 32 or 64 or 128)
            return selected;
        var free = RenderSafety.ReadMemory().AvailableBytes;
        return _hardware.LogicalCores <= 2 || free < 2L * 1024 * 1024 * 1024 ? 16 : 32;
    }

    /// <summary>Parses the solar latitude while bounding invalid input to a usable default.</summary>
    private double Latitude()
    {
        var text = LatitudeInput.Text.Replace(',', '.');
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value is >= -90 and <= 90 ? value : 12.1;
    }

    /// <summary>Combines solar, sky, exposure, and resource settings for a render request.</summary>
    private RenderSettings LightingFor(RenderQuality quality, ResourceMode resources)
    {
        var lighting = RenderPresets.Lighting(Scenario(), Latitude(), (SunDatePicker.SelectedDate ?? DateTime.Today).DayOfYear,
            HourSlider.Value, quality, resources, trueNorthDegrees: _source?.Site.TrueNorthDegrees ?? 0);
        // Under a storm front the sun breaks through dimmer and the whole sky darkens.
        var storm = SelectedClouds() == CloudStyle.Storm;
        return lighting with
        {
            AerialPerspective = HazeCheck.IsChecked == true ? 1f : 0f,
            Wetness = RainWetness.Effective((float)WetnessSlider.Value, (float)RainSlider.Value),
            GrassHeight = (float)(GrassSlider.Value * 0.4),
            WindStrength = (float)WindSlider.Value,
            WindSeconds = (float)_playTime,
            CloudCover = _hdriEnvironment is null ? (float)(CloudSlider.Value / 100) : 0f,
            Clouds = SelectedClouds(),
            CloudSeed = (uint)CloudSeedSlider.Value,
            CloudDirectionDegrees = (float)CloudDirectionSlider.Value,
            Turbidity = (float)TurbiditySlider.Value,
            // Radiance files can already carry the solar disk. Avoid counting the analytic sun twice.
            SunIntensity = _hdriEnvironment is null
                ? lighting.SunIntensity * (storm ? 0.55f : 1f) * (float)SunStrengthSlider.Value : 0f,
            SkyIntensity = _hdriEnvironment is null
                ? lighting.SkyIntensity * (storm ? 0.6f : 1f) * (float)SkyStrengthSlider.Value
                : lighting.SkyIntensity * (float)SkyStrengthSlider.Value *
                    MathF.Pow(2f, (float)HdriExposureSlider.Value),
            EnvironmentYawDegrees = (float)HdriYawSlider.Value,
            Environment = _hdriEnvironment
        };
    }

    /// <summary>Uses a bounded draft budget for progressive interactive feedback.</summary>
    private RenderSettings PreviewSettings() => LightingFor(RenderQuality.Draft, ResourceMode.Eco) with
    {
        MaxBounces = 2, MaxThreads = RenderPresets.Threads(ResourceUse() == ResourceMode.Maximum ? ResourceMode.Balanced : ResourceMode.Eco)
    };

    /// <summary>Combines display transforms without changing the accumulated scene illumination.</summary>
    private ImageFinish CurrentFinish()
    {
        var baseFinish = RenderPresets.Finish(Scenario());
        var styled = WithEffects(baseFinish);
        return styled with
        {
            AutoExposure = AutoExposureCheck.IsChecked == true,
            ExposureStops = baseFinish.ExposureStops + (float)ExposureSlider.Value,
            Bloom = BloomCheck.IsChecked == true ? Math.Max(0.12f, styled.Bloom) : 0,
            Vignette = VignetteCheck.IsChecked == true ? Math.Max(0.12f, styled.Vignette) : 0,
            ToneMapper = ((LookPicker.SelectedItem as ComboBoxItem)?.Tag as string) switch
            {
                "Aces" => ToneMapper.Aces,
                "Soft" => ToneMapper.Soft,
                _ => ToneMapper.AgX
            },
            Temperature = (float)TemperatureSlider.Value
        };
    }

    /// <summary>Reapplies the finish when the picture style changes; like exposure, no re-render is needed.</summary>
    private void Look_Changed(object sender, SelectionChangedEventArgs e) => Finish_Changed(sender, e);

    /// <summary>Shows the white balance as a color temperature and reapplies the finish.</summary>
    private void Temperature_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // About ±1500 K around 5500 K daylight: warm (lower) to the right, the way photographers move the slider.
        if (TemperatureText is not null) TemperatureText.Text = $"{5500 - 1500 * e.NewValue:0} K";
        Finish_Changed(sender, e);
    }

    /// <summary>Aerial perspective is part of the lighting, so it changes the preview settings, not only the finish.</summary>
    private void Haze_Changed(object sender, RoutedEventArgs e) => Lighting_Changed(sender, e);

    /// <summary>Switches the light fixtures on or off; their glow is part of the scene, so it is rebuilt.</summary>
    private void Lights_Changed(object sender, RoutedEventArgs e)
    {
        if (_source is not null) _source.ArtificialLights = ArtificialLightsCheck.IsChecked == true;
    }

    /// <summary>Builds the render camera with the current focus distance and depth-of-field settings.</summary>
    private RenderCamera CurrentCamera()
    {
        var camera = _source?.Camera ?? new NavigationCamera();
        var focus = 10f;
        if (_source?.Query is { } query && query.Pick(camera.Position, camera.Forward) is { } distance) focus = Math.Max(0.5f, distance);
        // A focus distance set by hand wins over the point at the centre of the view.
        if (FocusSlider is { Value: > 0 } manual) focus = (float)manual.Value;
        // A real lens opening (focal length over twice the f-number) wins over the older blur amount.
        var radius = ApertureCheck.IsChecked == true
            ? PhotographicCamera.ApertureRadius(FocalLengthOf(camera.FieldOfViewDegrees), ApertureSlider.Value)
            : (float)(DepthOfFieldSlider.Value / 100 * 0.25);
        return camera.ToRenderCamera(focus) with
        {
            CorrectVerticals = VerticalsCheck.IsChecked == true,
            ApertureRadius = radius,
            FocusDistance = focus,
            // A panorama sees every direction from the camera position; a lens opening has no meaning there.
            Panoramic = PanoramaMode,
            RollDegrees = (float)RollSlider.Value,
            ShiftX = (float)(ShiftXSlider.Value / 100),
            ShiftY = (float)(ShiftYSlider.Value / 100)
        };
    }

    /// <summary>Reads the requested output dimensions before applying quality scaling.</summary>
    private (int Width, int Height) SelectedBaseSize()
    {
        if (PanoramaMode) return (4096, 2048);
        var tag = Mode(SizePicker).Split('x');
        if (tag.Length == 2 && int.TryParse(tag[0], out var w) && int.TryParse(tag[1], out var h) && w > 0 && h > 0) return (w, h);
        var width = Math.Max(640, (int)ImageHost.ActualWidth);
        var height = Math.Max(360, (int)ImageHost.ActualHeight);
        return (width, height);
    }

    /// <summary>Shows the expected output size and resource cost before rendering starts.</summary>
    private void UpdateMemoryEstimate()
    {
        if (!_ready || MemoryText is null) return;
        var (baseWidth, baseHeight) = SelectedBaseSize();
        var (width, height) = RenderPresets.OutputSize(baseWidth, baseHeight, Quality());
        var memory = RenderSafety.ReadMemory();
        var estimate = RenderSafety.EstimateBytes(width, height, _sceneTriangles, _sceneBytes);
        MemoryText.Text = UiText.Format("RenderPanel.MemoryEstimate", UiText.Bytes(estimate), UiText.Bytes(memory.AvailableBytes)) +
                          $" · {width}×{height}";
    }

    /// <summary>Returns to the navigable model on camera movement while retaining any active render job.</summary>
    private void OnCameraChanged()
    {
        if (_source is null) return;
        if (PreviewToggle.IsChecked == true) PreviewToggle.IsChecked = false;
        if (_showingFinal)
        {
            _showingFinal = false;
            ShowInteractiveScene(true);
            if (!_rendering)
            {
                RenderProgressPanel.Visibility = Visibility.Collapsed;
                RenderTelemetryPanel.Visibility = Visibility.Collapsed;
            }
            if (PreviewToggle.IsChecked == true) ResumePreview();
        }
        SyncLensFromCamera();
        UpdateInteractiveCamera();
        HideStyledPreview();
        ScheduleStyledPreview();
        BeginInteractivePreview();
        _preview?.SetCamera(CurrentCamera());
    }

    // ---------------------------------------------------------------- event handlers

    /// <summary>Starts or suspends the live preview according to its visible toggle.</summary>
    private void PreviewToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (PreviewToggle.IsChecked == true)
        {
            _showingFinal = false;
            PreviewImage.Source = null;
            _bitmap = null;
            ShowInteractiveScene(false);
            RenderProgressPanel.Visibility = Visibility.Collapsed;
            RenderTelemetryPanel.Visibility = Visibility.Collapsed;
            if (IsVisible) ResumePreview();
        }
        else
        {
            _preview?.Suspend();
            if (!_showingFinal)
            {
                ShowInteractiveScene(true);
                if (!_rendering) StatusText.Text = UiText.Get("RenderPanel.Idle");
            }
        }
    }

    /// <summary>Invalidates preview illumination after the user changes environmental settings.</summary>
    private void Lighting_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        SunControls.Visibility = Scenario() == LightingScenario.Sunny ? Visibility.Visible : Visibility.Collapsed;
        _preview?.SetSettings(PreviewSettings());
        _preview?.SetFinish(CurrentFinish());
        if (_showingFinal) RefinishFinal();
    }

    /// <summary>Routes environmental slider changes through the lighting refresh path.</summary>
    private void Lighting_SliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => Lighting_Changed(sender, e);

    /// <summary>Recomputes solar illumination after latitude text changes.</summary>
    private void Latitude_Changed(object sender, TextChangedEventArgs e) => Lighting_Changed(sender, e);

    /// <summary>Refreshes resource estimates when output quality or resource settings change.</summary>
    private void Quality_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        UpdateMemoryEstimate();
        if (_preview is not null)
        {
            var (width, height) = PreviewSize();
            _preview.SetSize(width, height);
            _preview.SetSettings(PreviewSettings());
        }
    }

    /// <summary>Reapplies image finishing without discarding accumulated render samples.</summary>
    private void Finish_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _preview?.SetFinish(CurrentFinish());
        if (_showingFinal) RefinishFinal();
    }

    /// <summary>Routes image adjustment sliders through the non-destructive finish path.</summary>
    private void Finish_SliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => Finish_Changed(sender, e);

    /// <summary>Rain is a finish (the streaks) and also wets the ground, which is a lighting setting, so both are refreshed.</summary>
    private void Rain_SliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        Lighting_Changed(sender, e);
        Finish_Changed(sender, e);
    }

    /// <summary>Refreshes preview rays after a lens or focus setting changes.</summary>
    private void Camera_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _preview?.SetCamera(CurrentCamera());
    }

    /// <summary>Routes camera sliders through the lens refresh path.</summary>
    private void Camera_SliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => Camera_Changed(sender, e);

    /// <summary>Applies the collision preference to navigation without changing model geometry.</summary>
    private void Collision_Changed(object sender, RoutedEventArgs e)
    {
        if (_navigator is not null) _navigator.CollisionEnabled = CollisionCheck.IsChecked == true;
    }

    /// <summary>Restores a scene-wide camera framing using the shared navigator.</summary>
    private void FrameAll_Click(object sender, RoutedEventArgs e) => _navigator?.FrameAll();

}
