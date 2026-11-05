using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Desktop;
using Guanacaste3D.Desktop.Controls;

internal static partial class InteractionChecks
{

    /// <summary>Checks that electrical overlays can be hidden without deleting the corresponding model devices.</summary>
    private static void PlanServiceVisibilityChecks(DocumentSession session)
    {
        var visibility = (CheckBox)_window.FindName("ElectricalPlanVisibility");
        session.AddElectricalDevice(session.Document.Levels[0].Id, ElectricalDeviceKind.CeilingFixture,
            "Test fixture", new Point2(1, 1));
        var before = session.Document.ElectricalDevices.Count;
        Invoke("RenderPlan");
        var shown = _canvas.Children.Count;
        visibility.IsChecked = false;
        var hidden = _canvas.Children.Count;
        Check(hidden < shown && session.Document.ElectricalDevices.Count == before,
            "Electrical plan graphics can be hidden without deleting devices");
        visibility.IsChecked = true;
        Check(_canvas.Children.Count == shown, "Electrical plan graphics return when the overlay is enabled");
    }

    /// <summary>Verifies a middle click over the 3D content does not close its tab or return to the plan.</summary>
    private static void MiddleClickViewChecks()
    {
        var tabs = (TabControl)_window.FindName("ViewTabs");
        tabs.SelectedIndex = 1;
        Pump();
        var surface = (FrameworkElement)_window.FindName("ModelSurface");
        var mouse = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Middle)
        {
            RoutedEvent = UIElement.PreviewMouseDownEvent
        };
        surface.RaiseEvent(mouse);
        Check(tabs.SelectedIndex == 1 && ((TabItem)tabs.Items[1]).Visibility == Visibility.Visible,
            "A middle click on the 3D surface keeps the active 3D tab open");
        Mouse.Capture(null);
        tabs.SelectedIndex = 0;
    }

    /// <summary>
    /// Places a panel and a switch on a wall and a luminaire on the ceiling with the ribbon commands, wires the switch to
    /// the light and a new circuit from the panel to both, then flips the switch and checks the light follows.
    /// </summary>
    private static void ElectricalToolChecks(DocumentSession session)
    {
        var level = session.Document.Levels[0].Id;
        var wall = session.Document.Walls.First(candidate => candidate.LevelId == level && candidate.Length > 3);
        var dx = (wall.End.X - wall.Start.X) / wall.Length;
        var dy = (wall.End.Y - wall.Start.Y) / wall.Length;
        Point2 Near(double along, double off) => new(wall.Start.X + dx * along - dy * off, wall.Start.Y + dy * along + dx * off);
        var before = session.Document.ElectricalDevices.Count;
        Invoke("ExecuteApplicationCommand", "ElectricalEquipment");
        Invoke("PlaceElectricalDevice", ElectricalDeviceKind.Panel, Near(0.6, 0.3));
        Invoke("ExecuteApplicationCommand", "LightSwitch");
        Invoke("PlaceElectricalDevice", ElectricalDeviceKind.Switch, Near(1.6, 0.3));
        Invoke("ExecuteApplicationCommand", "LightingFixture");
        Invoke("PlaceElectricalDevice", ElectricalDeviceKind.CeilingFixture, Near(2.0, 2.0));
        var placed = session.Document.ElectricalDevices.Skip(before).ToArray();
        var panel = placed.Single(device => device.Kind == ElectricalDeviceKind.Panel);
        var light = placed.Single(device => device.Kind == ElectricalDeviceKind.CeilingFixture);
        var lightSwitch = placed.Single(device => device.Kind == ElectricalDeviceKind.Switch);
        Check(placed.Length == 3 && Math.Abs(Math.Abs(DistanceToLine(wall, panel.Position)) - wall.Thickness / 2) < 1e-6,
            "Panels and switches attach to the wall face; luminaires go where clicked");

        Invoke("ExecuteApplicationCommand", "Wire");
        Invoke("HandleWiringClick", lightSwitch.Position);
        Invoke("HandleWiringClick", light.Position);
        Invoke("CancelElectricalTools");
        Invoke("ExecuteApplicationCommand", "Wire");
        Invoke("HandleWiringClick", panel.Position);
        Invoke("HandleWiringClick", light.Position);
        Invoke("HandleWiringClick", lightSwitch.Position);
        Invoke("CancelElectricalTools");
        var analysis = ElectricalAnalysis.Analyze(session.Document);
        Check(session.Document.ElectricalDevices.Single(device => device.Id == lightSwitch.Id).Controls.Contains(light.Id) &&
              session.Document.ElectricalCircuits.Count(circuit => circuit.PanelId == panel.Id) == 1 &&
              analysis.Brightness(light.Id) == 1,
            "The wiring tool links a switch to its light and wires a new circuit from the panel");
        Invoke("FlipSwitch", lightSwitch.Id);
        Check(ElectricalAnalysis.Analyze(session.Document).Brightness(light.Id) == 0, "Flipping the switch turns the light off at once");
        Invoke("RenderPlan");
        Pump();
        Check(_canvas.Children.OfType<System.Windows.Shapes.Ellipse>().Any(shape => shape.Tag is Guid id && id == light.Id),
            "Electrical devices are drawn in plan with their symbols");
        Invoke("ExecuteApplicationCommand", "MechanicalEquipment");
        Check(Field<ElectricalDeviceKind?>("_electricalTool") == ElectricalDeviceKind.MiniSplit,
            "Mechanical equipment arms the air conditioner");
        Invoke("PlaceElectricalDevice", ElectricalDeviceKind.MiniSplit, Near(2.5, 0.3));
        Invoke("CancelElectricalTools");
        var unit = session.Document.ElectricalDevices.Last();
        Check(unit.Kind == ElectricalDeviceKind.MiniSplit && unit.OutdoorPosition is Point2 outdoor &&
              Math.Sign(DistanceToLine(wall, outdoor)) != Math.Sign(DistanceToLine(wall, unit.Position)),
            "An air conditioner placed on a wall gets its condenser on the other side of that wall");
        session.RemoveElectricalDevice(unit.Id);
        foreach (var device in placed) session.RemoveElectricalDevice(device.Id);
    }

    /// <summary>
    /// Places a meter and, after SPACE, an outlet with the real tool; pipes a toilet with the piping tool; connects the
    /// rest automatically from the ribbon; and checks that the plan draws the meter.
    /// </summary>
    private static void PlumbingToolChecks(DocumentSession session)
    {
        var level = session.Document.Levels[0].Id;
        Guid Place(string name, double x, double y) => session.AddElementFromPreset(level,
            ResidentialComponentCatalog.Presets.Single(preset => preset.Name == name), new Point2(x, y), new Point2(x, y));
        var toilet = Place("Toilet", 202, 204);
        var vanity = Place("Bathroom Vanity", 203.5, 204);
        var heater = Place("Water Heater", 206, 201);
        var shower = Place("Shower Enclosure", 205.2, 204);
        var sink = Place("Kitchen Sink Cabinet", 209, 204);
        var pointsBefore = session.Document.PlumbingPoints.Count;
        Invoke("ExecuteApplicationCommand", "PlumbingService");
        Invoke("PlacePlumbingPoint", PlumbingPointKind.WaterMeter, new Point2(200, 200));
        Invoke("TryPlumbingSpace");
        var afterMeter = Field<PlumbingPointKind?>("_plumbingPointTool");
        while (Field<PlumbingPointKind?>("_plumbingPointTool") != PlumbingPointKind.SewerOutlet) Invoke("TryPlumbingSpace");
        Invoke("PlacePlumbingPoint", Field<PlumbingPointKind?>("_plumbingPointTool")!.Value, new Point2(200, 205));
        Invoke("CancelPlumbingTools");
        var points = session.Document.PlumbingPoints.Skip(pointsBefore).ToArray();
        Check(points.Select(point => point.Kind).SequenceEqual([PlumbingPointKind.WaterMeter, PlumbingPointKind.SewerOutlet]),
            "The service tool places a water meter, and SPACE steps through the other services to the sewer outlet");
        Check(afterMeter == PlumbingPointKind.Cistern, "After the meter, SPACE offers the cistern with its pump");

        Invoke("ExecuteApplicationCommand", "Pipe");
        Invoke("HandlePipingClick", points[0].Position);
        Invoke("HandlePipingClick", session.Document.StructuralElements.Single(element => element.Id == toilet).Start);
        Invoke("CancelPlumbingTools");
        Check(session.Document.PlumbingSystems.Any(system => system.Kind == PlumbingSystemKind.ColdWater && system.Fixtures.Contains(toilet)),
            "The piping tool starts a cold system at the meter and connects the toilet clicked next");

        Invoke("ExecuteApplicationCommand", "ConnectPlumbing");
        var systems = session.Document.PlumbingSystems;
        Check(systems.Count == 3 && systems.Single(system => system.Kind == PlumbingSystemKind.HotWater).Fixtures.Contains(vanity),
            "Connecting plumbing from the ribbon builds the cold, hot and sanitary systems");
        Invoke("RenderPlan");
        Pump();
        Check(_canvas.Children.OfType<System.Windows.Shapes.Rectangle>().Any(shape => shape.Tag is Guid id && id == points[0].Id),
            "The water meter is drawn in plan");
        // Evidence: the plumbing plan of the little test house, framed on screen.
        var (scale, pan) = (Field<double>("_scale"), Field<Point>("_panOffset"));
        SetField("_scale", 70.0);
        SetField("_panOffset", new Point(-204.5 * 70, 202.5 * 70));
        Invoke("RenderPlan");
        Pump();
        Directory.CreateDirectory("artifacts/interaction-evidence");
        SaveBitmap("artifacts/interaction-evidence/plumbing-plan.png");
        SetField("_scale", scale);
        SetField("_panOffset", pan);
        foreach (var system in systems.ToArray()) session.RemovePlumbingSystem(system.Id);
        foreach (var point in points) session.RemovePlumbingPoint(point.Id);
        foreach (var id in new[] { toilet, vanity, heater, shower, sink }) session.RemoveStructuralElement(id);
    }

    /// <summary>Measures wall-axis deviation when checking plan snapping and service hosting.</summary>
    private static double DistanceToLine(Wall wall, Point2 point)
    {
        var dx = (wall.End.X - wall.Start.X) / wall.Length;
        var dy = (wall.End.Y - wall.Start.Y) / wall.Length;
        return (point.X - wall.Start.X) * -dy + (point.Y - wall.Start.Y) * dx;
    }
}
