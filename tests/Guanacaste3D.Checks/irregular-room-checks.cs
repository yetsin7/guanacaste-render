using Guanacaste3D.Core;

/// <summary>Nonrectangular automatic rooms must have true net areas, not bounding-box areas.</summary>
static class IrregularRoomChecks
{
    public static void Run(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Irregular room geometries"));
        var level = session.Document.Levels[0].Id;
        var corners = new[]
        {
            new Point2(0, 0), new Point2(8, 0), new Point2(8, 4),
            new Point2(4, 4), new Point2(4, 8), new Point2(0, 8)
        };
        for (var i = 0; i < corners.Length; i++)
            session.AddWall(level, corners[i], corners[(i + 1) % corners.Length]);

        var regions = RoomPolygonDetection.Detect(session.Document, level);
        check(regions.Count == 1 && regions[0].Boundary.Count == 6 &&
              Math.Abs(regions[0].Area - 44.84) < 1e-8,
            "A closed L-shaped enclosure yields one concave 6-vertex region and exact face-to-face area");

        check(session.AddAutomaticRooms(level) == 1, "Automatic room creation supports an L-shaped outline");
        var room = session.Document.Rooms.Single();
        check(room.IsAutomatic && room.Boundary is { Count: 6 } &&
              Math.Abs(room.Area - 44.84) < 1e-8 && Math.Abs(room.Area - 64) > 1,
            "Stored room geometry measures the real concave footprint, not its 8x8 bounding rectangle");
        check(RoomRegionGeometry.Contains(room.Boundary!, room.Center),
            "An irregular room's label anchor remains inside its actual footprint");

        var floorType = ArchitecturalLibrary.SlabPresets.First(preset => preset.Kind == FloorKind.Floor);
        var floorId = session.AddSlabOverRoom(room.Id, floorType);
        var footprintFloor = session.Document.Floors.Single(floor => floor.Id == floorId);
        check(footprintFloor.Boundary.Count == 6 &&
              Math.Abs(footprintFloor.CalculateArea() - 44.84) < 1e-8,
            "A slab created from an L-shaped room follows the actual footprint rather than filling its bounding box");
        var expandedId = session.AddSlabOverRoom(room.Id, floorType, 0.4);
        var expandedFloor = session.Document.Floors.Single(floor => floor.Id == expandedId);
        check(expandedFloor.Boundary.Count == 6 &&
              expandedFloor.CalculateArea() > footprintFloor.CalculateArea() &&
              RoomRegionGeometry.IsValid(expandedFloor.Boundary),
            "A roof or slab overhang expands an L-shaped perimeter without filling its recessed corner");


        var copy = session.Document.Clone();
        copy.Rooms[0].Boundary![0] = new Point2(20, 20);
        check(session.Document.Rooms[0].Boundary![0] != new Point2(20, 20),
            "Deep project cloning isolates mutable room boundary vertices");
        var invalid = session.Document.Clone();
        invalid.Rooms[0] = invalid.Rooms[0] with
        {
            Boundary = [new Point2(0, 0), new Point2(4, 4), new Point2(0, 4), new Point2(4, 0)]
        };
        var invalidRejected = false;
        try { invalid.Validate(); }
        catch (InvalidDataException) { invalidRejected = true; }
        check(invalidRejected, "Self-intersecting room boundaries fail project validation");

        var separator = session.AddStructuralElement(level, StructuralElementKind.RoomSeparator,
            "División de habitación", new(0, 5), new(4, 5));
        var divided = session.Document.Rooms.Where(item => item.IsAutomatic).ToArray();
        check(divided.Length == 2 &&
              divided.Sum(item => item.Area) > 0 &&
              Math.Abs(divided.Sum(item => item.Area) - 44.84) < 1e-7 &&
              session.Document.Walls.Count == 6,
            "A zero-thickness separator splits a concave space without creating extra wall mass");
        var larger = divided.OrderByDescending(item => item.Area).First();
        session.RenameRoom(larger.Id, "Galería principal", "L-01");
        var savedId = larger.Id;

        session.MoveStructuralElement(separator, 0, -0.50);
        check(session.Document.Rooms.Count(item => item.IsAutomatic) == 2 &&
              session.Document.Rooms.Any(item => item.Id == savedId &&
                  item.Name == "Galería principal" && item.Number == "L-01"),
            "Moving an irregular partition retains the most-overlapping room identity and custom label");
        session.Undo();
        check(session.Document.StructuralElements.Single(element => element.Id == separator).Start.Y == 5,
            "Undo restores the room separator and its geometry in one step");

        session.RemoveStructuralElement(separator);
        check(session.Document.Rooms.Count(item => item.IsAutomatic) == 1 &&
              Math.Abs(session.Document.Rooms.Single().Area - 44.84) < 1e-8 &&
              session.Document.Walls.Count == 6,
            "Deleting a separator restores one L-shaped space while retaining the six surrounding walls");
        session.Undo();
        check(session.Document.Rooms.Count(item => item.IsAutomatic) == 2,
            "Undo restores both rooms and their polygonal boundaries");

        var filename = Path.Combine(Path.GetTempPath(), "concave-rooms-" + Guid.NewGuid().ToString("N") + ".g3d");
        try
        {
            ProjectFile.Save(filename, session.Document);
            var loaded = ProjectFile.Load(filename);
            check(loaded.Rooms.Count == 2 &&
                  loaded.Rooms.All(item => item.Boundary is { Count: >= 4 }) &&
                  Math.Abs(loaded.Rooms.Sum(item => item.Area) - 44.84) < 1e-7,
                "Native .g3d round trip persists all partitioned polygon outlines and net areas");
        }
        finally
        {
            if (File.Exists(filename)) File.Delete(filename);
            if (File.Exists(filename + ".bak")) File.Delete(filename + ".bak");
        }
        check(session.AddAutomaticRooms(level) == 0, "Repeated polygon discovery does not create duplicate rooms");

        var open = new DocumentSession(BuildingDocument.Create("Open L"));
        var openLevel = open.Document.Levels[0].Id;
        for (var i = 0; i < corners.Length - 1; i++)
            open.AddWall(openLevel, corners[i], corners[i + 1]);
        check(RoomPolygonDetection.Detect(open.Document, openLevel).Count == 0,
            "A gap in an irregular perimeter prevents accidental room creation");
    }
}
