using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using Microsoft.Win32;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>Edits paper composition and reusable .g3df title-block families without mutating the project before Apply.</summary>
public sealed class SheetEditorWindow : Window
{
    private readonly DocumentSession _session;
    private readonly Guid _sheetId;
    private readonly Action _refresh;
    private FamilyDocument _family;
    private readonly ObservableCollection<SheetViewportRow> _placements = [];
    private readonly ObservableCollection<SheetGraphicRow> _graphics = [];
    private readonly DataGrid _placementGrid = new();
    private readonly DataGrid _graphicGrid = new();
    private readonly TextBox _name = new(), _number = new(), _description = new(), _subtitle = new(), _date = new();
    private readonly TextBox _width = new(), _height = new(), _margin = new(), _header = new(), _footer = new();
    private readonly TextBox _familyName = new();
    private readonly ComboBox _views = new(), _paper = new();
    private readonly CheckBox _portrait = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };

    /// <summary>Loads the chosen sheet and family as isolated staging records.</summary>
    public SheetEditorWindow(DocumentSession session, Guid sheetId, Action refresh)
    {
        _session = session; _sheetId = sheetId; _refresh = refresh;
        var sheet = session.Document.Sheets.Single(sheet => sheet.Id == sheetId);
        _family = session.Document.SheetFamilies.Single(family => family.Id == sheet.TitleBlockFamilyId).Clone();
        Title = Text("SheetEditor.Title"); Width = 1240; Height = 800; MinWidth = 840; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.WhiteSmoke;
        Language = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
        _name.Text = sheet.Name; _number.Text = sheet.Number; _description.Text = sheet.Description;
        _subtitle.Text = sheet.Subtitle; _date.Text = sheet.DateText;
        foreach (var placement in sheet.Viewports) _placements.Add(SheetViewportRow.From(placement));
        BuildInterface(sheet);
        LoadFamilyRows();
    }

    /// <summary>Resolves UI labels through the application's localization dictionaries.</summary>
    private static string Text(string key) => (string)(Application.Current.TryFindResource(key) ?? key);

    /// <summary>Builds responsive composition and family tabs with scrollable editable grids.</summary>
    private void BuildInterface(DrawingSheet sheet)
    {
        var root = new DockPanel { Margin = new Thickness(12) };
        var bottom = new StackPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(Button("Parameters.Apply", Apply_Click));
        actions.Children.Add(Button("Parameters.Close", Close_Click));
        bottom.Children.Add(_status); bottom.Children.Add(actions); root.Children.Add(bottom);
        var fields = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        AddField(fields, "SheetEditor.Name", _name, 240); AddField(fields, "SheetEditor.Number", _number, 100);
        AddField(fields, "SheetEditor.Description", _description, 340); AddField(fields, "SheetEditor.Subtitle", _subtitle, 300);
        AddField(fields, "SheetEditor.Date", _date, 130);
        DockPanel.SetDock(fields, Dock.Top); root.Children.Add(fields);
        var tabs = new TabControl();
        var placementPanel = new DockPanel();
        var tools = new WrapPanel(); DockPanel.SetDock(tools, Dock.Top);
        _views.Width = 240; _views.Margin = new Thickness(4); _views.DisplayMemberPath = "Label"; _views.SelectedValuePath = "Value";
        var choices = _session.Document.Views.Where(view => view.Kind is ProjectViewKind.Plan or ProjectViewKind.Section or
            ProjectViewKind.Elevation or ProjectViewKind.Model3D ||
            BuiltInProjectViews.IsGeneratedSheetView(view)).Select(view => new LabeledValue<Guid>(view.Name, view.Id))
            // Saved schedules (doors, windows, rooms) can be placed on a sheet as tables.
            .Concat(_session.Document.Schedules.Select(schedule => new LabeledValue<Guid>(schedule.Name, schedule.Id))).ToArray();
        _views.ItemsSource = choices; _views.SelectedIndex = 0; tools.Children.Add(_views);
        tools.Children.Add(Button("SheetEditor.AddView", AddView_Click));
        tools.Children.Add(Button("SheetEditor.RemoveView", RemoveView_Click));
        tools.Children.Add(Button("SheetEditor.Rows", ArrangeRows_Click)); tools.Children.Add(Button("SheetEditor.Columns", ArrangeColumns_Click));
        placementPanel.Children.Add(tools); ConfigureGrid(_placementGrid, _placements);
        _placementGrid.Columns.Add(new DataGridComboBoxColumn { Header = Text("SheetEditor.View"), ItemsSource = choices,
            DisplayMemberPath = "Label", SelectedValuePath = "Value", SelectedValueBinding = new Binding("ViewId"), Width = 210 });
        AddColumns(_placementGrid, [("Number", "SheetEditor.Number"), ("X", "SheetEditor.X"), ("Y", "SheetEditor.Y"),
            ("Width", "SheetEditor.Width"), ("Height", "SheetEditor.Height"), ("Scale", "SheetEditor.Scale")]);
        placementPanel.Children.Add(_placementGrid);
        tabs.Items.Add(new TabItem { Header = Text("SheetEditor.Composition"), Content = placementPanel });
        tabs.Items.Add(new TabItem { Header = Text("SheetEditor.Family"), Content = BuildFamilyTab(sheet) });
        root.Children.Add(tabs); Content = root;
    }

    /// <summary>Exposes every family line, border, text field, and physical paper dimension.</summary>
    private DockPanel BuildFamilyTab(DrawingSheet sheet)
    {
        var panel = new DockPanel();
        var controls = new StackPanel(); DockPanel.SetDock(controls, Dock.Top);
        var tools = new WrapPanel();
        _paper.ItemsSource = Enum.GetValues<SheetPaperSize>(); _paper.SelectedItem = sheet.PaperSize; _paper.Width = 65;
        _portrait.Content = Text("SheetEditor.Portrait"); _portrait.IsChecked = true; _portrait.Margin = new Thickness(8);
        tools.Children.Add(_paper); tools.Children.Add(_portrait);
        tools.Children.Add(Button("SheetEditor.NewFamily", NewFamily_Click));
        tools.Children.Add(Button("SheetEditor.LoadFamily", LoadFamily_Click)); tools.Children.Add(Button("SheetEditor.SaveFamily", SaveFamily_Click));
        tools.Children.Add(Button("SheetEditor.AddText", AddText_Click)); tools.Children.Add(Button("SheetEditor.AddLine", AddLine_Click));
        tools.Children.Add(Button("SheetEditor.RemoveGraphic", RemoveGraphic_Click)); controls.Children.Add(tools);
        var dimensions = new WrapPanel();
        AddField(dimensions, "SheetEditor.FamilyName", _familyName, 240);
        AddField(dimensions, "SheetEditor.Width", _width, 90); AddField(dimensions, "SheetEditor.Height", _height, 90);
        AddField(dimensions, "SheetEditor.Margin", _margin, 90); AddField(dimensions, "SheetEditor.Header", _header, 90);
        AddField(dimensions, "SheetEditor.Footer", _footer, 90); controls.Children.Add(dimensions);
        controls.Children.Add(new TextBlock { Text = Text("SheetEditor.Tokens"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 6, 4, 10) });
        panel.Children.Add(controls); ConfigureGrid(_graphicGrid, _graphics);
        _graphicGrid.Columns.Add(new DataGridComboBoxColumn { Header = Text("SheetEditor.Kind"),
            ItemsSource = Enum.GetValues<SheetGraphicKind>().Select(kind => new LabeledValue<SheetGraphicKind>(Text("SheetEditor.Kind." + kind), kind)).ToArray(),
            DisplayMemberPath = "Label", SelectedValuePath = "Value", SelectedValueBinding = new Binding("Kind"), Width = 100 });
        AddColumns(_graphicGrid, [("StartX", "SheetEditor.X"), ("StartY", "SheetEditor.Y"), ("EndX", "SheetEditor.EndX"),
            ("EndY", "SheetEditor.EndY"), ("Text", "SheetEditor.Text"), ("FontSize", "SheetEditor.FontSize"), ("Weight", "SheetEditor.Weight")]);
        _graphicGrid.Columns.Add(new DataGridCheckBoxColumn { Header = Text("SheetEditor.Bold"), Binding = new Binding("Bold") });
        panel.Children.Add(_graphicGrid); return panel;
    }

    /// <summary>Creates localized toolbar actions without hardcoded visible labels.</summary>
    private static Button Button(string key, RoutedEventHandler action)
    {
        var button = new Button { Content = Text(key), Margin = new Thickness(4), Padding = new Thickness(10, 5, 10, 5) };
        button.Click += action; return button;
    }

    /// <summary>Adds a labeled paper-space field to a wrapping form.</summary>
    private static void AddField(Panel panel, string label, TextBox input, double width)
    {
        var field = new StackPanel { Margin = new Thickness(4), Width = width };
        field.Children.Add(new TextBlock { Text = Text(label) }); field.Children.Add(input); panel.Children.Add(field);
    }

    /// <summary>Uses explicit columns and independent staging rows so project state cannot change on a keystroke.</summary>
    private static void ConfigureGrid(DataGrid grid, System.Collections.IEnumerable rows)
    {
        grid.AutoGenerateColumns = false; grid.CanUserAddRows = false; grid.ItemsSource = rows;
        grid.HeadersVisibility = DataGridHeadersVisibility.Column; grid.Margin = new Thickness(4);
    }

    /// <summary>Builds editable columns with localized headings and domain property bindings.</summary>
    private static void AddColumns(DataGrid grid, (string Property, string Label)[] columns)
    {
        foreach (var column in columns) grid.Columns.Add(new DataGridTextColumn { Header = Text(column.Label),
            Binding = new Binding(column.Property) { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus },
            Width = column.Property == "Text" ? 260 : 100 });
    }

    /// <summary>Refreshes staging graphics and paper dimensions after importing or creating a family.</summary>
    private void LoadFamilyRows()
    {
        var layout = _family.SheetLayout!; _graphics.Clear();
        _familyName.Text = _family.Name;
        _portrait.IsChecked = layout.Height >= layout.Width;
        _paper.SelectedItem = SheetFamilies.PaperSizeOf(layout) ?? _paper.SelectedItem ?? SheetPaperSize.A3;
        foreach (var graphic in layout.Graphics) _graphics.Add(SheetGraphicRow.From(graphic));
        _width.Text = layout.Width.ToString(CultureInfo.CurrentCulture); _height.Text = layout.Height.ToString(CultureInfo.CurrentCulture);
        _margin.Text = layout.Margin.ToString(CultureInfo.CurrentCulture); _header.Text = layout.HeaderHeight.ToString(CultureInfo.CurrentCulture);
        _footer.Text = layout.FooterHeight.ToString(CultureInfo.CurrentCulture);
    }

    /// <summary>Parses a physical paper dimension while accepting comma or period decimals.</summary>
    private static double Read(TextBox input) => double.TryParse(input.Text.Replace(',', '.'), NumberStyles.Float,
        CultureInfo.InvariantCulture, out var value) ? value : throw new InvalidDataException("Invalid sheet family layout.");

    /// <summary>Builds and validates a family snapshot without mutating the project catalog.</summary>
    private FamilyDocument ReadFamily()
    {
        CommitGrid(_graphicGrid);
        var family = _family.Clone(); family.Name = _familyName.Text.Trim();
        family.SheetLayout = new(Read(_width), Read(_height), Read(_margin), Read(_header), Read(_footer),
            _graphics.Select(row => row.ToModel()).ToList()); family.Validate(); return family;
    }

    /// <summary>Commits composition and shared-family changes as one undoable operation.</summary>
    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CommitGrid(_placementGrid);
            var family = ReadFamily(); var current = _session.Document.Sheets.Single(sheet => sheet.Id == _sheetId);
            _session.UpdateSheet(current with { Name = _name.Text.Trim(), Number = _number.Text.Trim(),
                Description = _description.Text, Subtitle = _subtitle.Text, DateText = _date.Text,
                PaperSize = SheetFamilies.PaperSizeOf(family.SheetLayout!) ?? current.PaperSize, TitleBlockFamilyId = family.Id,
                Viewports = _placements.Select(row => row.ToModel()).ToList() }, family);
            _family = family; _refresh(); _status.Text = Text("SheetEditor.Applied");
        }
        catch (InvalidDataException exception) { _status.Text = LocalizedErrors.Translate(exception.Message); }
    }

    /// <summary>Adds a chosen live view and distributes the current placements into readable rows.</summary>
    private void AddView_Click(object sender, RoutedEventArgs e)
    {
        if (_views.SelectedValue is not Guid id) return;
        _placements.Add(SheetViewportRow.From(new(Guid.NewGuid(), id, 0, 0, 30, 30, Number: (_placements.Count + 1).ToString())));
        Arrange(false);
    }

    /// <summary>Removes only the selected placed view, retaining its model and project view.</summary>
    private void RemoveView_Click(object sender, RoutedEventArgs e) { if (_placementGrid.SelectedItem is SheetViewportRow row) _placements.Remove(row); }

    /// <summary>Distributes staged views vertically within the family's reserved drawing area.</summary>
    private void ArrangeRows_Click(object sender, RoutedEventArgs e) => Arrange(false);

    /// <summary>Distributes staged views side by side for elevations or construction details.</summary>
    private void ArrangeColumns_Click(object sender, RoutedEventArgs e) => Arrange(true);

    /// <summary>Preserves each chosen scale while arranging view rectangles inside the paper margins.</summary>
    private void Arrange(bool columns)
    {
        if (_placements.Count == 0) return;
        var layout = _family.SheetLayout!; var inset = layout.Margin + 8;
        var width = layout.Width - 2 * inset; var height = layout.Height - 2 * inset - layout.HeaderHeight - layout.FooterHeight;
        for (var index = 0; index < _placements.Count; index++)
        {
            var row = _placements[index]; row.X = inset + (columns ? index * width / _placements.Count : 0);
            row.Y = inset + layout.HeaderHeight + (columns ? 0 : index * height / _placements.Count);
            row.Width = columns ? width / _placements.Count - 4 : width; row.Height = columns ? height : height / _placements.Count - 4;
        }
        _placementGrid.Items.Refresh();
    }

    /// <summary>Creates a separate native family for the selected paper/orientation, retaining the current family in the project.</summary>
    private void NewFamily_Click(object sender, RoutedEventArgs e)
    {
        _family = SheetFamilies.Create((SheetPaperSize)_paper.SelectedItem, _portrait.IsChecked == true); LoadFamilyRows(); Arrange(false);
    }

    /// <summary>Loads reusable .g3df family geometry and rejects other family categories with a localized error.</summary>
    private void LoadFamily_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = Text("SheetEditor.FamilyFilter") };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var family = NativeDocumentStore.LoadFamily(dialog.FileName);
            if (family.Category != FamilyCategory.TitleBlocks || family.SheetLayout is null)
                throw new InvalidDataException("The family is not a drawing sheet family.");
            _family = family; LoadFamilyRows(); Arrange(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException) { _status.Text = LocalizedErrors.Translate(exception.Message); }
    }

    /// <summary>Saves the editable paper family independently from its project and model views.</summary>
    private void SaveFamily_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = Text("SheetEditor.FamilyFilter"), DefaultExt = ".g3df", AddExtension = true, FileName = _family.Name };
        if (dialog.ShowDialog(this) != true) return;
        try { NativeDocumentStore.SaveFamily(dialog.FileName, ReadFamily()); _status.Text = Text("SheetEditor.FamilySaved"); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException) { _status.Text = LocalizedErrors.Translate(exception.Message); }
    }

    /// <summary>Adds a text field that can contain literal user text or supported sheet-parameter tokens.</summary>
    private void AddText_Click(object sender, RoutedEventArgs e) => _graphics.Add(SheetGraphicRow.From(
        new(Guid.NewGuid(), SheetGraphicKind.Text, new(25, 25), new(25, 25), "{SheetName}")));

    /// <summary>Adds a paper-space line to the editable family.</summary>
    private void AddLine_Click(object sender, RoutedEventArgs e) => _graphics.Add(SheetGraphicRow.From(
        new(Guid.NewGuid(), SheetGraphicKind.Line, new(25, 25), new(75, 25))));

    /// <summary>Removes only the selected family graphic.</summary>
    private void RemoveGraphic_Click(object sender, RoutedEventArgs e) { if (_graphicGrid.SelectedItem is SheetGraphicRow row) _graphics.Remove(row); }

    /// <summary>Closes the editor; only explicitly applied changes remain in the project history.</summary>
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Stops Apply when WPF rejects a typed cell value instead of silently committing the previous value.</summary>
    private static void CommitGrid(DataGrid grid)
    {
        if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true))
            throw new InvalidDataException("Invalid drawing sheet layout.");
    }
}
