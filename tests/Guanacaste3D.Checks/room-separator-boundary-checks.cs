using Guanacaste3D.Core;

/// <summary>Non-solid separators must partition orthogonal enclosed plans without becoming walls.</summary>
static class RoomSeparatorBoundaryChecks
{
    public static void Run(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Room separator partition"));
        var level = session.Document.Levels[0].Id;
        session.AddWall(level, new(0, 0), new(8, 0));
        session.AddWall(level, new(8, 0), new(8, 4));
        session.AddWall(level, new(8, 4), new(0, 4));
        session.AddWall(level, new(0, 4), new(0, 0));

        var before = RoomDetection.Detect(session.Document, level);
        check(before.Count == 1 && Math.Abs((before[0].Max.X - before[0].Min.X) - 7.8) < 1e-8,
            "A closed rectangular wall loop encloses one clear-width room");

        var partition = session.AddStructuralElement(level, StructuralElementKind.RoomSeparator,
            "Room separator", new(4, 0), new(4, 4));
        var after = RoomDetection.Detect(session.Document, level);
        check(after.Count == 2 &&
              after.All(space => Math.Abs((space.Max.X - space.Min.X) - 3.9) < 1e-8) &&
              session.Document.Walls.Count == 4,
            "Room separators partition enclosed rooms while leaving the load-bearing wall set unchanged");
        check(!session.Document.StructuralElements.Single(element => element.Id == partition).HasSolid,
            "Room separators have no 3D solid");

        var created = session.AddAutomaticRooms(level);
        check(created == 2 && session.Document.Rooms.Count == 2,
            "Automatic room placement honors room-separating lines");

        var filename = Path.Combine(Path.GetTempPath(), "separators-" + Guid.NewGuid().ToString("N") + ".g3d");
        try
        {
            ProjectFile.Save(filename, session.Document);
            var reopened = ProjectFile.Load(filename);
            check(RoomDetection.Detect(reopened, level).Count == 2 &&
                  reopened.StructuralElements.Any(element => element.Id == partition),
                "Separator boundaries survive native project save and reopen");
        }
        finally
        {
            if (File.Exists(filename)) File.Delete(filename);
            if (File.Exists(filename + ".bak")) File.Delete(filename + ".bak");
        }

        session.Undo(); // Automatic room creation is one transaction.
        check(session.Document.Rooms.Count == 0 &&
              RoomDetection.Detect(session.Document, level).Count == 2,
            "Room creation is undoable without deleting the separator");
        session.Undo(); // Remove the separator.
        check(RoomDetection.Detect(session.Document, level).Count == 1 &&
              session.Document.Walls.Count == 4,
            "Undoing a separator restores the original spatial partition without deleting walls");
    }
}
