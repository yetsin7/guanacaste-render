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
    private const int BallSize = 64;
    private const int PageSize = 24;
    private int _page;
    private readonly TextBlock _pageText = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Button _previous = new() { Content = "‹", Padding = new Thickness(12, 4, 12, 4) };
    private readonly Button _next = new() { Content = "›", Padding = new Thickness(12, 4, 12, 4) };
    private static readonly (string Id, string Name)[] Categories =
    [
        ("all", "Todos"), ("metal", "Metales"), ("plastic", "Plásticos y cauchos"), ("mineral", "Superficies minerales"), ("natural", "Superficies naturales"),
        ("wood", "Madera"), ("glass", "Vidrio y espejos"), ("paint", "Pinturas y estucos"), ("floor", "Pisos y azulejos"), ("fabric", "Telas y cuero"),
        ("water", "Agua"), ("other", "Otros acabados"), ("photo", "Texturas fotográficas")
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
        Width = 460;
        Height = 740;
        MinWidth = 420;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        // The same dark palette as the Render studio, set outright so the text is always readable on it.
        Background = new SolidColorBrush(Color.FromRgb(0x20, 0x21, 0x24));
        Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xF6, 0xFB));
        _categories.Foreground = Foreground;
        ScrollViewer.SetHorizontalScrollBarVisibility(_categories, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(_categories, ScrollBarVisibility.Disabled);

        var layout = new DockPanel { Margin = new Thickness(16) };
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        var libraryTab = new Button { Content = "▦  Biblioteca", Padding = new Thickness(16, 10, 16, 10) };
        var settingsTab = new Button { Content = "✎  Ajustes", Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(6, 0, 0, 0) };
        tabs.Children.Add(libraryTab); tabs.Children.Add(settingsTab);
        DockPanel.SetDock(tabs, Dock.Top); layout.Children.Add(tabs);
        var surfaceRow = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        surfaceRow.Children.Add(new TextBlock { Text = "Superficie", Foreground = Foreground, Margin = new Thickness(0, 0, 0, 5) });
        foreach (var name in _host.Surfaces()) _surface.Items.Add(name);
        _surface.SelectionChanged += (_, _) =>
        {
            if (_loading || _surface.SelectedIndex < 0) return;
            FlushAppearance();
            _host.SetSurface(_surface.SelectedIndex); SyncSliders(); Highlight();
        };
        surfaceRow.Children.Add(_surface); surfaceRow.Children.Add(_target);
        DockPanel.SetDock(surfaceRow, Dock.Top); layout.Children.Add(surfaceRow);
        var pages = new Grid();
        var library = new DockPanel();
        DockPanel.SetDock(_search, Dock.Top); library.Children.Add(_search);
        var wrap = new FrameworkElementFactory(typeof(WrapPanel));
        _categories.ItemsPanel = new ItemsPanelTemplate(wrap);
        var glyphs = new[] { "▦", "◇", "◉", "▧", "♧", "▥", "◈", "◐", "▤", "▨", "≈", "○", "▣" };
        for (var index = 0; index < Categories.Length; index++)
        {
            var (id, name) = Categories[index];
            var category = new ListBoxItem { Tag = id, Content = glyphs[index], ToolTip = name + " · " + Count(id),
                Width = 58, Height = 40, FontSize = 23, HorizontalContentAlignment = HorizontalAlignment.Center, Foreground = Foreground };
            System.Windows.Automation.AutomationProperties.SetName(category, name);
            _categories.Items.Add(category);
        }
        _categories.SelectedIndex = 0;
        DockPanel.SetDock(_categories, Dock.Top); library.Children.Add(_categories);
        var pager = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(_previous, Dock.Left); DockPanel.SetDock(_next, Dock.Right);
        pager.Children.Add(_previous); pager.Children.Add(_next); pager.Children.Add(_pageText);
        DockPanel.SetDock(pager, Dock.Bottom); library.Children.Add(pager);
        _previous.Click += (_, _) => { _page = Math.Max(0, _page - 1); Refresh(); };
        _next.Click += (_, _) => { _page++; Refresh(); };
        library.Children.Add(new ScrollViewer { Content = _tiles, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var settings = new ScrollViewer { Content = BuildControls(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed };
        pages.Children.Add(library); pages.Children.Add(settings); layout.Children.Add(pages);
        void Switch(bool editing)
        {
            library.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            settings.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            libraryTab.Background = new SolidColorBrush(editing ? Color.FromRgb(40, 42, 46) : Color.FromRgb(52, 95, 145));
            settingsTab.Background = new SolidColorBrush(editing ? Color.FromRgb(52, 95, 145) : Color.FromRgb(40, 42, 46));
        }
        libraryTab.Click += (_, _) => Switch(false); settingsTab.Click += (_, _) => Switch(true); Switch(false);
        Content = layout;

        _search.TextChanged += (_, _) => { _page = 0; Refresh(); };
        _categories.SelectionChanged += (_, _) => { _page = 0; Refresh(); };
        _debounce.Tick += (_, _) => { _debounce.Stop(); _host.Adjust(_gloss.Value, _metal.Value, _relief.Value, _size.Value, _tint); };
        Closed += (_, _) => { FlushAppearance(); _revision++; };
        Loaded += (_, _) => { SyncSliders(); Refresh(); };
        Activated += (_, _) => { SyncSliders(); Highlight(); };
    }

    private int Count(string id) => id == "all" ? _items.Count : _items.Count(item => item.Category == id);

    private void FlushAppearance()
    {
        if (!_debounce.IsEnabled) return;
        _debounce.Stop();
        _host.Adjust(_gloss.Value, _metal.Value, _relief.Value, _size.Value, _tint);
    }

    private FrameworkElement BuildControls()
    {
        var stack = new StackPanel { Margin = new Thickness(2, 8, 6, 8) };
        stack.Children.Add(new TextBlock { Text = "Color", Foreground = Foreground, Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(_swatches);
        stack.Children.Add(new TextBlock { Text = "Textura · selecciona una miniatura en Biblioteca", Foreground = Foreground,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 16) });
        foreach (var (label, control) in new[] { ("Escala de mapa (m)", _size), ("Relieve", _relief), ("Rugosidad", _gloss), ("Metálico", _metal) })
        {
            var value = new TextBlock { Foreground = Foreground, HorizontalAlignment = HorizontalAlignment.Right };
            var row = new DockPanel { Margin = new Thickness(0, 10, 0, 5) };
            DockPanel.SetDock(value, Dock.Right); row.Children.Add(value);
            row.Children.Add(new TextBlock { Text = label, Foreground = Foreground });
            stack.Children.Add(row); stack.Children.Add(control);
            System.Windows.Automation.AutomationProperties.SetName(control, label);
            control.ValueChanged += (_, _) =>
            {
                value.Text = control == _size ? $"{control.Value:0.0#} m" : $"{control.Value:0%}";
                if (_loading) return;
                _debounce.Stop(); _debounce.Start();
            };
        }
        var reset = new Button { Content = "Restablecer material", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 20, 0, 0) };
        reset.Click += (_, _) => { _host.Reset(); SyncSliders(); };
        stack.Children.Add(reset);
        return stack;
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
            _gloss.Value = roughness;
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
                                    MaterialCatalogSearch.Matches(query, item.Label, item.Category));
    }

    private void Refresh()
    {
        var revision = ++_revision;
        _tiles.Children.Clear();
        _tileOf.Clear();
        var matches = Visible().ToArray();
        var pages = Math.Max(1, (matches.Length + PageSize - 1) / PageSize);
        _page = Math.Clamp(_page, 0, pages - 1);
        _pageText.Text = matches.Length == 0 ? "Sin resultados" : $"{matches.Length} materiales · {_page + 1} / {pages}";
        _previous.IsEnabled = _page > 0; _next.IsEnabled = _page + 1 < pages;
        foreach (var item in matches.Skip(_page * PageSize).Take(PageSize))
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
                Child = stack, Margin = new Thickness(4), Padding = new Thickness(4, 6, 4, 6), CornerRadius = new CornerRadius(2), Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x1E, 0x2A)), BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x3A, 0x4A)), ToolTip = item.Label
            };
            tile.MouseLeftButtonUp += (_, _) => { _debounce.Stop(); _host.Apply(item); SyncSliders(); Highlight(); };
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
        try { if (revision != _revision) return; ball = await Task.Run(() => MakeBall(item)); }
        finally { _gate.Release(); }
        if (ball is null) return;
        await Dispatcher.InvokeAsync(() =>
        {
            if (revision != _revision || !IsLoaded) return;
            if (_balls.Count >= 64) _balls.Remove(_balls.Keys.First());
            _balls[item.Key] = ball;
            view.Source = ball;
        });
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
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            stream.Position = 0;
            if (frame.PixelWidth >= frame.PixelHeight) image.DecodePixelWidth = 192;
            else image.DecodePixelHeight = 192;
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
    internal static string CategoryOf(MaterialPreset preset) => preset.Id is "pool-water" or "dark-water" ? "water" : preset.Group switch
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
