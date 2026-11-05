using Guanacaste3D.Core;

/// <summary>Room metadata and boundaries follow separator edits without touching manual rooms.</summary>
static class AutomaticRoomSynchronizationChecks
{
    public static void Run(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Live room boundaries"));
        var level = session.Document.Levels[0].Id;
        session.AddWall(level, new(0, 0), new(8, 0));
        session.AddWall(level, new(8, 0), new(8, 4));
        session.AddWall(level, new(8, 4), new(0, 4));
        session.AddWall(level, new(0, 4), new(0, 0));
        var separator = session.AddStructuralElement(level, StructuralElementKind.RoomSeparator,
            "Room separator", new(4, 0), new(4, 4));
        check(session.AddAutomaticRooms(level) == 2 &&
              session.Document.Rooms.Count(room => room.IsAutomatic) == 2,
            "Detected rooms carry persistent automatic origin metadata");

        var left = session.Document.Rooms.Single(room => room.Center.X < 4);
        var right = session.Document.Rooms.Single(room => room.Center.X > 4);
        session.RenameRoom(left.Id, "Sala principal", "101");
        session.RenameRoom(right.Id, "Comedor", "102");

        var manual = session.AddRoom(level, new(12, 0), new(14, 3), "Terraza manual");
        check(!session.Document.Rooms.Single(room => room.Id == manual).IsAutomatic,
            "Hand-drawn rooms are independent of automatic boundary synchronization");

        // In one action the separator moves and both pre-existing rooms resize.
        session.MoveStructuralElement(separator, 1, 0);
        var movedLeft = session.Document.Rooms.Single(room => room.Id == left.Id);
        var movedRight = session.Document.Rooms.Single(room => room.Id == right.Id);
        check(movedLeft.Name == "Sala principal" && movedLeft.Number == "101" &&
              movedRight.Name == "Comedor" && movedRight.Number == "102" &&
              Math.Abs(movedLeft.Area - 4.9 * 3.8) < 1e-8 &&
              Math.Abs(movedRight.Area - 2.9 * 3.8) < 1e-8,
            "Moving a room separator recalculates room areas and preserves identities and custom labels");
        check(session.Document.Rooms.Single(room => room.Id == manual).Area == 6,
            "Automatic room changes leave manually authored room annotations unchanged");

        session.Undo();
        check(Math.Abs(session.Document.Rooms.Single(room => room.Id == left.Id).Area - 3.9 * 3.8) < 1e-8 &&
              session.Document.StructuralElements.Single(element => element.Id == separator).Start.X == 4,
            "Undo restores room geometry and the separator in one transaction");
        session.Redo();
        check(Math.Abs(session.Document.Rooms.Single(room => room.Id == left.Id).Area - 4.9 * 3.8) < 1e-8,
            "Redo reapplies updated automatic room boundaries");

        // Two rooms merge when the separator disappears; the largest-overlap identity survives.
        session.RemoveStructuralElement(separator);
        var survivors = session.Document.Rooms.Where(room => room.IsAutomatic).ToArray();
        check(survivors.Length == 1 && survivors[0].Id == left.Id &&
              Math.Abs(survivors[0].Area - 7.8 * 3.8) < 1e-8 &&
              session.Document.Walls.Count == 4 &&
              session.Document.Rooms.Any(room => room.Id == manual),
            "Removing a separator merges automatic rooms without deleting any wall or manual room");
        session.Undo();
        check(session.Document.Rooms.Any(room => room.Id == left.Id) &&
              session.Document.Rooms.Any(room => room.Id == right.Id) &&
              session.Document.StructuralElements.Any(element => element.Id == separator),
            "Undo restores separated rooms and their original identities");

        var filename = Path.Combine(Path.GetTempPath(), "automatic-rooms-" + Guid.NewGuid().ToString("N") + ".g3d");
        try
        {
            ProjectFile.Save(filename, session.Document);
            var restored = ProjectFile.Load(filename);
            check(restored.Rooms.Count(room => room.IsAutomatic) == 2 &&
                  restored.Rooms.Any(room => room.Id == manual && !room.IsAutomatic) &&
                  restored.Rooms.Any(room => room.Id == left.Id && room.Name == "Sala principal"),
                "Automatic room provenance and user-authored labels survive native save/reopen");
        }
        finally
        {
            if (File.Exists(filename)) File.Delete(filename);
            if (File.Exists(filename + ".bak")) File.Delete(filename + ".bak");
        }
        check(session.AddAutomaticRooms(level) == 0 &&
              session.Document.Rooms.Count == 3,
            "Re-running room discovery is idempotent after synchronization");

        var legacy = new DocumentSession(BuildingDocument.Create("Legacy manual rooms"));
        legacy.AddRoom(legacy.Document.Levels[0].Id, new(0, 0), new(4, 4), "Existing room");
        check(legacy.Document.Rooms.Single().IsAutomatic == false,
            "Existing projects retain manual-room semantics when auto metadata is absent");
    }
}
