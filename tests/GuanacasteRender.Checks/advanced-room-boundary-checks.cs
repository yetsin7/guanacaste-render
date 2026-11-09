using Guanacaste3D.Core;

/// <summary>Regression tests for arbitrary-angle room walls, nested courtyards and live wall edits.</summary>
static class AdvancedRoomBoundaryChecks
{
    public static void Run(Action<bool, string> check)
    {
        DiagonalWalls(check);
        CourtyardHole(check);
        CurvedCourtyard(check);
        WallByFaceAndRoof(check);
    }

    /// <summary>
    /// Production workflow: draw an enclosure, auto-detect a room, build secondary walls
    /// from that room face, then place a real pitched roof using the same room GUID.
    /// Adding nested walls must not convert an occupied room into an unscheduled courtyard.
    /// </summary>
    private static void WallByFaceAndRoof(Action<bool, string> check)
    {
        var session = new DocumentSession(ArchitecturalLibrary.CreateProject("Room-to-roof workflow"));
        var level = session.Document.Levels[0].Id;
        var corners = new[] { new Point2(0, 0), new Point2(8, 0),
            new Point2(8, 6), new Point2(0, 6) };
        session.AddWallsAroundBoundary(level, corners, BuiltInWallTypes.Basic200Id);
        check(session.AddAutomaticRooms(level) == 1, "Four walls form an automatic room");
        var initial = session.Document.Rooms.Single();
        session.RenameRoom(initial.Id, "Sala", "R-101");
        var wallsBefore = session.Document.Walls.Count;
        var face = RoomRegionGeometry.Outline(session.Document.Rooms.Single());
        check(session.AddWallsAroundBoundary(level, face, BuiltInWallTypes.Basic200Id) == 4 &&
              session.Document.Walls.Count == wallsBefore + 4,
            "Wall by face creates four real editable walls");
        var retained = session.Document.Rooms.FirstOrDefault(room => room.Id == initial.Id);
        check(retained is not null && retained.Name == "Sala" && retained.Number == "R-101" &&
              retained.Area > 20,
            "Nested walls preserve the selected occupied room, its GUID, number, name and valid area");
        var roofPreset = ArchitecturalLibrary.SlabPresets.Single(preset => preset.Name == "Hip Roof 25°");
        var roofId = session.AddSlabOverRoom(initial.Id, roofPreset, 0.6);
        var roof = session.Document.Floors.Single(floor => floor.Id == roofId);
        check(roof.Kind == FloorKind.Roof && roof.Shape == RoofShape.Hip &&
              roof.CalculateArea() > retained.Area,
            "Roof by face creates a real pitched roof with overhang around the retained room");
        session.Undo();
        check(session.Document.Floors.All(floor => floor.Id != roofId) &&
              session.Document.Rooms.Any(room => room.Id == initial.Id),
            "Undo roof by face does not delete the source room or nested walls");
        session.Redo();
        var path = Path.Combine(Path.GetTempPath(), "g3d-room-roof-" + Guid.NewGuid().ToString("N") + ".g3d");
        try
        {
            ProjectFile.Save(path, session.Document);
            var opened = ProjectFile.Load(path);
            check(opened.Floors.Any(floor => floor.Id == roofId && floor.Kind == FloorKind.Roof) &&
                  opened.Rooms.Any(room => room.Id == initial.Id && room.Number == "R-101"),
                "Room by face and pitched roof survive native save and reload");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
        }
    }

    private static void DiagonalWalls(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Diagonal enclosure"));
        var level = session.Document.Levels[0].Id;
        var vertices = new[] { new Point2(0, 0), new Point2(8, 0),
            new Point2(6, 4), new Point2(0, 4) };
        var ids = new List<Guid>();
        for (var i = 0; i < vertices.Length; i++)
            ids.Add(session.AddWall(level, vertices[i], vertices[(i + 1) % vertices.Length]));
        var detected = RoomPolygonDetection.Detect(session.Document, level);
        check(detected.Count == 1 && detected[0].Boundary.Count >= 4 &&
              detected[0].Area < 28 && detected[0].Area > 23,
            "A closed trapezoid with a diagonal wall creates one valid net-area room");

        check(session.AddAutomaticRooms(level) == 1, "Automatic rooms include non-orthogonal walls");
        var room = session.Document.Rooms.Single();
        var id = room.Id;
        var originalArea = room.Area;
        session.RenameRoom(id, "Sala diagonal", "D-01");
        session.MoveWall(ids[1], -0.5, 0);
        check(session.Document.Rooms.Count == 1 &&
              session.Document.Rooms[0].Id == id &&
              session.Document.Rooms[0].Name == "Sala diagonal" &&
              session.Document.Rooms[0].Number == "D-01" &&
              Math.Abs(session.Document.Rooms[0].Area - originalArea) > 0.05,
            "Moving a diagonal wall and its attached corners refreshes area and preserves room identity");
        session.Undo();
        check(Math.Abs(session.Document.Rooms.Single().Area - originalArea) < 1e-7,
            "One undo restores both diagonal wall geometry and room area");

        session.UpdateWall(ids[1], 0.4, 3);
        check(session.Document.Rooms.Single().Area < originalArea - 0.1,
            "Increasing the thickness of a diagonal wall recalculates net room area");
        session.Undo();
        check(Math.Abs(session.Document.Rooms.Single().Area - originalArea) < 1e-7,
            "Undoing thickness changes restores the original room measurement");

        session.RemoveWall(ids[1]);
        check(session.Document.Rooms.Count == 0 && session.Document.Walls.Count == 3,
            "Deleting a boundary wall leaves an unclosed outline and removes only automatic rooms");
        session.Undo();
        check(session.Document.Rooms.Single().Id == id && session.Document.Walls.Count == 4,
            "Undoing wall deletion restores the original room GUID and all four walls");
    }

    private static void CourtyardHole(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Courtyard enclosed by walls"));
        var level = session.Document.Levels[0].Id;
        var exterior = new[] { new Point2(0, 0), new Point2(10, 0),
            new Point2(10, 10), new Point2(0, 10) };
        var interior = new[] { new Point2(3, 3), new Point2(7, 3),
            new Point2(7, 7), new Point2(3, 7) };
        foreach (var ring in new[] { exterior, interior })
            for (var i = 0; i < ring.Length; i++)
                session.AddWall(level, ring[i], ring[(i + 1) % ring.Length]);
        var regions = RoomPolygonDetection.Detect(session.Document, level);
        var usable = regions.Single(region => !region.IsCourtyard);
        check(usable.Holes.Count == 1 && regions.Count == 2 &&
              Math.Abs(usable.Area - (9.8 * 9.8 - 4.2 * 4.2)) < 1e-7,
            "Nested exterior and courtyard wall loops produce an annular face with net area");

        check(session.AddAutomaticRooms(level) == 1, "Courtyard is not auto-scheduled as an interior room");
        var room = session.Document.Rooms.Single();
        var id = room.Id;
        check(room.Holes is { Count: 1 } &&
              Math.Abs(room.Area - 78.4) < 1e-7 &&
              RoomRegionGeometry.Contains(room, room.Center) &&
              !RoomRegionGeometry.Contains(room, new Point2(5, 5)),
            "Courtyard area is excluded, label lies inside occupied space, center of patio is not selectable");

        var filename = Path.Combine(Path.GetTempPath(), "courtyard-" + Guid.NewGuid().ToString("N") + ".g3d");
        try
        {
            ProjectFile.Save(filename, session.Document);
            var reopened = ProjectFile.Load(filename);
            check(reopened.Rooms.Single().Holes is { Count: 1 } &&
                  Math.Abs(reopened.Rooms.Single().Area - 78.4) < 1e-7,
                "Courtyard rings and net area survive project save/reopen");
            var independent = reopened.Clone();
            independent.Rooms[0].Holes![0][0] = new Point2(-20, -20);
            check(session.Document.Rooms[0].Holes![0][0] != new Point2(-20, -20),
                "Room hole lists are deep-copied and safe in undo snapshots");
        }
        finally
        {
            if (File.Exists(filename)) File.Delete(filename);
            if (File.Exists(filename + ".bak")) File.Delete(filename + ".bak");
        }
        var missing = session.Document.Walls.First(wall =>
            wall.Start == interior[0] && wall.End == interior[1]);
        session.RemoveWall(missing.Id);
        check(session.Document.Rooms.Count == 1 &&
              session.Document.Rooms[0].Id == id &&
              session.Document.Rooms[0].Holes is { Count: 0 } &&
              Math.Abs(session.Document.Rooms[0].Area - 96.04) < 1e-7,
            "Opening courtyard loop restores the enclosed outer area in the same undoable edit");
        session.Undo();
        check(session.Document.Rooms.Single().Holes is { Count: 1 } &&
              Math.Abs(session.Document.Rooms.Single().Area - 78.4) < 1e-7,
            "Undo restores the patio hole and prior net area without recreating walls");
    }

    private static void CurvedCourtyard(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Round courtyard"));
        var level = session.Document.Levels[0].Id;
        var ring = new[] { new Point2(0, 0), new Point2(10, 0),
            new Point2(10, 10), new Point2(0, 10) };
        for (var i = 0; i < ring.Length; i++)
            session.AddWall(level, ring[i], ring[(i + 1) % ring.Length]);
        check(session.AddAutomaticRooms(level) == 1, "First add the exterior room");
        var id = session.Document.Rooms.Single().Id;
        session.RenameRoom(id, "Sala alrededor del patio", "P-01");
        var circleId = session.AddCurvedRoomSeparator(level, new(5, 5), 1.5, 0, 360);
        var room = session.Document.Rooms.Single();
        check(room.Id == id && room.Holes is { Count: 1 } &&
              Math.Abs(room.Area - (96.04 - Math.PI * 1.5 * 1.5)) < 0.025 &&
              !RoomRegionGeometry.Contains(room, new Point2(5, 5)),
            "A complete circular separator creates a round courtyard hole with accurate tessellated area");
        check(room.Name == "Sala alrededor del patio" && room.Number == "P-01" &&
              session.Document.Rooms.Count == 1 && session.SynchronizeAutomaticRooms(level) == 0,
            "Adding a nested courtyard preserves the scheduled room name, number and GUID without creating an extra room");

        session.UpdateCurvedRoomSeparator(circleId, new(5, 5), 2, 0, 360);
        check(session.Document.Rooms.Single().Id == id &&
              Math.Abs(session.Document.Rooms.Single().Area - (96.04 - Math.PI * 4)) < 0.05,
            "Editing a circular separator's radius recalculates the enclosing room atomically");
        session.Undo();
        check(Math.Abs(session.Document.Rooms.Single().Area - room.Area) < 1e-7 &&
              Math.Abs(session.Document.CurvedRoomSeparators.Single().Radius - 1.5) < 1e-7,
            "Undo restores round room area and arc radius in one transaction");

        var filename = Path.Combine(Path.GetTempPath(), "curved-room-" + Guid.NewGuid().ToString("N") + ".g3d");
        try
        {
            ProjectFile.Save(filename, session.Document);
            var reopened = ProjectFile.Load(filename);
            check(reopened.CurvedRoomSeparators.Count == 1 &&
                  reopened.Rooms.Single().Holes is { Count: 1 },
                "Curved room separators are persisted and automatically detected after reopening a g3d");
        }
        finally
        {
            if (File.Exists(filename)) File.Delete(filename);
            if (File.Exists(filename + ".bak")) File.Delete(filename + ".bak");
        }
        session.RemoveCurvedRoomSeparator(circleId);
        check(session.Document.Rooms.Single().Holes is { Count: 0 } &&
              session.Document.Walls.Count == 4,
            "Removing a non-solid arc clears the hole without deleting architectural walls");
    }
}
