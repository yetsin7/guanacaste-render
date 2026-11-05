using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Regression checks for the electrical system: devices, switching, circuits, breakers and their simulation.</summary>
static class ElectricalChecks
{
    public static void Run(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Electrical"));
        var level = session.Document.Levels[0].Id;
        var light = session.AddElectricalDevice(level, ElectricalDeviceKind.CeilingFixture, "Sala", new Point2(3, 3));
        var down = session.AddElectricalDevice(level, ElectricalDeviceKind.Downlight, "Pasillo", new Point2(6, 3));
        var single = session.AddElectricalDevice(level, ElectricalDeviceKind.Switch, "S1", new Point2(0.1, 2), facing: 0);
        var first = session.AddElectricalDevice(level, ElectricalDeviceKind.ThreeWaySwitch, "S3-a", new Point2(0.1, 4));
        var second = session.AddElectricalDevice(level, ElectricalDeviceKind.ThreeWaySwitch, "S3-b", new Point2(8, 4));
        var dimmer = session.AddElectricalDevice(level, ElectricalDeviceKind.Dimmer, "SD", new Point2(0.1, 6));
        var device = session.Document.ElectricalDevices.Single(candidate => candidate.Id == light);
        check(device.Elevation > 2.5 && device.Watts > 0 &&
              session.Document.ElectricalDevices.Single(candidate => candidate.Id == single).Elevation == 1.2,
            "Devices take their standard mounting height and load: luminaires at the ceiling, switches at 1.20 m");

        double Light(Guid id) => ElectricalAnalysis.Analyze(session.Document).Brightness(id);
        check(Light(light) == 1, "A luminaire without switches or panel is lit while the design is not yet wired");
        session.ToggleSwitchLink(single, light);
        session.ToggleSwitch(single);
        check(Light(light) == 0, "Opening a switch turns off the luminaire it controls");
        session.ToggleSwitch(single);
        check(Light(light) == 1, "Closing the switch turns it back on");

        session.ToggleSwitchLink(first, down);
        session.ToggleSwitchLink(second, down);
        session.ToggleSwitch(first);
        var afterOne = Light(down);
        session.ToggleSwitch(second);
        var afterTwo = Light(down);
        check(afterOne == 0 && afterTwo == 1, "Either three-way switch toggles the shared light");
        session.ToggleSwitchLink(dimmer, light);
        session.SetDimLevel(dimmer, 0.4);
        check(Math.Abs(Light(light) - 0.4) < 1e-9, "A dimmer scales the output of the lights it controls");

        var panel = session.AddElectricalDevice(level, ElectricalDeviceKind.Panel, "Tablero", new Point2(0.1, 0.5));
        check(Light(light) == 0, "Once the project has a panel, an unwired luminaire has no power");
        var lighting = session.AddCircuit(panel, "Alumbrado", 15);
        session.AssignCircuit(light, lighting);
        session.AssignCircuit(down, lighting);
        check(Light(light) > 0 && Light(down) > 0, "Luminaires on a live circuit light again");

        var outlets = session.AddCircuit(panel, "Tomas cocina", 20);
        for (var index = 0; index < 13; index++)
        {
            var outlet = session.AddElectricalDevice(level, ElectricalDeviceKind.Outlet, $"T{index}", new Point2(1 + index * 0.6, 0.1));
            session.AssignCircuit(outlet, outlets);
        }
        var report = ElectricalAnalysis.Analyze(session.Document).Circuit(outlets)!;
        check(Math.Abs(report.LoadWatts - 13 * 180) < 1e-6 && Math.Abs(report.Current - 2340 / 120.0) < 1e-9 &&
              report.Status == CircuitStatus.Warning && report.ConductorAwg == "12",
            "Thirteen receptacles load a 20 A circuit to 97 %: a warning, on 12 AWG wire");
        var heater = session.AddElectricalDevice(level, ElectricalDeviceKind.Outlet, "Calentador", new Point2(9, 0.1));
        session.SetDeviceLoad(heater, 1500);
        session.AssignCircuit(heater, outlets);
        report = ElectricalAnalysis.Analyze(session.Document).Circuit(outlets)!;
        check(report.Status == CircuitStatus.Tripped && !report.Energized, "An overloaded circuit trips its breaker and goes dead");
        session.SetBreaker(outlets, 40, true);
        check(ElectricalAnalysis.Analyze(session.Document).Circuit(outlets)!.Status is CircuitStatus.Ok or CircuitStatus.Warning &&
              ElectricalAnalysis.Analyze(session.Document).Circuit(outlets)!.ConductorAwg == "8",
            "A larger breaker carries the load and calls for heavier wire");
        session.SetBreaker(lighting, 15, false);
        check(Light(light) == 0 && ElectricalAnalysis.Analyze(session.Document).Circuit(lighting)!.Status == CircuitStatus.Off,
            "Opening a breaker by hand turns off everything on its circuit");
        session.SetBreaker(lighting, 15, true);
        session.ToggleSwitch(panel);
        check(Light(down) == 0, "Opening the main breaker turns off the whole panel");
        session.ToggleSwitch(panel);
        var drop = ElectricalAnalysis.Analyze(session.Document).Circuit(outlets)!;
        check(drop.LengthMeters > 9 && drop.VoltageDropPercent > 0 && drop.Route.Count == drop.Devices.Count + 1,
            "Each circuit has a routed run from the panel with its length and voltage drop");

        var mesh = BuildingMesher.Build(session.Document);
        check(mesh.TriangleKinds.Contains(SurfaceKind.LightEmitter) && mesh.TriangleKinds.Contains(SurfaceKind.Ceramic),
            "Lit luminaires glow in the model and wall devices have their plates");
        session.ToggleSwitch(single);
        mesh = BuildingMesher.Build(session.Document);
        check(mesh.TriangleKinds.Contains(SurfaceKind.LightOff), "A switched-off luminaire shows a dark diffuser");

        // A dimmer at 40 % dims the emitter in the render to 40 % of its full radiance.
        session.ToggleSwitch(single);
        session.SetDimLevel(dimmer, 0.4);
        mesh = BuildingMesher.Build(session.Document);
        var scene = new Guanacaste3D.Core.Rendering.RenderSceneBuilder().AddBuilding(mesh).Build();
        var full = Guanacaste3D.Core.Rendering.MaterialLibrary.ForKind(SurfaceKind.LightEmitter).Emission;
        check(scene.Materials.Any(material => Math.Abs(material.Emission.X - full.X * 0.4) < full.X * 0.02) &&
              Enumerable.Range(0, mesh.TriangleCount).Any(triangle => Math.Abs(mesh.EmissionOf(triangle) - 0.4) < 1e-6),
            "A dimmed luminaire emits a matching fraction of its light in the render");
        session.SetDimLevel(dimmer, 1);

        var path = Path.Combine(Path.GetTempPath(), $"electrical-{Guid.NewGuid():N}.g3d");
        try
        {
            ProjectFile.Save(path, session.Document);
            var reloaded = ProjectFile.Load(path);
            check(reloaded.ElectricalDevices.Count == session.Document.ElectricalDevices.Count &&
                  reloaded.ElectricalCircuits.Count == 2 &&
                  reloaded.ElectricalDevices.Single(candidate => candidate.Id == first).Controls.Contains(down),
                "Devices, switch links and circuits survive a project round trip");
        }
        finally { File.Delete(path); }

        session.RemoveElectricalDevice(panel);
        check(session.Document.ElectricalCircuits.Count == 0 &&
              session.Document.ElectricalDevices.All(candidate => candidate.CircuitId is null),
            "Removing a panel removes its circuits and disconnects their devices");
    }
}
