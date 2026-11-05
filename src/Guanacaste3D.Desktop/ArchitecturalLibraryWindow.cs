using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>
/// The architectural type browser: every wall assembly, door and window family, floor, ceiling and roof recipe,
/// column section, framing member, guard, stair, ramp, gutter, downspout and curtain wall that Guanacaste 3D
/// offers. A type can be loaded into the project or placed directly, which arms the matching tool.
/// </summary>
internal sealed class ArchitecturalLibraryWindow : Window
{
    private sealed record Entry(string Category, string Name, string Detail, bool InProject, Action Place);

    private readonly MainWindow _main;
    private readonly DocumentSession _session;
    private readonly ListView _list = new();
    private readonly ComboBox _categories = new();
    private readonly TextBox _search = new() { MinWidth = 180 };
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly List<(string Key, string Label)> _categoryKeys = [];

    public ArchitecturalLibraryWindow(MainWindow main, DocumentSession session)
    {
        _main = main;
        _session = session;
        Title = Text("Library.Title");
        Width = 820;
        Height = 560;
        MinWidth = 620;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        FontSize = 12;
        SetResourceReference(BackgroundProperty, "AppBackgroundBrush");
        _status.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");

        var grid = new GridView();
        grid.Columns.Add(new GridViewColumn { Header = Text("Library.Name"), Width = 300, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Entry.Name)) });
        grid.Columns.Add(new GridViewColumn { Header = Text("Library.Detail"), Width = 330, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Entry.Detail)) });
        grid.Columns.Add(new GridViewColumn { Header = Text("Library.State"), Width = 110, DisplayMemberBinding = new System.Windows.Data.Binding("State") });
        _list.View = grid;
        _list.MouseDoubleClick += (_, _) => PlaceSelected();

        var place = new Button { Content = Text("Library.Place"), MinWidth = 100, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var load = new Button { Content = Text("Library.LoadAll"), MinWidth = 150, Margin = new Thickness(0, 0, 8, 0) };
        var close = new Button { Content = Text("Parameters.Close"), MinWidth = 90, IsCancel = true };
        place.Click += (_, _) => PlaceSelected();
        load.Click += (_, _) => LoadAll();
        close.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(load);
        buttons.Children.Add(place);
        buttons.Children.Add(close);
        var bottom = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        bottom.Children.Add(_status);

        var filters = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 10, 10, 0) };
        _categories.MinWidth = 220;
        _categories.Margin = new Thickness(0, 0, 10, 0);
        filters.Children.Add(_categories);
        filters.Children.Add(new TextBlock { Text = Text("Library.Search"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        filters.Children.Add(_search);

        var layout = new DockPanel();
        DockPanel.SetDock(filters, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        layout.Children.Add(filters);
        layout.Children.Add(bottom);
        _list.Margin = new Thickness(10, 8, 10, 0);
        layout.Children.Add(_list);
        Content = layout;

        _categories.SelectionChanged += (_, _) => Refresh();
        _search.TextChanged += (_, _) => Refresh();
        _session.Changed += Session_Changed;
        Closed += (_, _) => _session.Changed -= Session_Changed;
        BuildCategories();
        Refresh();
    }

    private static string Text(string key) => Application.Current.TryFindResource(key) as string ?? key;

    private void Session_Changed(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(Refresh, System.Windows.Threading.DispatcherPriority.Background);

    private static string Mm(double meters) => (meters * 1000).ToString("0", CultureInfo.CurrentCulture);

    private void BuildCategories()
    {
        foreach (var key in new[] { "All", "Walls", "Doors", "Windows", "Floors", "Ceilings", "Roofs", "Columns", "Framing",
                     "Guards", "Stairs", "Ramps", "Drainage", "CurtainWalls", "Seating", "Tables", "Bedroom",
                     "Storage", "Kitchen", "Bathroom", "Appliances", "Lighting", "Decor", "Outdoor" })
            _categoryKeys.Add((key, Text($"Library.Category.{key}")));
        foreach (var (_, label) in _categoryKeys) _categories.Items.Add(label);
        _categories.SelectedIndex = 0;
    }

    /// <summary>Builds every entry of the library, marking the ones the project already holds.</summary>
    private IEnumerable<Entry> Entries()
    {
        var walls = _session.Document.WallTypes.Select(type => type.Id).ToHashSet();
        var openings = _session.Document.OpeningTypes.Select(type => type.Id).ToHashSet();
        foreach (var wall in _session.Document.WallTypes.Concat(ArchitecturalLibrary.WallTypes()
                     .Where(type => !walls.Contains(type.Id))))
            yield return new Entry("Walls", wall.Name,
                $"{Mm(wall.Thickness)} mm · {Text($"WallFunction.{wall.Function}")} · {wall.MaterialName}",
                walls.Contains(wall.Id), () => PlaceWall(wall));
        foreach (var type in _session.Document.OpeningTypes.Concat(ArchitecturalLibrary.OpeningTypes()
                     .Where(candidate => !openings.Contains(candidate.Id))))
            yield return new Entry(type.Kind == OpeningKind.Door ? "Doors" : "Windows", type.DisplayName,
                $"{Mm(type.Width)} × {Mm(type.Height)} mm · {Text($"Operation.{type.Operation}")}" +
                (type.Kind == OpeningKind.Window ? $" · {Text("Library.Sill")} {Mm(type.DefaultSill)} mm" : string.Empty),
                openings.Contains(type.Id), () => PlaceOpening(type));
        foreach (var slab in ArchitecturalLibrary.SlabPresets)
            yield return new Entry(slab.Kind switch { FloorKind.Floor => "Floors", FloorKind.Ceiling => "Ceilings", FloorKind.Toposolid or FloorKind.Water => "Site", _ => "Roofs" },
                slab.Name, $"{Mm(slab.Thickness)} mm" +
                (slab.Shape == RoofShape.Flat ? string.Empty : $" · {Text($"RoofShape.{slab.Shape}")} {slab.Pitch:0}°"),
                true, () => { _main.PlaceLibrarySlab(slab); Close(); });
        yield return new Entry("Roofs", Text("Entity.Skylight"), Text("Library.SkylightDetail"), true,
            () => { _main.BeginKindToolFromLibrary(StructuralElementKind.Skylight); Close(); });
        foreach (var column in ArchitecturalLibrary.ColumnPresets)
            yield return new Entry("Columns", column.Name,
                $"{Mm(column.Width)} × {Mm(column.Depth)} mm · {Text($"ColumnShape.{column.Shape}")}", true,
                () => { _main.PlaceLibraryColumn(column); Close(); });
        foreach (var preset in ArchitecturalLibrary.ElementPresets)
        {
            var category = preset.ComponentCategory != ResidentialComponentCategory.None
                ? preset.ComponentCategory.ToString()
                : preset.Kind switch
            {
                StructuralElementKind.Railing => "Guards",
                StructuralElementKind.Stair => "Stairs",
                StructuralElementKind.Ramp => "Ramps",
                StructuralElementKind.Gutter or StructuralElementKind.Downspout => "Drainage",
                StructuralElementKind.CurtainWall => "CurtainWalls",
                StructuralElementKind.Planting => "Vegetation",
                _ => "Framing"
            };
            var displayName = PresetName(preset);
            var dimensions = preset.Kind == StructuralElementKind.Component && preset.Depth > 0
                ? $"{Mm(preset.Width)} × {Mm(preset.Depth)} × {Mm(preset.Height)} mm"
                : $"{Mm(preset.Width)} × {Mm(preset.Height)} mm";
            yield return new Entry(category, displayName,
                dimensions +
                (preset.Style == ElementStyle.Default ? string.Empty : $" · {Text($"ElementStyle.{preset.Style}")}"),
                true, () => { _main.BeginPresetTool(preset with { Name = displayName }); Close(); });
        }
    }

    /// <summary>Resolves a component family's localized display name while retaining canonical names in the core catalog.</summary>
    private static string PresetName(ElementPreset preset) =>
        string.IsNullOrWhiteSpace(preset.LocalizationKey) ? preset.Name : Text(preset.LocalizationKey);

    private void Refresh()
    {
        var selectedCategory = _categoryKeys.ElementAtOrDefault(Math.Max(0, _categories.SelectedIndex)).Key ?? "All";
        var query = _search.Text.Trim();
        var rows = Entries()
            .Where(entry => selectedCategory == "All" || entry.Category == selectedCategory)
            .Where(entry => query.Length == 0 ||
                entry.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                entry.Detail.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .Select(entry => new Row(entry, Text(entry.InProject ? "Library.InProject" : "Library.NotLoaded")))
            .ToList();
        _list.ItemsSource = rows;
        _status.Text = string.Format(CultureInfo.CurrentCulture, Text("Library.Count"), rows.Count);
    }

    /// <summary>An entry with its display state, so the list can bind to simple properties.</summary>
    private sealed class Row(Entry entry, string state)
    {
        public Entry Source { get; } = entry;
        public string Name => Source.Name;
        public string Detail => Source.Detail;
        public string State { get; } = state;
    }

    private void PlaceSelected()
    {
        if (_list.SelectedItem is not Row row)
        {
            _status.Text = Text("Library.SelectFirst");
            return;
        }
        row.Source.Place();
    }

    private void LoadAll()
    {
        var added = _session.LoadArchitecturalLibrary();
        _status.Text = string.Format(CultureInfo.CurrentCulture, Text(added == 0 ? "Library.NothingToLoad" : "Library.Loaded"), added);
        Refresh();
    }

    /// <summary>Loads a type that is only in the library, then runs the placement.</summary>
    private void PlaceWall(WallType type)
    {
        if (_session.Document.WallTypes.All(existing => existing.Id != type.Id)) _session.LoadArchitecturalLibrary();
        _main.PlaceLibraryWall(type);
        Close();
    }

    private void PlaceOpening(OpeningType type)
    {
        if (_session.Document.OpeningTypes.All(existing => existing.Id != type.Id)) _session.LoadArchitecturalLibrary();
        _main.PlaceLibraryOpening(type);
        Close();
    }
}
