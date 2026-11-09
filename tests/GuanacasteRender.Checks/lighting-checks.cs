using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Regression checks for room illuminance by the lumen method and for smoke alarms in bedrooms.</summary>
static class LightingChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(LightingAnalysis.RequiredLux("Cocina") == 300 && LightingAnalysis.RequiredLux("Baño principal") == 200 &&
              LightingAnalysis.RequiredLux("Dormitorio 2") == 150 && LightingAnalysis.RequiredLux("Pasillo") == 100,
            "Each room's use sets its recommended illuminance");

        var session = new DocumentSession(BuildingDocument.Create("Lighting"));
        var level = session.Document.Levels[0].Id;
        var bedroom = session.AddRoom(level, new Point2(0, 0), new Point2(4, 4), "Dormitorio");
        double Lux() => LightingAnalysis.Analyze(session.Document).Single(room => room.Room.Id == bedroom).Lux;
        var ceiling = session.AddElectricalDevice(level, ElectricalDeviceKind.CeilingFixture, "Plafón", new Point2(2, 2));
        var report = LightingAnalysis.Analyze(session.Document).Single();
        check(Math.Abs(report.Lux - 18 * 100 * 0.5 * 0.8 / 16) < 1e-9 && report.Status == LightingStatus.Underlit,
            "One 18 W ceiling fixture lights a 16 m² bedroom to 45 lx: underlit (E = Φ·UF·MF / A)");
        foreach (var (x, y) in new[] { (1.0, 1.0), (3.0, 1.0), (1.0, 3.0), (3.0, 3.0) })
            session.AddElectricalDevice(level, ElectricalDeviceKind.Downlight, "D", new Point2(x, y));
        check(LightingAnalysis.Analyze(session.Document).Single().Status == LightingStatus.Ok,
            "Adding four downlights reaches the bedroom's 150 lx");
        var lightSwitch = session.AddElectricalDevice(level, ElectricalDeviceKind.Dimmer, "SD", new Point2(0.1, 2));
        session.ToggleSwitchLink(lightSwitch, ceiling);
        var full = Lux();
        session.SetDimLevel(lightSwitch, 0.5);
        var dimmed = Lux();
        session.ToggleSwitch(lightSwitch);
        var off = Lux();
        check(Math.Abs(full - dimmed - 18 * 100 * 0.5 * 0.4 / 16) < 1e-9 && off < dimmed,
            "Dimming or switching off the fixture lowers the room's illuminance at once");

        check(LightingAnalysis.Analyze(session.Document).Single() is { NeedsSmokeDetector: true, HasSmokeDetector: false },
            "A bedroom without a smoke alarm is flagged");
        var detector = session.AddElectricalDevice(level, ElectricalDeviceKind.SmokeDetector, "Humo", new Point2(2, 2.5));
        check(LightingAnalysis.Analyze(session.Document).Single().HasSmokeDetector &&
              session.Document.ElectricalDevices.Single(device => device.Id == detector).Elevation > 2.5,
            "A smoke alarm on the bedroom ceiling satisfies the check");

        var closet = session.AddRoom(level, new Point2(5, 0), new Point2(6, 1), "Pasillo");
        foreach (var x in new[] { 5.3, 5.7 })
            session.AddElectricalDevice(level, ElectricalDeviceKind.CeilingFixture, "P", new Point2(x, 0.5));
        check(LightingAnalysis.Analyze(session.Document).Single(room => room.Room.Id == closet).Status == LightingStatus.Overlit,
            "Two fixtures in a 1 m² corridor are flagged as over-lit");
        check(BuildingMesher.Build(session.Document).TriangleCount > 0, "Smoke alarms are modelled");
    }
}
