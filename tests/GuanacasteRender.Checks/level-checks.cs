using System.Text.Json;
using System.Text.Json.Nodes;
using Guanacaste3D.Core;

/// <summary>
/// Regression checks for parametric levels and wall top constraints (schema 7):
/// constraint-driven heights, level moves with atomic rejection, offsets,
/// protected level deletion, view renaming, schedules, formulas, and migration.
/// </summary>
static class LevelChecks
{
    /// <summary>Runs every level check in sequence.</summary>
    public static void Run(Action<bool, string> check)
    {
        CheckConstraints(check);
        CheckLevelEditing(check);
        CheckSchedulesAndPersistence(check);
    }

    /// <summary>Creates a two-level session with two walls on the ground level.</summary>
    private static (DocumentSession Session, Guid Ground, Guid Upper, Guid WallA, Guid WallB) TwoStoreys()
    {
        var session = new DocumentSession(BuildingDocument.Create("Levels"));
        var ground = session.Document.Levels[0].Id;
        var upper = session.Document.Levels.Single(level => level.Role == LevelRole.BaseRoof).Id;
        var wallA = session.AddWall(ground, new Point2(0, 0), new Point2(6, 0), height: 2.5);
        var wallB = session.AddWall(ground, new Point2(6, 0), new Point2(6, 4), height: 2.5);
        return (session, ground, upper, wallA, wallB);
    }

    /// <summary>Verifies constraint heights, offsets, height edits, and release.</summary>
    private static void CheckConstraints(Action<bool, string> check)
    {
        var (session, ground, upper, wallA, wallB) = TwoStoreys();
        var attached = session.AttachWallsToLevelAbove(ground);
        check(attached == 2 && session.Document.Walls.All(wall => wall.TopLevelId == upper && wall.Height == 3) &&
              session.UndoName == "Transaction.AttachWallsToLevelAbove",
            "Attaching walls to the level above derives their height in one transaction");
        session.SetWallTopConstraint(wallA, upper, -0.3);
        check(Math.Abs(session.Document.Walls.Single(w => w.Id == wallA).Height - 2.7) < 1e-12,
            "A negative top offset ends the wall below the top level");
        session.UpdateWall(wallB, 0.2, 3.4);
        var editedB = session.Document.Walls.Single(w => w.Id == wallB);
        check(editedB.TopLevelId == upper && Math.Abs(editedB.TopOffset - 0.4) < 1e-12 && editedB.Height == 3.4,
            "Editing the height of a constrained wall keeps the constraint by adjusting its offset");
        var rejected = false;
        try { session.SetWallTopConstraint(wallA, ground); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "A wall cannot be constrained to its own base level");
        session.SetWallTopConstraint(wallA, null);
        var released = session.Document.Walls.Single(w => w.Id == wallA);
        check(released.TopLevelId is null && released.TopOffset == 0 && Math.Abs(released.Height - 2.7) < 1e-12,
            "Releasing a constraint keeps the current height as an unconnected height");
    }

    /// <summary>Verifies level moves, atomic rejection, renaming, and protected deletion.</summary>
    private static void CheckLevelEditing(Action<bool, string> check)
    {
        var (session, ground, upper, wallA, _) = TwoStoreys();
        session.AttachWallsToLevelAbove(ground);
        session.AddOpening(wallA, OpeningKind.Door, offset: 2);
        session.UpdateLevel(upper, "Nivel 2", 3.6);
        check(session.Document.Walls.All(wall => Math.Abs(wall.Height - 3.6) < 1e-12) &&
              session.UndoName == "Transaction.UpdateLevel",
            "Raising a level regenerates every wall constrained to it");
        session.Undo();
        check(session.Document.Walls.All(wall => wall.Height == 3), "Undo restores the level and the wall heights");
        var rejected = false;
        try { session.UpdateLevel(upper, "Nivel 2", 2.0); } catch (InvalidDataException) { rejected = true; }
        check(rejected && session.Document.Levels.Single(l => l.Id == upper).Elevation == 3,
            "Lowering a level below a hosted door's head is rejected atomically");
        rejected = false;
        try { session.UpdateLevel(upper, "Nivel 1", 3); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "Level names stay unique");
        session.UpdateLevel(upper, "Mezzanine", 3);
        check(session.Document.Views.Any(view => view.LevelId == upper && view.Name == "Mezzanine"),
            "Renaming a level renames its default plan view");
        rejected = false;
        try { session.RemoveLevel(upper); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "A level that constrains walls cannot be deleted");
        session.DetachWallsFromLevelAbove(ground);
        rejected = false;
        try { session.RemoveLevel(upper); } catch (InvalidDataException) { rejected = true; }
        check(rejected && session.Document.Levels.Count == 2,
            "The renamed base roof remains protected even after releasing every dependent wall");
        rejected = false;
        try { session.RemoveLevel(ground); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "The base floor cannot be deleted");
        var added = session.AddLevel();
        check(session.Document.Levels.Single(l => l.Id == added).Name == DefaultLabels.Get("LevelName", 3),
            "New levels receive a name that is not already taken");
        session.RemoveLevel(added);
        check(session.Document.Levels.Count == 2 && session.Document.Views.All(view => view.LevelId != added),
            "Unused additional levels can be deleted together with their plan views");
    }

    /// <summary>Verifies schedule columns, formulas over constraints, round trip, and migration.</summary>
    private static void CheckSchedulesAndPersistence(Action<bool, string> check)
    {
        var (session, ground, upper, _, _) = TwoStoreys();
        session.AttachWallsToLevelAbove(ground, 0.5);
        var schedule = CategoryScheduleService.Build(session.Document, ElementCategory.Walls);
        var top = schedule.Columns.Single(c => c.Key == ScheduleQuantities.TopConstraint);
        check(schedule.Rows.All(row => CategoryScheduleService.FormatCell(row, top,
                System.Globalization.CultureInfo.InvariantCulture) == "Nivel 2"),
            "Wall schedules show the top constraint level");
        var parapet = session.AddParameter("Parapet", ParameterSpec.YesNo, ParameterBinding.Instance,
            [ElementCategory.Walls], formula: "TopOffset > 0");
        check(CategoryScheduleService.Build(session.Document, ElementCategory.Walls).Rows
                .All(row => row.Values[parapet].Number == 1),
            "Formulas can read the top offset of constrained walls");
        var path = Path.Combine(Path.GetTempPath(), $"guanacaste-levels-{Guid.NewGuid():N}.g3");
        try
        {
            ProjectFile.Save(path, session.Document);
            var reloaded = ProjectFile.Load(path);
            check(reloaded.Walls.All(wall => wall.TopLevelId == upper && wall.TopOffset == 0.5 && wall.Height == 3.5),
                "Top constraints survive a .g3d round trip");
            var node = JsonNode.Parse(JsonSerializer.Serialize(SampleProjects.CreateDemonstrationHouse()))!.AsObject();
            node["SchemaVersion"] = 6;
            foreach (var wall in node["Walls"]!.AsArray()) { wall!.AsObject().Remove("TopLevelId"); wall.AsObject().Remove("TopOffset"); }
            File.WriteAllText(path, "G3-PROJECT/1\n" + node.ToJsonString());
            var migrated = ProjectFile.Load(path);
            check(migrated.SchemaVersion == BuildingDocument.CurrentSchemaVersion &&
                  migrated.Walls.All(wall => wall.TopLevelId is null && wall.TopOffset == 0),
                "Schema-6 walls migrate as unconnected walls");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
        }
    }
}
