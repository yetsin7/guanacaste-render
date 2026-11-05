using Guanacaste3D.Core;

/// <summary>Checks the wall editing commands: mirror, offset, array, split, trim, align, pin and match.</summary>
static class WallEditChecks
{
    /// <summary>Runs every wall editing scenario.</summary>
    public static void Run(Action<bool, string> check)
    {
        Mirror(check);
        Offset(check);
        Arrays(check);
        Split(check);
        Trim(check);
        Align(check);
        Pin(check);
        PinnedNeighbours(check);
        Match(check);
        Flip(check);
    }

    private static (DocumentSession Session, Guid Level) NewSession()
    {
        var session = new DocumentSession(BuildingDocument.Create());
        return (session, session.Document.Levels[0].Id);
    }

    private static Wall Wall(DocumentSession session, Guid id) => session.Document.Walls.Single(wall => wall.Id == id);

    private static bool Near(double first, double second) => Math.Abs(first - second) < 1e-9;

    private static bool Near(Point2 first, Point2 second) => first.DistanceTo(second) < 1e-9;

    private static bool Throws(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return true; }
        return false;
    }

    /// <summary>A mirrored wall keeps its faces' meaning and its door swings the other way.</summary>
    private static void Mirror(Action<bool, string> check)
    {
        var (session, level) = NewSession();
        var wall = session.AddWall(level, new Point2(1, 1), new Point2(5, 1), locationReference: WallLocationReference.ExteriorFace);
        var door = session.AddOpening(wall, OpeningKind.Door, offset: 1);
        var created = session.MirrorWalls([wall], new Point2(0, 0), new Point2(1, 0));

        var mirrored = Wall(session, created[0]);
        var mirroredDoor = session.Document.Openings.Single(opening => opening.WallId == created[0]);
        check(Near(mirrored.Start, new Point2(5, -1)) && Near(mirrored.End, new Point2(1, -1)) &&
              Near(Wall(session, wall).Start, new Point2(1, 1)),
            "Mirroring about the x axis reflects a wall and leaves the original in place");
        check(Near(mirroredDoor.Offset, 3) && mirroredDoor.FlipHand &&
              session.Document.Openings.Single(opening => opening.Id == door).Offset == 1,
            "A mirrored door keeps its place along the wall and its hinge flips");
        check(mirrored.LocationReference == WallLocationReference.ExteriorFace,
            "A mirrored wall keeps the face it was placed by");

        var inPlace = session.MirrorWalls([wall], new Point2(0, 0), new Point2(1, 0), copy: false);
        check(inPlace[0] == wall && Near(Wall(session, wall).Start, new Point2(5, -1)) &&
              session.Document.Openings.Single(opening => opening.Id == door).FlipHand,
            "Mirroring without copying moves the wall and keeps its identity");
        session.Undo();
        check(Near(Wall(session, wall).Start, new Point2(1, 1)), "Undo puts a mirrored wall back");
        check(Throws(() => session.MirrorWalls([wall], new Point2(0, 0), new Point2(0, 0.01))),
            "A mirror axis that is nearly a point is rejected");
    }

    /// <summary>An offset copy sits to the left; moving a wall sideways stretches the walls it meets.</summary>
    private static void Offset(Action<bool, string> check)
    {
        var (session, level) = NewSession();
        var first = session.AddWall(level, new Point2(0, 0), new Point2(4, 0));
        var second = session.AddWall(level, new Point2(4, 0), new Point2(4, 3));

        var copy = session.OffsetWalls([first], 1, copy: true);
        check(Near(Wall(session, copy[0]).Start, new Point2(0, 1)) && Near(Wall(session, first).Start, new Point2(0, 0)),
            "A positive offset copies a wall to its left");

        session.OffsetWalls([first], 1, copy: false);
        check(Near(Wall(session, first).Start, new Point2(0, 1)) && Near(Wall(session, first).End, new Point2(4, 1)) &&
              Near(Wall(session, second).Start, new Point2(4, 1)) && Near(Wall(session, second).End, new Point2(4, 3)),
            "Moving a wall sideways stretches the wall it meets in a corner");
        session.Undo();
        check(Near(Wall(session, second).Start, new Point2(4, 0)), "Undo restores the stretched corner");
        check(Throws(() => session.OffsetWalls([first], 0, copy: true)), "A zero offset is rejected");
    }

    /// <summary>Linear and radial arrays copy walls with their openings.</summary>
    private static void Arrays(Action<bool, string> check)
    {
        var (session, level) = NewSession();
        var wall = session.AddWall(level, new Point2(0, 0), new Point2(4, 0));
        session.AddOpening(wall, OpeningKind.Door, offset: 2);

        var line = session.ArrayWallsLinear([wall], 4, 0, 2);
        check(line.Count == 3 && Near(Wall(session, line[2]).Start, new Point2(0, 6)) &&
              session.Document.Openings.Count == 4,
            "A linear array of four makes three copies with their doors");
        var radial = session.ArrayWallsRadial([wall], 4, new Point2(0, 0), 90);
        check(radial.Count == 3 && Near(Wall(session, radial[0]).End, new Point2(0, 4)) &&
              Near(Wall(session, radial[1]).End, new Point2(-4, 0)),
            "A radial array turns each copy by the step angle about the center");
        check(Throws(() => session.ArrayWallsLinear([wall], 1, 1, 0)) &&
              Throws(() => session.ArrayWallsLinear([wall], 2, 0, 0)) &&
              Throws(() => session.ArrayWallsLinear([wall], DocumentSession.MaximumArrayCount + 1, 1, 0)),
            "Arrays reject a single member, no spacing and absurd counts");
        var before = session.Document.Walls.Count;
        session.Undo();
        check(session.Document.Walls.Count == before - 3, "Undo removes a whole array in one step");
    }

    /// <summary>Splitting divides the openings between the parts and refuses to cut through one.</summary>
    private static void Split(Action<bool, string> check)
    {
        var (session, level) = NewSession();
        var wall = session.AddWall(level, new Point2(0, 0), new Point2(10, 0));
        var door = session.AddOpening(wall, OpeningKind.Door, offset: 2);
        var window = session.AddOpening(wall, OpeningKind.Window, offset: 8);

        var second = session.Document.Walls.Count;
        var secondId = session.SplitWall(wall, new Point2(5, 0.4));
        check(Near(Wall(session, wall).End, new Point2(5, 0)) && Near(Wall(session, secondId).Start, new Point2(5, 0)) &&
              Near(Wall(session, secondId).End, new Point2(10, 0)),
            "Splitting a wall cuts its centerline at the point nearest to the pick");
        check(session.Document.Openings.Single(opening => opening.Id == door).WallId == wall &&
              session.Document.Openings.Single(opening => opening.Id == window).WallId == secondId &&
              Near(session.Document.Openings.Single(opening => opening.Id == window).Offset, 3),
            "Each opening goes with the part that holds it");
        session.Undo();
        check(session.Document.Walls.Count == second && Near(Wall(session, wall).End, new Point2(10, 0)),
            "Undo joins the two parts again");

        var crossing = session.AddOpening(wall, OpeningKind.Window, offset: 5);
        check(Throws(() => session.SplitWall(wall, new Point2(5, 0))), "A wall cannot be split through an opening");
        check(Throws(() => session.SplitWall(wall, new Point2(0.01, 0))), "A split next to an end is rejected");
        session.RemoveOpening(crossing);
    }

    /// <summary>Trim to corner keeps the picked parts and moves the other ends to the crossing.</summary>
    private static void Trim(Action<bool, string> check)
    {
        var (session, level) = NewSession();
        var first = session.AddWall(level, new Point2(0, 0), new Point2(3, 0));
        var second = session.AddWall(level, new Point2(5, -3), new Point2(5, 2));

        session.TrimToCorner(first, new Point2(1, 0), second, new Point2(5, 1));
        check(Near(Wall(session, first).End, new Point2(5, 0)) && Near(Wall(session, first).Start, new Point2(0, 0)),
            "Trim extends the wall whose picked part is short of the corner");
        check(Near(Wall(session, second).Start, new Point2(5, 0)) && Near(Wall(session, second).End, new Point2(5, 2)),
            "Trim cuts back the wall to the corner and keeps the picked part");
        var parallel = session.AddWall(level, new Point2(0, 5), new Point2(4, 5));
        var other = session.AddWall(level, new Point2(0, 6), new Point2(4, 6));
        check(Throws(() => session.TrimToCorner(parallel, new Point2(1, 5), other, new Point2(1, 6))),
            "Parallel walls cannot be trimmed to a corner");
    }

    /// <summary>Align moves a wall until its face meets the reference face, and the joined walls stretch.</summary>
    private static void Align(Action<bool, string> check)
    {
        var (session, level) = NewSession();
        var reference = session.AddWall(level, new Point2(0, 0), new Point2(6, 0));
        var moving = session.AddWall(level, new Point2(0, 3), new Point2(6, 3), wallTypeId: BuiltInWallTypes.Exterior300Id);
        var neighbour = session.AddWall(level, new Point2(6, 3), new Point2(6, 6));

        session.AlignWall(moving, -1, reference, 1);
        check(Near(Wall(session, moving).Start.Y, 0.25) && Near(Wall(session, moving).End.Y, 0.25),
            "Aligning faces puts the right face of one wall on the left face of the other");
        check(Near(Wall(session, neighbour).Start, new Point2(6, 0.25)),
            "A wall joined to the aligned wall stretches to stay joined");
        session.Undo();
        check(Near(Wall(session, moving).Start.Y, 3), "Undo returns the aligned wall");
        check(Throws(() => session.AlignWall(neighbour, 0, reference, 0)), "Only parallel walls can be aligned");
        check(Throws(() => session.AlignWall(moving, 2, reference, 0)), "An invalid face is rejected");
    }

    /// <summary>A pinned wall resists moving, rotating, deleting and trimming until it is released.</summary>
    private static void Pin(Action<bool, string> check)
    {
        var (session, level) = NewSession();
        var wall = session.AddWall(level, new Point2(0, 0), new Point2(4, 0));
        session.SetWallsPinned([wall], true);

        check(Throws(() => session.MoveWall(wall, 1, 1)) && Throws(() => session.RotateWall(wall, 30)) &&
              Throws(() => session.RemoveWall(wall)) && Throws(() => session.SplitWall(wall, new Point2(2, 0))) &&
              Throws(() => session.MirrorWalls([wall], new Point2(0, 5), new Point2(1, 5), copy: false)),
            "A pinned wall cannot be moved, rotated, deleted, split or mirrored in place");
        check(session.MirrorWalls([wall], new Point2(0, 5), new Point2(1, 5)).Count == 1 &&
              !session.Document.Walls.Single(candidate => candidate.Id != wall).Pinned,
            "A pinned wall can still be copied, and the copy is not pinned");
        var duplicate = session.DuplicateWall(wall);
        check(!Wall(session, duplicate).Pinned, "A duplicated wall is not pinned");

        var path = Path.Combine(Path.GetTempPath(), $"g3d-pin-{Guid.NewGuid():N}.g3d");
        try
        {
            ProjectFile.Save(path, session.Document);
            check(ProjectFile.Load(path).Walls.Single(candidate => candidate.Id == wall).Pinned,
                "A pinned wall stays pinned after saving and reopening");
        }
        finally
        {
            File.Delete(path);
        }

        session.SetWallsPinned([wall], false);
        session.MoveWall(wall, 1, 1);
        check(Near(Wall(session, wall).Start, new Point2(1, 1)), "An unpinned wall moves again");
    }

    /// <summary>Editing an adjacent wall must never stretch a pinned corner or tee stem.</summary>
    private static void PinnedNeighbours(Action<bool, string> check)
    {
        var (session, level) = NewSession();
        var moving = session.AddWall(level, new Point2(0, 0), new Point2(4, 0));
        var pinned = session.AddWall(level, new Point2(4, 0), new Point2(4, 3));
        session.SetWallsPinned([pinned], true);

        session.OffsetWalls([moving], 1, copy: false);
        check(Near(Wall(session, pinned).Start, new Point2(4, 0)) &&
              Near(Wall(session, pinned).End, new Point2(4, 3)),
            "Offsetting a joined wall leaves the pinned neighbour's endpoints untouched");
        session.Undo();
        check(Near(Wall(session, moving).End, new Point2(4, 0)) &&
              Near(Wall(session, pinned).Start, new Point2(4, 0)),
            "Undo restores the original corner without moving the pinned neighbour");

        var (teeSession, teeLevel) = NewSession();
        var host = teeSession.AddWall(teeLevel, new Point2(0, 0), new Point2(4, 0));
        var stem = teeSession.AddWall(teeLevel, new Point2(2, 0), new Point2(2, 3));
        teeSession.SetWallsPinned([stem], true);

        teeSession.OffsetWalls([host], 1, copy: false);
        check(Near(Wall(teeSession, stem).Start, new Point2(2, 0)) &&
              Near(Wall(teeSession, stem).End, new Point2(2, 3)),
            "Moving a tee host does not drag the pinned stem");
    }

    /// <summary>Flipping swaps a wall's faces but leaves every door where it was and swinging the same way.</summary>
    private static void Flip(Action<bool, string> check)
    {
        var (session, level) = NewSession();
        var wall = session.AddWall(level, new Point2(0, 0), new Point2(4, 0), locationReference: WallLocationReference.ExteriorFace);
        var door = session.AddOpening(wall, OpeningKind.Door, offset: 1);
        session.SetWallJoins([wall], WallJoinSetting.Butt, WallEnd.End);

        session.FlipWalls([wall]);
        var flipped = Wall(session, wall);
        var flippedDoor = session.Document.Openings.Single(opening => opening.Id == door);
        check(Near(flipped.Start, new Point2(4, 0)) && Near(flipped.End, new Point2(0, 0)) &&
              flipped.LocationReference == WallLocationReference.InteriorFace,
            "Flipping a wall swaps its ends and its exterior and interior placement faces");
        check(flipped.StartJoin == WallJoinSetting.Butt && flipped.EndJoin == WallJoinSetting.Miter,
            "The join settings travel with their physical ends");
        check(Near(flipped.Start.X - flippedDoor.Offset, 1) && flippedDoor.FlipHand && flippedDoor.FlipFacing,
            "A door keeps its place in the model, its hinge and the side it swings to");
        session.FlipWalls([wall]);
        check(Near(Wall(session, wall).Start, new Point2(0, 0)) && !session.Document.Openings.Single(opening => opening.Id == door).FlipHand &&
              Near(session.Document.Openings.Single(opening => opening.Id == door).Offset, 1),
            "Flipping twice restores the wall and its door");
        session.Undo();
        check(Near(Wall(session, wall).Start, new Point2(4, 0)), "Undo takes back one flip");
        session.SetWallsPinned([wall], true);
        check(Throws(() => session.FlipWalls([wall])), "A pinned wall cannot be flipped");
    }

    /// <summary>Matching gives walls the source's type and settings but leaves their place alone.</summary>
    private static void Match(Action<bool, string> check)
    {
        var (session, level) = NewSession();
        var source = session.AddWall(level, new Point2(0, 0), new Point2(4, 0), height: 4, wallTypeId: BuiltInWallTypes.Exterior300Id);
        var target = session.AddWall(level, new Point2(0, 10), new Point2(4, 10), locationReference: WallLocationReference.InteriorFace);

        session.MatchWallProperties(source, [target, source]);
        var matched = Wall(session, target);
        check(matched.WallTypeId == BuiltInWallTypes.Exterior300Id && Near(matched.Thickness, 0.30) && Near(matched.Height, 4),
            "Matching copies the type, thickness and height of the source wall");
        check(Near(matched.Start.Y + 0.15, 10.10) && Near(matched.End.X, 4),
            "A matched wall keeps its length and the face it was placed by");
        session.Undo();
        check(Wall(session, target).WallTypeId == BuiltInWallTypes.Basic200Id, "Undo restores the matched wall");
    }
}
