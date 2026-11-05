using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;
using Microsoft.Win32;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>
/// The Materials card of the Render tab: for each surface category, its stock material, a preset of the material
/// library, one of the shipped textures, or any image from disk, plus the real size one copy of the image covers. Choices are stored in the project and
/// undoable; the scene is rebuilt when they change.
/// </summary>
public partial class RenderPanel
{
    private const string StockTag = "stock";
    private const string CustomTag = "custom";
    private const string PbrPackageTag = "pbr-package";
    private const string CreatePbrTag = "create-pbr";
    private const string BrowsePbrTag = "browse-pbr";
    private bool _refreshingMaterials;
    private SurfaceKind? _focusedMaterial;
    private Guid? _focusedElement;

    internal void FocusMaterial(SurfaceKind category, Guid? elementId = null)
    {
        _focusedMaterial = category;
        _focusedElement = elementId;
        if (SectionTabs.Items.Count > 3) SectionTabs.SelectedIndex = 3;
        SyncMaterialGalleryTarget();
        RefreshMaterialRows();
    }


    private void MaterialScopeReset_Click(object sender, RoutedEventArgs e)
    {
        _focusedElement = null;
        SyncMaterialGalleryTarget();
        RefreshMaterialRows();
    }

    /// <summary>Rebuilds the material rows from the project, for example after undo or opening another project.</summary>
    private void RefreshMaterialRows()
    {
        if (_source is null || MaterialRows is null) return;
        _refreshingMaterials = true;
        var ballRevision = ++_materialBallRevision;
        MaterialRows.Children.Clear();
        MaterialScopeInfo.Text = _focusedElement is Guid
            ? "Material propio del elemento seleccionado (solo esta categoría de caras)"
            : "Material general: afecta a elementos sin personalización";
        MaterialScopeReset.Visibility = _focusedElement is Guid ? Visibility.Visible : Visibility.Collapsed;
        var categories = _focusedElement is Guid
            ? RenderTextureLibrary.Categories.Where(item => item.Category == _focusedMaterial)
            : RenderTextureLibrary.Categories;
        foreach (var (category, labelKey) in categories)
        {
            var scope = _focusedElement;
            var assignment = _source.RenderMaterials.FirstOrDefault(material =>
                material.Category == category && material.ElementId == scope);
            var label = new TextBlock { Style = (Style)FindResource("PanelLabel"), Text = Text(labelKey) };
            if (_focusedMaterial == category) { label.FontWeight = FontWeights.Bold; label.Foreground = System.Windows.Media.Brushes.DarkOrange; }
            MaterialRows.Children.Add(label);
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            var size = new TextBox
            {
                Width = 52, Margin = new Thickness(6, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center,
                Text = (assignment?.TextureSize ?? 1.0).ToString("0.##", CultureInfo.CurrentCulture),
                ToolTip = Text("RenderPanel.TextureSizeHint"), IsEnabled = assignment is not null
            };
            var picker = new ComboBox();
            if (_focusedMaterial == category) { picker.BorderBrush = System.Windows.Media.Brushes.DarkOrange; picker.BorderThickness = new Thickness(2); }
            // Keep the current assignment visible, but defer the large searchable catalog until opened.
            picker.Items.Add(new ComboBoxItem {
                Content = scope is Guid ? "Heredar material general" : Text("RenderPanel.MaterialStock"), Tag = StockTag });
            if (assignment is not null)
                picker.Items.Add(new ComboBoxItem
                {
                    Content = MaterialDisplayName(assignment.TexturePath), Tag = assignment.TexturePath
                });
            picker.Items.Add(new ComboBoxItem { Content = "Importar material PBR (.g3dm)…", Tag = PbrPackageTag });
            picker.Items.Add(new ComboBoxItem { Content = "Explorar biblioteca de materiales .g3dm…", Tag = BrowsePbrTag });
        picker.Items.Add(new ComboBoxItem { Content = "Crear material .g3dm desde imagen…", Tag = CreatePbrTag });
        picker.Items.Add(new ComboBoxItem { Content = Text("RenderPanel.MaterialFromFile"), Tag = CustomTag });
            picker.SelectedIndex = assignment is null ? 0 : 1;
            var loadingOptions = false;
            picker.DropDownOpened += (_, _) =>
            {
                loadingOptions = true;
                try
                {
                    PopulateMaterialOptions(picker, category, _source?.RenderMaterials.FirstOrDefault(material =>
                        material.Category == category && material.ElementId == scope));
                }
                finally { loadingOptions = false; }
            };
            picker.SelectionChanged += (_, _) => { if (!loadingOptions) MaterialChosen(category, picker, size, scope); };
            var face = BuildMaterialPickerFace(picker, category, focusedHere: _focusedMaterial == category,
                populate: () =>
                {
                    loadingOptions = true;
                    try
                    {
                        PopulateMaterialOptions(picker, category, _source?.RenderMaterials.FirstOrDefault(material =>
                            material.Category == category && material.ElementId == scope));
                    }
                    finally { loadingOptions = false; }
                });
            size.LostFocus += (_, _) => MaterialSizeChanged(category, size, scope);
            size.KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter) MaterialSizeChanged(category, size, scope);
            };
            var restore = new Button
            {
                Content = "Restaurar", ToolTip = "Volver al material original de esta superficie",
                Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(7, 2, 7, 2),
                IsEnabled = assignment is not null
            };
            restore.Click += (_, _) =>
            {
                _source?.AssignRenderMaterial(category, null, 1, 0, scope);
                FocusMaterial(category, scope);
            };
            var duplicate = new Button
            {
                Content = "Duplicar a…", ToolTip = "Copiar este material a otra categoría",
                Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(7, 2, 7, 2),
                IsEnabled = assignment is not null && scope is null
            };
            var menu = new ContextMenu();
            foreach (var (targetCategory, targetLabel) in RenderTextureLibrary.Categories)
            {
                if (targetCategory == category) continue;
                var target = targetCategory;
                var item = new MenuItem { Header = Text(targetLabel) };
                item.Click += (_, _) =>
                {
                    if (_source is null) return;
                    var current = _source.RenderMaterials.FirstOrDefault(material => material.Category == category && material.ElementId == scope);
                    if (current is null) return;
                    _source.AssignRenderMaterial(target, current.TexturePath, current.TextureSize, current.Relief, scope);
                    FocusMaterial(target);
                };
                menu.Items.Add(item);
            }
            duplicate.Click += (_, _) => { duplicate.ContextMenu = menu; menu.PlacementTarget = duplicate; menu.IsOpen = true; };
            // Give the material name its full width; controls no longer compete for
            // the same narrow horizontal row in the 420 px render sidebar.
            // The picker stays as the model (selection and change handling); the face opens the ball grid instead.
            picker.Visibility = Visibility.Collapsed;
            row.Children.Add(picker);
            row.Children.Add(face);
            var details = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            details.Children.Add(new TextBlock
            {
                Style = (Style)FindResource("PanelLabel"), Margin = new Thickness(0),
                Text = "Escala (m)", VerticalAlignment = VerticalAlignment.Center
            });
            details.Children.Add(size);
            var relief = new TextBox
            {
                Width = 52, Margin = new Thickness(8, 0, 0, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Text = (assignment?.Relief ?? 0.3).ToString("0.##", CultureInfo.CurrentCulture),
                ToolTip = "Relieve de la textura: 0 (plano) a 1 (máximo)",
                IsEnabled = assignment is not null
            };
            relief.LostFocus += (_, _) => MaterialReliefChanged(category, relief, scope);
            relief.KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter) MaterialReliefChanged(category, relief, scope);
            };
            details.Children.Add(new TextBlock
            {
                Style = (Style)FindResource("PanelLabel"), Text = "Relieve", Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            details.Children.Add(relief);
            details.Children.Add(duplicate);
            details.Children.Add(restore);
            row.Children.Add(details);

            // Maps are separate: the picture gives albedo, the optional maps control surface microstructure.
            var maps = new WrapPanel { Margin = new Thickness(0, 3, 0, 7) };
            AddPbrMapButton(maps, "Normal", assignment?.NormalPath, category, scope, 0);
            AddPbrMapButton(maps, "Rugosidad", assignment?.RoughnessPath, category, scope, 1);
            AddPbrMapButton(maps, "Metálico", assignment?.MetallicPath, category, scope, 2);
            row.Children.Add(maps);
            // Ball on the left, controls on the right: still one child per category, as the focus code expects.
            var layout = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.Children.Add(BuildMaterialBall(assignment, category, ballRevision));
            Grid.SetColumn(row, 1);
            layout.Children.Add(row);
            MaterialRows.Children.Add(layout);
        }
        _refreshingMaterials = false;
        UpdateMaterialGallerySelection();
        if (_focusedMaterial is { } focused)
        {
            var index = 0;
            foreach (var (category, _) in categories)
            {
                if (category == focused && MaterialRows.Children[index * 2] is FrameworkElement label)
                {
                    Dispatcher.BeginInvoke(new Action(label.BringIntoView),
                        System.Windows.Threading.DispatcherPriority.Loaded);
                    break;
                }
                index++;
            }
        }
    }

    /// <summary>Builds the full library only when the user opens a picker, avoiding thousands of idle WPF items.</summary>
    private void PopulateMaterialOptions(ComboBox picker, SurfaceKind category, RenderMaterialAssignment? assignment)
    {
        picker.Items.Clear();
        picker.Items.Add(new ComboBoxItem { Content = _focusedElement is Guid ? "Heredar material general" : Text("RenderPanel.MaterialStock"), Tag = StockTag });
        // The material library, grouped under headings that cannot be chosen themselves.
        var search = MaterialSearch.Text.Trim();
        foreach (var group in MaterialPresets.All
            .Where(preset => MaterialCatalogSearch.Matches(search, preset.Name, preset.Id,
                Text($"MaterialFamily.{preset.Group}")))
            .GroupBy(preset => preset.Group))
        {
            picker.Items.Add(new ComboBoxItem
            {
                Content = Text($"MaterialFamily.{group.Key}"), IsEnabled = false, FontWeight = FontWeights.SemiBold
            });
            foreach (var preset in group)
                picker.Items.Add(new ComboBoxItem
                {
                    Content = preset.Name,
                    Tag = MaterialPresets.Prefix + preset.Id
                });
        }
        // Filtering must never hide the currently assigned preset or make it appear stock.
        if (assignment is not null && MaterialPresets.FromPath(assignment.TexturePath) is { } selectedPreset &&
            !picker.Items.OfType<ComboBoxItem>().Any(item => item.Tag as string == assignment.TexturePath))
            picker.Items.Add(new ComboBoxItem
            {
                Content = "✓ " + selectedPreset.Name, Tag = assignment.TexturePath
            });
        // Preserve a missing preset in the UI instead of silently displaying Stock.
        // The renderer uses a safe stock fallback until the user replaces it.
        if (assignment is not null &&
            assignment.TexturePath.StartsWith(MaterialPresets.Prefix, StringComparison.Ordinal) &&
            MaterialPresets.FromPath(assignment.TexturePath) is null)
            picker.Items.Add(new ComboBoxItem
            {
                Content = "⚠ Material no disponible: " + assignment.TexturePath[MaterialPresets.Prefix.Length..],
                Tag = assignment.TexturePath
            });
        picker.Items.Add(new ComboBoxItem
        {
            Content = Text("MaterialFamily.Textures"), IsEnabled = false, FontWeight = FontWeights.SemiBold
        });
        foreach (var texture in RenderTextureLibrary.BuiltIn.Where(texture =>
            MaterialCatalogSearch.Matches(search, texture.Key, Text(texture.LabelKey))))
            picker.Items.Add(new ComboBoxItem
            {
                Content = Text(texture.LabelKey),
                Tag = RenderTextureLibrary.BuiltInPrefix + texture.Key
            });
        if (assignment is not null &&
            assignment.TexturePath.StartsWith(RenderTextureLibrary.BuiltInPrefix, StringComparison.Ordinal) &&
            !picker.Items.OfType<ComboBoxItem>().Any(item => item.Tag as string == assignment.TexturePath))
        {
            var selectedTexture = RenderTextureLibrary.BuiltIn.FirstOrDefault(texture =>
                RenderTextureLibrary.BuiltInPrefix + texture.Key == assignment.TexturePath);
            picker.Items.Add(new ComboBoxItem
            {
                Content = selectedTexture is null
                    ? "⚠ Textura no disponible: " + assignment.TexturePath[RenderTextureLibrary.BuiltInPrefix.Length..]
                    : "✓ " + Text(selectedTexture.LabelKey),
                Tag = assignment.TexturePath
            });
        }
        if (assignment is not null && !assignment.TexturePath.StartsWith(RenderTextureLibrary.BuiltInPrefix, StringComparison.Ordinal) &&
            !assignment.TexturePath.StartsWith(MaterialPresets.Prefix, StringComparison.Ordinal))
            picker.Items.Add(new ComboBoxItem { Content = Path.GetFileName(assignment.TexturePath), Tag = assignment.TexturePath });
        picker.Items.Add(new ComboBoxItem { Content = Text("RenderPanel.MaterialFromFile"), Tag = CustomTag });
        picker.SelectedItem = picker.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag as string == (assignment?.TexturePath ?? StockTag)) ?? picker.Items[0];
    }

    private string MaterialDisplayName(string path)
    {
        if (path == "stock:") return "Material original + mapas PBR";
        if (MaterialPresets.FromPath(path) is { } preset) return preset.Name;
        if (path.StartsWith(MaterialPresets.Prefix, StringComparison.Ordinal))
            return "⚠ Material no disponible: " + path[MaterialPresets.Prefix.Length..];
        if (path.StartsWith(RenderTextureLibrary.BuiltInPrefix, StringComparison.Ordinal))
        {
            var texture = RenderTextureLibrary.BuiltIn.FirstOrDefault(item =>
                RenderTextureLibrary.BuiltInPrefix + item.Key == path);
            return texture is null
                ? "⚠ Textura no disponible: " + path[RenderTextureLibrary.BuiltInPrefix.Length..]
                : Text(texture.LabelKey);
        }
        if (PbrMaterialPackages.IsPackage(path))
        {
            var imported = PbrMaterialPackages.Read(PbrMaterialPackages.ResolveManifest(path, _source?.ProjectFilePath));
            return imported?.Manifest.Name ?? "⚠ Paquete PBR no disponible: " + Path.GetFileName(path);
        }
        return Path.GetFileName(path);
    }

    private void MaterialChosen(SurfaceKind category, ComboBox picker, TextBox size, Guid? scope)
    {
        if (_refreshingMaterials || _source is null || picker.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        switch (tag)
        {
            case StockTag:
                _source.AssignRenderMaterial(category, null, 1, 0, scope);
                break;
            case CreatePbrTag:
                CreatePbrMaterialFromImage(category, scope);
                RefreshMaterialRows();
                return;
            case BrowsePbrTag:
                BrowsePbrMaterialLibrary(category, scope);
                RefreshMaterialRows();
                return;
            case PbrPackageTag:
                var pbrDialog = new OpenFileDialog
                {
                    Title = "Importar material PBR de Guanacaste 3D",
                    Filter = "Paquete de material PBR (*.g3dm)|*.g3dm"
                };
                if (pbrDialog.ShowDialog(Window.GetWindow(this)) == true)
                {
                    var package = PbrMaterialPackages.Read(pbrDialog.FileName);
                    if (package is null)
                        MessageBox.Show(Window.GetWindow(this),
                            "El paquete no es válido. Comprueba los parámetros y que la imagen albedo esté junto al manifiesto.",
                            "Importación PBR", MessageBoxButton.OK, MessageBoxImage.Warning);
                    else
                    {
                        PbrGalleryLibrary.Remember(pbrDialog.FileName);
                        _source.AssignRenderMaterial(category, PbrMaterialPackages.Prefix + pbrDialog.FileName,
                            package.Value.Manifest.TextureSize, package.Value.Manifest.Relief, scope);
                        RefreshMaterialGallery();
                    }
                }
                RefreshMaterialRows();
                return;
            case CustomTag:
                var dialog = new OpenFileDialog
                {
                    Title = Text("RenderPanel.MaterialFromFile"),
                    Filter = Text("RenderPanel.TextureFilter")
                };
                if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                    _source.AssignRenderMaterial(category, dialog.FileName, ReadSize(size, 1), 0.3, scope);
                // Cancel must restore the actual selection rather than leave "From file" selected.
                RefreshMaterialRows();
                return;
            case var preset when MaterialPresets.FromPath(preset) is { } chosen:
                _source.AssignRenderMaterial(category, preset, chosen.TextureSize, chosen.Relief, scope);
                break;
            default:
                var builtIn = RenderTextureLibrary.BuiltIn.FirstOrDefault(texture =>
                    RenderTextureLibrary.BuiltInPrefix + texture.Key == tag);
                _source.AssignRenderMaterial(category, tag, builtIn?.Size ?? ReadSize(size, 1), builtIn?.Relief ?? 0.3, scope);
                break;
        }
        RefreshMaterialRows();
    }

    /// <summary>Creates an editable standalone material package and immediately assigns it.</summary>
    private void CreatePbrMaterialFromImage(SurfaceKind category, Guid? scope)
    {
        var owner = Window.GetWindow(this);
        var chooseImage = new OpenFileDialog
        {
            Title = "Elegir imagen de color (albedo)",
            Filter = "Imágenes (*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff"
        };
        if (chooseImage.ShowDialog(owner) != true) return;
        var companions = PbrMaterialPackages.DiscoverCompanionMaps(chooseImage.FileName);

        var dialog = new Window
        {
            Title = "Nuevo material PBR · Guanacaste 3D",
            Owner = owner, Width = 420, Height = 420,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var panel = new StackPanel { Margin = new Thickness(20) };
        TextBox AddField(string label, string initial)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 3) });
            var input = new TextBox { Text = initial };
            panel.Children.Add(input);
            return input;
        }
        var nameInput = AddField("Nombre del material", Path.GetFileNameWithoutExtension(chooseImage.FileName));
        panel.Children.Add(new TextBlock
        {
            Text = companions.Count == 0
                ? "No se detectaron mapas adicionales. Se usará albedo y propiedades físicas."
                : "Mapas encontrados: " + string.Join(", ", companions.Keys.Select(key => key switch
                {
                    "normal" => "Normal", "roughnessMap" => "Rugosidad", _ => "Metalicidad"
                })),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 4)
        });
        var roughnessInput = AddField("Rugosidad (0.02 a 1)", "0.75");
        var metalInput = AddField("Metalicidad (0 a 1)", "0");
        var sizeInput = AddField("Escala de textura (metros)", "1");
        var reliefInput = AddField("Relieve (0 a 1)", "0.2");
        var create = new Button
        {
            Content = "Crear material .g3dm", Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(15, 6, 15, 6),
            IsDefault = true
        };
        create.Click += (_, _) => dialog.DialogResult = true;
        panel.Children.Add(create);
        dialog.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        if (dialog.ShowDialog() != true) return;
        static bool ReadUnit(TextBox input, double minimum, double maximum, out double value) =>
            double.TryParse(input.Text.Trim().Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value) &&
            double.IsFinite(value) && value >= minimum && value <= maximum;
        if (string.IsNullOrWhiteSpace(nameInput.Text) ||
            !ReadUnit(roughnessInput, 0.02, 1, out var roughness) ||
            !ReadUnit(metalInput, 0, 1, out var metallic) ||
            !ReadUnit(sizeInput, 0.01, 1000, out var size) ||
            !ReadUnit(reliefInput, 0, 1, out var relief))
        {
            MessageBox.Show(owner, "Revisa el nombre y los valores PBR.", "Material inválido",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var save = new SaveFileDialog
        {
            Title = "Guardar material de Guanacaste 3D",
            Filter = "Material Guanacaste 3D (*.g3dm)|*.g3dm",
            DefaultExt = ".g3dm", AddExtension = true,
            FileName = Path.GetFileNameWithoutExtension(chooseImage.FileName) + ".g3dm",
            OverwritePrompt = true
        };
        if (save.ShowDialog(owner) != true) return;
        try
        {
            // No silent overwrite: protect both the manifest and its companion image.
            var destination = PbrMaterialPackages.CreateFromImage(chooseImage.FileName,
                save.FileName, nameInput.Text, roughness, metallic, size, relief);
            PbrGalleryLibrary.Remember(destination);
            _source?.AssignRenderMaterial(category, PbrMaterialPackages.Prefix + destination, size, relief, scope);
            RefreshMaterialGallery();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                            ArgumentException or NotSupportedException)
        {
            MessageBox.Show(owner, exception.Message, "No se pudo crear el material",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void MaterialSizeChanged(SurfaceKind category, TextBox size, Guid? scope)
    {
        if (_refreshingMaterials || _source is null) return;
        var assignment = _source.RenderMaterials.FirstOrDefault(material => material.Category == category && material.ElementId == scope);
        if (assignment is null) return;
        var value = ReadSize(size, assignment.TextureSize);
        if (Math.Abs(value - assignment.TextureSize) < 1e-9) return;
        _source.AssignRenderMaterial(category, assignment.TexturePath, value, assignment.Relief, scope);
    }

    /// <summary>Updates the image relief independently of texture scale; the change is undoable.</summary>
    private void MaterialReliefChanged(SurfaceKind category, TextBox relief, Guid? scope)
    {
        if (_refreshingMaterials || _source is null) return;
        var assignment = _source.RenderMaterials.FirstOrDefault(material => material.Category == category && material.ElementId == scope);
        if (assignment is null) return;
        var value = double.TryParse(relief.Text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed)
            && double.IsFinite(parsed) && parsed is >= 0 and <= 1 ? parsed : assignment.Relief;
        if (Math.Abs(value - assignment.Relief) < 1e-9) return;
        _source.AssignRenderMaterial(category, assignment.TexturePath, assignment.TextureSize, value, scope);
    }

    /// <summary>Each map has a load control and an independent remove action.</summary>
    private void AddPbrMapButton(WrapPanel panel, string label, string? selectedPath,
        SurfaceKind category, Guid? scope, int channel)
    {
        var button = new Button
        {
            Content = selectedPath is null ? label + " +" : label + " ✓",
            ToolTip = selectedPath is null ? "Cargar mapa PBR de " + label.ToLowerInvariant()
                : "Mapa cargado: " + Path.GetFileName(selectedPath),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 3, 5, 0)
        };
        button.Click += (_, _) => ChoosePbrMap(category, scope, channel);
        panel.Children.Add(button);
        if (selectedPath is not null)
        {
            var remove = new Button
            {
                Content = "×", ToolTip = "Quitar mapa de " + label.ToLowerInvariant(),
                Padding = new Thickness(5, 2, 5, 2),
                Margin = new Thickness(0, 3, 8, 0)
            };
            remove.Click += (_, _) => ChangePbrMap(category, scope, channel, null);
            panel.Children.Add(remove);
        }
    }

    private void ChoosePbrMap(SurfaceKind category, Guid? scope, int channel)
    {
        if (_source is null) return;
        var dialog = new OpenFileDialog
        {
            Title = channel switch { 0 => "Elegir mapa normal OpenGL (+Y)", 1 => "Elegir mapa de rugosidad", _ => "Elegir mapa de metalicidad" },
            Filter = "Imágenes (PNG, JPEG, BMP, TIFF)|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|Todos los archivos|*.*"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            ChangePbrMap(category, scope, channel, dialog.FileName);
    }

    private void ChangePbrMap(SurfaceKind category, Guid? scope, int channel, string? value)
    {
        if (_source is null) return;
        var current = _source.RenderMaterials.FirstOrDefault(a => a.Category == category && a.ElementId == scope);
        var normal = channel == 0 ? value : current?.NormalPath;
        var rough = channel == 1 ? value : current?.RoughnessPath;
        var metal = channel == 2 ? value : current?.MetallicPath;
        _source.SetRenderMaterialMaps(category, scope, normal, rough, metal);
        RefreshMaterialRows();
    }

    private static double ReadSize(TextBox box, double fallback) =>
        double.TryParse(box.Text.Replace("m", string.Empty).Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var value) &&
        value is >= 0.01 and <= 1000 ? value : fallback;

    private static string Text(string key) => Application.Current?.TryFindResource(key) as string ?? key;
}
