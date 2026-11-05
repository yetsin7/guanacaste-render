using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>
/// The electrical system in the plan: placing receptacles, switches, luminaires and panels; wiring switch legs and
/// circuits; flipping switches; and drawing every device with its circuit's state, the switch legs as arcs and the
/// circuit runs back to the panel, all recomputed from the live simulation after each change.
/// </summary>
public partial class MainWindow
{
    /// <summary>Device families a placement command steps through with Space.</summary>
    private static readonly ElectricalDeviceKind[] Receptacles =
        [ElectricalDeviceKind.Outlet, ElectricalDeviceKind.GfciOutlet, ElectricalDeviceKind.UsbOutlet, ElectricalDeviceKind.Outlet240];
    private static readonly ElectricalDeviceKind[] Switches =
        [ElectricalDeviceKind.Switch, ElectricalDeviceKind.ThreeWaySwitch, ElectricalDeviceKind.Dimmer];
    private static readonly ElectricalDeviceKind[] Luminaires =
        [ElectricalDeviceKind.CeilingFixture, ElectricalDeviceKind.Downlight, ElectricalDeviceKind.WallSconce, ElectricalDeviceKind.Pendant,
            ElectricalDeviceKind.CeilingFan, ElectricalDeviceKind.SmokeDetector];

    private static readonly ElectricalDeviceKind[] Cooling = [ElectricalDeviceKind.MiniSplit];
    private static readonly ElectricalDeviceKind[] Solar = [ElectricalDeviceKind.SolarArray, ElectricalDeviceKind.Inverter];

    private ElectricalDeviceKind? _electricalTool;
    private ElectricalDeviceKind[] _electricalFamily = [];
    private bool _wiring;
    private Guid? _wiringSwitch;
    private Guid? _wiringCircuit;
    private Guid? _selectedElectricalId;

    /// <summary>Distance within which a wall device attaches to a wall face.</summary>
    private const double WallAttachDistance = 0.8;

    private void BeginElectricalTool(ElectricalDeviceKind[] family)
    {
        CancelStructuralTool();
        SetWallTool(false);
        ViewTabs.SelectedIndex = 0;
        _electricalFamily = family;
        _electricalTool = family[0];
        StatusText.Text = FormatText("Status.ElectricalPlace", ElectricalName(family[0]));
        RenderPlan();
    }

    private void BeginWiringTool()
    {
        CancelStructuralTool();
        SetWallTool(false);
        ViewTabs.SelectedIndex = 0;
        _wiring = true;
        StatusText.Text = GetText("Status.WiringPickSource");
        RenderPlan();
    }

    /// <summary>Leaves the electrical tools; true when one was armed.</summary>
    private bool CancelElectricalTools()
    {
        var armed = _electricalTool is not null || _wiring;
        _electricalTool = null;
        _wiring = false;
        _wiringSwitch = null;
        _wiringCircuit = null;
        if (armed) RenderPlan();
        return armed;
    }

    /// <summary>Space: steps to the next device of the armed family, or flips the selected switch.</summary>
    private bool TryElectricalSpace()
    {
        if (_electricalTool is ElectricalDeviceKind kind && _electricalFamily.Length > 0)
        {
            var next = _electricalFamily[(Array.IndexOf(_electricalFamily, kind) + 1) % _electricalFamily.Length];
            _electricalTool = next;
            StatusText.Text = FormatText("Status.ElectricalPlace", ElectricalName(next));
            RenderPlan();
            return true;
        }
        if (_selectedElectricalId is Guid id && _session.Document.ElectricalDevices.FirstOrDefault(device => device.Id == id) is
                { } device && (device.IsSwitch || device.Kind == ElectricalDeviceKind.Panel))
        {
            FlipSwitch(id);
            return true;
        }
        if (_selectedElectricalId is Guid solarId &&
            _session.Document.ElectricalDevices.FirstOrDefault(device => device.Id == solarId) is
                { Kind: ElectricalDeviceKind.SolarArray or ElectricalDeviceKind.Inverter } solar)
        {
            // SPACE steps an array through panel counts and an inverter through its AC ratings.
            if (solar.Kind == ElectricalDeviceKind.SolarArray)
            {
                var counts = ElectricalStandards.PanelCounts;
                _session.SetPanelCount(solarId, counts.FirstOrDefault(count => count > solar.PanelCount, counts[0]));
            }
            else
            {
                var ratings = ElectricalStandards.InverterRatings;
                _session.SetInverterRating(solarId, ratings.FirstOrDefault(rating => rating > solar.Watts, ratings[0]));
            }
            RefreshAll();
            return true;
        }
        if (_selectedElectricalId is Guid unitId &&
            _session.Document.ElectricalDevices.FirstOrDefault(device => device.Id == unitId) is { Kind: ElectricalDeviceKind.MiniSplit } unit)
        {
            // SPACE steps a selected air conditioner through the standard capacities.
            var capacities = ElectricalStandards.CoolingCapacities;
            var next = capacities[(capacities.ToList().IndexOf(unit.CoolingBtu) + 1) % capacities.Count];
            _session.SetCoolingCapacity(unitId, next);
            var report = CoolingAnalysis.Analyze(_session.Document).First(candidate => candidate.Unit.Id == unitId);
            StatusText.Text = FormatText("Status.CoolingCapacity", unit.Name, next, report.RequiredBtu, GetText($"Cooling.Status.{report.Status}"));
            RefreshAll();
            return true;
        }
        return false;
    }

    /// <summary>Places, wires or flips; false when no electrical action applies so the plan handles the click.</summary>
    private bool TryHandleElectricalClick(MouseButtonEventArgs e)
    {
        var point = CanvasToWorld(PlanPosition(e));
        if (_electricalTool is ElectricalDeviceKind kind)
        {
            PlaceElectricalDevice(kind, point);
            return true;
        }
        if (_wiring)
        {
            HandleWiringClick(point);
            return true;
        }
        // A double click on a switch flips it, the way a person would press it.
        if (e.ClickCount == 2 && HitElectrical(point) is { } hit && (hit.IsSwitch || hit.Kind == ElectricalDeviceKind.Panel))
        {
            FlipSwitch(hit.Id);
            return true;
        }
        return false;
    }

    /// <summary>Selects a device under the pointer; false when there is none.</summary>
    private bool TrySelectElectrical(MouseButtonEventArgs e)
    {
        if (HitElectrical(CanvasToWorld(PlanPosition(e))) is not { } device) return false;
        SelectExtendedElement();
        _selectedElectricalId = device.Id;
        RefreshProperties();
        RenderPlan();
        return true;
    }

    private void PlaceElectricalDevice(ElectricalDeviceKind kind, Point2 point)
    {
        var (position, facing) = (point, 0.0);
        if (ElectricalStandards.MountsOnWall(kind))
        {
            if (AttachToWall(point) is not { } face)
            {
                StatusText.Text = GetText("Status.ElectricalNeedsWall");
                return;
            }
            (position, facing) = face;
        }
        var count = _session.Document.ElectricalDevices.Count(device => device.Kind == kind) + 1;
        try
        {
            _selectedElectricalId = _session.AddElectricalDevice(_activeLevelId, kind, $"{ElectricalName(kind)} {count}", position, facing);
            StatusText.Text = FormatText("Status.ElectricalPlaced", ElectricalName(kind));
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
        }
        RefreshAll();
    }

    /// <summary>The nearest wall face within reach: the point on it and the direction the face looks (radians).</summary>
    private (Point2 Position, double Facing)? AttachToWall(Point2 point)
    {
        (Point2, double)? best = null;
        var bestDistance = WallAttachDistance;
        foreach (var wall in _session.Document.Walls.Where(wall => wall.LevelId == _activeLevelId && wall.Length > 1e-6))
        {
            var dx = (wall.End.X - wall.Start.X) / wall.Length;
            var dy = (wall.End.Y - wall.Start.Y) / wall.Length;
            var along = Math.Clamp((point.X - wall.Start.X) * dx + (point.Y - wall.Start.Y) * dy, 0.05, wall.Length - 0.05);
            var foot = new Point2(wall.Start.X + dx * along, wall.Start.Y + dy * along);
            var across = (point.X - foot.X) * -dy + (point.Y - foot.Y) * dx;
            var distance = Math.Abs(across);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            var side = across >= 0 ? 1 : -1;
            var normal = (-dy * side, dx * side);
            best = (new Point2(foot.X + normal.Item1 * wall.Thickness / 2, foot.Y + normal.Item2 * wall.Thickness / 2),
                Math.Atan2(normal.Item2, normal.Item1));
        }
        return best;
    }

    private void HandleWiringClick(Point2 point)
    {
        var device = HitElectrical(point);
        var component = device is null ? HitLightingComponent(point) : null;
        if (_wiringSwitch is Guid switchId)
        {
            var target = device is { IsLuminaire: true } ? device.Id : component?.Id;
            if (target is not Guid targetId)
            {
                StatusText.Text = GetText("Status.WiringPickLuminaire");
                return;
            }
            var linked = _session.ToggleSwitchLink(switchId, targetId);
            StatusText.Text = GetText(linked ? "Status.WiringLinked" : "Status.WiringUnlinked");
            RefreshAll();
            return;
        }
        if (_wiringCircuit is Guid circuitId)
        {
            if (device is { IsDirectCurrent: true })
            {
                StatusText.Text = GetText("Status.ArrayToInverter");
                return;
            }
            if (device is null || device.Kind == ElectricalDeviceKind.Panel)
            {
                StatusText.Text = GetText("Status.WiringPickDevice");
                return;
            }
            _session.AssignCircuit(device.Id, device.CircuitId == circuitId ? null : circuitId);
            ReportCircuit(circuitId);
            RefreshAll();
            return;
        }
        switch (device)
        {
            case { IsSwitch: true }:
                _wiringSwitch = device.Id;
                StatusText.Text = FormatText("Status.WiringSwitchPicked", device.Name);
                break;
            case { Kind: ElectricalDeviceKind.Panel }:
            {
                // A new circuit on this panel: 15 A for lighting, the devices clicked next are wired into it.
                var number = _session.Document.ElectricalCircuits.Count(circuit => circuit.PanelId == device.Id) + 1;
                _wiringCircuit = _session.AddCircuit(device.Id, FormatText("Electrical.CircuitName", number), 20);
                StatusText.Text = FormatText("Status.WiringCircuitStarted", number);
                RefreshAll();
                break;
            }
            default:
                StatusText.Text = GetText("Status.WiringPickSource");
                break;
        }
    }

    /// <summary>Says what the circuit carries after a change, and warns when its breaker trips.</summary>
    private void ReportCircuit(Guid circuitId)
    {
        if (ElectricalAnalysis.Analyze(_session.Document).Circuit(circuitId) is not { } report) return;
        StatusText.Text = FormatText(report.Status == CircuitStatus.Tripped ? "Status.CircuitTripped" : "Status.CircuitLoad",
            report.Circuit.Number, report.Devices.Count, report.LoadWatts, report.Current, report.Circuit.BreakerAmps,
            report.Utilization * 100);
    }

    private void FlipSwitch(Guid deviceId)
    {
        _session.ToggleSwitch(deviceId);
        var device = _session.Document.ElectricalDevices.First(candidate => candidate.Id == deviceId);
        StatusText.Text = FormatText(device.IsOn ? "Status.SwitchOn" : "Status.SwitchOff", device.Name);
        RefreshAll();
    }

    private ElectricalDevice? HitElectrical(Point2 point)
    {
        var reach = Math.Max(0.18, 10 / _scale);
        return _session.Document.ElectricalDevices.Where(device => device.LevelId == _activeLevelId)
            .Where(device => device.Position.DistanceTo(point) <= reach)
            .MinBy(device => device.Position.DistanceTo(point));
    }

    private StructuralElement? HitLightingComponent(Point2 point) =>
        _session.Document.StructuralElements.FirstOrDefault(element => element.LevelId == _activeLevelId &&
            element.ComponentShape is ResidentialComponentShape.CeilingLight or ResidentialComponentShape.FloorLamp &&
            element.Start.DistanceTo(point) <= Math.Max(0.3, element.Width));

    private bool DeleteElectricalSelection()
    {
        if (_selectedElectricalId is not Guid id) return false;
        _session.RemoveElectricalDevice(id);
        _selectedElectricalId = null;
        RefreshAll();
        return true;
    }

    private bool TryRefreshElectricalProperties()
    {
        if (_session.Document.ElectricalDevices.FirstOrDefault(device => device.Id == _selectedElectricalId) is not { } device)
            return false;
        var analysis = ElectricalAnalysis.Analyze(_session.Document);
        var circuit = device.CircuitId is Guid id ? analysis.Circuit(id) : null;
        if (device.Kind is ElectricalDeviceKind.SolarArray or ElectricalDeviceKind.Inverter)
        {
            var solar = SolarAnalysis.Analyze(_session.Document, analysis);
            if (device.Kind == ElectricalDeviceKind.SolarArray)
            {
                var owner = solar.FirstOrDefault(report => report.Arrays.Any(array => array.Id == device.Id));
                PropertiesText.Text = FormatText("Properties.SolarArraySummary", device.Name, device.PanelCount, device.PanelWatts,
                    device.PanelCount * device.PanelWatts / 1000, device.Tilt, Compass(device.Facing),
                    SolarAnalysis.OrientationFactor(device.Tilt, device.Facing) * 100, SolarAnalysis.DailyYield(device),
                    owner?.Inverter.Name ?? GetText("Solar.NoInverter"));
            }
            else if (solar.FirstOrDefault(report => report.Inverter.Id == device.Id) is { } report)
                PropertiesText.Text = FormatText("Properties.InverterSummary", device.Name, device.Watts / 1000, report.Arrays.Count,
                    report.DcWatts / 1000, report.DcToAc, GetText(report.Producing ? "Solar.Producing" : "Solar.Off"), report.DailyKilowattHours,
                    report.ConsumptionKilowattHours, report.Coverage * 100,
                    report.Issue.Length == 0 ? GetText("PlumbingStatus.Ok") : GetText(report.Issue),
                    circuit is null ? GetText("Electrical.NoCircuit")
                        : $"{FormatText("Electrical.CircuitName", circuit.Circuit.Number)} · {circuit.Circuit.Voltage:0} V · {circuit.Circuit.BreakerAmps:0} A");
            return true;
        }
        if (device.Kind == ElectricalDeviceKind.MiniSplit)
        {
            var cooling = CoolingAnalysis.Analyze(_session.Document).First(report => report.Unit.Id == device.Id);
            PropertiesText.Text = FormatText("Properties.MiniSplitSummary", device.Name, device.CoolingBtu, device.Watts,
                device.Watts / 240, cooling.Room?.Name ?? GetText("Cooling.NoRoom"), cooling.RoomArea, cooling.RequiredBtu,
                cooling.InstalledBtu, GetText($"Cooling.Status.{cooling.Status}"),
                circuit is null ? GetText("Electrical.NoCircuit")
                    : $"{FormatText("Electrical.CircuitName", circuit.Circuit.Number)} · {circuit.Circuit.Voltage:0} V · {GetText($"CircuitStatus.{circuit.Status}")}");
            return true;
        }
        var state = device.IsLuminaire ? (analysis.Brightness(device.Id) > 0 ? GetText("Electrical.Lit") : GetText("Electrical.Dark"))
            : device.IsSwitch || device.Kind == ElectricalDeviceKind.Panel ? GetText(device.IsOn ? "Electrical.On" : "Electrical.Off")
            : GetText(analysis.Energized(device) ? "Electrical.Energized" : "Electrical.NoPower");
        PropertiesText.Text = FormatText("Properties.ElectricalSummary", device.Name, ElectricalName(device.Kind),
            circuit is null ? GetText("Electrical.NoCircuit") : FormatText("Electrical.CircuitName", circuit.Circuit.Number),
            device.Watts, state, device.Elevation, device.Controls.Count);
        return true;
    }

    private string ElectricalName(ElectricalDeviceKind kind) => GetText($"Electrical.Kind.{kind}");

    /// <summary>The compass direction a plan angle points to (plan +Y is north).</summary>
    private string Compass(double angle)
    {
        var degrees = (90 - angle * 180 / Math.PI + 360) % 360;
        return GetText($"Compass.{(int)Math.Round(degrees / 45) % 8}");
    }

    // ---------------------------------------------------------------- plan

    private static readonly Brush ElectricalInk = new SolidColorBrush(Color.FromRgb(20, 70, 160));

    private static Brush StatusBrush(CircuitStatus status) => status switch
    {
        CircuitStatus.Tripped or CircuitStatus.WrongVoltage => Brushes.Red,
        CircuitStatus.Off => Brushes.Gray,
        CircuitStatus.Warning => Brushes.DarkOrange,
        CircuitStatus.Ok => Brushes.SeaGreen,
        _ => Brushes.SlateGray
    };

    /// <summary>
    /// Draws the electrical layer of the active level: circuit runs from each panel through its devices, switch legs as
    /// arcs to the lights they control, and every device with its symbol, lit lights in yellow, devices coloured by the
    /// state of their circuit, with the circuit number.
    /// </summary>
    private void RenderElectricalPlan()
    {
        var devices = _session.Document.ElectricalDevices.Where(device => device.LevelId == _activeLevelId).ToArray();
        if (devices.Length == 0 && _electricalTool is null) return;
        var analysis = ElectricalAnalysis.Analyze(_session.Document);
        foreach (var panel in analysis.Panels.Where(panel => panel.Panel.LevelId == _activeLevelId))
            foreach (var circuit in panel.Circuits)
                for (var index = 0; index + 1 < circuit.Route.Count; index++)
                    AddPlanPath(circuit.Route[index], circuit.Route[index + 1], StatusBrush(circuit.Status), 1.1,
                        new DoubleCollection { 5, 3 }, bend: 0);
        foreach (var device in devices.Where(device => device.IsSwitch))
            foreach (var target in device.Controls)
            {
                var to = devices.FirstOrDefault(candidate => candidate.Id == target)?.Position
                         ?? _session.Document.StructuralElements.FirstOrDefault(element => element.Id == target)?.Start;
                if (to is Point2 end) AddPlanPath(device.Position, end, ElectricalInk, 0.9, new DoubleCollection { 2, 2 }, bend: 0.25);
            }
        foreach (var device in devices)
        {
            var report = device.CircuitId is Guid id ? analysis.Circuit(id) : null;
            var brush = device.Id == _selectedElectricalId ? Brushes.DarkOrange
                : report is null ? ElectricalInk : StatusBrush(report.Status);
            var lit = device.IsLuminaire && analysis.Brightness(device.Id) > 0;
            DrawElectricalSymbol(device.Kind, device.Position, device.Facing, brush, lit, device.IsOn, device.Id);
            if (report is not null)
                AddPlanLabel(device.Position, $"C{report.Circuit.Number}", brush, 9, 8, 6);
        }
        // Lighting: each room's average illuminance against what its use needs, and bedrooms missing a smoke alarm.
        foreach (var room in LightingAnalysis.Analyze(_session.Document, analysis).Where(room => room.Room.LevelId == _activeLevelId))
        {
            var corner = new Point2(Math.Min(room.Room.FirstCorner.X, room.Room.SecondCorner.X) + 0.15,
                Math.Max(room.Room.FirstCorner.Y, room.Room.SecondCorner.Y) - 0.15);
            var brush = room.Status switch
            {
                LightingStatus.Ok => Brushes.SeaGreen,
                LightingStatus.Dark => Brushes.SlateGray,
                _ => Brushes.DarkOrange
            };
            var label = FormatText("Lighting.RoomLabel", room.Lux, room.RequiredLux);
            if (room.NeedsSmokeDetector && !room.HasSmokeDetector) label += " · " + GetText("Lighting.NoSmokeDetector");
            var at = WorldToCanvas(corner);
            var text = new TextBlock
            {
                Text = label, FontSize = 10, FontWeight = FontWeights.SemiBold, IsHitTestVisible = false,
                Foreground = room.NeedsSmokeDetector && !room.HasSmokeDetector ? Brushes.Red : brush
            };
            Canvas.SetLeft(text, at.X);
            Canvas.SetTop(text, at.Y);
            PlanCanvas.Children.Add(text);
        }
        // Air conditioners: the condenser outside, the line set to it, and the capacity coloured by the room's load.
        foreach (var cooling in CoolingAnalysis.Analyze(_session.Document).Where(report => report.Unit.LevelId == _activeLevelId))
        {
            var brush = cooling.Status switch
            {
                CoolingStatus.Ok => Brushes.SeaGreen,
                CoolingStatus.NoRoom => ElectricalInk,
                _ => Brushes.DarkOrange
            };
            AddPlanLabel(cooling.Unit.Position, $"{cooling.Unit.CoolingBtu / 1000:0}k BTU", brush, 9, 0, -14);
            if (cooling.Unit.OutdoorPosition is not Point2 outdoor) continue;
            AddPlanPath(cooling.Unit.Position, outdoor, ElectricalInk, 1.0, new DoubleCollection { 3, 2 }, bend: 0);
            var at = WorldToCanvas(outdoor);
            var size = Math.Clamp(0.7 * _scale, 14, 60);
            var box = new Rectangle { Width = size, Height = size * 0.45, Stroke = ElectricalInk, StrokeThickness = 1.3, Fill = Brushes.White };
            var turn = -cooling.Unit.Facing * 180 / Math.PI + 90;
            box.RenderTransform = new RotateTransform(turn, size / 2, size * 0.225);
            Canvas.SetLeft(box, at.X - size / 2);
            Canvas.SetTop(box, at.Y - size * 0.225);
            PlanCanvas.Children.Add(box);
            AddCanvasLabel("CE", at, ElectricalInk, Math.Max(8, size * 0.22), null);
        }
        if (_electricalTool is ElectricalDeviceKind kind && _placementPointer is Point2 pointer)
        {
            var (at, facing) = ElectricalStandards.MountsOnWall(kind) && AttachToWall(pointer) is { } face ? face : (pointer, 0.0);
            DrawElectricalSymbol(kind, at, facing, Brushes.DarkOrange, false, true, null);
        }
    }

    private void DrawElectricalSymbol(ElectricalDeviceKind kind, Point2 position, double facing, Brush brush, bool lit, bool on, Guid? tag)
    {
        var center = WorldToCanvas(position);
        var size = Math.Clamp(0.14 * _scale, 7, 16);
        // Wall devices sit just off the face, into the room, like Revit's symbols.
        var outward = new Vector(Math.Cos(facing), -Math.Sin(facing));
        var symbolCenter = ElectricalStandards.MountsOnWall(kind) ? center + outward * size : center;
        Shape Circle(double radius, Brush? fill) => new Ellipse
        {
            Width = radius * 2, Height = radius * 2, Stroke = brush, StrokeThickness = 1.3, Fill = fill ?? Brushes.White, Tag = tag
        };
        void Place(Shape shape, Point at)
        {
            Canvas.SetLeft(shape, at.X - shape.Width / 2);
            Canvas.SetTop(shape, at.Y - shape.Height / 2);
            PlanCanvas.Children.Add(shape);
        }
        void Stroke(Point a, Point b, double thickness = 1.3) =>
            PlanCanvas.Children.Add(new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = brush, StrokeThickness = thickness, Tag = tag });
        void Text(string text, Point at) => AddCanvasLabel(text, at, brush, Math.Max(8, size * 0.8), tag);
        var glow = lit ? new SolidColorBrush(Color.FromRgb(255, 214, 80)) : null;
        switch (kind)
        {
            case ElectricalDeviceKind.Outlet or ElectricalDeviceKind.GfciOutlet or ElectricalDeviceKind.UsbOutlet:
            {
                Place(Circle(size * 0.55, null), symbolCenter);
                var across = new Vector(outward.Y, -outward.X) * size * 0.35;
                foreach (var shift in new[] { -0.6, 0.6 })
                    Stroke(symbolCenter + across * shift - outward * size * 0.9, symbolCenter + across * shift + outward * size * 0.9);
                if (kind == ElectricalDeviceKind.GfciOutlet) Text("GFI", symbolCenter + outward * size * 1.3);
                if (kind == ElectricalDeviceKind.UsbOutlet) Text("USB", symbolCenter + outward * size * 1.3);
                break;
            }
            case ElectricalDeviceKind.Outlet240:
                Place(Circle(size * 0.6, brush), symbolCenter);
                Text("240V", symbolCenter + outward * size * 1.4);
                break;
            case ElectricalDeviceKind.Switch or ElectricalDeviceKind.ThreeWaySwitch or ElectricalDeviceKind.Dimmer:
            {
                Stroke(center, symbolCenter, 1.3);
                Place(Circle(size * 0.18, on ? brush : Brushes.White), center);
                Text(kind switch { ElectricalDeviceKind.ThreeWaySwitch => "S3", ElectricalDeviceKind.Dimmer => "SD", _ => "S" },
                    symbolCenter + outward * size * 0.3);
                break;
            }
            case ElectricalDeviceKind.Panel:
            {
                var across = new Vector(outward.Y, -outward.X) * size * 1.2;
                var depth = outward * size * 0.6;
                PlanCanvas.Children.Add(new Polygon
                {
                    Points = [center - across, center + across, center + across + depth, center - across + depth],
                    Fill = brush, Stroke = brush, Tag = tag
                });
                break;
            }
            case ElectricalDeviceKind.CeilingFixture:
                Place(Circle(size * 0.75, glow), center);
                Stroke(center + new Vector(-size * 0.53, -size * 0.53), center + new Vector(size * 0.53, size * 0.53));
                Stroke(center + new Vector(-size * 0.53, size * 0.53), center + new Vector(size * 0.53, -size * 0.53));
                break;
            case ElectricalDeviceKind.Downlight:
                Place(Circle(size * 0.45, glow), center);
                Place(Circle(size * 0.15, brush), center);
                break;
            case ElectricalDeviceKind.WallSconce:
                Place(Circle(size * 0.5, glow), symbolCenter);
                Stroke(center, symbolCenter);
                break;
            case ElectricalDeviceKind.MiniSplit:
            {
                // The indoor unit: a long box on the wall face with "AC", as in Revit's mechanical equipment tags.
                var across = new Vector(outward.Y, -outward.X) * size * 1.6;
                var depth = outward * size * 0.7;
                PlanCanvas.Children.Add(new Polygon
                {
                    Points = [center - across, center + across, center + across + depth, center - across + depth],
                    Fill = Brushes.White, Stroke = brush, StrokeThickness = 1.4, Tag = tag
                });
                Text("AC", center + depth * 0.5);
                break;
            }
            case ElectricalDeviceKind.SmokeDetector:
                Place(Circle(size * 0.45, null), center);
                Text("SD", center);
                break;
            case ElectricalDeviceKind.SolarArray:
            {
                // The array's outline in plan, with the diagonal that marks PV modules, and an arrow toward where it faces.
                var device = _session.Document.ElectricalDevices.FirstOrDefault(candidate => candidate.Id == tag);
                var count = device?.PanelCount ?? 10;
                var tilt = (device?.Tilt ?? 10) * Math.PI / 180;
                var (rows, columns) = SolarGeometry.Layout(count);
                var halfAcross = columns * ElectricalStandards.PanelWidth / 2 * _scale;
                var halfUp = rows * ElectricalStandards.PanelLength * Math.Cos(tilt) / 2 * _scale;
                var face = new Vector(Math.Cos(facing), -Math.Sin(facing));
                var side = new Vector(-face.Y, face.X);
                Point C(double a, double u) => center + side * a - face * u;
                PlanCanvas.Children.Add(new Polygon
                {
                    Points = [C(-halfAcross, -halfUp), C(halfAcross, -halfUp), C(halfAcross, halfUp), C(-halfAcross, halfUp)],
                    Stroke = brush, StrokeThickness = 1.3, Fill = new SolidColorBrush(Color.FromArgb(60, 20, 40, 110)), Tag = tag
                });
                Stroke(C(-halfAcross, -halfUp), C(halfAcross, halfUp), 0.8);
                Stroke(center, center + face * (halfUp + size), 1.2);
                Text("FV", center);
                break;
            }
            case ElectricalDeviceKind.Inverter:
            {
                var across = new Vector(outward.Y, -outward.X) * size * 0.9;
                var depth = outward * size * 0.8;
                PlanCanvas.Children.Add(new Polygon
                {
                    Points = [center - across, center + across, center + across + depth, center - across + depth],
                    Fill = Brushes.White, Stroke = brush, StrokeThickness = 1.4, Tag = tag
                });
                Text("~", center + depth * 0.5);
                break;
            }
            case ElectricalDeviceKind.WaterPump:
                // A pump: a circle with the impeller triangle, as on plumbing plans.
                Place(Circle(size * 0.7, null), center);
                PlanCanvas.Children.Add(new Polygon
                {
                    Points = [center + new Vector(-size * 0.4, size * 0.35), center + new Vector(size * 0.45, 0), center + new Vector(-size * 0.4, -size * 0.35)],
                    Stroke = brush, StrokeThickness = 1.2, Tag = tag
                });
                break;
            case ElectricalDeviceKind.CeilingFan:
                // Four blades around the hub, as in Revit's fan symbol, the hub yellow when its light is on.
                foreach (var angle in new[] { 0.4, 0.4 + Math.PI / 2, 0.4 + Math.PI, 0.4 + 3 * Math.PI / 2 })
                {
                    var tip = new Vector(Math.Cos(angle), Math.Sin(angle)) * size * 1.6;
                    var side = new Vector(-tip.Y, tip.X) * 0.18;
                    PlanCanvas.Children.Add(new Polygon
                    {
                        Points = [center + tip * 0.3 - side, center + tip - side * 0.6, center + tip + side * 0.6, center + tip * 0.3 + side],
                        Stroke = brush, StrokeThickness = 1.1, Fill = Brushes.White, Tag = tag
                    });
                }
                Place(Circle(size * 0.4, glow), center);
                break;
            case ElectricalDeviceKind.Pendant:
                Place(Circle(size * 0.7, glow), center);
                Place(Circle(size * 0.3, null), center);
                break;
        }
    }

    /// <summary>A dashed straight line or, with a bend, a gentle arc between two plan points (switch legs are arcs).</summary>
    private void AddPlanPath(Point2 from, Point2 to, Brush brush, double thickness, DoubleCollection dashes, double bend)
    {
        var a = WorldToCanvas(from);
        var b = WorldToCanvas(to);
        var middle = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        var normal = new Vector(-(b.Y - a.Y), b.X - a.X) * bend;
        var figure = new PathFigure { StartPoint = a };
        figure.Segments.Add(new QuadraticBezierSegment(middle + normal, b, true));
        PlanCanvas.Children.Add(new System.Windows.Shapes.Path
        {
            Data = new PathGeometry([figure]), Stroke = brush, StrokeThickness = thickness, StrokeDashArray = dashes,
            IsHitTestVisible = false
        });
    }

    private void AddPlanLabel(Point2 position, string text, Brush brush, double size, double dx, double dy)
    {
        var at = WorldToCanvas(position);
        AddCanvasLabel(text, new Point(at.X + dx, at.Y + dy), brush, size, null);
    }

    private void AddCanvasLabel(string text, Point at, Brush brush, double size, Guid? tag)
    {
        var label = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.SemiBold, Foreground = brush, Tag = tag };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(label, at.X - label.DesiredSize.Width / 2);
        Canvas.SetTop(label, at.Y - label.DesiredSize.Height / 2);
        PlanCanvas.Children.Add(label);
    }

    /// <summary>Opens the panel schedule: circuits, loads, breakers and their state, with live controls.</summary>
    private void OpenPanelSchedule()
    {
        if (!_session.Document.ElectricalDevices.Any(device => device.Kind == ElectricalDeviceKind.Panel))
        {
            StatusText.Text = GetText("Status.NoPanel");
            return;
        }
        var window = new ElectricalPanelWindow(_session, RefreshAll) { Owner = this };
        window.Show();
    }
}
