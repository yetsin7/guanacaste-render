using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>One material of the picker: what a click applies, how it is listed, and the values its sliders start from.</summary>
internal sealed record PickerItem(string Key, string Label, string Category, double Size, double Relief, double Roughness, double Metallic,
    RenderMaterial? Material, string? ImageFile);

/// <summary>What the picker needs from the Render panel: where a click lands, what is applied now, and how to apply and adjust.</summary>
internal sealed record PickerHost(Func<IReadOnlyList<string>> Surfaces, Func<int> SurfaceIndex, Func<bool> SurfaceEnabled, Action<int> SetSurface, Func<string> TargetName, Func<string?> CurrentKey, Func<(double Roughness, double Metallic, double Relief, double Size, string? Tint)> Appearance,
    Action<PickerItem> Apply, Action<double, double, double, double, string?> Adjust, Action Reset);

/// <summary>
/// The material library, like Lumion's: a big grid of lit balls grouped by kind (metals, plastics and rubbers, mineral and natural
/// surfaces, wood, glass…), each with its name under it. One click applies the material to the surface being edited; the
/// sliders under the grid (gloss, metal, relief, size) and the colour boxes change it at once, with no technical fields.
/// </summary>
internal sealed class MaterialPickerWindow : Window
{
    private const int BallSize = 112;
    private static readonly (string Id, string Name)[] Categories =
    [
        ("all", "Todos"), ("metal", "Metales"), ("plastic", "Plásticos y cauchos"), ("mineral", "Superficies minerales"), ("natural", "Superficies naturales"),
        ("wood", "Madera"), ("glass", "Vidrio y espejos"), ("paint", "Pinturas y estucos"), ("floor", "Pisos y azulejos"), ("fabric", "Telas y cuero"),
        ("other", "Otros acabados"), ("photo", "Texturas fotográficas")
    ];

    private static readonly string[] Tints =
    [
        "#FFFFFF", "#F2E6D0", "#E8C9A0", "#D9A066", "#B5651D", "#8B4513", "#C0392B", "#E07A5F",
        "#F2CC8F", "#A7C957", "#6A994E", "#2A9D8F", "#81B3D6", "#4A6FA5", "#7D7D7D", "#2B2B2B"
    ];

    private static readonly HashSet<string> MineralOutdoor = ["asphalt", "gravel", "pavers", "nicaragua-paving", "retaining-drainage-gravel", "sand", "asphalt-worn"];

    private readonly IReadOnlyList<PickerItem> _items;
    private readonly PickerHost _host;
    private readonly TextBox _search = new() { MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 8), ToolTip = "Busca por nombre" };
    private readonly ListBox _categories = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly WrapPanel _tiles = new() { Margin = new Thickness(8) };
    private readonly TextBlock _target = new() { FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 6), VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _surface = new() { MinWidth = 220, Margin = new Thickness(0, 0, 0, 6), ToolTip = "Qué superficie del proyecto recibe el material" };
    private readonly Slider _gloss = new() { Minimum = 0, Maximum = 1, TickFrequency = 0.02 };
    private readonly Slider _metal = new() { Minimum = 0, Maximum = 1, TickFrequency = 0.02 };
    private readonly Slider _relief = new() { Minimum = 0, Maximum = 1, TickFrequency = 0.02 };
    private readonly Slider _size = new() { Minimum = 0.1, Maximum = 6, TickFrequency = 0.05 };
    private readonly WrapPanel _swatches = new();
    private readonly SemaphoreSlim _gate = new(3, 3);
    private readonly Dictionary<string, BitmapSource> _balls = [];
    private readonly Dictionary<string, Border> _tileOf = [];
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private string? _tint;
    private bool _loading;
    private int _revision;

    public MaterialPickerWindow(IReadOnlyList<PickerItem> items, PickerHost host)
    {
        _items = items;
        _host = host;
        Title = "Biblioteca de materiales";
        Width = 1020;
        Height = 700;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        // The same dark palette as the Render studio, set outright so the text is always readable on it.
        Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x2A, 0x3A));
        Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xF6, 0xFB));
        _categories.Foreground = Foreground;

        var layout = new Grid { Margin = new Thickness(14) };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var side = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
        DockPanel.SetDock(_search, Dock.Top);
        side.Children.Add(_search);
        foreach (var (id, name) in Categories)
            _categories.Items.Add(new ListBoxItem { Tag = id, Content = $"{name}  ·  {Count(id)}", Padding = new Thickness(10, 7, 10, 7), FontSize = 13 });
        _categories.SelectedIndex = 0;
        side.Children.Add(_categories);
        layout.Children.Add(side);

        var scroller = new ScrollViewer { Content = _tiles, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(scroller, 1);
        layout.Children.Add(scroller);

        var controls = BuildControls();
        Grid.SetRow(controls, 1);
        Grid.SetColumnSpan(controls, 2);
        layout.Children.Add(controls);
        Content = layout;

        _search.TextChanged += (_, _) => Refresh();
        _categories.SelectionChanged += (_, _) => Refresh();
        _debounce.Tick += (_, _) => { _debounce.Stop(); _host.Adjust(1 - _gloss.Value, _metal.Value, _relief.Value, _size.Value, _tint); };
        Closed += (_, _) => { _debounce.Stop(); _revision++; };
        Loaded += (_, _) => { SyncSliders(); Refresh(); };
        Activated += (_, _) => { SyncSliders(); Highlight(); };
    }

    private int Count(string id) => id == "all" ? _items.Count : _items.Count(item => item.Category == id);

    private FrameworkElement BuildControls()
    {
        var panel = new Border
        {
            Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(14, 10, 14, 10), CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x30, 0x42)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x53, 0x65)), BorderThickness = new Thickness(1)
        };
        var grid = new Grid();
        for (var i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock { Text = "Aplicar a: ", Opacity = 0.7, Margin = new Thickness(0, 0, 4, 6), VerticalAlignment = VerticalAlignment.Center });
        foreach (var name in _host.Surfaces()) _surface.Items.Add(name);
        _surface.SelectionChanged += (_, _) =>
        {
            if (_loading || _surface.SelectedIndex < 0) return;
            _host.SetSurface(_surface.SelectedIndex);
            SyncSliders();
            Highlight();
        };
        header.Children.Add(_surface);
        header.Children.Add(_target);
        Grid.SetColumnSpan(header, 5);
        grid.Children.Add(header);
        var sliders = new (string Label, string Hint, Slider Control)[]
        {
            ("Brillo", "De mate a espejo", _gloss), ("Metálico", "Cuánto parece metal", _metal), ("Relieve", "Profundidad de la textura", _relief), ("Tamaño (m)", "Medida real de una repetición", _size)
        };
        for (var i = 0; i < sliders.Length; i++)
        {
            var (label, hint, control) = sliders[i];
            var cell = new StackPanel { Margin = new Thickness(0, 0, 14, 0), ToolTip = hint };
            var value = new TextBlock { Opacity = 0.8, HorizontalAlignment = HorizontalAlignment.Right };
            var row = new DockPanel();
            DockPanel.SetDock(value, Dock.Right);
            row.Children.Add(value);
            row.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });
            cell.Children.Add(row);
            cell.Children.Add(control);
            control.ValueChanged += (_, _) =>
            {
                value.Text = control == _size ? $"{control.Value:0.0#}" : $"{control.Value:0%}";
                if (_loading) return;
                _debounce.Stop();
                _debounce.Start();
            };
            Grid.SetRow(cell, 1);
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }
        var colour = new StackPanel();
        colour.Children.Add(new TextBlock { Text = "Color", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        colour.Children.Add(_swatches);
        var reset = new Button { Content = "Restablecer", Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0) };
        reset.Click += (_, _) => { _host.Reset(); SyncSliders(); };
        colour.Children.Add(reset);
        Grid.SetRow(colour, 1);
        Grid.SetColumn(colour, 4);
        grid.Children.Add(colour);
        panel.Child = grid;
        return panel;
    }

    private void BuildSwatches()
    {
        _swatches.Children.Clear();
        foreach (var hex in Tints)
        {
            var none = hex == Tints[0];
            var chosen = none ? _tint is null : string.Equals(_tint, hex, StringComparison.OrdinalIgnoreCase);
            var swatch = new Border
            {
                Width = 22, Height = 22, Margin = new Thickness(0, 0, 4, 4), CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
                Background = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!, BorderThickness = new Thickness(chosen ? 2 : 1),
                BorderBrush = new SolidColorBrush(chosen ? Color.FromRgb(0x70, 0xDA, 0xC9) : Color.FromRgb(0x40, 0x60, 0x75)), ToolTip = none ? "Sin colorización" : hex
            };
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                _tint = none ? null : hex;
                BuildSwatches();
                _debounce.Stop();
                _debounce.Start();
            };
            _swatches.Children.Add(swatch);
        }
    }

    private void SyncSliders()
    {
        _loading = true;
        try
        {
            var (roughness, metallic, relief, size, tint) = _host.Appearance();
            _gloss.Value = 1 - roughness;
            _metal.Value = metallic;
            _relief.Value = relief;
            _size.Value = Math.Clamp(size, _size.Minimum, _size.Maximum);
            _tint = tint;
            _surface.SelectedIndex = _host.SurfaceIndex();
            _surface.IsEnabled = _host.SurfaceEnabled();
            _target.Text = _host.TargetName();
            BuildSwatches();
        }
        finally { _loading = false; }
    }

    private IEnumerable<PickerItem> Visible()
    {
        var category = (_categories.SelectedItem as ListBoxItem)?.Tag as string ?? "all";
        var query = _search.Text.Trim();
        return _items.Where(item => (category == "all" || item.Category == category) &&
                                    (query.Length == 0 || item.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase)));
    }

    private void Refresh()
    {
        var revision = ++_revision;
        _tiles.Children.Clear();
        _tileOf.Clear();
        foreach (var item in Visible())
        {
            var picture = new Image { Width = BallSize, Height = BallSize, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            var label = new TextBlock
            {
                Text = item.Label, Foreground = Foreground, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxHeight = 36, FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 4, 2, 0), HorizontalAlignment = HorizontalAlignment.Center
            };
            var stack = new StackPanel { Width = BallSize + 12 };
            stack.Children.Add(picture);
            stack.Children.Add(label);
            var tile = new Border
            {
                Child = stack, Margin = new Thickness(4), Padding = new Thickness(4, 6, 4, 6), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x1E, 0x2A)), BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x3A, 0x4A)), ToolTip = item.Label
            };
            tile.MouseLeftButtonUp += (_, _) => { _host.Apply(item); SyncSliders(); Highlight(); };
            tile.MouseEnter += (_, _) => { if (tile.BorderBrush is SolidColorBrush { Color.G: not 0xDA }) tile.BorderBrush = new SolidColorBrush(Color.FromRgb(0x5A, 0x78, 0x90)); };
            tile.MouseLeave += (_, _) => Highlight();
            _tiles.Children.Add(tile);
            _tileOf[item.Key] = tile;
            if (_balls.TryGetValue(item.Key, out var cached)) picture.Source = cached;
            else _ = LoadBallAsync(item, picture, revision);
        }
        Highlight();
    }

    private void Highlight()
    {
        var current = _host.CurrentKey();
        foreach (var (key, tile) in _tileOf)
        {
            var selected = key == current;
            tile.BorderBrush = new SolidColorBrush(selected ? Color.FromRgb(0x70, 0xDA, 0xC9) : Color.FromRgb(0x2A, 0x3A, 0x4A));
            tile.Background = new SolidColorBrush(selected ? Color.FromRgb(0x1D, 0x33, 0x40) : Color.FromRgb(0x14, 0x1E, 0x2A));
        }
    }

    private async Task LoadBallAsync(PickerItem item, Image view, int revision)
    {
        BitmapSource? ball;
        await _gate.WaitAsync();
        try { ball = await Task.Run(() => MakeBall(item)); }
        finally { _gate.Release(); }
        if (ball is null) return;
        _balls[item.Key] = ball;
        if (revision == _revision) view.Source = ball;
    }

    private static BitmapSource? MakeBall(PickerItem item)
    {
        try
        {
            var material = item.Material ?? new RenderMaterial { Name = item.Label, BaseColor = new Vector3(0.5f), Roughness = (float)item.Roughness, Metallic = (float)item.Metallic };
            Func<float, float, Vector3>? sampler = null;
            if (item.ImageFile is { } file && File.Exists(file)) sampler = LoadSampler(file);
            if (sampler is not null) material = material with { BaseColor = Vector3.One, Pattern = SurfacePattern.None };
            var bytes = MaterialBall.Render(material, BallSize, sampler, (float)item.Relief);
            var result = BitmapSource.Create(BallSize, BallSize, 96, 96, PixelFormats.Bgra32, null, bytes, BallSize * 4);
            result.Freeze();
            return result;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>A bilinear reader of a photograph in linear light, wrapping round the ball, from a copy no larger than 192 pixels.</summary>
    private static Func<float, float, Vector3> LoadSampler(string file)
    {
        var image = new BitmapImage();
        using (var stream = File.OpenRead(file))
        {
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 192;
            image.StreamSource = stream;
            image.EndInit();
        }
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int width = converted.PixelWidth, height = converted.PixelHeight;
        var pixels = new byte[width * height * 4];
        converted.CopyPixels(pixels, width * 4, 0);
        static float Linear(byte value) { var s = value / 255f; return s <= 0.04045f ? s / 12.92f : MathF.Pow((s + 0.055f) / 1.055f, 2.4f); }
        Vector3 At(int x, int y)
        {
            var offset = (((y % height + height) % height) * width + ((x % width + width) % width)) * 4;
            return new Vector3(Linear(pixels[offset + 2]), Linear(pixels[offset + 1]), Linear(pixels[offset]));
        }
        return (u, v) =>
        {
            var fx = (u - MathF.Floor(u)) * width - 0.5f;
            var fy = Math.Clamp(v, 0, 1) * (height - 1);
            int x = (int)MathF.Floor(fx), y = (int)MathF.Floor(fy);
            float tx = fx - x, ty = fy - y;
            return Vector3.Lerp(Vector3.Lerp(At(x, y), At(x + 1, y), tx), Vector3.Lerp(At(x, y + 1), At(x + 1, y + 1), tx), ty);
        };
    }

    /// <summary>The picker's category for a preset of the library.</summary>
    internal static string CategoryOf(MaterialPreset preset) => preset.Group switch
    {
        MaterialFamily.Metal => "metal",
        MaterialFamily.Plastic => "plastic",
        MaterialFamily.Concrete or MaterialFamily.Masonry or MaterialFamily.Stone => "mineral",
        MaterialFamily.Outdoor => MineralOutdoor.Contains(preset.Id) ? "mineral" : "natural",
        MaterialFamily.Wood => "wood",
        MaterialFamily.Glass => "glass",
        MaterialFamily.Paint => "paint",
        MaterialFamily.Floor => "floor",
        MaterialFamily.Fabric => "fabric",
        _ => "other"
    };
}
