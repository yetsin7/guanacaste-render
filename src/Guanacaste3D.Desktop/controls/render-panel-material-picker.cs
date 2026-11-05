using System.IO;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>The big material library of the Render tab: a window of lit balls by category that applies with one click.</summary>
public partial class RenderPanel
{
    private MaterialPickerWindow? _materialPicker;

    private void MaterialPicker_Click(object sender, RoutedEventArgs e) => OpenMaterialPicker();

    /// <summary>The ready-made materials and the shipped photographs as items of the picker, in the order they are listed.</summary>
    private IReadOnlyList<PickerItem> MaterialPickerItems()
    {
        var presets = MaterialPresets.All.Select(preset => new PickerItem(MaterialPresets.Prefix + preset.Id, preset.Name,
            MaterialPickerWindow.CategoryOf(preset), preset.TextureSize, preset.Relief, preset.Material.Roughness, preset.Material.Metallic,
            preset.Material, preset.TextureKey is { } key ? RenderTextureLibrary.Resolve(RenderTextureLibrary.BuiltInPrefix + key, null) : null));
        var photographs = RenderTextureLibrary.BuiltIn.Select(texture => new PickerItem(RenderTextureLibrary.BuiltInPrefix + texture.Key, Text(texture.LabelKey),
            "photo", texture.Size, texture.Relief, 0.82, 0, null, RenderTextureLibrary.Resolve(RenderTextureLibrary.BuiltInPrefix + texture.Key, null)));
        return [.. presets, .. photographs];
    }

    private void OpenMaterialPicker()
    {
        if (_source is null) return;
        if (_materialPicker is { IsLoaded: true })
        {
            _materialPicker.Activate();
            return;
        }
        var host = new PickerHost(
            Surfaces: () => GalleryTargetPicker.Items.OfType<ComboBoxItem>().Select(item => item.Content as string ?? "").ToList(),
            SurfaceIndex: () => GalleryTargetPicker.SelectedIndex,
            SurfaceEnabled: () => GalleryTargetPicker.IsEnabled,
            SetSurface: index => GalleryTargetPicker.SelectedIndex = index,
            TargetName: () => _focusedElement is Guid ? "(elemento elegido)" : "(todo el proyecto)",
            CurrentKey: () => GalleryAssignment()?.TexturePath,
            Appearance: () =>
            {
                var current = GalleryAssignment();
                var initial = GalleryOriginalAppearance(current, GalleryTarget());
                return (current?.RoughnessOverride ?? initial.Roughness, current?.MetallicOverride ?? initial.Metallic, current?.Relief ?? initial.Relief,
                    current?.TextureSize ?? 1.0, current?.TintHex);
            },
            Apply: item => ApplyGalleryTexture(new GalleryEntry(item.Key, item.Label, item.Size, item.Relief, false, item.Roughness, item.Metallic)),
            Adjust: (roughness, metallic, relief, size, tint) =>
            {
                var category = GalleryTarget();
                if (_source is null || (_focusedElement is Guid && category != _focusedMaterial)) return;
                _galleryTintHex = tint;
                _source.SetRenderMaterialAppearance(category, _focusedElement, roughness, metallic, relief, tint, size);
                RefreshMaterialRows();
            },
            Reset: () => GalleryResetAppearance_Click(this, new RoutedEventArgs()));
        _materialPicker = new MaterialPickerWindow(MaterialPickerItems(), host) { Owner = Window.GetWindow(this) };
        _materialPicker.Show();
    }
}
