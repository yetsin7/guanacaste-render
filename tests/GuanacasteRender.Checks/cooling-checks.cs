using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Regression checks for air conditioning: sizing by room, the outdoor unit, and its 240 V circuit.</summary>
static class CoolingChecks
{
    public static void Run(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Cooling"));
        var level = session.Document.Levels[0].Id;
        var corners = new[] { new Point2(0, 0), new Point2(5, 0), new Point2(5, 4), new Point2(0, 4) };
        for (var index = 0; index < 4; index++) session.AddWall(level, corners[index], corners[(index + 1) % 4]);
        var room = session.AddRoom(level, new Point2(0.1, 0.1), new Point2(4.9, 3.9), "Dormitorio");
        // On the inside face of the north wall, looking south into the room.
        var unit = session.AddElectricalDevice(level, ElectricalDeviceKind.MiniSplit, "AC 1", new Point2(2.5, 3.9), facing: -Math.PI / 2);
        var device = session.Document.ElectricalDevices.Single(candidate => candidate.Id == unit);
        check(device.CoolingBtu == 12000 && device.Watts == ElectricalStandards.CoolingWatts(12000) && device.Elevation == 2.15 &&
              device.OutdoorPosition is Point2 outdoor && outdoor.Y > 4.1 && Math.Abs(outdoor.X - 2.5) < 1e-9,
            "A minisplit is sized for its room (18 m² takes 12000 BTU/h) and its condenser goes outside the wall behind it");
        var report = CoolingAnalysis.Analyze(session.Document).Single();
        check(report.Room?.Id == room && report.Status == CoolingStatus.Ok && Math.Abs(report.RequiredBtu - 10900) < 1e-9,
            "The cooling check finds the room and compares its load with the installed capacity");
        session.SetCoolingCapacity(unit, 9000);
        check(CoolingAnalysis.Analyze(session.Document).Single().Status == CoolingStatus.Undersized &&
              session.Document.ElectricalDevices.Single(candidate => candidate.Id == unit).Watts == ElectricalStandards.CoolingWatts(9000),
            "A smaller unit is undersized and draws less power");
        session.SetCoolingCapacity(unit, 24000);
        check(CoolingAnalysis.Analyze(session.Document).Single().Status == CoolingStatus.Oversized,
            "A unit far larger than the load is flagged as oversized (short cycling)");
        session.SetCoolingCapacity(unit, 12000);

        var panel = session.AddElectricalDevice(level, ElectricalDeviceKind.Panel, "Tablero", new Point2(0.1, 1));
        var circuit = session.AddCircuit(panel, "AC", 20);
        session.AssignCircuit(unit, circuit);
        var ac = ElectricalAnalysis.Analyze(session.Document).Circuit(circuit)!;
        check(ac.Circuit.Voltage == 240 && ac.Status == CircuitStatus.Ok && Math.Abs(ac.Current - device.Watts / 240) < 1e-9,
            "Wiring an air conditioner into an empty circuit makes it a two-pole 240 V circuit");
        var outlet = session.AddElectricalDevice(level, ElectricalDeviceKind.Outlet, "T", new Point2(0.1, 2));
        session.AssignCircuit(outlet, circuit);
        var mixed = ElectricalAnalysis.Analyze(session.Document);
        check(mixed.Circuit(circuit)!.Status == CircuitStatus.WrongVoltage &&
              !mixed.Energized(session.Document.ElectricalDevices.Single(candidate => candidate.Id == outlet)),
            "A 120 V receptacle on the 240 V circuit is a wrong-voltage fault and the circuit is held dead");
        session.AssignCircuit(outlet, null);
        session.SetCircuitVoltage(circuit, 120);
        check(ElectricalAnalysis.Analyze(session.Document).Circuit(circuit)!.Status == CircuitStatus.WrongVoltage,
            "An air conditioner on a 120 V circuit is a wrong-voltage fault");
        session.SetCircuitVoltage(circuit, 240);

        var mesh = BuildingMesher.Build(session.Document);
        check(mesh.Positions.Any(position => position.Y > 4.15f && position.Z < 0.7f) &&
              mesh.Positions.Any(position => position.Y > 3.6f && position.Y < 3.95f && position.Z > 2f && position.Z < 2.35f),
            "The indoor unit hangs high on the wall and the condenser stands on the ground outside");

        var reloaded = ProjectFile.DeserializeDocument(ProjectFile.SerializeDocument(session.Document));
        var copy = reloaded.ElectricalDevices.Single(candidate => candidate.Id == unit);
        check(copy.CoolingBtu == 12000 && copy.OutdoorPosition == device.OutdoorPosition && reloaded.ElectricalCircuits.Single().Voltage == 240,
            "Air conditioner capacity, condenser position and circuit voltage persist");
    }
}
