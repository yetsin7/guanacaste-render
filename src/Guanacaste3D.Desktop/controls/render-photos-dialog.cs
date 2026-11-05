using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>What the user asked the batch to produce.</summary>
internal sealed record PhotoRenderOptions(IReadOnlyList<Guid> PhotoIds, string Size, string Quality, bool Jpeg,
    string Folder, bool OpenFolder);

/// <summary>
/// Lumion's "Render" window: pick one photo, several or all of them, the resolution and quality, and where the pictures go.
/// Everything is rendered one after another, each with its own camera, style and filters.
/// </summary>
internal sealed class RenderPhotosDialog : Window
{
    private static readonly (string Label, string Tag)[] Sizes =
    [
        ("1280 × 720 (HD)", "1280x720"), ("1920 × 1080 (Full HD)", "1920x1080"),
        ("2560 × 1440 (QHD)", "2560x1440"), ("3840 × 2160 (4K)", "3840x2160")
    ];

    private static readonly (string Label, string Tag)[] Qualities =
    [
        ("Automática (según tu equipo)", "Auto"), ("Borrador (rápida)", "Draft"), ("Estándar", "Standard"),
        ("Alta", "High"), ("Final (máxima calidad)", "Final")
    ];

    private readonly List<(CheckBox Box, Guid Id)> _entries = [];
    private readonly ComboBox _size = new() { MinHeight = 30 };
    private readonly ComboBox _quality = new() { MinHeight = 30 };
    private readonly ComboBox _format = new() { MinHeight = 30 };
    private readonly TextBox _folder = new() { MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly CheckBox _open = new() { Content = "Abrir la carpeta al terminar", IsChecked = true, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _summary = new() { Margin = new Thickness(0, 6, 0, 0), Opacity = 0.75, FontSize = 12 };
    private readonly Button _accept = new() { MinWidth = 130, MinHeight = 34, Margin = new Thickness(0, 0, 8, 0), IsDefault = true,
        FontWeight = FontWeights.SemiBold };

    public PhotoRenderOptions? Result { get; private set; }

    public RenderPhotosDialog(IReadOnlyList<RenderSceneBookmark> photos, Guid? only, string folder)
    {
        Title = "Renderizar fotos";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "AppBackgroundBrush");
        SetResourceReference(ForegroundProperty, "PrimaryTextBrush");

        var root = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };
        root.Children.Add(new TextBlock { Text = "Renderizar fotos", FontSize = 18, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock
        {
            Text = photos.Count == 0
                ? "Aún no tienes fotos guardadas; se renderizará la vista actual."
                : "Marca las fotos que quieres renderizar. Cada una usa su propia cámara, estilo y filtros.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10), Opacity = 0.8
        });

        if (photos.Count > 0)
        {
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            bar.Children.Add(LinkButton("Todas", () => SetAll(true)));
            bar.Children.Add(LinkButton("Ninguna", () => SetAll(false)));
            root.Children.Add(bar);
            var list = new StackPanel();
            foreach (var photo in photos)
            {
                var box = new CheckBox { IsChecked = only is null || only == photo.Id, VerticalAlignment = VerticalAlignment.Center };
                box.Checked += (_, _) => UpdateSummary();
                box.Unchecked += (_, _) => UpdateSummary();
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
                row.Children.Add(box);
                row.Children.Add(new Image { Width = 64, Height = 36, Stretch = Stretch.UniformToFill, Margin = new Thickness(10, 0, 10, 0),
                    Source = PhotoThumbnails.Of(photo) });
                row.Children.Add(new TextBlock { Text = photo.Name, VerticalAlignment = VerticalAlignment.Center });
                list.Children.Add(row);
                _entries.Add((box, photo.Id));
            }
            root.Children.Add(new Border
            {
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6), MaxHeight = 230,
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)),
                Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list }
            });
        }
        root.Children.Add(_summary);

        root.Children.Add(Label("Resolución"));
        foreach (var (label, tag) in Sizes) _size.Items.Add(new ComboBoxItem { Content = label, Tag = tag });
        _size.SelectedIndex = 1;
        root.Children.Add(_size);
        root.Children.Add(Label("Calidad"));
        foreach (var (label, tag) in Qualities) _quality.Items.Add(new ComboBoxItem { Content = label, Tag = tag });
        _quality.SelectedIndex = 0;
        root.Children.Add(_quality);
        root.Children.Add(Label("Formato"));
        _format.Items.Add(new ComboBoxItem { Content = "PNG (sin pérdida)", Tag = "png" });
        _format.Items.Add(new ComboBoxItem { Content = "JPG (más liviano)", Tag = "jpg" });
        _format.SelectedIndex = 0;
        root.Children.Add(_format);
        root.Children.Add(Label("Carpeta de destino"));
        _folder.Text = folder;
        var folderRow = new DockPanel();
        var browse = new Button { Content = "Cambiar…", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0), MinHeight = 30 };
        DockPanel.SetDock(browse, Dock.Right);
        browse.Click += (_, _) =>
        {
            var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Carpeta para guardar los renders", InitialDirectory = _folder.Text };
            if (picker.ShowDialog(this) == true) _folder.Text = picker.FolderName;
        };
        folderRow.Children.Add(browse);
        folderRow.Children.Add(_folder);
        root.Children.Add(folderRow);
        root.Children.Add(_open);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        _accept.Click += (_, _) => Accept();
        buttons.Children.Add(_accept);
        buttons.Children.Add(new Button { Content = "Cancelar", MinWidth = 86, MinHeight = 34, IsCancel = true });
        root.Children.Add(buttons);
        Content = root;
        UpdateSummary();
    }

    private static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 12, 0, 4), Opacity = 0.8, FontSize = 12 };

    private static Button LinkButton(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
        button.Click += (_, _) => action();
        return button;
    }

    private void SetAll(bool value)
    {
        foreach (var (box, _) in _entries) box.IsChecked = value;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var count = _entries.Count(entry => entry.Box.IsChecked == true);
        if (_entries.Count == 0)
        {
            _accept.Content = "Renderizar vista";
            return;
        }
        _summary.Text = count == _entries.Count ? $"Se renderizarán las {count} fotos, una tras otra." : $"{count} de {_entries.Count} fotos marcadas.";
        _accept.Content = count == 1 ? "Renderizar 1 foto" : $"Renderizar {count} fotos";
        _accept.IsEnabled = count > 0;
    }

    private void Accept()
    {
        if (string.IsNullOrWhiteSpace(_folder.Text))
        {
            MessageBox.Show(this, "Elige una carpeta de destino.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Result = new PhotoRenderOptions(
            [.. _entries.Where(entry => entry.Box.IsChecked == true).Select(entry => entry.Id)],
            (_size.SelectedItem as ComboBoxItem)?.Tag as string ?? "1920x1080",
            (_quality.SelectedItem as ComboBoxItem)?.Tag as string ?? "Auto",
            ((_format.SelectedItem as ComboBoxItem)?.Tag as string) == "jpg",
            _folder.Text.Trim(), _open.IsChecked == true);
        DialogResult = true;
    }
}
