using Guanacaste3D.Core;

/// <summary>Checks copy, cut and paste of walls and the model warnings review.</summary>
static class WallClipboardAndWarningChecks
{
    /// <summary>Runs the clipboard and warning scenarios.</summary>
    public static void Run(Action<bool, string> check)
    {
        Clipboard(check);
        Warnings(check);
    }

    private static Wall Wall(DocumentSession session, Guid id) => session.Document.Walls.Single(wall => wall.Id == id);

    private static bool Near(Point2 first, Point2 second) => first.DistanceTo(second) < 1e-9;

    private static bool Throws(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return true; }
        return false;
    }

    /// <summary>Pasted walls are new elements with their openings; another level drops the top constraint.</summary>
    private static void Clipboard(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create());
        var ground = session.Document.Levels[0].Id;
        var upper = session.AddLevel();
        var wall = session.AddWall(ground, new Point2(0, 0), new Point2(4, 0));
        session.AddOpening(wall, OpeningKind.Door, offset: 2);
        session.SetWallTopConstraint(wall, upper, 0);

        var clip = WallClipboard.Capture(session.Document, [wall]);
        var pasted = session.PasteWalls(clip, ground, 0, 2);
        var copy = Wall(session, pasted[0]);
        check(pasted.Count == 1 && pasted[0] != wall && Near(copy.Start, new Point2(0, 2)) && Near(Wall(session, wall).Start, new Point2(0, 0)),
            "Pasting makes a new wall moved by the given distance and leaves the original");
        check(session.Document.Openings.Count(opening => opening.WallId == pasted[0]) == 1 &&
              session.Document.Openings.Select(opening => opening.Id).Distinct().Count() == 2,
            "A pasted wall brings its door with a new identity");
        check(copy.TopLevelId == upper, "Pasting on the same level keeps the top constraint");

        var onUpper = Wall(session, session.PasteWalls(clip, upper, 0, 0)[0]);
        check(onUpper.LevelId == upper && onUpper.TopLevelId is null && Math.Abs(onUpper.Height - clip.Walls[0].Height) < 1e-9,
            "Pasting on another level moves the wall there and keeps its height without a top constraint");

        session.Undo();
        check(session.Document.Walls.Count == 2, "Undo removes a pasted wall with its openings");
        check(Throws(() => session.PasteWalls(clip, Guid.NewGuid(), 0, 0)) &&
              Throws(() => session.PasteWalls(clip, ground, double.NaN, 0)) &&
              Throws(() => WallClipboard.Capture(session.Document, [Guid.NewGuid()])),
            "An unknown level, a bad distance and an empty copy are rejected");

        using (var group = session.BeginGroup("Cut"))
        {
            session.RemoveWall(wall);
            group.Assimilate();
        }
        check(session.Document.Walls.All(candidate => candidate.Id != wall), "A cut wall leaves the project");
        var restored = session.PasteWalls(clip, ground, 0, 0);
        check(Near(Wall(session, restored[0]).Start, new Point2(0, 0)) && session.Document.Openings.Any(opening => opening.WallId == restored[0]),
            "A cut wall can be pasted back where it was, with its door");
    }

    /// <summary>Each warning code is raised for its own situation, and a clean project has none.</summary>
    private static void Warnings(Action<bool, string> check)
    {
        var house = SampleProjects.CreateDemonstrationHouse();
        var houseWarnings = ModelWarnings.Analyze(house).Where(warning => warning.Severity == WarningSeverity.Warning).ToArray();
        check(houseWarnings.Length == 0,
            "The demonstration house has no warnings" + (houseWarnings.Length == 0 ? "" : ": " + string.Join(", ", houseWarnings.Select(warning => warning.Code))));

        var session = new DocumentSession(BuildingDocument.Create());
        var level = session.Document.Levels[0].Id;
        var wall = session.AddWall(level, new Point2(0, 0), new Point2(6, 0));
        session.AddWall(level, new Point2(0, 0), new Point2(6, 0));
        session.AddOpening(wall, OpeningKind.Door, offset: 0.52);
        session.AddWall(level, new Point2(10, 0), new Point2(10.08, 0));
        session.AddWall(level, new Point2(3, 0.05), new Point2(8, 0.05));
        session.AddWall(level, new Point2(0, 5), new Point2(4, 5));
        session.AddWall(level, new Point2(4.2, 5), new Point2(8, 5));
        var codes = ModelWarnings.Analyze(session.Document).Select(warning => warning.Code).ToHashSet();
        check(codes.Contains("DuplicateWalls"), "Two identical walls are reported");
        check(codes.Contains("ShortWall"), "A very short wall is reported");
        check(codes.Contains("OpeningNearWallEnd"), "An opening almost at a wall end is reported");
        check(codes.Contains("OverlappingWalls"), "Walls running over one another are reported");
        check(codes.Contains("WallGap"), "A wall stopping short of another is reported as a gap");

        var farSession = new DocumentSession(BuildingDocument.Create());
        farSession.AddWall(farSession.Document.Levels[0].Id, new Point2(250000, 1000000), new Point2(250005, 1000000));
        var farWarning = ModelWarnings.Analyze(farSession.Document).SingleOrDefault(warning => warning.Code == "FarFromOrigin");
        check(farWarning is not null && farWarning.Value >= 1000000 && !codes.Contains("FarFromOrigin"),
            "Walls a million meters from the origin, as in CRTM05 coordinates, are reported as losing 3D precision");

        var tight = session.Document.Walls.First(candidate => candidate.Length > 5 && candidate.Id != wall);
        session.AddOpening(tight.Id, OpeningKind.Door, offset: 3.5, height: tight.Height - 0.07);
        var rooms = session.Document.Clone();
        rooms.Rooms.Add(new Room(Guid.NewGuid(), level, "1", "A", new Point2(0, 0), new Point2(4, 4)));
        rooms.Rooms.Add(new Room(Guid.NewGuid(), level, " 1 ", "B", new Point2(5, 0), new Point2(9, 4)));
        rooms.Rooms.Add(new Room(Guid.NewGuid(), level, "2", "Closet", new Point2(0, 0), new Point2(0.5, 0.5)));
        rooms.Columns.Add(new Column(Guid.NewGuid(), level, new Point2(1, 1), 0.3, 0.3, 3));
        rooms.Columns.Add(new Column(Guid.NewGuid(), level, new Point2(1.0001, 1), 0.3, 0.3, 3));
        var roomCodes = ModelWarnings.Analyze(rooms).Select(warning => warning.Code).ToHashSet();
        check(roomCodes.Contains("DuplicateRoomNumber") && roomCodes.Contains("TinyRoom") &&
              roomCodes.Contains("DuplicateColumns"),
            "Duplicate room numbers, tiny rooms and stacked columns are reported");

        rooms.Rooms.Add(new Room(Guid.NewGuid(), level, "3", "Overlap", new Point2(3, 0), new Point2(6, 3)));
        var auditCodes = ModelWarnings.Analyze(rooms).Select(warning => warning.Code).ToHashSet();
        check(auditCodes.Contains("OverlappingRooms") && auditCodes.Contains("LowOpeningHeadClearance"),
            "Warnings include the audit's overlapping rooms and low opening head clearance");

        var ordered = ModelWarnings.Analyze(rooms);
        check(ordered.SequenceEqual(ModelWarnings.Analyze(rooms), new WarningComparer()) &&
              ordered.TakeWhile(warning => warning.Severity == WarningSeverity.Warning).Count() ==
              ordered.Count(warning => warning.Severity == WarningSeverity.Warning),
            "The warning list is stable and shows warnings before information");
    }

    private sealed class WarningComparer : IEqualityComparer<ModelWarning>
    {
        public bool Equals(ModelWarning? first, ModelWarning? second) =>
            first?.Code == second?.Code && first!.ElementIds.SequenceEqual(second!.ElementIds);

        public int GetHashCode(ModelWarning warning) => warning.Code.GetHashCode();
    }
}
