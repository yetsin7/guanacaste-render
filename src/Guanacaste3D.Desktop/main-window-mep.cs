using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private Window? _schematicWindow;

    /// <summary>Routes the Systems tab's ducts, trays, conduits, devices, stiffeners, routes, fixtures and the schematic.</summary>
    private bool TryExecuteMepCommand(string commandId)
    {
        switch (commandId)
        {
            case "Duct": BeginKindTool(StructuralElementKind.Duct); return true;
            case "DuctPlaceholder": BeginKindTool(StructuralElementKind.DuctPlaceholder); return true;
            case "FlexDuct": BeginKindTool(StructuralElementKind.FlexDuct); return true;
            case "CableTray": BeginKindTool(StructuralElementKind.CableTray); return true;
            case "Conduit": BeginKindTool(StructuralElementKind.Conduit); return true;
            case "FlexPipe": BeginKindTool(StructuralElementKind.FlexPipe); return true;
            case "FabricationPart": BeginKindTool(StructuralElementKind.FabricationPart); return true;
            case "AirTerminal": BeginKindTool(StructuralElementKind.AirTerminal); return true;
            case "MechanicalControlDevice": BeginKindTool(StructuralElementKind.MechanicalControlDevice); return true;
            case "DuctStiffener": AddStiffenersToSelectedDuct(); return true;
            case "MultipointRouting": AskForRoute(); return true;
            case "PlumbingFixture": BeginPlumbingFixture(); return true;
            case "PidModeler": ShowSchematicWindow(); return true;
            default: return false;
        }
    }

    private void AddStiffenersToSelectedDuct()
    {
        var duct = _selectedStructuralId is { } id ? _session.Document.StructuralElements.FirstOrDefault(item => item.Id == id && item.Kind == StructuralElementKind.Duct) : null;
        if (duct is null)
        {
            StatusText.Text = GetText("Status.DuctNotSelected");
            return;
        }
        var text = TextPrompt.Ask(this, GetText("Stiffener.Title"), GetText("Prompt.Stiffener"), GetText("Prompt.Accept"), GetText("Prompt.Cancel"), Units.LengthNumber(1.2));
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!Units.TryParseLength(text, out var spacing))
        {
            StatusText.Text = GetText("Dimension.Invalid");
            return;
        }
        try
        {
            var made = _session.AddDuctStiffeners(duct.Id, spacing);
            StatusText.Text = FormatText("Status.StiffenersAdded", made);
            RefreshAll();
        }
        catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
    }

    /// <summary>Reads "x y; x y; x y" (in the project's unit) and draws a run of fabrication parts through the points.</summary>
    private bool CreateRouteFromText(string text)
    {
        var points = new List<Point2>();
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split((char)32, StringSplitOptions.RemoveEmptyEntries);
            if (pair.Length != 2 || !Units.TryParseLength(pair[0], out var x) || !Units.TryParseLength(pair[1], out var y))
            {
                StatusText.Text = GetText("Status.RouteInvalid");
                return false;
            }
            points.Add(new Point2(x, y));
        }
        try
        {
            var made = _session.AddRoute(_activeLevelId, StructuralElementKind.FabricationPart, points, GetText("Entity.Route"));
            StatusText.Text = FormatText("Status.RouteCreated", made.Count);
            RefreshAll();
            return true;
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
            return false;
        }
    }

    private void AskForRoute()
    {
        var text = TextPrompt.Ask(this, GetText("Route.Title"), GetText("Prompt.Route"), GetText("Prompt.Accept"), GetText("Prompt.Cancel"));
        if (!string.IsNullOrWhiteSpace(text)) CreateRouteFromText(text);
    }

    /// <summary>Asks which bathroom fixture to place and arms the component tool with it.</summary>
    private void BeginPlumbingFixture()
    {
        var fixtures = ArchitecturalLibrary.PresetsFor(StructuralElementKind.Component)
            .Where(preset => preset.ComponentCategory == ResidentialComponentCategory.Bathroom).ToList();
        if (fixtures.Count == 0) { StatusText.Text = GetText("Status.NoFixtures"); return; }
        string Name(ElementPreset preset) => preset.LocalizationKey is { } key ? GetText(key) : preset.Name;
        var text = TextPrompt.Ask(this, GetText("PlumbingFixture.Title"), FormatText("Prompt.PlumbingFixture", string.Join(", ", fixtures.Select(Name))),
            GetText("Prompt.Accept"), GetText("Prompt.Cancel"), Name(fixtures[0]));
        if (string.IsNullOrWhiteSpace(text)) return;
        var chosen = fixtures.FirstOrDefault(preset => Name(preset).Equals(text.Trim(), StringComparison.CurrentCultureIgnoreCase)) ??
                     fixtures.FirstOrDefault(preset => Name(preset).StartsWith(text.Trim(), StringComparison.CurrentCultureIgnoreCase));
        if (chosen is null) { StatusText.Text = GetText("Status.FixtureNotFound"); return; }
        BeginPresetTool(chosen);
    }

    // ------------------------------------------------------------------ the schematic

    private void ShowSchematicWindow()
    {
        _schematicWindow?.Close();
        var schematic = MepSchematic.Build(_session.Document);
        var window = new Window
        {
            Title = GetText("Schematic.Title"), Owner = this, Width = 760, Height = 560, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        window.Closed += (_, _) => _schematicWindow = null;
        _schematicWindow = window;
        var canvas = new Canvas { Name = "SchematicCanvas", Background = Brushes.White };
        DrawSchematic(canvas, schematic);
        var summary = new TextBlock
        {
            Name = "SchematicSummary", Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap,
            Text = schematic.Nodes.Count == 0 ? GetText("Schematic.Empty") : FormatText("Schematic.Summary", schematic.Nodes.Count, schematic.Edges.Count)
        };
        var buttons = new WrapPanel();
        buttons.Children.Add(ManageButton("SchematicSave", GetText("Schematic.Save"), (_, _) =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PNG (*.png)|*.png", FileName = _session.Document.Name + "-esquema.png" };
            if (dialog.ShowDialog(this) == true) SaveSchematicImage(canvas, dialog.FileName);
        }));
        buttons.Children.Add(ManageButton("SchematicClose", GetText("Phases.Close"), (_, _) => window.Close()));
        var scroll = new ScrollViewer { Content = canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var layout = new DockPanel { Margin = new Thickness(10) };
        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = GetText("Schematic.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        top.Children.Add(summary);
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(top);
        layout.Children.Add(buttons);
        layout.Children.Add(scroll);
        window.Content = layout;
        window.Show();
    }

    /// <summary>Lays the nodes out in three columns, grouped by service, and joins them with lines.</summary>
    private static void DrawSchematic(Canvas canvas, MepSchematic schematic)
    {
        const double boxWidth = 200, boxHeight = 34, columnGap = 60, rowGap = 12;
        var positions = new Dictionary<Guid, Point>();
        var y = new double[3];
        foreach (var group in schematic.Nodes.GroupBy(node => node.Group))
        {
            var top = y.Max() + 14;
            for (var column = 0; column < 3; column++) y[column] = top;
            foreach (var node in group)
            {
                var column = Math.Clamp(node.Column, 0, 2);
                positions[node.Id] = new Point(16 + column * (boxWidth + columnGap), y[column]);
                y[column] += boxHeight + rowGap;
            }
        }
        foreach (var edge in schematic.Edges)
            if (positions.TryGetValue(edge.From, out var from) && positions.TryGetValue(edge.To, out var to))
                canvas.Children.Add(new Line { X1 = from.X + boxWidth, Y1 = from.Y + boxHeight / 2, X2 = to.X, Y2 = to.Y + boxHeight / 2, Stroke = Brushes.SlateGray, StrokeThickness = 1.4 });
        foreach (var node in schematic.Nodes)
        {
            var at = positions[node.Id];
            var fill = node.Group switch { "Plumbing" => Color.FromRgb(214, 234, 248), "Electrical" => Color.FromRgb(253, 235, 208), _ => Color.FromRgb(223, 240, 216) };
            var box = new Border
            {
                Width = boxWidth, Height = boxHeight, Background = new SolidColorBrush(fill), BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4), Child = new TextBlock { Text = node.Label, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11 }
            };
            Canvas.SetLeft(box, at.X);
            Canvas.SetTop(box, at.Y);
            canvas.Children.Add(box);
        }
        canvas.Width = 16 * 2 + 3 * boxWidth + 2 * columnGap;
        canvas.Height = Math.Max(120, y.Max() + 20);
    }

    private bool SaveSchematicImage(Canvas canvas, string path)
    {
        try
        {
            canvas.Measure(new Size(canvas.Width, canvas.Height));
            canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height));
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(canvas.Width), (int)Math.Ceiling(canvas.Height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(canvas);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
            StatusText.Text = FormatText("Status.SchematicSaved", System.IO.Path.GetFileName(path));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return Fail(exception); }
    }
}
