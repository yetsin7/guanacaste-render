using Guanacaste3D.Core;

/// <summary>Checks tee joins, butt joins, square-off and disallowed ends, and their undo and persistence.</summary>
static class WallJoinStyleChecks
{
    /// <summary>Runs the join-style scenarios.</summary>
    public static void Run(Action<bool, string> check)
    {
        TeeJoins(check);
        ButtJoins(check);
        FlatAndDisallowedEnds(check);
        SessionAndPersistence(check);
    }

    private static Wall Make(Guid level, Point2 start, Point2 end, WallJoinSetting startJoin = default,
        WallJoinSetting endJoin = default) =>
        new(Guid.NewGuid(), level, start, end, 0.2, 3) { StartJoin = startJoin, EndJoin = endJoin };

    /// <summary>A free end resting on another wall is cut against that wall's face; the host is untouched.</summary>
    private static void TeeJoins(Action<bool, string> check)
    {
        var level = Guid.NewGuid();
        var host = Make(level, new Point2(0, 0), new Point2(4, 0));
        var stem = Make(level, new Point2(2, 0), new Point2(2, 3));
        var index = WallJoinIndex.Build([host, stem]);
        var stemFootprint = index.Footprint(stem);
        check(Near(stemFootprint.StartLeft, new Point2(1.9, 0.1)) && Near(stemFootprint.StartRight, new Point2(2.1, 0.1)),
            "A wall resting on another ends at the face of the wall it meets, not at its centerline");
        check(index.TeeHostAt(stem.Id, WallEnd.Start)?.Id == host.Id && index.Footprint(host).EndLeft == new Point2(4, 0.1),
            "The wall the tee rests on keeps its own square ends");

        var angled = Make(level, new Point2(2, 0), new Point2(3, 2));
        var angledFootprint = WallJoinIndex.Build([host, angled]).Footprint(angled);
        check(Near(angledFootprint.StartLeft.Y, 0.1) && Near(angledFootprint.StartRight.Y, 0.1),
            "An angled tee ends along the face of the wall it meets");

        var crossing = Make(level, new Point2(2, -2), new Point2(2, 3));
        check(WallJoinIndex.Build([host, crossing]).TeeHostAt(crossing.Id, WallEnd.Start) is null,
            "A wall that merely crosses another is not a tee");
        var parallel = Make(level, new Point2(1, 0), new Point2(3, 0));
        check(WallJoinIndex.Build([host, parallel]).TeeHostAt(parallel.Id, WallEnd.Start) is null,
            "A collinear wall is not a tee");

        var otherLevel = stem with { Id = Guid.NewGuid(), LevelId = Guid.NewGuid() };
        check(WallJoinIndex.Build([host, otherLevel]).TeeHostAt(otherLevel.Id, WallEnd.Start) is null,
            "Walls on different levels never form a tee");
    }

    /// <summary>A butt corner stops one wall against the face of the other, which runs on to cover the corner.</summary>
    private static void ButtJoins(Action<bool, string> check)
    {
        var level = Guid.NewGuid();
        var east = Make(level, new Point2(0, 0), new Point2(4, 0), endJoin: WallJoinSetting.Butt);
        var north = Make(level, new Point2(4, 0), new Point2(4, 3));
        var index = WallJoinIndex.Build([east, north]);
        var abutting = index.Footprint(east);
        var through = index.Footprint(north);
        check(Near(abutting.EndLeft, new Point2(3.9, 0.1)) && Near(abutting.EndRight, new Point2(3.9, -0.1)),
            "A butting wall stops at the near face of the wall it meets");
        check(Near(through.StartLeft, new Point2(3.9, -0.1)) && Near(through.StartRight, new Point2(4.1, -0.1)),
            "The other wall runs on to the outer face and covers the corner");

        var reversed = WallJoinIndex.Build([east with { EndJoin = WallJoinSetting.Miter },
            north with { StartJoin = WallJoinSetting.Butt }]);
        check(Near(reversed.Footprint(north).StartLeft, new Point2(3.9, 0.1)) &&
              Near(reversed.Footprint(east).EndRight, new Point2(4.1, -0.1)),
            "Butting the other wall swaps which one runs through");

        var both = WallJoinIndex.Build([east, north with { StartJoin = WallJoinSetting.Butt }]);
        check(Near(both.Footprint(east).EndLeft, new Point2(3.9, 0.1)) && Near(both.Footprint(east).EndRight, new Point2(4.1, -0.1)),
            "When both ends ask to butt the corner stays mitered");
    }

    /// <summary>Square-off and disallowed ends keep flat ends, and disallowed ends refuse tees too.</summary>
    private static void FlatAndDisallowedEnds(Action<bool, string> check)
    {
        var level = Guid.NewGuid();
        var east = Make(level, new Point2(0, 0), new Point2(4, 0));
        var north = Make(level, new Point2(4, 0), new Point2(4, 3));
        foreach (var setting in new[] { WallJoinSetting.SquareOff, WallJoinSetting.Disallowed })
        {
            var index = WallJoinIndex.Build([east with { EndJoin = setting }, north]);
            check(Near(index.Footprint(east).EndLeft, new Point2(4, 0.1)) && Near(index.Footprint(north).StartLeft, new Point2(3.9, 0)),
                $"A {setting} end leaves the corner flat for both walls");
        }

        var host = Make(level, new Point2(0, 0), new Point2(4, 0));
        var stem = Make(level, new Point2(2, 0), new Point2(2, 3), startJoin: WallJoinSetting.Disallowed);
        var teeIndex = WallJoinIndex.Build([host, stem]);
        check(teeIndex.TeeHostAt(stem.Id, WallEnd.Start) is null && Near(teeIndex.Footprint(stem).StartLeft, new Point2(1.9, 0)),
            "A disallowed end does not cut itself against a wall it rests on");
    }

    /// <summary>Settings change in one undoable step, follow tees through thickness changes, and survive saving.</summary>
    private static void SessionAndPersistence(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create());
        var level = session.Document.Levels[0].Id;
        var east = session.AddWall(level, new Point2(0, 0), new Point2(4, 0));
        var north = session.AddWall(level, new Point2(4, 0), new Point2(4, 3));

        session.SetWallJoins([north], WallJoinSetting.Butt, WallEnd.Start);
        check(Wall(session, north).StartJoin == WallJoinSetting.Butt && Wall(session, north).EndJoin == WallJoinSetting.Miter,
            "A join setting can be changed for one end of a wall");
        session.SetWallJoins([east], WallJoinSetting.Butt, WallEnd.End);
        check(Wall(session, east).EndJoin == WallJoinSetting.Butt && Wall(session, north).StartJoin == WallJoinSetting.Miter,
            "Making one wall butt makes its neighbour run through");
        session.Undo();
        check(Wall(session, east).EndJoin == WallJoinSetting.Miter && Wall(session, north).StartJoin == WallJoinSetting.Butt,
            "Undo restores both ends of the corner as they were");

        session.SetWallJoins([east, north], WallJoinSetting.Disallowed);
        check(Wall(session, east).StartJoin == WallJoinSetting.Disallowed && Wall(session, north).EndJoin == WallJoinSetting.Disallowed,
            "Without an end both ends of every listed wall change");

        var rejected = false;
        try { session.SetWallJoins([Guid.NewGuid()], WallJoinSetting.Butt); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "An unknown wall is rejected without changing the project");

        var path = Path.Combine(Path.GetTempPath(), $"g3d-joins-{Guid.NewGuid():N}.g3d");
        try
        {
            ProjectFile.Save(path, session.Document);
            var loaded = ProjectFile.Load(path).Walls.Single(wall => wall.Id == east);
            check(loaded.StartJoin == WallJoinSetting.Disallowed && loaded.EndJoin == WallJoinSetting.Disallowed,
                "Join settings survive saving and reopening the project");
        }
        finally
        {
            File.Delete(path);
        }

        var tees = new DocumentSession(BuildingDocument.Create());
        var tlevel = tees.Document.Levels[0].Id;
        var host = tees.AddWall(tlevel, new Point2(0, 0), new Point2(4, 0), locationReference: WallLocationReference.ExteriorFace);
        var stem = tees.AddWall(tlevel, new Point2(2, 0), new Point2(2, 3));
        tees.UpdateWall(host, 0.30, 3);
        var movedStem = Wall(tees, stem);
        check(Near(movedStem.Start, new Point2(2, 0.05)) &&
              WallJoinIndex.Build(tees.Document.Walls).TeeHostAt(stem, WallEnd.Start)?.Id == host,
            "A tee stays on the wall it rests on when that wall changes thickness");
    }

    private static Wall Wall(DocumentSession session, Guid id) => session.Document.Walls.Single(wall => wall.Id == id);

    private static bool Near(Point2 first, Point2 second) => first.DistanceTo(second) < 1e-9;

    private static bool Near(double first, double second) => Math.Abs(first - second) < 1e-9;
}
