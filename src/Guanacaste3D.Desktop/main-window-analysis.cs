using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private Window? _analyticalWindow;
    private Window? _zoneWindow;
    private Window? _reportWindow;
    private Window? _fillWindow;
    private readonly HashSet<string> _analyticalLayers = [];

    private static readonly string[] AnalyticalKinds = ["Member", "Panel", "Link", "Boundary", "Opening"];

    /// <summary>Routes the Analyze tab: the analytical model, zones, the three reports, mass shapes and the results fill.</summary>
    private bool TryExecuteAnalysisCommand(string commandId)
    {
        switch (commandId)
        {
            case "AnalyticalMember": ShowAnalyticalWindow("Member"); return true;
            case "AnalyticalPanel": ShowAnalyticalWindow("Panel"); return true;
            case "AnalyticalLink": ShowAnalyticalWindow("Link"); return true;
            case "AnalyticalBoundary": ShowAnalyticalWindow("Boundary"); return true;
            case "AnalyticalOpening": ShowAnalyticalWindow("Opening"); return true;
            case "Zone": ShowZoneWindow(); return true;
            case "StructuralAnalysis": ShowReport("Report.Structural", StructuralReportText()); return true;
            case "SystemsAnalysis": ShowReport("Report.Systems", SystemsReportText()); return true;
            case "EnergyOptimization": ShowReport("Report.Energy", EnergyReportText()); return true;
            case "FormCreation": CreateMassForms(); return true;
            case "ResultsFill": ShowResultsFillWindow(); return true;
            default: return false;
        }
    }

    // ------------------------------------------------------------------ analytical model overlay

    private void ShowAnalyticalWindow(string layer)
    {
        _analyticalLayers.Add(layer);
        _analyticalWindow?.Close();
        var window = NewManageWindow("Analytical.Title", 420);
        window.Closed += (_, _) => _analyticalWindow = null;
        _analyticalWindow = window;
        var model = AnalyticalModel.Build(_session.Document);
        var counts = new Dictionary<string, int>
        {
            ["Member"] = model.Members.Count, ["Panel"] = model.Panels.Count, ["Link"] = model.Links.Count, ["Boundary"] = model.Supports.Count, ["Opening"] = model.Openings.Count
        };
        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText("Analytical.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        foreach (var kind in AnalyticalKinds)
        {
            var box = new CheckBox { Name = "Layer_" + kind, Content = FormatText("Analytical." + kind, counts[kind]), IsChecked = _analyticalLayers.Contains(kind), Margin = new Thickness(0, 2, 0, 2) };
            var captured = kind;
            box.Checked += (_, _) => { _analyticalLayers.Add(captured); RenderPlan(); };
            box.Unchecked += (_, _) => { _analyticalLayers.Remove(captured); RenderPlan(); };
            layout.Children.Add(box);
        }
        layout.Children.Add(ManageButton("AnalyticalClose", GetText("Phases.Close"), (_, _) => window.Close()));
        window.Content = layout;
        window.Show();
        RenderPlan();
    }

    /// <summary>Draws the layers of the analytical model that are on, on the active level: bars, panels, nodes, supports and openings.</summary>
    private void RenderAnalyticalOverlay()
    {
        if (_analyticalLayers.Count == 0) return;
        var level = _session.Document.Levels.FirstOrDefault(item => item.Id == _activeLevelId);
        if (level is null) return;
        var model = AnalyticalModel.Build(_session.Document);
        var bottom = level.Elevation - 1e-6;
        var top = _session.Document.Levels.Where(item => item.Elevation > level.Elevation).Select(item => item.Elevation).DefaultIfEmpty(level.Elevation + 4).Min() + 1e-6;
        bool Here(Point3 point) => point.Z >= bottom && point.Z <= top;
        Point P(Point3 point) => WorldToCanvas(new Point2(point.X, point.Y));
        void Add(UIElement element)
        {
            if (element is Shape shape) shape.IsHitTestVisible = false;
            PlanCanvas.Children.Add(element);
        }
        if (_analyticalLayers.Contains("Panel"))
            foreach (var panel in model.Panels.Where(item => item.Corners.Any(Here) && item.Kind != "Wall"))
                Add(new Polygon { Points = new PointCollection(panel.Corners.Select(P)), Fill = new SolidColorBrush(Color.FromArgb(50, 70, 130, 220)), Stroke = Brushes.RoyalBlue, StrokeDashArray = [4, 3], StrokeThickness = 1 });
        if (_analyticalLayers.Contains("Panel"))
            foreach (var wall in model.Panels.Where(item => item.Kind == "Wall" && item.Corners.Any(Here)))
                Add(new Line { X1 = P(wall.Corners[0]).X, Y1 = P(wall.Corners[0]).Y, X2 = P(wall.Corners[1]).X, Y2 = P(wall.Corners[1]).Y, Stroke = Brushes.RoyalBlue, StrokeThickness = 3, Opacity = 0.7 });
        if (_analyticalLayers.Contains("Member"))
            foreach (var member in model.Members.Where(item => Here(item.A) || Here(item.B)))
                Add(new Line { X1 = P(member.A).X, Y1 = P(member.A).Y, X2 = P(member.B).X, Y2 = P(member.B).Y, Stroke = member.Kind == "Column" ? Brushes.Crimson : Brushes.DarkBlue, StrokeThickness = member.Kind == "Column" ? 5 : 3 });
        if (_analyticalLayers.Contains("Opening"))
            foreach (var opening in model.Openings.Where(item => item.Corners.Any(Here)))
                Add(new Polygon { Points = new PointCollection(opening.Corners.Select(P)), Stroke = Brushes.DarkOrange, StrokeThickness = 2, StrokeDashArray = [2, 2], Fill = Brushes.Transparent });
        if (_analyticalLayers.Contains("Link"))
            foreach (var link in model.Links.Where(item => Here(item.At)))
            {
                var at = P(link.At);
                var dot = new Ellipse { Width = 9, Height = 9, Fill = Brushes.Gold, Stroke = Brushes.Black, StrokeThickness = 1 };
                Canvas.SetLeft(dot, at.X - 4.5);
                Canvas.SetTop(dot, at.Y - 4.5);
                Add(dot);
            }
        if (_analyticalLayers.Contains("Boundary"))
            foreach (var support in model.Supports.Where(item => Here(item.At)))
            {
                var at = P(support.At);
                Add(new Polygon
                {
                    Points = new PointCollection { new(at.X, at.Y - 7), new(at.X + 7, at.Y + 6), new(at.X - 7, at.Y + 6) },
                    Fill = support.Condition == "Fixed" ? Brushes.DarkGreen : Brushes.White, Stroke = Brushes.DarkGreen, StrokeThickness = 1.5
                });
            }
    }

    // ------------------------------------------------------------------ zones

    private void ShowZoneWindow()
    {
        _zoneWindow?.Close();
        var window = NewManageWindow("Zone.Title", 520);
        window.Closed += (_, _) => _zoneWindow = null;
        _zoneWindow = window;
        var rooms = _session.Document.Rooms.Where(room => room.LevelId == _activeLevelId && !_session.Document.Zones.Any(zone => zone.RoomIds.Contains(room.Id))).ToList();
        var boxes = rooms.Select(room => new CheckBox { Content = $"{room.Number} · {room.Name} · {Units.FormatArea(room.Area)}", Tag = room.Id }).ToList();
        var roomPanel = new StackPanel { Name = "ZoneRooms" };
        foreach (var box in boxes) roomPanel.Children.Add(box);
        if (boxes.Count == 0) roomPanel.Children.Add(new TextBlock { Text = GetText("Zone.NoRooms") });
        var name = new TextBox { Name = "ZoneName", Text = $"{GetText("Zone.Default")} {_session.Document.Zones.Count + 1}", Margin = new Thickness(0, 4, 0, 6), Padding = new Thickness(4, 2, 4, 2) };
        var list = new ListBox { Name = "ZoneList", Height = 110, Margin = new Thickness(0, 0, 0, 6) };
        var summaries = _session.SummarizeZones();
        foreach (var summary in summaries) list.Items.Add($"{summary.Name} · {FormatText("Zone.Rooms", summary.Rooms)} · {Units.FormatArea(summary.Area)} · {Units.FormatVolume(summary.Volume)}");
        var buttons = new WrapPanel();
        buttons.Children.Add(ManageButton("ZoneCreate", GetText("Zone.Create"), (_, _) =>
        {
            var chosen = boxes.Where(box => box.IsChecked == true).Select(box => (Guid)box.Tag).ToList();
            try
            {
                _session.CreateZone(name.Text, chosen);
                StatusText.Text = FormatText("Status.ZoneCreated", chosen.Count);
                RefreshAll();
                ShowZoneWindow();
            }
            catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
        }));
        buttons.Children.Add(ManageButton("ZoneRemove", GetText("Zone.Remove"), (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= summaries.Count) return;
            try
            {
                _session.RemoveZone(summaries[list.SelectedIndex].ZoneId);
                StatusText.Text = GetText("Status.ZoneRemoved");
                RefreshAll();
                ShowZoneWindow();
            }
            catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
        }));
        buttons.Children.Add(ManageButton("ZoneClose", GetText("Phases.Close"), (_, _) => window.Close()));
        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText("Zone.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(new TextBlock { Text = GetText("Zone.Existing"), FontWeight = FontWeights.SemiBold });
        layout.Children.Add(list);
        layout.Children.Add(new TextBlock { Text = GetText("Zone.NewFrom"), FontWeight = FontWeights.SemiBold });
        layout.Children.Add(roomPanel);
        layout.Children.Add(name);
        layout.Children.Add(buttons);
        window.Content = new ScrollViewer { Content = layout, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        window.Show();
    }

    // ------------------------------------------------------------------ reports

    private void ShowReport(string titleKey, string text)
    {
        _reportWindow?.Close();
        var window = NewManageWindow(titleKey, 600);
        window.Closed += (_, _) => _reportWindow = null;
        _reportWindow = window;
        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBox { Name = "ReportText", Text = text, IsReadOnly = true, Height = 380, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 6) });
        layout.Children.Add(ManageButton("ReportClose", GetText("Phases.Close"), (_, _) => window.Close()));
        window.Content = layout;
        window.Show();
    }

    private string Kilos(double value) => FormatText("Report.Kilos", value);

    internal string StructuralReportText()
    {
        var summary = StructuralSummary.Build(_session.Document);
        var lines = new List<string> { GetText("Report.StructuralHint"), string.Empty };
        foreach (var level in summary.Levels)
            lines.Add(FormatText("Report.StructuralLevel", level.Name, Kilos(level.Walls), Kilos(level.Slabs), Kilos(level.Frames), Kilos(level.Live)));
        lines.Add(string.Empty);
        lines.Add(FormatText("Report.StructuralTotal", Kilos(summary.TotalDead), Kilos(summary.TotalLive), Kilos(summary.TotalDead + summary.TotalLive)));
        if (summary.Columns.Count == 0) lines.Add(GetText("Report.NoColumns"));
        foreach (var load in summary.Columns.OrderByDescending(item => item.Accumulated).Take(12))
            lines.Add(FormatText("Report.ColumnLoad", load.ColumnId.ToString("N")[..6], Units.FormatArea(load.TributaryArea), Kilos(load.Load), Kilos(load.Accumulated)));
        if (summary.UnassignedSlabLoad > 0) lines.Add(FormatText("Report.Unassigned", Kilos(summary.UnassignedSlabLoad)));
        return string.Join(Environment.NewLine, lines);
    }

    internal string SystemsReportText()
    {
        var s = SystemsSummary.Build(_session.Document);
        return string.Join(Environment.NewLine,
        [
            FormatText("Report.Plumbing", s.PlumbingSystems, s.FixturesServed, s.FixturesUnserved, s.PlumbingWarnings, s.PlumbingFailures),
            FormatText("Report.Electrical", s.Panels, s.Circuits, s.ConnectedWatts, s.CircuitsWarning, s.CircuitsOverloaded)
        ]);
    }

    internal string EnergyReportText()
    {
        var energy = EnergySummary.Build(_session.Document);
        var lines = new List<string> { GetText("Report.EnergyHint"), string.Empty };
        foreach (var facade in energy.Facades)
            lines.Add(FormatText("Report.Facade", GetText("Orientation." + facade.Orientation), Units.FormatArea(facade.WallArea), Units.FormatArea(facade.WindowArea), facade.WindowRatio * 100));
        lines.Add(FormatText("Report.EnergyTotal", Units.FormatArea(energy.TotalWallArea), Units.FormatArea(energy.TotalWindowArea), energy.TotalRatio * 100));
        lines.Add(string.Empty);
        lines.AddRange(energy.Advice.Select(code => "• " + GetText("Advice." + code)));
        return string.Join(Environment.NewLine, lines);
    }

    // ------------------------------------------------------------------ mass forms and results fill

    private void CreateMassForms()
    {
        if (SelectedMass() is not { } mass)
        {
            StatusText.Text = GetText("Status.MassNotSelected");
            return;
        }
        var text = TextPrompt.Ask(this, GetText("Form.Title"), GetText("Prompt.Form"), GetText("Prompt.Accept"), GetText("Prompt.Cancel"), "1 2 3");
        if (string.IsNullOrWhiteSpace(text)) return;
        var proportions = Words(text).Select(word => double.TryParse(word.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : double.NaN).ToList();
        try
        {
            var variants = _session.GenerateMassVariants(mass.Id, proportions);
            var best = variants.MaxBy(variant => variant.Compactness)!;
            StatusText.Text = FormatText("Status.FormsMade", variants.Count - 1, best.Name);
            RefreshAll();
            ShowReport("Form.Title", string.Join(Environment.NewLine, variants.Select(variant =>
                FormatText("Form.Line", variant.Name, Units.FormatLength(variant.Perimeter), Units.FormatArea(variant.FacadeArea), variant.Compactness))));
        }
        catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
    }

    private void ShowResultsFillWindow()
    {
        _fillWindow?.Close();
        var window = NewManageWindow("Fill.Title", 420);
        window.Closed += (_, _) => _fillWindow = null;
        _fillWindow = window;
        var combo = new ComboBox { Name = "FillMetric", Margin = new Thickness(0, 0, 0, 8) };
        foreach (var metric in Enum.GetValues<ResultsMetric>()) combo.Items.Add(GetText("Fill.Metric." + metric));
        combo.SelectedIndex = (int)_session.Document.ResultsMetric;
        var buttons = new WrapPanel();
        buttons.Children.Add(ManageButton("FillApply", GetText("Units.Apply"), (_, _) =>
        {
            try
            {
                _session.SetResultsMetric((ResultsMetric)Math.Max(0, combo.SelectedIndex));
                StatusText.Text = GetText("Status.FillChanged");
                RefreshAll();
            }
            catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
        }));
        buttons.Children.Add(ManageButton("FillClose", GetText("Phases.Close"), (_, _) => window.Close()));
        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText("Fill.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(combo);
        layout.Children.Add(buttons);
        window.Content = layout;
        window.Show();
    }

    /// <summary>The caption of the results fill drawn in the plan: the metric and the range it spans on this level.</summary>
    private void AddResultsFillLegend()
    {
        var metric = _session.Document.ResultsMetric;
        if (metric == ResultsMetric.None) return;
        var rooms = _session.Document.Rooms.Where(room => room.LevelId == _activeLevelId).ToList();
        if (rooms.Count == 0) return;
        var values = rooms.Select(room => ResultsFill.ValueOf(_session.Document, room, metric)).ToList();
        string Show(double value) => metric switch { ResultsMetric.Area => Units.FormatArea(value), ResultsMetric.WindowRatio => $"{value * 100:0} %", _ => Units.FormatLength(value) };
        var bar = new Border
        {
            Width = 120, Height = 12, BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(1),
            Background = new LinearGradientBrush(new GradientStopCollection(Enumerable.Range(0, 6).Select(step =>
            {
                var (r, g, b) = ResultsFill.ColorAt(step / 5.0);
                return new GradientStop(Color.FromRgb(r, g, b), step / 5.0);
            })), 0)
        };
        Canvas.SetLeft(bar, 14);
        Canvas.SetTop(bar, 32);
        PlanCanvas.Children.Add(bar);
        var caption = new TextBlock { Text = $"{GetText("Fill.Metric." + metric)}: {Show(values.Min())} → {Show(values.Max())}", FontSize = 11, Foreground = Brushes.DimGray, IsHitTestVisible = false };
        Canvas.SetLeft(caption, 14);
        Canvas.SetTop(caption, 12);
        PlanCanvas.Children.Add(caption);
    }
}
