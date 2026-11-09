using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Rendering;
using Microsoft.Win32;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Insert > Decal: lists the images stuck on walls and sticks a new one on the selected wall, choosing the face, the size and
/// where it goes. The result is the whole list; the window saves nothing itself.
/// </summary>
internal sealed class DecalDialog : Window
{
    private readonly BuildingDocument _document;
    private readonly Wall? _wall;
    private readonly string? _projectFile;
    private readonly List<Decal> _decals;
    private readonly ListBox _list = new() { MinHeight = 120, MaxHeight = 190, Margin = new Thickness(0, 6, 0, 6) };
    private readonly TextBox _image = new() { MinHeight = 28, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ComboBox _face = new() { MinHeight = 28, SelectedIndex = 0 };
    private readonly TextBox _width = new() { Text = "1", MinHeight = 28, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox _height = new() { Text = "1", MinHeight = 28, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox _along = new() { MinHeight = 28, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox _up = new() { MinHeight = 28, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Image _preview = new() { Height = 70, Stretch = System.Windows.Media.Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), MinHeight = 34 };
    private double _aspect = 1;

    /// <summary>The decals the person left, or null when the window was cancelled.</summary>
    public List<Decal>? Result { get; private set; }

    public DecalDialog(BuildingDocument document, Wall? wall, string? projectFile)
    {
        _document = document;
        _wall = wall;
        _projectFile = projectFile;
        _decals = [.. Decals.Of(document)];
        Title = "Estampados";
        Width = 540;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 800;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "AppBackgroundBrush");
        SetResourceReference(ForegroundProperty, "PrimaryTextBrush");

        var root = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
        root.Children.Add(new TextBlock { Text = "Estampados", FontSize = 18, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock
        {
            Text = "Imágenes pegadas en los muros que se ven en el render (rótulos, murales, logotipos). Un PNG con transparencia conserva sus huecos.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 12, Margin = new Thickness(0, 4, 0, 8)
        });
        root.Children.Add(_list);
        var remove = new Button { Content = "Quitar el seleccionado", Padding = new Thickness(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Left };
        remove.Click += (_, _) => RemoveSelected();
        root.Children.Add(remove);

        root.Children.Add(new TextBlock
        {
            Text = wall is null
                ? "Para añadir uno, cierra esta ventana, selecciona un muro y vuelve a abrirla."
                : $"Muro seleccionado: {wall.Length:0.##} m de largo y {wall.Height:0.##} m de alto.",
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6), TextWrapping = TextWrapping.Wrap
        });
        var form = new Grid { IsEnabled = wall is not null };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var browse = new Button { Content = "Examinar…", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0) };
        browse.Click += (_, _) => Browse();
        var imageRow = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        imageRow.Children.Add(browse);
        imageRow.Children.Add(_image);
        _face.Items.Add("Izquierda del sentido del muro");
        _face.Items.Add("Derecha del sentido del muro");
        var rows = new (string Label, FrameworkElement Field)[]
        {
            ("Imagen", imageRow), ("Cara del muro", _face), ("Ancho (m)", _width), ("Alto (m)", _height),
            ("Posición a lo largo del muro (m)", _along), ("Altura del centro sobre el nivel (m)", _up)
        };
        for (var row = 0; row < rows.Length; row++)
        {
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = rows[row].Label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 8, 3) };
            Grid.SetRow(label, row);
            Grid.SetRow(rows[row].Field, row);
            Grid.SetColumn(rows[row].Field, 1);
            rows[row].Field.Margin = new Thickness(0, 3, 0, 3);
            form.Children.Add(label);
            form.Children.Add(rows[row].Field);
        }
        if (wall is not null)
        {
            _along.Text = Format(wall.Length / 2);
            _up.Text = Format(Math.Min(1.5, (wall.BaseOffset + wall.Height) / 2));
        }
        root.Children.Add(form);
        root.Children.Add(_preview);
        var add = new Button { Content = "Añadir este estampado", Padding = new Thickness(12, 5, 12, 5), HorizontalAlignment = HorizontalAlignment.Left,
            IsEnabled = wall is not null, Margin = new Thickness(0, 6, 0, 0) };
        add.Click += (_, _) => Add();
        root.Children.Add(add);
        root.Children.Add(_message);

        var accept = new Button { Content = "Aceptar", MinWidth = 110, MinHeight = 32, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        accept.Click += (_, _) => { Result = _decals; DialogResult = true; };
        var cancel = new Button { Content = "Cancelar", MinWidth = 90, MinHeight = 32, IsCancel = true };
        root.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0), Children = { accept, cancel } });
        Content = root;
        RefreshList();
    }

    private static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static bool TryNumber(TextBox box, out double value) =>
        double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private void RefreshList()
    {
        _list.Items.Clear();
        foreach (var decal in _decals)
        {
            var wall = _document.Walls.FirstOrDefault(item => item.Id == decal.WallId);
            _list.Items.Add(new ListBoxItem
            {
                Tag = decal,
                Content = $"{decal.Name} · {Format(decal.Width)} × {Format(decal.Height)} m · a {Format(decal.Along)} m del inicio, " +
                          $"{(decal.Face == DecalFace.Left ? "cara izquierda" : "cara derecha")}" + (wall is null ? " · el muro ya no existe" : "")
            });
        }
    }

    private void RemoveSelected()
    {
        if (_list.SelectedItem is not ListBoxItem { Tag: Decal decal }) return;
        _decals.Remove(decal);
        RefreshList();
        _message.Text = $"«{decal.Name}» se quitará al pulsar Aceptar.";
    }

    private void Browse()
    {
        var dialog = new OpenFileDialog { Title = "Imagen del estampado", Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif" };
        if (dialog.ShowDialog(this) != true) return;
        _image.Text = dialog.FileName;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(dialog.FileName);
            bitmap.DecodePixelHeight = 140;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            _preview.Source = bitmap;
            _aspect = bitmap.PixelWidth / (double)Math.Max(1, bitmap.PixelHeight);
            // The height follows the picture's shape so it is not stretched; it can still be changed by hand.
            if (TryNumber(_width, out var width)) _height.Text = Format(width / _aspect);
            _message.Text = "";
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or UriFormatException or InvalidOperationException)
        {
            _preview.Source = null;
            _message.Text = "No se pudo leer esa imagen: " + exception.Message;
        }
    }

    private void Add()
    {
        if (_wall is null) return;
        if (_decals.Count >= Decals.Maximum) { _message.Text = $"Un proyecto admite hasta {Decals.Maximum} estampados."; return; }
        if (string.IsNullOrWhiteSpace(_image.Text) || !File.Exists(_image.Text)) { _message.Text = "Elige primero una imagen que exista."; return; }
        if (!TryNumber(_width, out var width) || !TryNumber(_height, out var height) || !TryNumber(_along, out var along) || !TryNumber(_up, out var up))
        {
            _message.Text = "Escribe números en el ancho, el alto y las dos posiciones.";
            return;
        }
        var decal = new Decal(Guid.NewGuid(), _wall.Id, _face.SelectedIndex == 1 ? DecalFace.Right : DecalFace.Left, along, up, width, height,
            StoredPath(_image.Text));
        if (Decals.Fit(_wall, decal) is { } problem) { _message.Text = problem; return; }
        _decals.Add(decal);
        RefreshList();
        _message.Text = $"«{decal.Name}» añadido. Pulsa Aceptar para verlo en el render.";
    }

    /// <summary>A path inside the project's folder is kept relative to it, so the project and its images can move together.</summary>
    private string StoredPath(string path)
    {
        var folder = _projectFile is null ? null : Path.GetDirectoryName(Path.GetFullPath(_projectFile));
        if (folder is null) return path;
        var relative = Path.GetRelativePath(folder, path);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? path : relative;
    }
}
