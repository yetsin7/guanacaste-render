using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Regression checks for photovoltaic arrays and grid-tie inverters.</summary>
static class PhotovoltaicChecks
{
    public static void Run(Action<bool, string> check)
    {
        var session = new DocumentSession(SampleProjects.CreateDemonstrationHouse());
        var level = session.Document.Levels[0].Id;
        var gable = ArchitecturalLibrary.SlabPresets.Single(slab => slab.Name == "Gable Roof 30°");
        session.AddSlabFromPreset(level, [new(0, 0), new(12, 0), new(12, 8), new(0, 8)], gable, session.SlabElevation(level, gable));

        // The ridge runs east-west at y = 4: the slope south of it faces south, the other north.
        var south = session.AddElectricalDevice(level, ElectricalDeviceKind.SolarArray, "FV sur", new Point2(6, 2));
        var north = session.AddElectricalDevice(level, ElectricalDeviceKind.SolarArray, "FV norte", new Point2(6, 6));
        var southArray = session.Document.ElectricalDevices.Single(device => device.Id == south);
        var northArray = session.Document.ElectricalDevices.Single(device => device.Id == north);
        check(Math.Abs(southArray.Tilt - 30) < 1e-9 && Math.Abs(southArray.Facing + Math.PI / 2) < 1e-6 &&
              Math.Abs(northArray.Facing - Math.PI / 2) < 1e-6 && southArray.Elevation > 3 && southArray.PanelCount == 10,
            "An array on a gable roof lies on the slope under it, tilted with the roof and facing downhill");
        check(SolarAnalysis.OrientationFactor(10, -Math.PI / 2) == 1 &&
              SolarAnalysis.DailyYield(southArray) > SolarAnalysis.DailyYield(northArray) * 1.2,
            "South-facing panels yield more than north-facing ones at Costa Rica's latitude");
        session.RemoveElectricalDevice(north);

        var inverter = session.AddElectricalDevice(level, ElectricalDeviceKind.Inverter, "Inversor", new Point2(0.1, 3), facing: 0);
        var panel = session.AddElectricalDevice(level, ElectricalDeviceKind.Panel, "Tablero", new Point2(0.1, 1));
        var report = SolarAnalysis.Analyze(session.Document).Single();
        check(report.Status == SolarStatus.Offline && report.Issue == "Solar.Issue.NotConnected" && report.DailyKilowattHours == 0,
            "An inverter not wired to the panel produces nothing");
        try
        {
            var dc = session.AddCircuit(panel, "DC", 20);
            session.AssignCircuit(south, dc);
            check(false, "Arrays are not wired to breakers");
        }
        catch (InvalidDataException) { check(true, "Arrays are not wired to breakers"); }
        session.RemoveCircuit(session.Document.ElectricalCircuits.Single().Id);

        var pv = session.AddCircuit(panel, "Solar", 30);
        session.AssignCircuit(inverter, pv);
        var electrical = ElectricalAnalysis.Analyze(session.Document);
        report = SolarAnalysis.Analyze(session.Document, electrical).Single();
        var panelReport = electrical.Panels.Single();
        check(electrical.Circuit(pv)!.Circuit.Voltage == 240 && report.Producing && Math.Abs(report.DcWatts - 5500) < 1e-9 &&
              Math.Abs(report.DcToAc - 1.1) < 1e-9 && report.DailyKilowattHours > 15 && panelReport.GenerationWatts == 5000,
            "Wired on a 240 V two-pole breaker, 5.5 kWp feed a 5 kW inverter and yield about 20 kWh a day");
        check(!panelReport.BackfeedWithinLimit && report.Status == SolarStatus.Warning && report.Issue == "Solar.Issue.Backfeed",
            "A 30 A inverter breaker on a 100 A panel breaks the 120 % rule");
        session.SetInverterRating(inverter, 3800);
        session.SetBreaker(pv, 20, true);
        electrical = ElectricalAnalysis.Analyze(session.Document);
        check(electrical.Panels.Single().BackfeedWithinLimit && electrical.Circuit(pv)!.Status != CircuitStatus.Tripped,
            "A 3.8 kW inverter on a 20 A breaker fits the 120 % rule");
        session.SetPanelCount(south, 24);
        check(SolarAnalysis.Analyze(session.Document).Single().Issue == "Solar.Issue.Clipping",
            "Far more panels than the inverter can take are flagged: it would clip");
        session.SetPanelCount(south, 8);
        session.ToggleSwitch(panel);
        report = SolarAnalysis.Analyze(session.Document).Single();
        check(!report.Producing && report.Issue == "Solar.Issue.GridDown" && report.DailyKilowattHours == 0,
            "With the main breaker open the inverter shuts down (anti-islanding)");
        session.ToggleSwitch(panel);

        var mesh = BuildingMesher.Build(session.Document);
        var cells = Enumerable.Range(0, mesh.TriangleCount).Where(index => mesh.TriangleKinds[index] == SurfaceKind.SolarCell).ToArray();
        check(cells.Length == 8 * 12 && cells.All(index => mesh.Positions[mesh.Indices[index * 3]].Z > 3),
            "Eight modules are drawn on the roof as dark cell faces");

        var reloaded = ProjectFile.DeserializeDocument(ProjectFile.SerializeDocument(session.Document));
        var copy = reloaded.ElectricalDevices.Single(device => device.Id == south);
        check(copy.PanelCount == 8 && Math.Abs(copy.Tilt - 30) < 1e-9, "Arrays keep their panels and tilt");
    }
}
