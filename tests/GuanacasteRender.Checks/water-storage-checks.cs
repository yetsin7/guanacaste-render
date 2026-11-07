using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Regression checks for cisterns with pumps and elevated tanks, and the pump's tie to the electrical system.</summary>
static class WaterStorageChecks
{
    public static void Run(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Storage"));
        var level = session.Document.Levels[0].Id;
        session.AddRoom(level, new Point2(0, 0), new Point2(4, 4), "Dormitorio 1");
        session.AddRoom(level, new Point2(4, 0), new Point2(8, 4), "Dormitorio 2");
        Guid Place(string name, double x, double y) => session.AddElementFromPreset(level,
            ResidentialComponentCatalog.Presets.Single(preset => preset.Name == name), new Point2(x, y), new Point2(x, y));
        var shower = Place("Shower Enclosure", 3, 2);
        var sink = Place("Pedestal Sink", 4, 2);
        session.AddPlumbingPoint(level, PlumbingPointKind.WaterMeter, "Medidor", new Point2(-4, 0));
        var cistern = session.AddPlumbingPoint(level, PlumbingPointKind.Cistern, "Cisterna", new Point2(-2, 0), "Bomba");
        var point = session.Document.PlumbingPoints.Single(candidate => candidate.Id == cistern);
        var pump = session.Document.ElectricalDevices.Single(device => device.Id == point.PumpDeviceId);
        check(pump.Kind == ElectricalDeviceKind.WaterPump && pump.Watts == WaterStorage.PumpWatts(1) && pump.Elevation == 0,
            "A cistern comes with its pump, an electrical load of a 1 HP motor");

        session.ConnectPlumbing((kind, number) => $"{kind} {number}");
        var cold = session.Document.PlumbingSystems.Single(system => system.Kind == PlumbingSystemKind.ColdWater);
        var analysis = PlumbingAnalysis.Analyze(session.Document);
        var storage = analysis.Storage.Single();
        check(cold.SourceId == cistern && storage.Occupants == 4 && storage.DailyLiters == 800 && storage.CapacityLiters == 1100 &&
              storage.PumpRequiredHorsepower > 0 && storage.PumpRequiredHorsepower < 1 && storage.Status == PlumbingStatus.Ok &&
              analysis.System(cold.Id)!.Fixtures.All(fixture => fixture.Status == PlumbingStatus.Ok),
            "The house draws from the cistern: four people need 800 L a day, a 1100 L tank, and the pump delivers its pressure");

        // The pump runs on electricity: once the project has a panel, an unwired pump stops and the house loses pressure.
        var panel = session.AddElectricalDevice(level, ElectricalDeviceKind.Panel, "Tablero", new Point2(0.1, 1));
        var dry = PlumbingAnalysis.Analyze(session.Document);
        check(dry.System(cold.Id)!.Fixtures.All(fixture => fixture.Issue == "Plumbing.Issue.PumpOff") && !dry.Storage.Single().PumpPowered,
            "With a panel but the pump unwired, every fixture loses its water pressure");
        var circuit = session.AddCircuit(panel, "Bomba", 20);
        session.AssignCircuit(pump.Id, circuit);
        check(PlumbingAnalysis.Analyze(session.Document).System(cold.Id)!.Status == PlumbingStatus.Ok,
            "Wiring the pump to a circuit brings the pressure back");
        session.SetBreaker(circuit, 20, false);
        check(PlumbingAnalysis.Analyze(session.Document).System(cold.Id)!.Fixtures.Single(fixture => fixture.FixtureId == shower).Issue ==
              "Plumbing.Issue.PumpOff",
            "Opening the pump's breaker leaves the shower dry at once");
        session.SetBreaker(circuit, 20, true);
        session.SetPump(cistern, 2, 280);
        check(session.Document.ElectricalDevices.Single(device => device.Id == pump.Id).Watts == WaterStorage.PumpWatts(2),
            "A larger pump draws more power from its circuit");

        var mesh = BuildingMesher.Build(session.Document);
        check(mesh.TriangleKinds.Contains(SurfaceKind.OpeningFrame) && mesh.Positions.Any(position => position.X < -1.4f && position.Z > 1.0f),
            "The cistern stands as a tank on its pad beside the pump");

        // An elevated tank feeds by gravity: 6 m is not enough for a shower, 20 m is.
        var gravity = new DocumentSession(BuildingDocument.Create("Gravity"));
        var ground = gravity.Document.Levels[0].Id;
        var tankShower = gravity.AddElementFromPreset(ground, ResidentialComponentCatalog.Presets.Single(preset => preset.Name == "Shower Enclosure"),
            new Point2(3, 2), new Point2(3, 2));
        var tank = gravity.AddPlumbingPoint(ground, PlumbingPointKind.ElevatedTank, "Tanque", new Point2(-2, 0));
        gravity.ConnectPlumbing((kind, number) => $"{kind} {number}");
        var low = PlumbingAnalysis.Analyze(gravity.Document).Fixture(tankShower, PlumbingSystemKind.ColdWater)!;
        gravity.SetTankHeight(tank, 20);
        var high = PlumbingAnalysis.Analyze(gravity.Document).Fixture(tankShower, PlumbingSystemKind.ColdWater)!;
        check(low.Status == PlumbingStatus.Fail && low.PressureKPa < 60 && high.Status != PlumbingStatus.Fail && high.PressureKPa > low.PressureKPa + 130,
            "A tank 6 m up gives a shower too little pressure; at 20 m it is enough (about 10 kPa per meter)");
        var tower = BuildingMesher.Build(gravity.Document);
        check(tower.Positions.Any(position => position.Z > 20.5f && position.X < -1.5f), "The elevated tank stands on its tower");

        var reloaded = ProjectFile.DeserializeDocument(ProjectFile.SerializeDocument(session.Document));
        var copy = reloaded.PlumbingPoints.Single(candidate => candidate.Id == cistern);
        check(copy.PumpDeviceId == pump.Id && copy.PumpHorsepower == 2, "The cistern keeps its pump and settings");
        session.RemovePlumbingPoint(cistern);
        check(session.Document.ElectricalDevices.All(device => device.Id != pump.Id), "Removing the cistern removes its pump");
        _ = sink;
    }
}
