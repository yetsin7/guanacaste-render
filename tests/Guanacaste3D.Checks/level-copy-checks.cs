using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Checks repeatable storey copying and the elevator geometry used by multi-storey studies.</summary>
static class LevelCopyChecks
{
    /// <summary>Exercises copied hosted objects, circuit links, wall constraints, undo and persistence.</summary>
    public static void Run(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Level copy"));
        var ground = session.Document.Levels.OrderBy(level => level.Elevation).First().Id;
        var upper = session.Document.Levels.OrderBy(level => level.Elevation).Last().Id;
        var top = session.AddLevel();
        session.UpdateLevel(top, "Upper level", 7);
        var wall = session.AddWall(ground, new(0, 0), new(6, 0));
        session.AddOpening(wall, OpeningKind.Door, 2, width: 0.9, height: 2.1);
        session.SetWallTopConstraint(wall, upper, 0.2);
        session.AddRoom(ground, new(0, 0), new(5, 4), "Room");
        session.AddFloor(ground, [new(0, 0), new(6, 0), new(6, 4), new(0, 4)]);
        var panel = session.AddElectricalDevice(ground, ElectricalDeviceKind.Panel, "Panel", new(1, 1));
        var circuit = session.AddCircuit(panel, "Lighting", 15);
        var light = session.AddElectricalDevice(ground, ElectricalDeviceKind.CeilingFixture, "Light", new(2, 2));
        var lightSwitch = session.AddElectricalDevice(ground, ElectricalDeviceKind.Switch, "Switch", new(1, 2));
        session.AssignCircuit(light, circuit);
        session.ToggleSwitchLink(lightSwitch, light);
        var count = session.CopyLevelContent(ground, [upper, top, top]);
        var copiedUpperWall = session.Document.Walls.Single(item => item.LevelId == upper);
        var copiedTopWall = session.Document.Walls.Single(item => item.LevelId == top);
        check(count > 0 && session.Document.Walls.Count == 3 && session.Document.Openings.Count == 3 &&
              copiedUpperWall.TopLevelId == top && Math.Abs(copiedUpperWall.Height - 4.2) < 1e-9 &&
              copiedTopWall.TopLevelId is null && copiedTopWall.TopOffset == 0 && Math.Abs(copiedTopWall.Height - 3.2) < 1e-9,
            "Copying storeys preserves hosted openings and regenerates or releases wall top constraints");
        var copiedPanel = session.Document.ElectricalDevices.Single(item => item.LevelId == upper && item.Kind == ElectricalDeviceKind.Panel);
        var copiedLight = session.Document.ElectricalDevices.Single(item => item.LevelId == upper && item.Kind == ElectricalDeviceKind.CeilingFixture);
        var copiedSwitch = session.Document.ElectricalDevices.Single(item => item.LevelId == upper && item.Kind == ElectricalDeviceKind.Switch);
        check(copiedLight.CircuitId is Guid copiedCircuit && copiedCircuit != circuit &&
              session.Document.ElectricalCircuits.Single(item => item.Id == copiedCircuit).PanelId == copiedPanel.Id &&
              copiedSwitch.Controls.SequenceEqual([copiedLight.Id]),
            "Copied panels, circuits, lights and switches point to the copied storey");
        check(session.Document.Rooms.Select(item => item.Number).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3,
            "Copied rooms receive distinct numbers");
        session.Undo();
        check(session.Document.Walls.Count == 1 && session.Document.ElectricalCircuits.Count == 1,
            "One undo removes the entire multi-level copy");
        session.Redo();
        check(session.Document.Walls.Count == 3 && session.Document.ElectricalCircuits.Count == 3,
            "Redo restores the entire multi-level copy");
        var path = Path.Combine(Path.GetTempPath(), $"level-copy-{Guid.NewGuid():N}.g3d");
        try
        {
            ProjectFile.Save(path, session.Document);
            var reopened = ProjectFile.Load(path);
            check(reopened.Walls.Count == 3 && reopened.Openings.Count == 3 && reopened.ElectricalCircuits.Count == 3,
                "Copied storeys survive native project persistence");
        }
        finally { File.Delete(path); }
        Elevator(check);
        Tower(check);
    }

    /// <summary>Verifies an elevator serves the intended levels and creates a continuous hoistway.</summary>
    private static void Elevator(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Elevator"));
        var ground = session.Document.Levels.OrderBy(level => level.Elevation).First().Id;
        var upper = session.Document.Levels.OrderBy(level => level.Elevation).Last().Id;
        var top = session.AddLevel();
        foreach (var level in new[] { ground, upper, top })
            session.AddFloor(level, [new(0, 0), new(8, 0), new(8, 8), new(0, 8)]);
        var id = session.AddElevator(ground, top, new(2, 2), new(4, 4), "Elevator");
        var elevator = session.Document.StructuralElements.Single(item => item.Id == id);
        var parts = ElevatorGeometry.Parts(elevator).ToArray();
        check(ElevatorGeometry.Landings(elevator) == 3 && Math.Abs(ElevatorGeometry.Travel(elevator) - 6) < 1e-9 &&
              parts.Any(part => Math.Abs(part.Bottom - 6) < 1e-9 && part.Top > 8),
            "Elevator doors reach all three uniformly spaced landings");
        var withElevator = BuildingMesher.Build(session.Document);
        var withoutElevator = session.Document.Clone();
        withoutElevator.StructuralElements.RemoveAll(item => item.Id == id);
        check(withElevator.TriangleCount != BuildingMesher.Build(withoutElevator).TriangleCount,
            "The elevator participates in coordinated building geometry");
    }

    /// <summary>Checks that the generated tower retains architecture and fitted services on every occupied storey.</summary>
    private static void Tower(Action<bool, string> check)
    {
        var building = TenStoreyBuilding.Create();
        var levels = building.Levels.OrderBy(level => level.Elevation).ToArray();
        var occupied = levels.Take(TenStoreyBuilding.Storeys).ToArray();
        check(levels.Length == 11 && occupied.All(level => building.Rooms.Any(room => room.LevelId == level.Id) &&
              building.ElectricalDevices.Any(device => device.LevelId == level.Id)) &&
              building.Floors.Any(floor => floor.LevelId == levels[^1].Id && floor.Kind == FloorKind.Roof),
            "The ten-storey study includes rooms and electrical devices on each occupied level and a roof");
        check(building.StructuralElements.Count(element => element.Kind == StructuralElementKind.Elevator) == 1 &&
              building.StructuralElements.Count(element => element.Kind == StructuralElementKind.Stair) == 18 &&
              building.Sheets.Count == 5 && building.PlumbingSystems.Count > 0,
            "The tower study includes two stair runs per storey transition, an elevator, plumbing and drawing sheets");
        var path = Path.Combine(Path.GetTempPath(), $"tower-{Guid.NewGuid():N}.g3d");
        try
        {
            ProjectFile.Save(path, building);
            var reopened = ProjectFile.Load(path);
            check(reopened.Levels.Count == levels.Length && reopened.ElectricalDevices.Count == building.ElectricalDevices.Count &&
                  reopened.StructuralElements.Count == building.StructuralElements.Count && reopened.Sheets.Count == 5,
                "The full tower study and its services survive native project persistence");
        }
        finally { File.Delete(path); }
    }
}
