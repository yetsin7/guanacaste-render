using Guanacaste3D.Core;

/// <summary>Regression tests for named references and independently deletable, hosted wall associations.</summary>
static class GridAxisAssociationChecks
{
    public static void Run(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Linked datums"));
        var level = session.Document.Levels[0].Id;
        var firstWall = session.AddWall(level, new(1, 0), new(5, 0));
        var door = session.AddOpening(firstWall, OpeningKind.Door, 2);
        var unbound = session.AddWall(level, new(0, 3), new(4, 3));
        var gridA = session.AddGridAxis("A", new(0, 0), new(10, 0));

        check(session.Document.Walls.Single(w => w.Id == firstWall).GridAxisId == gridA &&
              session.Document.Walls.Single(w => w.Id == unbound).GridAxisId is null,
            "Creating an axis auto-attaches only coincident wall centerlines");
        var secondWall = session.AddWall(level, new(6, 0), new(9, 0));
        check(session.Document.Walls.Single(w => w.Id == secondWall).GridAxisId == gridA,
            "Drawing on a datum centerline attaches the new wall");

        bool conflict = false;
        try { session.AddGridAxis(" a ", new(0, 4), new(8, 4)); }
        catch (DuplicateGridAxisNameException exception) { conflict = exception.ExistingAxisId == gridA; }
        check(conflict && session.Document.Grids.Count == 1,
            "Case-insensitive duplicate axis names reveal the existing datum without mutating the project");

        var gridEight = session.AddGridAxis("8", new(0, 5), new(10, 5));
        conflict = false;
        try { session.UpdateGridAxis(gridEight, "A", new(0, 5), new(10, 5)); }
        catch (DuplicateGridAxisNameException exception) { conflict = exception.ExistingAxisId == gridA; }
        check(conflict && session.Document.Grids.Single(g => g.Id == gridEight).Name == "8",
            "Renaming a numeric axis to an existing letter fails atomically");

        session.MoveGridAxis(gridA, 2, -1);
        check(session.Document.Walls.Single(w => w.Id == firstWall).Start == new Point2(3, -1) &&
              session.Document.Walls.Single(w => w.Id == secondWall).Start == new Point2(8, -1) &&
              session.Document.Walls.Single(w => w.Id == unbound).Start == new Point2(0, 3) &&
              session.Document.Openings.Single(o => o.Id == door).WallId == firstWall,
            "Moving a datum carries its bound walls and hosted openings, not independent walls");
        session.Undo();
        check(session.Document.Walls.Single(w => w.Id == firstWall).Start == new Point2(1, 0) &&
              session.Document.Grids.Single(g => g.Id == gridA).Start == new Point2(0, 0),
            "Grid and attached walls undo in one transaction");
        session.Redo();
        check(session.Document.Walls.Single(w => w.Id == firstWall).Start == new Point2(3, -1),
            "Grid and attached walls redo together");

        var saved = Path.Combine(Path.GetTempPath(), "linked-datums-" + Guid.NewGuid().ToString("N") + ".g3d");
        try
        {
            ProjectFile.Save(saved, session.Document);
            var restored = ProjectFile.Load(saved);
            check(restored.Grids.Count == 2 &&
                  restored.Walls.Single(w => w.Id == firstWall).GridAxisId == gridA &&
                  restored.Openings.Any(o => o.Id == door),
                "Grid bindings survive native project save and reload");
        }
        finally
        {
            if (File.Exists(saved)) File.Delete(saved);
            if (File.Exists(saved + ".bak")) File.Delete(saved + ".bak");
        }

        session.MoveWall(firstWall, 0, 2);
        check(session.Document.Walls.Single(w => w.Id == firstWall).GridAxisId is null,
            "Moving a wall independently detaches its datum instead of dragging the axis");
        var wallPosition = session.Document.Walls.Single(w => w.Id == firstWall).Start;
        session.MoveGridAxis(gridA, 0, 1);
        check(session.Document.Walls.Single(w => w.Id == firstWall).Start == wallPosition,
            "Subsequent axis moves do not move walls that have been detached");
        bool mismatch = false;
        try { session.AttachWallToGridAxis(firstWall, gridEight); }
        catch (InvalidDataException) { mismatch = true; }
        check(mismatch, "A wall cannot attach to a non-collinear datum");

        session.RemoveGridAxis(gridA);
        check(session.Document.Grids.Count == 1 && session.Document.Walls.Count == 3 &&
              session.Document.Walls.All(w => w.GridAxisId is null) && session.Document.Openings.Any(o => o.Id == door),
            "Deleting a datum keeps every wall and hosted opening");
        session.Undo();
        check(session.Document.Grids.Any(g => g.Id == gridA) &&
              session.Document.Walls.Single(w => w.Id == secondWall).GridAxisId == gridA,
            "Undoing datum deletion restores wall associations");
        session.RemoveWall(secondWall);
        check(session.Document.Grids.Any(g => g.Id == gridA),
            "Deleting an attached wall never deletes its datum");

        var rotating = new DocumentSession(BuildingDocument.Create("Rotating datum"));
        var rotatingLevel = rotating.Document.Levels[0].Id;
        var rotatingAxis = rotating.AddGridAxis("B", new(0, 0), new(10, 0));
        var rotatingWall = rotating.AddWall(rotatingLevel, new(1, 0), new(4, 0));
        rotating.UpdateGridAxis(rotatingAxis, "B", new(0, 0), new(0, 10));
        check(rotating.Document.Walls.Single(w => w.Id == rotatingWall).Start.DistanceTo(new(0, 1)) < 1e-8 &&
              rotating.Document.Walls.Single(w => w.Id == rotatingWall).End.DistanceTo(new(0, 4)) < 1e-8,
            "Rotating an axis rigidly rotates its centered wall without changing the wall length");
        rotating.Document.Validate();

        // Copies and manual rotations are explicit detach operations, including when a new
        // coincident datum is added later (legacy geometric inference must not reattach them).
        var editing = new DocumentSession(BuildingDocument.Create("Detached wall edits"));
        var editingLevel = editing.Document.Levels[0].Id;
        var editingAxis = editing.AddGridAxis("C", new(0, 0), new(10, 0));
        var original = editing.AddWall(editingLevel, new(1, 0), new(7, 0));
        var copy = editing.DuplicateWall(original, 0, 2);
        check(editing.Document.Walls.Single(w => w.Id == copy) is
                { GridAxisId: null, GridAxisAssociationDisabled: true },
            "Duplicated walls explicitly opt out of their source datum");
        editing.AddGridAxis("D", new(0, 2), new(10, 2));
        check(editing.Document.Walls.Single(w => w.Id == copy).GridAxisId is null,
            "A newly created coincident axis cannot silently reattach a copied wall");

        editing.RotateWall(original, 15);
        var rotatedWall = editing.Document.Walls.Single(w => w.Id == original);
        check(rotatedWall.GridAxisId is null && rotatedWall.GridAxisAssociationDisabled,
            "Manual wall rotation explicitly releases the datum");
        var rotatedStart = rotatedWall.Start;
        var rotatedEnd = rotatedWall.End;
        editing.AddGridAxis("E", rotatedStart, rotatedEnd);
        check(editing.Document.Walls.Single(w => w.Id == original).GridAxisId is null,
            "Creating a coincident axis cannot reattach a manually rotated wall");
        editing.MoveGridAxis(editingAxis, 0, 1);
        check(editing.Document.Walls.Single(w => w.Id == original).Start == rotatedStart &&
              editing.Document.Walls.Single(w => w.Id == original).End == rotatedEnd,
            "Moving the former datum leaves a manually rotated wall unchanged");
        editing.Undo(); // MoveGridAxis
        editing.Undo(); // AddGridAxis E
        editing.Undo(); // RotateWall
        check(editing.Document.Walls.Single(w => w.Id == original).GridAxisId == editingAxis,
            "Undoing a rotation restores its previous datum constraint");
        editing.RotateWall(original, 0);
        check(editing.Document.Walls.Single(w => w.Id == original).GridAxisId == editingAxis,
            "A zero-degree rotation preserves the existing datum constraint");
    }
}
