using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Plumbing in the plan: placing the water meter and the sewer outlet, piping fixtures into the cold, hot and sanitary
/// systems (or connecting everything at once), and drawing each system's routed pipes with their sizes, every fixture
/// marked by the result of its hydraulic check, and fixtures left unconnected.
/// </summary>
public partial class MainWindow
{
    private PlumbingPointKind? _plumbingPointTool;
    private bool _piping;
    private Guid? _pipingSystem;
    private Guid? _selectedPlumbingId;

    private void BeginPlumbingPointTool()
    {
        CancelStructuralTool();
        SetWallTool(false);
        ViewTabs.SelectedIndex = 0;
        _plumbingPointTool = PlumbingPointKind.WaterMeter;
        StatusText.Text = FormatText("Status.PlumbingPlace", PlumbingName(PlumbingPointKind.WaterMeter));
        RenderPlan();
    }

    private void BeginPipingTool()
    {
        CancelStructuralTool();
        SetWallTool(false);
        ViewTabs.SelectedIndex = 0;
        _piping = true;
        StatusText.Text = GetText("Status.PipingPickSource");
        RenderPlan();
    }

    /// <summary>Leaves the plumbing tools; true when one was armed.</summary>
    private bool CancelPlumbingTools()
    {
        var armed = _plumbingPointTool is not null || _piping;
        _plumbingPointTool = null;
        _piping = false;
        _pipingSystem = null;
        if (armed) RenderPlan();
        return armed;
    }

    /// <summary>Space while placing: switches between the water meter and the sewer outlet.</summary>
    private bool TryPlumbingSpace()
    {
        if (_plumbingPointTool is not PlumbingPointKind kind) return false;
        var next = kind switch
        {
            PlumbingPointKind.WaterMeter => PlumbingPointKind.Cistern,
            PlumbingPointKind.Cistern => PlumbingPointKind.ElevatedTank,
            PlumbingPointKind.ElevatedTank => PlumbingPointKind.SewerOutlet,
            PlumbingPointKind.SewerOutlet => PlumbingPointKind.SepticTank,
            PlumbingPointKind.SepticTank => PlumbingPointKind.StormOutlet,
            PlumbingPointKind.StormOutlet => PlumbingPointKind.VentStack,
            _ => PlumbingPointKind.WaterMeter
        };
        _plumbingPointTool = next;
        StatusText.Text = FormatText("Status.PlumbingPlace", PlumbingName(next));
        RenderPlan();
        return true;
    }

    private bool TryHandlePlumbingClick(MouseButtonEventArgs e)
    {
        var point = CanvasToWorld(PlanPosition(e));
        if (_plumbingPointTool is PlumbingPointKind kind)
        {
            PlacePlumbingPoint(kind, point);
            return true;
        }
        if (!_piping) return false;
        HandlePipingClick(point);
        return true;
    }

    private void PlacePlumbingPoint(PlumbingPointKind kind, Point2 point)
    {
        var count = _session.Document.PlumbingPoints.Count(candidate => candidate.Kind == kind) + 1;
        try
        {
            var name = $"{PlumbingName(kind)} {count}";
            _selectedPlumbingId = _session.AddPlumbingPoint(_activeLevelId, kind, name, point, FormatText("Plumbing.PumpName", name));
            StatusText.Text = FormatText("Status.PlumbingPlaced", PlumbingName(kind));
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
        }
        RefreshAll();
    }

    /// <summary>
    /// Piping: a meter, heater or outlet picks (or starts) its system; each fixture clicked next is connected to it, or
    /// disconnected when it already was. With a cold system active, a heater is a fixture that takes cold water.
    /// </summary>
    private void HandlePipingClick(Point2 point)
    {
        var source = HitPlumbing(point);
        var active = _session.Document.PlumbingSystems.FirstOrDefault(system => system.Id == _pipingSystem);
        var fixture = source is null ? HitFixture(point, active?.Kind == PlumbingSystemKind.StormWater) : null;
        if (source is { Kind: PlumbingPointKind.VentStack })
        {
            StatusText.Text = GetText("Status.VentIsAutomatic");
            return;
        }
        if (source is not null)
        {
            SelectPipingSystem(source.Kind switch
            {
                PlumbingPointKind.WaterMeter or PlumbingPointKind.Cistern or PlumbingPointKind.ElevatedTank => PlumbingSystemKind.ColdWater,
                PlumbingPointKind.StormOutlet => PlumbingSystemKind.StormWater,
                _ => PlumbingSystemKind.Sanitary
            }, source.Id, source.Name);
            return;
        }
        if (fixture is null)
        {
            StatusText.Text = GetText(active is null ? "Status.PipingPickSource" : "Status.PipingPickFixture");
            return;
        }
        if (PlumbingStandards.IsWaterHeater(fixture) && active?.Kind != PlumbingSystemKind.ColdWater)
        {
            SelectPipingSystem(PlumbingSystemKind.HotWater, fixture.Id, fixture.Name);
            return;
        }
        if (active is null)
        {
            StatusText.Text = GetText("Status.PipingPickSource");
            return;
        }
        try
        {
            var connected = _session.TogglePlumbingFixture(active.Id, fixture.Id);
            var report = PlumbingAnalysis.Analyze(_session.Document).Fixture(fixture.Id, active.Kind);
            StatusText.Text = connected && report is not null
                ? FormatText("Status.PipingConnected", fixture.Name, active.Name, FixtureResult(report, active.Kind))
                : FormatText("Status.PipingDisconnected", fixture.Name, active.Name);
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
        }
        RefreshAll();
    }

    private void SelectPipingSystem(PlumbingSystemKind kind, Guid sourceId, string sourceName)
    {
        var system = _session.Document.PlumbingSystems.FirstOrDefault(candidate => candidate.Kind == kind && candidate.SourceId == sourceId);
        _pipingSystem = system?.Id ?? _session.AddPlumbingSystem(kind, sourceId,
            FormatText($"Plumbing.SystemName.{kind}", _session.Document.PlumbingSystems.Count(candidate => candidate.Kind == kind) + 1));
        StatusText.Text = FormatText("Status.PipingSystemPicked", GetText($"Plumbing.Kind.{kind}"), sourceName);
        RefreshAll();
    }

    /// <summary>Connects every fixture to the nearest meter, heater and outlet in one undoable step.</summary>
    private void ConnectAllPlumbing()
    {
        try
        {
            var count = _session.ConnectPlumbing((kind, number) => FormatText($"Plumbing.SystemName.{kind}", number),
                number => FormatText("Plumbing.VentName", number), number => FormatText("Plumbing.DownspoutName", number));
            var analysis = PlumbingAnalysis.Analyze(_session.Document);
            StatusText.Text = FormatText("Status.PlumbingConnectedAll", count,
                analysis.Systems.Sum(report => report.Fixtures.Count(fixture => fixture.Status == PlumbingStatus.Fail)),
                analysis.Unserved.Count);
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
        }
        RefreshAll();
    }

    private string FixtureResult(FixtureReport report, PlumbingSystemKind kind) => kind switch
    {
        PlumbingSystemKind.Sanitary => FormatText("Plumbing.DrainResult", report.Nominal, report.Cover * 100,
            string.IsNullOrEmpty(report.Issue) ? string.Empty : GetText(report.Issue)),
        PlumbingSystemKind.HotWater => FormatText("Plumbing.HotResult", report.Nominal, report.PressureKPa, report.WaitSeconds,
            string.IsNullOrEmpty(report.Issue) ? string.Empty : GetText(report.Issue)),
        _ => FormatText("Plumbing.ColdResult", report.Nominal, report.PressureKPa,
            string.IsNullOrEmpty(report.Issue) ? string.Empty : GetText(report.Issue))
    };

    private bool TrySelectPlumbing(MouseButtonEventArgs e)
    {
        if (HitPlumbing(CanvasToWorld(PlanPosition(e))) is not { } point) return false;
        SelectExtendedElement();
        _selectedPlumbingId = point.Id;
        RefreshProperties();
        RenderPlan();
        return true;
    }

    private PlumbingPoint? HitPlumbing(Point2 point)
    {
        var reach = Math.Max(0.25, 10 / _scale);
        return _session.Document.PlumbingPoints.Where(candidate => candidate.LevelId == _activeLevelId)
            .Where(candidate => candidate.Position.DistanceTo(point) <= reach)
            .MinBy(candidate => candidate.Position.DistanceTo(point));
    }

    /// <summary>The plumbing fixture (or water heater) under the pointer on the active level.</summary>
    private StructuralElement? HitFixture(Point2 point, bool downspouts = false) =>
        _session.Document.StructuralElements
            .Where(element => element.LevelId == _activeLevelId &&
                              (downspouts ? element.Kind == StructuralElementKind.Downspout : PlumbingStandards.Fixture(element) is not null))
            .Where(element => element.Start.DistanceTo(point) <= Math.Max(0.3, Math.Max(element.Width, element.Depth) / 2))
            .MinBy(element => element.Start.DistanceTo(point));

    private bool DeletePlumbingSelection()
    {
        if (_selectedPlumbingId is not Guid id) return false;
        _session.RemovePlumbingPoint(id);
        _selectedPlumbingId = null;
        RefreshAll();
        return true;
    }

    private bool TryRefreshPlumbingProperties()
    {
        if (_session.Document.PlumbingPoints.FirstOrDefault(point => point.Id == _selectedPlumbingId) is not { } point) return false;
        var systems = _session.Document.PlumbingSystems.Count(system => system.SourceId == point.Id);
        var analysis = PlumbingAnalysis.Analyze(_session.Document);
        var vent = analysis.Systems.SelectMany(report => report.Vents).FirstOrDefault(candidate => candidate.Id == point.Id);
        var storage = analysis.Storage.FirstOrDefault(candidate => candidate.Id == point.Id);
        PropertiesText.Text = point.Kind switch
        {
            PlumbingPointKind.WaterMeter => FormatText("Properties.WaterMeterSummary", point.Name, point.SupplyPressureKPa,
                point.SupplyPressureKPa / 6.895, systems),
            PlumbingPointKind.SewerOutlet => FormatText("Properties.SewerOutletSummary", point.Name, point.InvertDepth, systems),
            PlumbingPointKind.SepticTank when SiteDrainage.Septic(_session.Document, point) is var septic =>
                FormatText("Properties.SepticSummary", point.Name, septic.Occupants, septic.DailyLiters, septic.VolumeLiters / 1000,
                    septic.Length, septic.Width, septic.LiquidDepth, point.Percolation, septic.FieldArea, septic.FieldLength, septic.Trenches),
            PlumbingPointKind.Cistern when storage is not null => FormatText("Properties.CisternSummary", point.Name, storage.Occupants,
                storage.DailyLiters, storage.CapacityLiters, point.PumpHorsepower, point.PumpPressureKPa, storage.PumpRequiredHorsepower,
                GetText(storage.PumpPowered ? "Plumbing.PumpOn" : "Plumbing.PumpNoPower"),
                storage.Issue.Length == 0 ? GetText("PlumbingStatus.Ok") : GetText(storage.Issue)),
            PlumbingPointKind.ElevatedTank when storage is not null => FormatText("Properties.ElevatedTankSummary", point.Name, storage.Occupants,
                storage.DailyLiters, storage.CapacityLiters, point.TankHeight, point.TankHeight * 9.81,
                storage.Issue.Length == 0 ? GetText("PlumbingStatus.Ok") : GetText(storage.Issue)),
            PlumbingPointKind.StormOutlet => FormatText("Properties.StormOutletSummary", point.Name, point.RainfallMmPerHour, point.InvertDepth, systems),
            _ => FormatText("Properties.VentStackSummary", point.Name, vent?.Nominal ?? "—", vent?.Units ?? 0, vent?.Top ?? 0)
        };
        return true;
    }

    private string PlumbingName(PlumbingPointKind kind) => GetText($"Plumbing.Point.{kind}");

    // ---------------------------------------------------------------- plan

    private static readonly Brush ColdInk = new SolidColorBrush(Color.FromRgb(30, 90, 200));
    private static readonly Brush HotInk = new SolidColorBrush(Color.FromRgb(200, 40, 30));
    private static readonly Brush DrainInk = new SolidColorBrush(Color.FromRgb(120, 90, 40));
    private static readonly Brush VentInk = new SolidColorBrush(Color.FromRgb(40, 140, 70));

    private static readonly Brush StormInk = new SolidColorBrush(Color.FromRgb(40, 130, 60));

    private static Brush PlumbingBrush(PlumbingSystemKind kind) => kind switch
    {
        PlumbingSystemKind.ColdWater => ColdInk,
        PlumbingSystemKind.HotWater => HotInk,
        PlumbingSystemKind.StormWater => StormInk,
        _ => DrainInk
    };

    private static Brush PlumbingStatusBrush(PlumbingStatus status) => status switch
    {
        PlumbingStatus.Fail => Brushes.Red,
        PlumbingStatus.Warning => Brushes.DarkOrange,
        PlumbingStatus.Ok => Brushes.SeaGreen,
        _ => Brushes.SlateGray
    };

    /// <summary>
    /// Draws the plumbing of the active level: cold water solid blue, hot water dashed red, drains long-dashed brown
    /// with size and slope, a status dot on every fixture connection, the meter and outlet symbols, and a red ring
    /// around fixtures that still lack a system they need.
    /// </summary>
    private void RenderPlumbingPlan()
    {
        var document = _session.Document;
        if (document.PlumbingPoints.Count == 0 && document.PlumbingSystems.Count == 0 && _plumbingPointTool is null && !_piping) return;
        var analysis = PlumbingAnalysis.Analyze(document);
        var level = document.Levels.FirstOrDefault(candidate => candidate.Id == _activeLevelId)?.Elevation ?? 0;
        foreach (var report in analysis.Systems)
        {
            var brush = PlumbingBrush(report.System.Kind);
            var active = report.System.Id == _pipingSystem;
            var dashes = report.System.Kind switch
            {
                PlumbingSystemKind.ColdWater => null,
                PlumbingSystemKind.HotWater => new DoubleCollection { 6, 3 },
                PlumbingSystemKind.StormWater => new DoubleCollection { 8, 3, 2, 3 },
                _ => new DoubleCollection { 10, 4 }
            };
            // Systems that share a route are drawn side by side, each shifted a few pixels on the diagonal, which
            // separates both horizontal and vertical runs and keeps every polyline continuous at its corners.
            var lane = report.System.Kind switch
            {
                PlumbingSystemKind.ColdWater => -1, PlumbingSystemKind.HotWater => 0, PlumbingSystemKind.StormWater => 2, _ => 1
            };
            var shiftWorld = lane * 4 / _scale;
            Point2 Shift(Point2 point) => new(point.X + shiftWorld, point.Y - shiftWorld);
            foreach (var run in report.Runs.Where(run => !run.Detail && run.From.DistanceTo(run.To) > 1e-6))
            {
                // Only the runs under this level's floor (mains) belong to its plan.
                if (Math.Abs(run.FromZ - level) > 1.2) continue;
                var thickness = (report.System.Kind == PlumbingSystemKind.Sanitary ? 2.0 : 1.4) + (active ? 1.2 : 0);
                AddPlanPath(Shift(run.From), Shift(run.To), brush, thickness, dashes ?? [], bend: 0);
                if (run.From.DistanceTo(run.To) >= 1.2)
                {
                    // Labels sit at different stations along shared runs: cold at a quarter, hot halfway, drain at three quarters.
                    var station = 0.5 + Math.Clamp(lane, -1, 1) * 0.25;
                    var at = Shift(new Point2(run.From.X + (run.To.X - run.From.X) * station, run.From.Y + (run.To.Y - run.From.Y) * station));
                    var label = report.System.Kind switch
                    {
                        PlumbingSystemKind.Sanitary => $"Ø{run.Nominal} {PlumbingStandards.DrainSlope * 100:0}%",
                        PlumbingSystemKind.StormWater => $"Ø{run.Nominal} {SiteDrainage.StormSlope * 100:0}%",
                        _ => $"Ø{run.Nominal}"
                    };
                    var vertical = Math.Abs(run.To.X - run.From.X) < 1e-6;
                    AddPlanLabel(at, label, brush, 9, vertical ? 18 : 0, vertical ? 0 : -9);
                }
            }
            foreach (var fixture in report.Fixtures)
            {
                if (document.StructuralElements.FirstOrDefault(element => element.Id == fixture.FixtureId) is not { } element ||
                    element.LevelId != _activeLevelId)
                    continue;
                var at = WorldToCanvas(element.Kind == StructuralElementKind.Downspout ? element.Start : PlumbingStandards.Connection(element));
                // The trap arm to its vent: dotted green, red when it is too long.
                if (report.Vents.FirstOrDefault(vent => vent.Name == fixture.Vent) is { } vent)
                    AddPlanPath(PlumbingStandards.Connection(element), vent.Position,
                        fixture.TrapArm > fixture.TrapArmLimit ? Brushes.Red : VentInk, 0.9, new DoubleCollection { 1, 2 }, bend: 0.15);
                var shift = report.System.Kind switch
                {
                    PlumbingSystemKind.ColdWater => -6.0, PlumbingSystemKind.HotWater => 0.0, PlumbingSystemKind.StormWater => 9.0, _ => 6.0
                };
                var dot = new Ellipse
                {
                    Width = 7, Height = 7, Fill = PlumbingStatusBrush(fixture.Status), Stroke = brush, StrokeThickness = 1.2,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(dot, at.X + shift - 3.5);
                Canvas.SetTop(dot, at.Y - 3.5);
                PlanCanvas.Children.Add(dot);
            }
        }
        foreach (var (fixture, _) in analysis.Unserved.Where(item => item.Fixture.LevelId == _activeLevelId).DistinctBy(item => item.Fixture.Id))
        {
            var at = WorldToCanvas(fixture.Start);
            var radius = Math.Max(10, Math.Max(fixture.Width, fixture.Depth) * _scale * 0.6);
            var ring = new Ellipse
            {
                Width = radius * 2, Height = radius * 2, Stroke = Brushes.Red, StrokeThickness = 1.2,
                StrokeDashArray = [3, 2], IsHitTestVisible = false
            };
            Canvas.SetLeft(ring, at.X - radius);
            Canvas.SetTop(ring, at.Y - radius);
            PlanCanvas.Children.Add(ring);
        }
        foreach (var septic in analysis.Septic)
            if (document.PlumbingPoints.FirstOrDefault(point => point.Id == septic.Id)?.LevelId == _activeLevelId)
                DrawSeptic(septic, septic.Id == _selectedPlumbingId);
        foreach (var point in document.PlumbingPoints.Where(point => point.LevelId == _activeLevelId && point.Kind != PlumbingPointKind.SepticTank))
            DrawPlumbingPoint(point.Kind, point.Position, point.Id == _selectedPlumbingId ? Brushes.DarkOrange : null, point.Id);
        if (_plumbingPointTool is PlumbingPointKind kind && _placementPointer is Point2 pointer)
            DrawPlumbingPoint(kind, pointer, Brushes.DarkOrange, null);
    }

    private void DrawPlumbingPoint(PlumbingPointKind kind, Point2 position, Brush? highlight, Guid? tag)
    {
        var center = WorldToCanvas(position);
        var size = Math.Clamp(0.25 * _scale, 10, 20);
        var brush = highlight ?? kind switch
        {
            PlumbingPointKind.WaterMeter or PlumbingPointKind.Cistern or PlumbingPointKind.ElevatedTank => ColdInk,
            PlumbingPointKind.SewerOutlet or PlumbingPointKind.SepticTank => DrainInk,
            PlumbingPointKind.StormOutlet => StormInk,
            _ => VentInk
        };
        Shape shape = kind == PlumbingPointKind.WaterMeter
            ? new Rectangle { Width = size, Height = size }
            : new Ellipse { Width = size, Height = size };
        shape.Stroke = brush;
        shape.StrokeThickness = 1.6;
        shape.Fill = Brushes.White;
        shape.Tag = tag;
        Canvas.SetLeft(shape, center.X - size / 2);
        Canvas.SetTop(shape, center.Y - size / 2);
        PlanCanvas.Children.Add(shape);
        AddCanvasLabel(kind switch
        {
            PlumbingPointKind.WaterMeter => "M", PlumbingPointKind.SewerOutlet => "S", PlumbingPointKind.SepticTank => "TS",
            PlumbingPointKind.Cistern => "CI", PlumbingPointKind.ElevatedTank => "TE",
            PlumbingPointKind.StormOutlet => "P", _ => "V"
        }, center, brush, size * (kind is PlumbingPointKind.SepticTank or PlumbingPointKind.Cistern or PlumbingPointKind.ElevatedTank ? 0.45 : 0.6), tag);
    }

    /// <summary>The septic tank to scale, with its two chambers, and the trenches of its drain field dashed beyond it.</summary>
    private void DrawSeptic(SepticReport septic, bool selected)
    {
        var brush = selected ? Brushes.DarkOrange : DrainInk;
        var (dx, dy) = (septic.FieldDirection.X, septic.FieldDirection.Y);
        var (nx, ny) = (-dy, dx);
        Point2 At(double along, double across) =>
            new(septic.Position.X + dx * along + nx * across, septic.Position.Y + dy * along + ny * across);
        var (halfL, halfW) = (septic.Length / 2 + 0.1, septic.Width / 2 + 0.1);
        PlanCanvas.Children.Add(new Polygon
        {
            Points = [WorldToCanvas(At(-halfL, -halfW)), WorldToCanvas(At(halfL, -halfW)), WorldToCanvas(At(halfL, halfW)), WorldToCanvas(At(-halfL, halfW))],
            Stroke = brush, StrokeThickness = 1.6, Fill = new SolidColorBrush(Color.FromArgb(40, 120, 90, 40)), Tag = septic.Id
        });
        var baffle = -halfL + 2 * halfL * 2 / 3;
        AddPlanPath(At(baffle, -halfW), At(baffle, halfW), brush, 1.0, [], bend: 0);
        AddPlanLabel(septic.Position, FormatText("Plumbing.SepticLabel", septic.VolumeLiters / 1000, septic.Occupants), brush, 9, 0, 0);
        var box = halfL + 1.5;
        AddPlanPath(At(halfL, 0), At(box, 0), brush, 1.2, [6, 3], bend: 0);
        var trench = septic.FieldLength / septic.Trenches;
        for (var index = 0; index < septic.Trenches; index++)
        {
            var offset = (index - (septic.Trenches - 1) / 2.0) * 2;
            AddPlanPath(At(box, 0), At(box + 1, offset), brush, 1.0, [3, 3], bend: 0);
            AddPlanPath(At(box + 1, offset), At(box + 1 + trench, offset), brush, 2.2, [2, 4], bend: 0);
        }
        AddPlanLabel(At(box + 1 + trench / 2, (septic.Trenches - 1) + 0.8), FormatText("Plumbing.FieldLabel", septic.FieldLength, septic.Trenches),
            brush, 9, 0, 0);
    }

    /// <summary>Opens the plumbing schedule: every system with its fixtures, sizes, pressures, waits and drain cover.</summary>
    private void OpenPlumbingSchedule()
    {
        var window = new PlumbingScheduleWindow(_session, RefreshAll, ConnectAllPlumbing) { Owner = this };
        window.Show();
    }
}
