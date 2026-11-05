using Guanacaste3D.Core;

static class GridAxisChecks
{
    public static void Run(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Ejes asociativos"));
        var level = session.Document.Levels[0].Id;
        var id = session.AddGridAxis("A", new Point2(0, 0), new Point2(10, 0));
        var other = session.AddGridAxis("8", new Point2(0, 3), new Point2(10, 3));
        var rejected = false;
        try { session.AddGridAxis(" a ", new Point2(0, 4), new Point2(10, 4)); }
        catch (DuplicateGridAxisNameException ex) { rejected = ex.ExistingAxisId == id; }
        check(rejected && session.Document.Grids.Count == 2, "Duplicate alphabetic axis labels are rejected with existing axis ID");

        rejected = false;
        try { session.UpdateGridAxis(id, " 8 ", new Point2(0, 0), new Point2(10, 0)); }
        catch (DuplicateGridAxisNameException ex) { rejected = ex.ExistingAxisId == other; }
        check(rejected && session.Document.Grids.Single(x => x.Id == id).Name == "A",
            "Duplicate numerical labels cannot overwrite existing axes");

        // Files can be imported or edited outside DocumentSession. They must obey the
        // same normalized-name uniqueness rule as the interactive grid-axis editor.
        var imported = BuildingDocument.Create("Imported axes");
        imported.Grids.Add(new GridAxis(Guid.NewGuid(), "A", new(0, 0), new(10, 0)));
        imported.Grids.Add(new GridAxis(Guid.NewGuid(), " a ", new(0, 4), new(10, 4)));
        rejected = false;
        try { imported.Validate(); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "Imported axes cannot duplicate names by case or surrounding whitespace");
        imported.Grids.RemoveAt(1);
        imported.Validate();
        check(imported.Grids.Count == 1, "Valid existing grid axes remain compatible with document validation");

        // Imported/native files must not bypass the editor's wall-to-datum collinearity
        // check: otherwise dragging a grid could unexpectedly move an unrelated wall.
        var importedLinks = new DocumentSession(BuildingDocument.Create("Imported wall links"));
        var importedLevel = importedLinks.Document.Levels[0].Id;
        var importedDatum = importedLinks.AddGridAxis("Z", new(0, 0), new(10, 0));
        var offAxisId = importedLinks.AddWall(importedLevel, new(1, 3), new(4, 3));
        var corrupted = importedLinks.Document.Clone();
        var wallIndex = corrupted.Walls.FindIndex(w => w.Id == offAxisId);
        corrupted.Walls[wallIndex] = corrupted.Walls[wallIndex] with { GridAxisId = importedDatum };
        rejected = false;
        try { corrupted.Validate(); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "Imported walls cannot reference a non-collinear grid axis");

        // A datum is an infinite reference line, even if its visible segment is short.
        // A valid existing link should survive a later shortening of that segment.
        var beyondSegmentId = importedLinks.AddWall(importedLevel, new(20, 0), new(25, 0));
        var shortened = importedLinks.Document.Clone();
        var beyondIndex = shortened.Walls.FindIndex(w => w.Id == beyondSegmentId);
        shortened.Walls[beyondIndex] = shortened.Walls[beyondIndex] with { GridAxisId = importedDatum };
        shortened.Validate();
        check(shortened.Walls[beyondIndex].GridAxisId == importedDatum,
            "Collinear wall associations survive shortening the visible datum segment");

        var duplicateIds = importedLinks.Document.Clone();
        duplicateIds.Grids.Add(duplicateIds.Grids[0] with { Name = "Y" });
        rejected = false;
        try { duplicateIds.Validate(); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "Imported duplicate datum IDs fail document validation predictably");

        var wallId = session.AddWall(level, new Point2(1, 0), new Point2(7, 0));
        var crossing = session.AddWall(level, new Point2(4, -2), new Point2(4, 2));
        session.AddOpening(wallId, OpeningKind.Door, offset: 2);
        session.MoveGridAxis(id, 0, 1.5);
        var shifted = session.Document.Walls.Single(x => x.Id == wallId);
        check(shifted.Start == new Point2(1, 1.5) && shifted.End == new Point2(7, 1.5),
            "Moving axis translates wall aligned to its centerline");
        check(session.Document.Walls.Single(x => x.Id == crossing).Start == new Point2(4, -2),
            "Crossing walls are not inadvertently translated");
        check(session.Document.Openings.Single().WallId == wallId, "Hosted opening remains attached after axis translation");
        session.Undo();
        check(session.Document.Walls.Single(x => x.Id == wallId).Start == new Point2(1, 0) &&
              session.Document.Grids.Single(x => x.Id == id).Start == new Point2(0, 0),
            "Undo atomically restores grid and hosted wall coordinates");
        session.Redo();
        check(session.Document.Walls.Single(x => x.Id == wallId).Start == new Point2(1, 1.5),
            "Redo replays associative axis move");
        check(session.Document.Walls.Single(x => x.Id == wallId).GridAxisId == id,
            "Axis association persists after moving the grid");

        // Removing the datum must NEVER remove the wall or its hosted door.
        session.RemoveGridAxis(id);
        check(!session.Document.Grids.Any(x => x.Id == id) &&
              session.Document.Walls.Any(x => x.Id == wallId && x.GridAxisId == null) &&
              session.Document.Openings.Any(x => x.WallId == wallId),
            "Deleting an axis releases wall association without deleting wall or doors");
        session.Undo();
        check(session.Document.Grids.Any(x => x.Id == id) &&
              session.Document.Walls.Single(x => x.Id == wallId).GridAxisId == id,
            "Undo axis deletion restores its association");

        // Removing the wall may delete its HOSTED door by the ordinary wall ownership rule,
        // but must never delete the independent datum.
        session.RemoveWall(wallId);
        check(session.Document.Grids.Any(x => x.Id == id) &&
              !session.Document.Walls.Any(x => x.Id == wallId),
            "Deleting a wall preserves its grid axis");
        session.Undo();
        check(session.Document.Walls.Any(x => x.Id == wallId) &&
              session.Document.Grids.Any(x => x.Id == id),
            "Undo wall deletion restores wall and leaves axis intact");

        var unlinkedWall = session.AddWall(level, new Point2(1, 3), new Point2(7, 3));
        check(session.Document.Walls.Single(x => x.Id == unlinkedWall).GridAxisId == other,
            "A new centerline-aligned wall automatically binds to its axis");
        session.SetWallGridAxis(unlinkedWall, null);
        check(session.Document.Walls.Single(x => x.Id == unlinkedWall).GridAxisId is null &&
              session.Document.Walls.Single(x => x.Id == unlinkedWall).GridAxisAssociationDisabled,
            "A wall can explicitly release its datum association");
        var beforeUnlinkedMove = session.Document.Walls.Single(x => x.Id == unlinkedWall).Start;
        session.MoveGridAxis(other, 0, -0.5);
        check(session.Document.Walls.Single(x => x.Id == unlinkedWall).Start == beforeUnlinkedMove,
            "Detached wall stays fixed when its former grid is moved");
        session.Undo();
        session.SetWallGridAxis(unlinkedWall, other);
        check(session.Document.Walls.Single(x => x.Id == unlinkedWall).GridAxisId == other,
            "A wall can rebind to an existing aligned datum");
        session.MoveWall(unlinkedWall, 0, 1);
        check(session.Document.Walls.Single(x => x.Id == unlinkedWall).GridAxisId is null &&
              session.Document.Grids.Any(x => x.Id == other),
            "Moving an individual wall detaches it from the axis without moving the axis");

        // A copied wall retains its geometry and hosted openings, not its source's datum constraint.
        var editSession = new DocumentSession(BuildingDocument.Create("Ejes al duplicar y girar"));
        var editLevel = editSession.Document.Levels[0].Id;
        var editAxis = editSession.AddGridAxis("C", new Point2(0, 0), new Point2(10, 0));
        var sourceWall = editSession.AddWall(editLevel, new Point2(1, 0), new Point2(7, 0));
        var copiedWall = editSession.DuplicateWall(sourceWall, 0, 2);
        check(editSession.Document.Walls.Single(w => w.Id == copiedWall) is
                { GridAxisId: null, GridAxisAssociationDisabled: true },
            "Duplicated walls never inherit an unrelated grid-axis constraint");
        editSession.MoveGridAxis(editAxis, 0, .5);
        check(editSession.Document.Walls.Single(w => w.Id == sourceWall).Start.Y == .5 &&
              editSession.Document.Walls.Single(w => w.Id == copiedWall).Start.Y == 2,
            "Moving an axis affects its source wall but not the detached copy");
        editSession.Undo();

        editSession.RotateWall(sourceWall, 15);
        var rotated = editSession.Document.Walls.Single(w => w.Id == sourceWall);
        check(rotated.GridAxisId is null && rotated.GridAxisAssociationDisabled,
            "Rotating a wall releases its stale grid-axis association");
        var rotatedStart = rotated.Start;
        var rotatedEnd = rotated.End;
        editSession.MoveGridAxis(editAxis, 0, 1);
        check(editSession.Document.Walls.Single(w => w.Id == sourceWall).Start == rotatedStart &&
              editSession.Document.Walls.Single(w => w.Id == sourceWall).End == rotatedEnd,
            "Moving the former axis cannot displace a manually rotated wall");
        editSession.Undo();
        editSession.Undo();
        check(editSession.Document.Walls.Single(w => w.Id == sourceWall).GridAxisId == editAxis,
            "Undoing rotation restores the original axis constraint");
        editSession.RotateWall(sourceWall, 0);
        check(editSession.Document.Walls.Single(w => w.Id == sourceWall).GridAxisId == editAxis,
            "A zero-degree rotation preserves a valid axis constraint");
    }
}
