using Guanacaste3D.Core;

/// <summary>
/// Regression checks for constrained wall dimensions: a face-area request moves only
/// the free dimension, explicit edits respect locks, and a rejected edit leaves the
/// document untouched.
/// </summary>
static class WallDimensionChecks
{
    /// <summary>Runs every wall-dimension check in sequence.</summary>
    public static void Run(Action<bool, string> check)
    {
        CheckLockedAreaSolving(check);
        CheckLockedEditsAreRejected(check);
        CheckSessionEditIsAtomic(check);
        CheckFinishAreas(check);
    }

    /// <summary>Verifies finish area takeoff: both faces, openings subtracted, and undoable finish assignment.</summary>
    private static void CheckFinishAreas(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Finishes"));
        var wallId = session.AddWall(session.Document.Levels[0].Id, new Point2(0, 0), new Point2(4, 0));
        session.AddOpening(wallId, OpeningKind.Window, offset: 2.0, width: 1.0, sill: 1.0, height: 1.0);
        var wall = session.Document.Walls.Single();
        var openings = session.Document.Openings;
        check(Math.Abs(FinishRules.WallFinishArea(wall, openings) - 2 * (4 * 3 - 1.0)) < 1e-9,
            "Wall finish area covers both faces and subtracts window openings");

        session.SetWallFinish(wallId, FinishKind.Tile);
        check(session.Document.Walls.Single().Finish == FinishKind.Tile, "A finish can be assigned to a wall");
        session.Undo();
        check(session.Document.Walls.Single().Finish == FinishKind.None, "Finish assignment is undoable");

        var floorId = session.AddFloor(session.Document.Levels[0].Id,
            [new Point2(0, 0), new Point2(3, 0), new Point2(3, 2), new Point2(0, 2)]);
        session.SetFloorFinish(floorId, FinishKind.ExposedConcrete);
        var floor = session.Document.Floors.Single(candidate => candidate.Id == floorId);
        check(floor.Finish == FinishKind.ExposedConcrete &&
              Math.Abs(FinishRules.FloorFinishArea(floor) - 6) < 1e-9,
            "Floor finish area equals the walking surface of the slab");
    }

    /// <summary>Verifies that the area is reached by changing only the unlocked dimension.</summary>
    private static void CheckLockedAreaSolving(Action<bool, string> check)
    {
        var wall = new Wall(Guid.NewGuid(), Guid.NewGuid(), new Point2(0, 0), new Point2(5, 0), 0.2, 3);
        var heightFree = WallDimensionSolver.Resolve(wall, new WallDimensionEdit(
            FaceArea: 12, Locks: new WallDimensionLocks(Length: true)));
        check(Math.Abs(heightFree.Length - 5) < 1e-9 && Math.Abs(heightFree.Height - 2.4) < 1e-9,
            "Area with length locked changes only the height");

        var lengthFree = WallDimensionSolver.Resolve(wall, new WallDimensionEdit(
            FaceArea: 20, Locks: new WallDimensionLocks(Height: true)));
        check(Math.Abs(lengthFree.Length - 20.0 / 3) < 1e-9 && Math.Abs(lengthFree.Height - 3) < 1e-9 &&
              Math.Abs(lengthFree.End.X - 20.0 / 3) < 1e-9,
            "Area with height locked moves the end point along the wall direction");

        var explicitHeight = WallDimensionSolver.Resolve(wall, new WallDimensionEdit(
            Height: 2.5, Locks: new WallDimensionLocks(Length: true)));
        check(Math.Abs(explicitHeight.Height - 2.5) < 1e-9 && Math.Abs(explicitHeight.Length - 5) < 1e-9,
            "An explicit height keeps the locked length");

        var thickness = WallDimensionSolver.Resolve(wall, new WallDimensionEdit(
            Thickness: 0.3, FaceArea: 12, Locks: new WallDimensionLocks(Length: true)));
        check(Math.Abs(thickness.Thickness - 0.3) < 1e-9 && Math.Abs(thickness.Height - 2.4) < 1e-9,
            "Thickness and area can be edited together without disturbing each other");
    }

    /// <summary>Verifies that contradictory or locked requests fail with a message instead of guessing.</summary>
    private static void CheckLockedEditsAreRejected(Action<bool, string> check)
    {
        var wall = new Wall(Guid.NewGuid(), Guid.NewGuid(), new Point2(0, 0), new Point2(5, 0), 0.2, 3);
        check(Rejects(() => WallDimensionSolver.Resolve(wall, new WallDimensionEdit(
                FaceArea: 12, Locks: new WallDimensionLocks(Length: true, Height: true)))),
            "Area cannot change when length and height are both locked");
        check(Rejects(() => WallDimensionSolver.Resolve(wall, new WallDimensionEdit(
                Length: 6, Locks: new WallDimensionLocks(Length: true)))),
            "An explicit change to a locked length is rejected");
        check(Rejects(() => WallDimensionSolver.Resolve(wall, new WallDimensionEdit(
                FaceArea: -4))),
            "A non-positive area is rejected");
        check(Rejects(() => WallDimensionSolver.Resolve(wall, new WallDimensionEdit(
                Thickness: 0.3, Locks: new WallDimensionLocks(Thickness: true)))),
            "An explicit change to a locked thickness is rejected");
    }

    /// <summary>Verifies that a length change which pushes a door outside its wall is rolled back whole.</summary>
    private static void CheckSessionEditIsAtomic(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Wall dimensions"));
        var wallId = session.AddWall(session.Document.Levels[0].Id, new Point2(0, 0), new Point2(8, 0));
        session.AddOpening(wallId, OpeningKind.Door, offset: 2.0);
        var before = session.Document.Walls.Single();

        session.ApplyWallDimensions(wallId, new WallDimensionEdit(FaceArea: 12, Locks: new WallDimensionLocks(Height: true)));
        var applied = session.Document.Walls.Single();
        check(Math.Abs(applied.Length - 4) < 1e-9 && applied.Height == before.Height,
            "Session applies a locked area edit as one transaction");

        var rejected = Rejects(() => session.ApplyWallDimensions(wallId,
            new WallDimensionEdit(Length: 1.5, Locks: new WallDimensionLocks(Height: true))));
        check(rejected && session.Document.Walls.Single() == applied,
            "A length that would strand a door leaves the wall unchanged");
        session.Undo();
        check(session.Document.Walls.Single() == before, "Undo restores the wall before the area edit");
    }

    /// <summary>Returns true when the action throws the validation exception a user should see.</summary>
    private static bool Rejects(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return true; }
        return false;
    }
}
