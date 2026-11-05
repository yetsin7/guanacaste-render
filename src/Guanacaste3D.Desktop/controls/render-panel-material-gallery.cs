using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>
/// A lightweight, optional visual catalog for shipped textures. Only eight images are decoded per page,
/// off the UI thread; it reuses exactly the assignments made by the existing material editor.
/// </summary>
public partial class RenderPanel
{
    private sealed record GalleryEntry(string Key, string Label, double Size, double Relief, bool Imported,
        double Roughness, double Metallic);
    private const int GalleryPageSize = 8;
    private const int GalleryCacheLimit = 24;
    private bool _galleryReady;
    private int _galleryPage;
    private int _galleryRevision;
    private readonly Dictionary<string, BitmapSource> _galleryPreviews = new(StringComparer.Ordinal);
    private readonly Queue<string> _galleryPreviewOrder = new();
    private readonly SemaphoreSlim _galleryDecodeGate = new(2, 2);
    private CancellationTokenSource _galleryLoadCancellation = new();
    private static readonly Brush GalleryNormalBorder = new SolidColorBrush(Color.FromRgb(65, 84, 102));
    private static readonly Brush GallerySelectedBorder = new SolidColorBrush(Color.FromRgb(107, 215, 197));
    private static readonly Brush GalleryTileBackground = new SolidColorBrush(Color.FromRgb(31, 47, 64));

    private void InitializeMaterialGallery()
    {
        if (GalleryTargetPicker is null) return;
        foreach (var (category, label) in RenderTextureLibrary.Categories)
            GalleryTargetPicker.Items.Add(new ComboBoxItem { Content = Text(label), Tag = category });
        GalleryTargetPicker.SelectedItem = GalleryTargetPicker.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is SurfaceKind kind && kind == SurfaceKind.Bark)
            ?? GalleryTargetPicker.Items.OfType<ComboBoxItem>().FirstOrDefault();
        _galleryReady = true;
        // The library opens expanded; thumbnails can only be decoded once the control is on screen.
        Loaded += (_, _) => RefreshMaterialGallery();
    }

    /// <summary>When editing the picked element, the gallery must target its own surface kind.</summary>
    private void SyncMaterialGalleryTarget()
    {
        if (!_galleryReady) return;
        GalleryTargetPicker.IsEnabled = _focusedElement is null;
        if (_focusedMaterial is { } category)
        {
            var target = GalleryTargetPicker.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag is SurfaceKind kind && kind == category);
            if (target is not null && !ReferenceEquals(GalleryTargetPicker.SelectedItem, target))
                GalleryTargetPicker.SelectedItem = target;
        }
        UpdateMaterialGallerySelection();
    }

    private SurfaceKind GalleryTarget() =>
        GalleryTargetPicker.SelectedItem is ComboBoxItem { Tag: SurfaceKind category }
            ? category : SurfaceKind.Bark;

    private void GalleryExpanded(object sender, RoutedEventArgs e)
    {
        if (!_galleryReady || !ReferenceEquals(sender, GalleryExpander)) return;
        RefreshMaterialGallery();
    }

    private void GalleryCollapsed(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, GalleryExpander))
        {
            _galleryRevision++;
            _galleryLoadCancellation.Cancel();
        }
    }

    private void GalleryFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_galleryReady) return;
        _galleryPage = 0;
        RefreshMaterialGallery();
    }

    private void GalleryPrevious_Click(object sender, RoutedEventArgs e)
    {
        _galleryPage = Math.Max(0, _galleryPage - 1);
        RefreshMaterialGallery();
    }

    private void GalleryNext_Click(object sender, RoutedEventArgs e)
    {
        _galleryPage++;
        RefreshMaterialGallery();
    }

    private static bool GalleryGroupMatches(string group, string key) => group switch
    {
        "trees" => key.StartsWith("tree-", StringComparison.Ordinal),
        "bark" => key.StartsWith("tree-bark-", StringComparison.Ordinal),
        "foliage" => key.StartsWith("tree-leaves-", StringComparison.Ordinal),
        "other" => !key.StartsWith("tree-", StringComparison.Ordinal),
        "presets" => false,
        "pbr" => false,
        _ => true
    };

    private void RefreshMaterialGallery()
    {
        if (!_galleryReady || GalleryExpander.IsExpanded != true) return;
        var revision = ++_galleryRevision;
        _galleryLoadCancellation.Cancel();
        _galleryLoadCancellation = new CancellationTokenSource();
        var token = _galleryLoadCancellation.Token;
        var group = (GalleryGroupPicker.SelectedItem as ComboBoxItem)?.Tag as string ?? "trees";
        var search = GallerySearch.Text.Trim();
        var included = RenderTextureLibrary.BuiltIn
            .Where(texture => GalleryGroupMatches(group, texture.Key) &&
                MaterialCatalogSearch.Matches(search, texture.Key, Text(texture.LabelKey)))
            .Select(texture => new GalleryEntry(RenderTextureLibrary.BuiltInPrefix + texture.Key,
                Text(texture.LabelKey), texture.Size, texture.Relief, false, .82, 0));
        // Only the active project's packages and explicitly imported files: never scan a disk or network folder.
        var packages = group is "pbr" or "all"
            ? PbrGalleryLibrary.Entries(_source?.RenderMaterials, _source?.ProjectFilePath)
                .Where(item => MaterialCatalogSearch.Matches(search, item.Name, item.Key))
                .Select(item => new GalleryEntry(item.Key, item.Name, item.Size, item.Relief, true,
                    item.Roughness, item.Metallic))
            : Enumerable.Empty<GalleryEntry>();
        // The ready-made materials (steel, concrete, glass…), each shown as a ball like Lumion's library.
        var presets = group is "presets" or "all"
            ? MaterialPresets.All
                .Where(preset => MaterialCatalogSearch.Matches(search, preset.Id, preset.Name,
                    Text($"MaterialFamily.{preset.Group}")))
                .Select(preset => new GalleryEntry(MaterialPresets.Prefix + preset.Id, preset.Name,
                    preset.TextureSize, preset.Relief, false, preset.Material.Roughness, preset.Material.Metallic))
            : Enumerable.Empty<GalleryEntry>();
        var matches = presets.Concat(included).Concat(packages).ToArray();
        var pageCount = (matches.Length + GalleryPageSize - 1) / GalleryPageSize;
        _galleryPage = Math.Clamp(_galleryPage, 0, Math.Max(0, pageCount - 1));
        GalleryTiles.Children.Clear();
        foreach (var texture in matches.Skip(_galleryPage * GalleryPageSize).Take(GalleryPageSize))
        {
            var key = texture.Key;
            var picture = new Image
            {
                Width = 132, Height = 84, Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var placeholder = new TextBlock
            {
                Text = "Cargando…", Foreground = new SolidColorBrush(Color.FromRgb(155, 181, 199)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11, IsHitTestVisible = false
            };
            var pictureFrame = new Grid { Width = 132, Height = 84, Background = new SolidColorBrush(Color.FromRgb(38, 56, 72)) };
            pictureFrame.Children.Add(placeholder);
            pictureFrame.Children.Add(picture);
            var label = new TextBlock
            {
                Text = texture.Label, Foreground = Brushes.White,
                FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxHeight = 32,
                TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center,
                Margin = new Thickness(2, 5, 2, 0)
            };
            var content = new StackPanel { Orientation = Orientation.Vertical };
            content.Children.Add(pictureFrame);
            content.Children.Add(label);
            var button = new Button
            {
                Tag = key, Content = content, Width = 149, MinHeight = 145,
                Padding = new Thickness(5, 5, 5, 6),
                Margin = new Thickness(3, 3, 3, 3), BorderThickness = new Thickness(2),
                BorderBrush = GalleryNormalBorder, Background = GalleryTileBackground,
                ToolTip = $"{texture.Label} · escala {texture.Size:0.##} m" + (texture.Imported ? " · .g3dm PBR" : "")
            };
            button.Click += (_, _) => ApplyGalleryTexture(texture);
            button.PreviewMouseLeftButtonDown += (_, e) => _galleryDragOrigin = e.GetPosition(button);
            button.MouseMove += (_, e) => StartGalleryDrag(button, texture, e);
            GalleryTiles.Children.Add(button);
            if (_galleryPreviews.TryGetValue(key, out var cached))
            {
                picture.Source = cached;
                placeholder.Visibility = Visibility.Collapsed;
            }
            else _ = LoadGalleryThumbnailAsync(key, picture, placeholder, revision, token,
                sphere: true, texture.Roughness, texture.Metallic, texture.Relief);
        }
        GalleryPageText.Text = pageCount == 0 ? "0 / 0" : $"{_galleryPage + 1} / {pageCount}";
        GalleryPreviousButton.IsEnabled = _galleryPage > 0;
        GalleryNextButton.IsEnabled = _galleryPage + 1 < pageCount;
        GalleryFeedback.Text = matches.Length == 0
            ? "Sin resultados. Cambia el filtro o la búsqueda."
            : $"{matches.Length} texturas disponibles · pulsa una miniatura para aplicarla.";
        UpdateMaterialGallerySelection();
    }

    private void GalleryImportPbr_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Agregar material PBR a la biblioteca visual",
            Filter = "Material Guanacaste 3D (*.g3dm)|*.g3dm",
            Multiselect = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        if (!PbrGalleryLibrary.Remember(dialog.FileName))
        {
            MessageBox.Show(Window.GetWindow(this),
                "No se pudo guardar este material en la biblioteca. Comprueba el manifiesto y la imagen albedo.",
                "Material .g3dm", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        foreach (var item in GalleryGroupPicker.Items.OfType<ComboBoxItem>())
            if (item.Tag is "pbr") { GalleryGroupPicker.SelectedItem = item; break; }
        _galleryPage = 0;
        RefreshMaterialGallery();
    }

    private void UpdateMaterialGallerySelection()
    {
        if (!_galleryReady || GalleryExpander.IsExpanded != true || _source is null) return;
        var target = GalleryTarget();
        var activePath = _source.RenderMaterials.FirstOrDefault(material =>
            material.Category == target && material.ElementId == _focusedElement)?.TexturePath;
        foreach (var button in GalleryTiles.Children.OfType<Button>())
        {
            var selected = button.Tag is string key && (string.Equals(key, activePath, StringComparison.Ordinal) ||
                (PbrMaterialPackages.IsPackage(key) && PbrMaterialPackages.IsPackage(activePath) &&
                 string.Equals(PbrMaterialPackages.ResolveManifest(key, _source.ProjectFilePath),
                    PbrMaterialPackages.ResolveManifest(activePath!, _source.ProjectFilePath),
                    StringComparison.OrdinalIgnoreCase)));
            button.BorderBrush = selected ? GallerySelectedBorder : GalleryNormalBorder;
            button.BorderThickness = new Thickness(selected ? 3 : 1);
        }
        UpdateGalleryAppearanceEditor();
    }

    private void ApplyGalleryTexture(GalleryEntry texture)
    {
        if (_source is null) return;
        var category = GalleryTarget();
        if (_focusedElement is Guid && _focusedMaterial != category) return;
        var path = texture.Key;
        var current = _source.RenderMaterials.FirstOrDefault(material =>
            material.Category == category && material.ElementId == _focusedElement);
        if (current is not null && current.TexturePath == path &&
            Math.Abs(current.TextureSize - texture.Size) < 1e-9 &&
            Math.Abs(current.Relief - texture.Relief) < 1e-9) return;
        _source.AssignRenderMaterial(category, path, texture.Size, texture.Relief, _focusedElement);
        RefreshMaterialRows();
    }

    /// <summary>
    /// Decode only the longest edge of a thumbnail. DecodePixelWidth alone can
    /// allocate an enormous bitmap for a narrow, very tall user-supplied image.
    /// Read dimensions from metadata first, without decoding the full raster.
    /// </summary>
    private static BitmapSource DecodeGalleryImage(string file, int maxDimension)
    {
        using var metadataStream = File.OpenRead(file);
        var decoder = BitmapDecoder.Create(metadataStream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        if (frame.PixelWidth < 1 || frame.PixelHeight < 1)
            throw new InvalidDataException("Invalid material image dimensions.");

        using var stream = File.OpenRead(file);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (frame.PixelWidth >= frame.PixelHeight)
            image.DecodePixelWidth = maxDimension;
        else
            image.DecodePixelHeight = maxDimension;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private async Task LoadGalleryThumbnailAsync(string key, Image view, TextBlock placeholder,
        int revision, CancellationToken token, bool sphere = false, double roughness = .8,
        double metallic = 0, double relief = 0)
    {
        try
        {
            await _galleryDecodeGate.WaitAsync(token);
            BitmapSource? thumbnail;
            try
            {
                token.ThrowIfCancellationRequested();
                thumbnail = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var file = RenderTextureLibrary.Resolve(key, null);
                    token.ThrowIfCancellationRequested();
                    if (file is null || !File.Exists(file))
                        return MaterialPresets.FromPath(key) is { } flat
                            ? SolidMaterialBall(flat.Material, roughness, metallic, relief, null) : null;
                    var image = DecodeGalleryImage(file, sphere ? 192 : 132);
                    return sphere ? MaterialSpherePreview.Create(image, roughness, metallic, relief) : image;
                }, token);
            }
            finally { _galleryDecodeGate.Release(); }
            if (token.IsCancellationRequested || revision != _galleryRevision ||
                !IsLoaded || GalleryExpander.IsExpanded != true) return;
            if (thumbnail is not null)
            {
                if (!_galleryPreviews.ContainsKey(key))
                {
                    _galleryPreviews[key] = thumbnail;
                    _galleryPreviewOrder.Enqueue(key);
                    while (_galleryPreviewOrder.Count > GalleryCacheLimit)
                        _galleryPreviews.Remove(_galleryPreviewOrder.Dequeue());
                }
                view.Source = thumbnail;
                placeholder.Visibility = Visibility.Collapsed;
            }
            else placeholder.Text = "Sin vista previa";
        }
        catch (OperationCanceledException) { /* A search or page change superseded this thumbnail. */ }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                            NotSupportedException or ArgumentException or FormatException or
                                            InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            if (revision == _galleryRevision) placeholder.Text = "Sin vista previa";
        }
    }
}
