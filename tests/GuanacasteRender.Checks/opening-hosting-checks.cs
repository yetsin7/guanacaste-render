using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>
/// Regression checks for moving doors and windows: host resolution onto the nearest wall, the air
/// case, clamping at wall ends, overlap detection, atomic re-hosting across walls, and the layered
/// door and window geometry produced for the 3D view.
/// </summary>
static class OpeningHostingChecks
{
    /// <summary>Runs every opening-hosting check in sequence.</summary>
    public static void Run(Action<bool, string> check)
    {
        CheckResolution(check);
        CheckMoveTransactions(check);
        CheckOpeningGeometry(check);
    }

    /// <summary>Verifies that candidate centers resolve onto walls, clamp at ends, and detect conflicts.</summary>
    private static void CheckResolution(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Hosting"));
        var levelId = session.Document.Levels[0].Id;
        var northWall = session.AddWall(levelId, new Point2(0, 0), new Point2(8, 0));
        var eastWall = session.AddWall(levelId, new Point2(8, 0), new Point2(8, 6));
        var doorId = session.AddOpening(northWall, OpeningKind.Door, offset: 2.0);
        var door = session.Document.Openings.Single(opening => opening.Id == doorId);

        var beside = OpeningHosting.Resolve(door, new Point2(8.12, 3.0), session.Document.Walls, session.Document.Openings, 0.3);
        check(beside is OpeningHost onEast && onEast.WallId == eastWall && onEast.Fits &&
              Math.Abs(onEast.Offset - 3.0) < 1e-9,
            "A door dragged beside a wall re-hosts onto it at the projected offset");

        var pastEnd = OpeningHosting.Resolve(door, new Point2(8.1, 5.99), session.Document.Walls, session.Document.Openings, 0.3);
        check(pastEnd is OpeningHost clamped && clamped.WallId == eastWall && clamped.Fits &&
              Math.Abs(clamped.Offset - 5.5) < 1e-6,
            "An offset beyond the wall end clamps so the opening keeps its end clearance");

        check(OpeningHosting.Resolve(door, new Point2(4, 3), session.Document.Walls, session.Document.Openings, 0.3) is null,
            "A door dragged into the room has no host and stays in the air");

        session.AddOpening(eastWall, OpeningKind.Window, offset: 1.0);
        var conflict = OpeningHosting.Resolve(door, new Point2(8.1, 1.9), session.Document.Walls, session.Document.Openings, 0.3);
        check(conflict is OpeningHost blocked && blocked.WallId == eastWall && !blocked.Fits,
            "A candidate that overlaps another opening on the target wall is reported as not fitting");

        var shortWall = session.AddWall(levelId, new Point2(0, 10), new Point2(0.5, 10));
        var shortHost = OpeningHosting.Resolve(door, new Point2(0.25, 10.1), session.Document.Walls, session.Document.Openings, 0.3);
        check(shortHost is OpeningHost tooShort && tooShort.WallId == shortWall && !tooShort.Fits,
            "An opening wider than its candidate wall does not fit there");
    }

    /// <summary>Verifies that re-hosting is one validated transaction that rejects conflicts and foreign levels.</summary>
    private static void CheckMoveTransactions(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Move"));
        var levelId = session.Document.Levels[0].Id;
        var northWall = session.AddWall(levelId, new Point2(0, 0), new Point2(8, 0));
        var eastWall = session.AddWall(levelId, new Point2(8, 0), new Point2(8, 6));
        var doorId = session.AddOpening(northWall, OpeningKind.Door, offset: 2.0);
        session.AddOpening(eastWall, OpeningKind.Window, offset: 1.0);

        session.MoveOpening(doorId, eastWall, 3.0);
        var moved = session.Document.Openings.Single(opening => opening.Id == doorId);
        check(moved.WallId == eastWall && Math.Abs(moved.Offset - 3.0) < 1e-12 && moved.Id == doorId,
            "MoveOpening re-hosts an opening onto another wall of its level and keeps its identity");

        var rejected = false;
        try { session.MoveOpening(doorId, eastWall, 1.9); }
        catch (InvalidDataException) { rejected = true; }
        var unchanged = session.Document.Openings.Single(opening => opening.Id == doorId);
        check(rejected && unchanged.WallId == eastWall && Math.Abs(unchanged.Offset - 3.0) < 1e-12,
            "An overlapping re-host is rejected and leaves the opening where it was");

        var upperLevel = session.AddLevel();
        var upperWall = session.AddWall(upperLevel, new Point2(0, 0), new Point2(4, 0));
        var foreignLevel = false;
        try { session.MoveOpening(doorId, upperWall, 2.0); }
        catch (InvalidDataException) { foreignLevel = true; }
        check(foreignLevel && session.Document.Openings.Single(opening => opening.Id == doorId).WallId == eastWall,
            "An opening cannot be re-hosted onto a wall of another level");
    }

    /// <summary>Verifies the layered door and window geometry: leaf thickness, sill projection, and glazing.</summary>
    private static void CheckOpeningGeometry(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Geometry"));
        var levelId = session.Document.Levels[0].Id;
        var wallId = session.AddWall(levelId, new Point2(0, 0), new Point2(8, 0));
        var doorId = session.AddOpening(wallId, OpeningKind.Door, offset: 2.0);
        var windowId = session.AddOpening(wallId, OpeningKind.Window, offset: 5.0);
        var door = session.Document.Openings.Single(opening => opening.Id == doorId);
        var window = session.Document.Openings.Single(opening => opening.Id == windowId);
        var mesh = BuildingMesher.Build(session.Document);
        var halfThickness = session.Document.Walls.Single().Thickness / 2;

        var leafInside = true;
        var leafDepth = 0.0;
        var leafCount = 0;
        var glassInside = true;
        var glassCount = 0;
        var sillProjects = false;
        for (var triangle = 0; triangle < mesh.TriangleCount; triangle++)
        {
            var kind = mesh.TriangleKinds[triangle];
            for (var corner = 0; corner < 3; corner++)
            {
                var point = mesh.Positions[mesh.Indices[triangle * 3 + corner]];
                if (kind == SurfaceKind.DoorLeaf && point.X >= door.Begin - 1e-6 && point.X <= door.End + 1e-6)
                {
                    leafCount++;
                    leafDepth = Math.Max(leafDepth, Math.Abs(point.Y));
                    leafInside &= point.Z >= 0 && point.Z <= door.Height + 1e-6;
                }
                if (kind == SurfaceKind.Glass)
                {
                    glassCount++;
                    glassInside &= point.X >= window.Begin - 1e-6 && point.X <= window.End + 1e-6 &&
                                   Math.Abs(point.Y) <= 0.004;
                }
                if (kind == SurfaceKind.OpeningFrame && Math.Abs(point.Y) > halfThickness + 1e-6)
                    sillProjects = true;
            }
        }
        check(leafCount > 0 && leafInside && leafDepth <= 0.02 + 1e-6,
            "The door leaf stays inside its opening and is no thicker than its type depth");
        check(glassCount > 0 && glassInside, "The window glazing stays inside its opening, within the sash");
        check(sillProjects, "A window sill projects beyond both wall faces");
        check(mesh.Bounds().Max.Z <= session.Document.Walls.Single().Height + 1e-6,
            "The opening assembly stays within the wall height");
    }
}
