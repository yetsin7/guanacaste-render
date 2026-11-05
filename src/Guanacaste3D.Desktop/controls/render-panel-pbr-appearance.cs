using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>
/// Non-destructive PBR inspector: scene-wide or element-local values are persisted in the project.
/// A slider drag only changes the label; Apply creates one undoable transaction and scene rebuild.
/// </summary>
public partial class RenderPanel
{
    private bool _settingGalleryAppearance;
    private string? _galleryTintHex;
    private DispatcherTimer? _galleryApplyTimer;

    // Lumion's colour boxes: neutral first, then warm, cool and earth tones that suit architecture.
    private static readonly string[] GalleryTints =
    [
        "#FFFFFF", "#F2E6D0", "#E8C9A0", "#D9A066", "#B5651D", "#8B4513", "#C0392B", "#E07A5F",
        "#F2CC8F", "#A7C957", "#6A994E", "#2A9D8F", "#81B3D6", "#4A6FA5", "#7D7D7D", "#2B2B2B"
    ];

    private RenderMaterialAssignment? GalleryAssignment() =>
        _source?.RenderMaterials.FirstOrDefault(item =>
            item.Category == GalleryTarget() && item.ElementId == _focusedElement);

    private (double Roughness, double Metallic, double Relief) GalleryOriginalAppearance(
        RenderMaterialAssignment? assignment, SurfaceKind category)
    {
        if (assignment is not null)
        {
            if (PbrMaterialPackages.IsPackage(assignment.TexturePath))
            {
                var package = PbrMaterialPackages.Read(
                    PbrMaterialPackages.ResolveManifest(assignment.TexturePath, _source?.ProjectFilePath));
                if (package is { } imported)
                    return (imported.Manifest.Roughness, imported.Manifest.Metallic, imported.Manifest.Relief);
            }
            if (MaterialPresets.FromPath(assignment.TexturePath) is { } preset)
                return (preset.Material.Roughness, preset.Material.Metallic, preset.Relief);
            var library = RenderTextureLibrary.BuiltIn.FirstOrDefault(item =>
                RenderTextureLibrary.BuiltInPrefix + item.Key == assignment.TexturePath);
            if (library is not null)
            {
                var stock = MaterialLibrary.ForKind(category);
                return (stock.Roughness, stock.Metallic, library.Relief);
            }
        }
        var material = MaterialLibrary.ForKind(category);
        return (material.Roughness, material.Metallic, assignment?.Relief ?? 0.3);
    }

    private void UpdateGalleryAppearanceEditor()
    {
        if (!_galleryReady || GalleryAppearanceEditor is null || _source is null ||
            GalleryExpander.IsExpanded != true) return;
        var target = GalleryTarget();
        var current = GalleryAssignment();
        var initial = GalleryOriginalAppearance(current, target);
        _settingGalleryAppearance = true;
        try
        {
            GalleryRoughnessSlider.Value = current?.RoughnessOverride ?? initial.Roughness;
            GalleryMetallicSlider.Value = current?.MetallicOverride ?? initial.Metallic;
            GalleryReliefSlider.Value = current?.Relief ?? initial.Relief;
            GalleryScaleSlider.Value = Math.Clamp(current?.TextureSize ?? 1.0, GalleryScaleSlider.Minimum, GalleryScaleSlider.Maximum);
            _galleryTintHex = current?.TintHex;
            GalleryAppearanceName.Text = current is null
                ? Text("RenderGallery.StockMaterial")
                : MaterialDisplayName(current.TexturePath);
            GalleryApplyAppearanceButton.IsEnabled = true;
            GalleryResetAppearanceButton.IsEnabled = current is not null;
            UpdateGalleryAppearanceLabels();
            BuildGalleryTintSwatches();
            RefreshGalleryMapCards(current, target);
        }
        finally { _settingGalleryAppearance = false; }
    }

    private void GalleryAppearanceValue_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_settingGalleryAppearance) return;
        UpdateGalleryAppearanceLabels();
        ScheduleGalleryApply();
    }

    /// <summary>
    /// Like Lumion, a change shows up without pressing Apply: the change is applied once the slider has rested a moment,
    /// as one undoable step, instead of rebuilding the scene on every tick of a drag.
    /// </summary>
    private void ScheduleGalleryApply()
    {
        if (!_galleryReady) return;
        _galleryApplyTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _galleryApplyTimer.Tick -= GalleryApplyTimer_Tick;
        _galleryApplyTimer.Tick += GalleryApplyTimer_Tick;
        _galleryApplyTimer.Stop();
        _galleryApplyTimer.Start();
    }

    private void GalleryApplyTimer_Tick(object? sender, EventArgs e)
    {
        _galleryApplyTimer?.Stop();
        ApplyGalleryAppearance();
    }

    /// <summary>One box per colour; the chosen one is outlined, and the first clears the tint.</summary>
    private void BuildGalleryTintSwatches()
    {
        GalleryTintSwatches.Children.Clear();
        foreach (var hex in GalleryTints)
        {
            var none = hex == GalleryTints[0];
            var chosen = none ? _galleryTintHex is null : string.Equals(_galleryTintHex, hex, StringComparison.OrdinalIgnoreCase);
            var swatch = new Border
            {
                Width = 24, Height = 24, Margin = new Thickness(0, 0, 5, 5), CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
                Background = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!,
                BorderThickness = new Thickness(chosen ? 2 : 1),
                BorderBrush = new SolidColorBrush(chosen ? Color.FromRgb(0x70, 0xDA, 0xC9) : Color.FromRgb(0x40, 0x60, 0x75)),
                ToolTip = none ? "Sin colorización" : hex
            };
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                _galleryTintHex = none ? null : hex;
                BuildGalleryTintSwatches();
                UpdateGalleryPreview();
                ScheduleGalleryApply();
            };
            GalleryTintSwatches.Children.Add(swatch);
        }
    }

    private string? _bigBallKey;
    private BitmapSource? _bigBallAlbedo;

    /// <summary>
    /// Draws the large ball under the sliders with the same shader as the thumbnails, so roughness, metalness,
    /// relief and colour are judged on a lit sphere. The picture is decoded once per material, not per slider tick.
    /// </summary>
    private void UpdateGalleryPreview()
    {
        if (GalleryPreviewSphere is null) return;
        var category = GalleryTarget();
        var current = GalleryAssignment();
        var key = current?.TexturePath ?? "stock:" + category;
        if (key != _bigBallKey)
        {
            _bigBallKey = key;
            _bigBallAlbedo = null;
            try
            {
                var file = current is null || current.TexturePath == "stock:"
                    ? null : RenderTextureLibrary.Resolve(current.TexturePath, _source?.ProjectFilePath);
                if (file is not null && File.Exists(file)) _bigBallAlbedo = DecodeGalleryImage(file, 256);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                                NotSupportedException or ArgumentException or FormatException or
                                                InvalidOperationException)
            { /* The flat colour below stands in for an unreadable picture. */ }
        }
        var preset = current is null ? null : MaterialPresets.FromPath(current.TexturePath);
        var material = preset?.Material ?? MaterialLibrary.ForKind(category);
        var albedo = _bigBallAlbedo ?? SolidSwatch(_galleryTintHex is { } hex ? TintToLinear(hex) : material.BaseColor);
        GalleryPreviewSphere.Source = MaterialSpherePreview.CreateSized(albedo, GalleryRoughnessSlider.Value,
            GalleryMetallicSlider.Value, GalleryReliefSlider.Value, 176, 176, 80f);
    }

    private void UpdateGalleryAppearanceLabels()
    {
        if (GalleryRoughnessValue is null || GalleryMetallicValue is null || GalleryReliefValue is null) return;
        GalleryRoughnessValue.Text = GalleryRoughnessSlider.Value.ToString("0.00");
        GalleryMetallicValue.Text = GalleryMetallicSlider.Value.ToString("0.00");
        GalleryReliefValue.Text = GalleryReliefSlider.Value.ToString("0.00");
        if (GalleryScaleValue is not null) GalleryScaleValue.Text = GalleryScaleSlider.Value.ToString("0.00") + " m";
        UpdateGalleryPreview();
    }

    private void GalleryApplyAppearance_Click(object sender, RoutedEventArgs e)
    {
        _galleryApplyTimer?.Stop();
        ApplyGalleryAppearance();
    }

    private void ApplyGalleryAppearance()
    {
        if (_source is null || !_galleryReady) return;
        var category = GalleryTarget();
        if (_focusedElement is Guid && category != _focusedMaterial) return;
        _source.SetRenderMaterialAppearance(category, _focusedElement,
            GalleryRoughnessSlider.Value, GalleryMetallicSlider.Value, GalleryReliefSlider.Value,
            _galleryTintHex, GalleryScaleSlider.Value);
        RefreshMaterialRows();
    }

    private void GalleryResetAppearance_Click(object sender, RoutedEventArgs e)
    {
        if (_source is null || !_galleryReady) return;
        var category = GalleryTarget();
        if (_focusedElement is Guid && category != _focusedMaterial) return;
        var defaults = GalleryOriginalAppearance(GalleryAssignment(), category);
        _source.SetRenderMaterialAppearance(category, _focusedElement,
            null, null, defaults.Relief);
        RefreshMaterialRows();
    }

    private void RefreshGalleryMapCards(RenderMaterialAssignment? selected, SurfaceKind category)
    {
        GalleryPbrMapCards.Children.Clear();
        // The package channels are inherited when there is no explicit per-element replacement.
        PbrMaterialManifest? package = null;
        string? packageFolder = null;
        if (selected is not null && PbrMaterialPackages.IsPackage(selected.TexturePath))
        {
            var manifestPath = PbrMaterialPackages.ResolveManifest(selected.TexturePath, _source?.ProjectFilePath);
            package = PbrMaterialPackages.Read(manifestPath)?.Manifest;
            packageFolder = manifestPath is null ? null : Path.GetDirectoryName(manifestPath);
        }
        for (var channel = 0; channel < 4; channel++)
        {
            var index = channel;
            var label = channel switch { 0 => "Normal", 1 => "Rugosidad", 2 => "Metálico", _ => "Altura" };
            var overridePath = channel switch
            {
                0 => selected?.NormalPath,
                1 => selected?.RoughnessPath,
                2 => selected?.MetallicPath,
                _ => null
            };
            var bundledName = channel switch
            {
                0 => package?.Normal,
                1 => package?.RoughnessMap,
                2 => package?.MetallicMap,
                _ => package?.HeightMap
            };
            var bundlePath = packageFolder is not null && bundledName is not null
                ? Path.Combine(packageFolder, bundledName) : null;
            var path = overridePath is not null
                ? RenderTextureLibrary.Resolve(overridePath, _source?.ProjectFilePath)
                : bundlePath;

            var card = new StackPanel
            {
                Width = 98, Margin = new Thickness(2), Background = new SolidColorBrush(Color.FromRgb(27, 41, 56))
            };
            var frame = new Grid { Width = 90, Height = 56, Background = new SolidColorBrush(Color.FromRgb(45, 62, 78)) };
            var placeholder = new TextBlock
            {
                Text = path is null ? "Sin mapa" : "Cargando…", FontSize = 10, TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray,
                VerticalAlignment = VerticalAlignment.Center
            };
            var preview = new Image
            {
                Width = 90, Height = 56, Stretch = Stretch.UniformToFill
            };
            frame.Children.Add(placeholder);
            frame.Children.Add(preview);
            card.Children.Add(frame);
            card.Children.Add(new TextBlock
            {
                Text = label + (overridePath is not null ? " ✓" : bundlePath is not null ? " · PBR" : ""),
                Foreground = Brushes.White, TextAlignment = TextAlignment.Center,
                FontSize = 11, Margin = new Thickness(0, 4, 0, 4)
            });
            var button = new Button
            {
                Content = overridePath is not null ? "Cambiar" : "Elegir",
                Padding = new Thickness(5, 3, 5, 3), Margin = new Thickness(2, 0, 2, 0),
                ToolTip = "Seleccionar imagen del mapa " + label
            };
            // Height is defined by the material package; per-element height overrides do
            // not exist yet and must not accidentally be passed to the 3-channel editor.
            if (channel < 3)
            {
                button.Click += (_, _) => ChoosePbrMap(category, _focusedElement, index);
                card.Children.Add(button);
            }
            else card.Children.Add(new TextBlock
            {
                Text = "Desde .g3dm", Foreground = Brushes.LightGray,
                FontSize = 10, TextAlignment = TextAlignment.Center
            });
            if (overridePath is not null)
            {
                var clear = new Button
                {
                    Content = "Quitar", Padding = new Thickness(5, 2, 5, 2),
                    Margin = new Thickness(2, 4, 2, 0),
                    ToolTip = "Quita el mapa individual; si existe un mapa del .g3dm, vuelve a utilizarlo"
                };
                clear.Click += (_, _) => ChangePbrMap(category, _focusedElement, index, null);
                card.Children.Add(clear);
            }
            GalleryPbrMapCards.Children.Add(card);
            if (path is not null)
            {
                // Existing bounded thumbnail cache and cancellation policy are shared with catalog cards.
                var key = Path.GetFullPath(path);
                if (_galleryPreviews.TryGetValue(key, out var bitmap))
                {
                    preview.Source = bitmap;
                    placeholder.Visibility = Visibility.Collapsed;
                }
                else _ = LoadGalleryThumbnailAsync(key, preview, placeholder,
                    _galleryRevision, _galleryLoadCancellation.Token);
            }
        }
    }
}
