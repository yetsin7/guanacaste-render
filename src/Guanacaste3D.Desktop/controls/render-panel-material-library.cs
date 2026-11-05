using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;
using Microsoft.Win32;

namespace Guanacaste3D.Desktop.Controls;

public partial class RenderPanel
{
    /// <summary>Library browser: audit before assigning, show missing maps and the real albedo preview.</summary>
    private void BrowsePbrMaterialLibrary(SurfaceKind category, Guid? scope)
    {
        var owner = Window.GetWindow(this);
        var folder = new OpenFolderDialog { Title = "Carpeta de materiales .g3dm" };
        if (folder.ShowDialog(owner) != true) return;
        var items = PbrMaterialAudit.Scan(folder.FolderName)
            .Select(item => new MaterialCatalogEntry(item)).ToArray();
        var dialog = new Window
        {
            Title = "Biblioteca PBR — Guanacaste 3D",
            Owner = owner, Width = 800, Height = 610, MinWidth = 650, MinHeight = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var layout = new Grid { Margin = new Thickness(16) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(285) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var search = new TextBox { Margin = new Thickness(0, 0, 0, 10),
            ToolTip = "Busca por material, categoría, etiqueta, licencia o autor" };
        Grid.SetColumnSpan(search, 2);
        layout.Children.Add(search);
        var list = new ListBox { Margin = new Thickness(0, 0, 12, 0) };
        Grid.SetRow(list, 1);
        layout.Children.Add(list);
        var details = new StackPanel { Margin = new Thickness(12) };
        var preview = new Image { Width = 224, Height = 160, Stretch = Stretch.Uniform };
        details.Children.Add(preview);
        var report = new TextBlock { TextWrapping = TextWrapping.Wrap };
        details.Children.Add(report);
        var description = new ScrollViewer
        {
            Content = details,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(description, 1);
        Grid.SetColumn(description, 1);
        layout.Children.Add(description);

        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = true };
        var apply = new Button { Content = "Asignar material", MinWidth = 130,
            Padding = new Thickness(12, 6, 12, 6), IsEnabled = false };
        var close = new Button { Content = "Cerrar", MinWidth = 90,
            Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 6, 12, 6) };
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(apply, Dock.Right);
        footer.Children.Add(close);
        footer.Children.Add(apply);
        footer.Children.Add(new TextBlock
        {
            Text = $"{items.Length} materiales encontrados · Los inválidos se muestran para facilitar su reparación.",
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap
        });
        Grid.SetRow(footer, 2);
        Grid.SetColumnSpan(footer, 2);
        layout.Children.Add(footer);

        void Filter()
        {
            var query = (search.Text ?? "").Trim();
            list.ItemsSource = items.Where(entry => query.Length == 0 ||
                entry.SearchText.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        }

        list.SelectionChanged += (_, _) =>
        {
            preview.Source = null;
            if (list.SelectedItem is not MaterialCatalogEntry entry)
            {
                report.Text = "";
                apply.IsEnabled = false;
                return;
            }
            var inspected = entry.Inspection;
            apply.IsEnabled = inspected.IsValid;
            report.Text = inspected.Material is { } material
                ? $"{material.Name}\nCategoría: {material.Category}\n" +
                  $"Autor: {material.Author}\nLicencia: {material.License}\n" +
                  $"Etiquetas: {string.Join(", ", material.Tags)}\n" +
                  $"Rugosidad: {material.Roughness:0.##}   Metal: {material.Metallic:0.##}\n" +
                  $"IOR: {material.Ior:0.##}   Escala: {material.TextureSize:0.##} m\n" +
                  $"Mapas: {string.Join(", ", material.Maps.Keys)}\n{material.Description}\n" +
                  string.Join("\n", inspected.Warnings.Select(w => "Aviso: " + w))
                : "Material no válido:\n" + string.Join("\n", inspected.Errors);
            if (inspected.Material?.Maps.TryGetValue("albedo", out var imagePath) == true)
            {
                try
                {
                    // Bound the longest image axis, not just the width. A narrow,
                    // extremely tall texture must not allocate an enormous preview.
                    var uri = new Uri(imagePath);
                    var decoder = BitmapDecoder.Create(uri, BitmapCreateOptions.DelayCreation,
                        BitmapCacheOption.None);
                    var frame = decoder.Frames[0];
                    if (frame.PixelWidth < 1 || frame.PixelHeight < 1)
                        throw new FileFormatException("Invalid PBR preview dimensions.");
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    if (frame.PixelWidth >= frame.PixelHeight)
                        bitmap.DecodePixelWidth = 320;
                    else
                        bitmap.DecodePixelHeight = 320;
                    bitmap.UriSource = uri;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    preview.Source = bitmap;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                            FileFormatException or NotSupportedException or ArgumentException)
                {
                    report.Text += "\nLa vista previa no se pudo decodificar.";
                }
            }
        };
        search.TextChanged += (_, _) => Filter();
        close.Click += (_, _) => dialog.Close();
        apply.Click += (_, _) =>
        {
            if (list.SelectedItem is not MaterialCatalogEntry { Inspection: { IsValid: true, Material: { } material } }) return;
            _source?.AssignRenderMaterial(category, PbrMaterialPackages.Prefix + material.Path,
                material.TextureSize, material.Relief, scope);
            dialog.Close();
        };
        Filter();
        dialog.Content = layout;
        dialog.ShowDialog();
    }

    private sealed record MaterialCatalogEntry(PbrMaterialAudit.Inspection Inspection)
    {
        public string SearchText => Inspection.Material is { } m
            ? string.Join(" ", new[] { m.Name, m.Category, m.Description, m.Author, m.License,
                string.Join(" ", m.Tags) })
            : Path.GetFileName(Inspection.Path) + " " + string.Join(" ", Inspection.Errors);
        public override string ToString() => Inspection.Material is { } m
            ? $"{m.Name}  ·  {m.Category}"
            : "⚠ " + Path.GetFileName(Inspection.Path);
    }
}
