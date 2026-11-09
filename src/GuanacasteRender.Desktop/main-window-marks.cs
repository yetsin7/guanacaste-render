using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private string? _pendingSymbolKey;
    private string? _pendingKeynote;
    private Window? _spellingWindow;

    /// <summary>Routes the spot marks, keynote, symbol, detail component, colour legend and spelling buttons of the Annotate tab.</summary>
    private bool TryExecuteMarkCommand(string commandId)
    {
        switch (commandId)
        {
            case "ElevationDimension": BeginKindTool(StructuralElementKind.SpotElevation); return true;
            case "SpotCoordinate": BeginKindTool(StructuralElementKind.SpotCoordinate); return true;
            case "SpotSlope": BeginKindTool(StructuralElementKind.SpotSlope); return true;
            case "ColorFillLegend": BeginKindTool(StructuralElementKind.ColorLegend); return true;
            case "Keynote": BeginKeynote(); return true;
            case "Symbol": BeginSymbol(PlanSymbols.Symbols, "Symbol"); return true;
            case "DetailComponent": BeginSymbol(PlanSymbols.DetailComponents, "DetailComponent"); return true;
            case "Spelling": ShowSpellingWindow(); return true;
            default: return false;
        }
    }

    private void BeginKeynote()
    {
        var table = string.Join(" · ", Keynotes.Table.Select(entry => $"{entry.Code} {entry.Text}"));
        var text = TextPrompt.Ask(this, GetText("Keynote.Title"), FormatText("Prompt.Keynote", table), GetText("Prompt.Accept"), GetText("Prompt.Cancel"));
        if (string.IsNullOrWhiteSpace(text)) return;
        _pendingKeynote = Keynotes.Resolve(text);
        BeginStructuralTool(StructuralElementKind.Keynote, StructuralPlacementMode.Point);
        StatusText.Text = FormatText("Status.KeynoteArmed", _pendingKeynote);
    }

    private void PlaceKeynote(Point2 point)
    {
        if (_pendingKeynote is not { } text) return;
        try
        {
            _selectedStructuralId = _session.AddKeynote(_activeLevelId, point, text);
            StatusText.Text = GetText("Status.KeynotePlaced");
            RefreshAll();
        }
        catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
    }

    private void BeginSymbol(IReadOnlyList<string> keys, string titleKey)
    {
        var names = string.Join(", ", keys.Select(key => $"{key} ({GetText("PlanSymbol." + key)})"));
        var text = TextPrompt.Ask(this, GetText(titleKey == "Symbol" ? "Symbol.Title" : "DetailComponent.Title"), FormatText("Prompt.Symbol", names),
            GetText("Prompt.Accept"), GetText("Prompt.Cancel"), keys[0]);
        if (string.IsNullOrWhiteSpace(text)) return;
        var key = keys.FirstOrDefault(item => item.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase)) ??
                  keys.FirstOrDefault(item => GetText("PlanSymbol." + item).Equals(text.Trim(), StringComparison.CurrentCultureIgnoreCase));
        if (key is null)
        {
            StatusText.Text = GetText("Status.SymbolNotFound");
            return;
        }
        _pendingSymbolKey = key;
        BeginStructuralTool(StructuralElementKind.Symbol, StructuralPlacementMode.Point);
        StatusText.Text = FormatText("Status.SymbolArmed", GetText("PlanSymbol." + key));
    }

    private void PlaceSymbol(Point2 point)
    {
        if (_pendingSymbolKey is not { } key) return;
        try
        {
            _selectedStructuralId = _session.AddSymbol(_activeLevelId, point, key);
            StatusText.Text = GetText("Status.SymbolPlaced");
            RefreshAll();
        }
        catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
    }

    /// <summary>A slope between two clicks: the rise comes from the terrain between them when there is one, and can be changed.</summary>
    private void CreateSpotSlope(Point2 start, Point2 end)
    {
        var suggested = SpotMarks.TerrainRise(_session.Document, _activeLevelId, start, end) ?? 0;
        var text = TextPrompt.Ask(this, GetText("SpotSlope.Title"), GetText("Prompt.SpotSlope"), GetText("Prompt.Accept"), GetText("Prompt.Cancel"),
            Units.LengthNumber(suggested));
        if (text is null) { _pendingStructuralStart = null; _structuralPreviewPoint = null; RenderPlan(); return; }
        if (!Units.TryParseLength(text, out var rise))
        {
            StatusText.Text = GetText("Dimension.Invalid");
            return;
        }
        try
        {
            _selectedStructuralId = _session.AddSpotSlope(_activeLevelId, start, end, rise);
            _pendingStructuralStart = null;
            _structuralPreviewPoint = null;
            StatusText.Text = GetText("Status.SpotSlopePlaced");
            RefreshAll();
        }
        catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
    }

    // ------------------------------------------------------------------ drawing

    private static readonly Brush MarkBrush = Brushes.Sienna;

    /// <summary>Draws spot marks, keynotes, symbols and the colour legend in the plan.</summary>
    private void AddAnnotationMark(StructuralElement element, bool selected)
    {
        var brush = selected ? Brushes.DarkOrange : MarkBrush;
        var at = WorldToCanvas(element.Start);
        TextBlock Text(string value, double x, double y, double size = 11, FontWeight? weight = null)
        {
            var block = new TextBlock { Text = value, FontSize = size, Foreground = brush, FontWeight = weight ?? FontWeights.Normal, Tag = element.Id };
            Canvas.SetLeft(block, x);
            Canvas.SetTop(block, y);
            PlanCanvas.Children.Add(block);
            return block;
        }
        switch (element.Kind)
        {
            case StructuralElementKind.SpotElevation:
            case StructuralElementKind.SpotCoordinate:
                PlanCanvas.Children.Add(new Line { X1 = at.X - 6, Y1 = at.Y, X2 = at.X + 6, Y2 = at.Y, Stroke = brush, StrokeThickness = 1.5, IsHitTestVisible = false });
                PlanCanvas.Children.Add(new Line { X1 = at.X, Y1 = at.Y - 6, X2 = at.X, Y2 = at.Y + 6, Stroke = brush, StrokeThickness = 1.5, IsHitTestVisible = false });
                var dot = new Ellipse { Width = 12, Height = 12, Stroke = brush, StrokeThickness = 1.2, Fill = Brushes.Transparent, Tag = element.Id };
                Canvas.SetLeft(dot, at.X - 6);
                Canvas.SetTop(dot, at.Y - 6);
                PlanCanvas.Children.Add(dot);
                Text(SpotMarks.Label(_session.Document, element), at.X + 10, at.Y - 18, 11, FontWeights.SemiBold);
                break;
            case StructuralElementKind.SpotSlope:
                var end = WorldToCanvas(element.End);
                PlanCanvas.Children.Add(new Line { X1 = at.X, Y1 = at.Y, X2 = end.X, Y2 = end.Y, Stroke = brush, StrokeThickness = selected ? 2.5 : 1.5, Tag = element.Id });
                var along = end - at;
                if (along.Length > 1)
                {
                    along.Normalize();
                    var side = new Vector(-along.Y, along.X);
                    foreach (var sign in new[] { -1, 1 })
                    {
                        var head = end - along * 10 + side * (5 * sign);
                        PlanCanvas.Children.Add(new Line { X1 = end.X, Y1 = end.Y, X2 = head.X, Y2 = head.Y, Stroke = brush, StrokeThickness = 1.5, IsHitTestVisible = false });
                    }
                }
                Text(SpotMarks.Label(_session.Document, element), (at.X + end.X) / 2 + 6, (at.Y + end.Y) / 2 - 18, 11, FontWeights.SemiBold);
                break;
            case StructuralElementKind.Keynote:
                var flag = new Polygon
                {
                    Points = new PointCollection { new(at.X, at.Y), new(at.X + 14, at.Y - 10), new(at.X + 16, at.Y - 10) },
                    Fill = brush, Tag = element.Id
                };
                PlanCanvas.Children.Add(flag);
                Text(element.Name, at.X + 18, at.Y - 18, 11);
                break;
            case StructuralElementKind.Symbol:
                AddSymbolGlyph(element, at, brush);
                break;
            case StructuralElementKind.ColorLegend:
                var rows = RoomColors.Legend(_session.Document, element.LevelId);
                Text(GetText("ColorLegend.Title"), at.X, at.Y, 12, FontWeights.Bold);
                for (var index = 0; index < rows.Count; index++)
                {
                    var (r, g, b) = RoomColors.Of(rows[index].Name);
                    var swatch = new Rectangle { Width = 14, Height = 14, Fill = new SolidColorBrush(Color.FromRgb(r, g, b)), Stroke = Brushes.DimGray, Tag = element.Id };
                    Canvas.SetLeft(swatch, at.X);
                    Canvas.SetTop(swatch, at.Y + 20 + index * 20);
                    PlanCanvas.Children.Add(swatch);
                    Text($"{rows[index].Name} · {FormatText("ColorLegend.Row", rows[index].Rooms, Units.FormatArea(rows[index].Area))}", at.X + 20, at.Y + 19 + index * 20);
                }
                if (rows.Count == 0) Text(GetText("ColorLegend.Empty"), at.X, at.Y + 20);
                break;
        }
    }

    /// <summary>The drawing of a symbol or detail component of the library, <c>element.Width</c> meters across.</summary>
    private void AddSymbolGlyph(StructuralElement element, Point centre, Brush brush)
    {
        var size = Math.Max(14, element.Width * (WorldToCanvas(new Point2(1, 0)) - WorldToCanvas(new Point2(0, 0))).Length);
        var r = size / 2;
        void Segment(double x1, double y1, double x2, double y2, double thickness = 1.6) =>
            PlanCanvas.Children.Add(new Line { X1 = centre.X + x1, Y1 = centre.Y + y1, X2 = centre.X + x2, Y2 = centre.Y + y2, Stroke = brush, StrokeThickness = thickness, IsHitTestVisible = false });
        Ellipse Circle(double radius, Brush? fill = null)
        {
            var circle = new Ellipse { Width = radius * 2, Height = radius * 2, Stroke = brush, StrokeThickness = 1.6, Fill = fill ?? Brushes.Transparent, Tag = element.Id };
            Canvas.SetLeft(circle, centre.X - radius);
            Canvas.SetTop(circle, centre.Y - radius);
            PlanCanvas.Children.Add(circle);
            return circle;
        }
        switch (element.Name)
        {
            case "NorthArrow":
                Circle(r);
                PlanCanvas.Children.Add(new Polygon { Points = new PointCollection { new(centre.X, centre.Y - r), new(centre.X + r * 0.45, centre.Y + r * 0.7), new(centre.X, centre.Y + r * 0.35), new(centre.X - r * 0.45, centre.Y + r * 0.7) }, Fill = brush, IsHitTestVisible = false });
                break;
            case "Benchmark":
                Circle(r * 0.6);
                Segment(-r, 0, r, 0);
                Segment(0, -r, 0, r);
                break;
            case "Entrance":
                PlanCanvas.Children.Add(new Polygon { Points = new PointCollection { new(centre.X - r, centre.Y - r * 0.4), new(centre.X, centre.Y - r * 0.4), new(centre.X, centre.Y - r), new(centre.X + r, centre.Y), new(centre.X, centre.Y + r), new(centre.X, centre.Y + r * 0.4), new(centre.X - r, centre.Y + r * 0.4) }, Stroke = brush, StrokeThickness = 1.6, Fill = Brushes.Transparent, Tag = element.Id });
                break;
            case "Warning":
                PlanCanvas.Children.Add(new Polygon { Points = new PointCollection { new(centre.X, centre.Y - r), new(centre.X + r, centre.Y + r * 0.8), new(centre.X - r, centre.Y + r * 0.8) }, Stroke = brush, StrokeThickness = 1.6, Fill = Brushes.Transparent, Tag = element.Id });
                Segment(0, -r * 0.35, 0, r * 0.2, 2);
                break;
            case "Anchor":
                Circle(r * 0.35);
                Segment(0, r * 0.35, 0, r);
                Segment(-r * 0.6, r * 0.6, r * 0.6, r * 0.6);
                break;
            case "Bolt":
                Circle(r * 0.5, new SolidColorBrush(Color.FromArgb(60, 120, 80, 40)));
                Segment(-r * 0.5, 0, r * 0.5, 0, 1.2);
                Segment(0, -r * 0.5, 0, r * 0.5, 1.2);
                break;
            case "RebarSection":
                Circle(r * 0.3, brush);
                break;
            case "BreakLine":
                PlanCanvas.Children.Add(new Polyline { Points = new PointCollection { new(centre.X - r, centre.Y), new(centre.X - r * 0.3, centre.Y), new(centre.X - r * 0.1, centre.Y - r * 0.5), new(centre.X + r * 0.1, centre.Y + r * 0.5), new(centre.X + r * 0.3, centre.Y), new(centre.X + r, centre.Y) }, Stroke = brush, StrokeThickness = 1.6, Tag = element.Id });
                break;
            default:
                Circle(r);
                break;
        }
        // A thin invisible target keeps even the finest drawings easy to select.
        var hit = new Ellipse { Width = size, Height = size, Fill = Brushes.Transparent, Tag = element.Id };
        Canvas.SetLeft(hit, centre.X - r);
        Canvas.SetTop(hit, centre.Y - r);
        PlanCanvas.Children.Add(hit);
    }

    // ------------------------------------------------------------------ spelling

    private void ShowSpellingWindow()
    {
        _spellingWindow?.Close();
        var window = NewManageWindow("Spelling.Title", 520);
        window.Closed += (_, _) => _spellingWindow = null;
        _spellingWindow = window;
        var issues = TextChecker.Check(_session.Document);
        var list = new ListBox { Name = "SpellingList", Height = 220, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var issue in issues)
            list.Items.Add($"{GetText("Spelling.Source." + issue.Source)}: «{issue.Text}» — {GetText("Spelling.Problem." + issue.Problem)}" +
                           (issue.Suggestion is { } suggestion && issue.Problem != "Unbalanced" ? $" → {suggestion}" : string.Empty));
        var summary = new TextBlock
        {
            Name = "SpellingSummary", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
            Text = issues.Count == 0 ? GetText("Spelling.Clean") : FormatText("Spelling.Found", issues.Count)
        };
        var buttons = new WrapPanel();
        buttons.Children.Add(ManageButton("SpellingFix", GetText("Spelling.Fix"), (_, _) =>
        {
            try
            {
                var changed = _session.FixTexts();
                StatusText.Text = changed == 0 ? GetText("Spelling.NothingToFix") : FormatText("Spelling.Fixed", changed);
                RefreshAll();
                ShowSpellingWindow();
            }
            catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
        }));
        buttons.Children.Add(ManageButton("SpellingClose", GetText("Phases.Close"), (_, _) => window.Close()));
        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText("Spelling.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(summary);
        layout.Children.Add(list);
        layout.Children.Add(buttons);
        window.Content = layout;
        window.Show();
    }
}
