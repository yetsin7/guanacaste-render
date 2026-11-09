using System.Globalization;
using Guanacaste3D.Core;

/// <summary>
/// Regression checks for named transactions, transaction groups, and project
/// parameters (schema 4). Invoked from Program.cs with its Check delegate.
/// </summary>
static class ParameterChecks
{
    /// <summary>Runs every parameter and transaction check in sequence.</summary>
    public static void Run(Action<bool, string> check)
    {
        CheckNamedTransactions(check);
        CheckTransactionGroups(check);
        CheckDefinitionsAndValues(check);
        CheckValueParsing(check);
        CheckPersistence(check);
    }

    /// <summary>Verifies that history entries expose stable transaction names.</summary>
    private static void CheckNamedTransactions(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Transactions"));
        var levelId = session.Document.Levels[0].Id;
        session.AddWall(levelId, new Point2(0, 0), new Point2(4, 0));
        session.AddRoom(levelId, new Point2(0, 0), new Point2(3, 3));
        check(session.UndoName == "Transaction.AddRoom" &&
              session.UndoNames.SequenceEqual(["Transaction.AddRoom", "Transaction.AddWall"]),
            "Transactions record localizable names in undo order");
        session.Undo();
        check(session.RedoName == "Transaction.AddRoom" && session.UndoName == "Transaction.AddWall",
            "Undo moves the named transaction to the redo stack");
    }

    /// <summary>Verifies assimilate, rollback, dispose, and nesting rules of groups.</summary>
    private static void CheckTransactionGroups(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Groups"));
        var levelId = session.Document.Levels[0].Id;
        using (var group = session.BeginGroup("Transaction.DrawRectangle"))
        {
            session.AddWall(levelId, new Point2(0, 0), new Point2(5, 0));
            session.AddWall(levelId, new Point2(5, 0), new Point2(5, 4));
            check(!session.CanUndo, "Undo is unavailable while a transaction group is open");
            var nested = false;
            try { session.BeginGroup("Nested"); } catch (InvalidOperationException) { nested = true; }
            check(nested, "Transaction groups cannot be nested");
            group.Assimilate();
        }
        check(session.Document.Walls.Count == 2 && session.UndoNames.Count == 1 &&
              session.UndoName == "Transaction.DrawRectangle", "Assimilated group becomes one undo step");
        session.Undo();
        check(session.Document.Walls.Count == 0, "Undoing an assimilated group reverts all inner transactions");
        session.Redo();
        check(session.Document.Walls.Count == 2, "Redo reapplies the whole assimilated group");

        using (session.BeginGroup("Transaction.Abandoned"))
            session.AddWall(levelId, new Point2(0, 4), new Point2(5, 4));
        check(session.Document.Walls.Count == 2 && session.UndoName == "Transaction.DrawRectangle",
            "Disposing an unfinished group rolls back its transactions");

        var group2 = session.BeginGroup("Transaction.Failing");
        session.AddWall(levelId, new Point2(0, 8), new Point2(5, 8));
        var rejected = false;
        try { session.AddWall(levelId, new Point2(1, 1), new Point2(1, 1)); }
        catch (InvalidDataException) { rejected = true; }
        group2.RollBack();
        check(rejected && session.Document.Walls.Count == 2 && !session.IsGroupOpen,
            "A failing step can roll back the whole group to its starting state");
    }

    /// <summary>Verifies binding rules, type-bound propagation, regeneration, and undo.</summary>
    private static void CheckDefinitionsAndValues(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Parameters"));
        var levelId = session.Document.Levels[0].Id;
        var wallA = session.AddWall(levelId, new Point2(0, 0), new Point2(4, 0), wallTypeId: BuiltInWallTypes.Basic200Id);
        var wallB = session.AddWall(levelId, new Point2(0, 2), new Point2(4, 2), wallTypeId: BuiltInWallTypes.Basic200Id);
        var roomId = session.AddRoom(levelId, new Point2(0, 0), new Point2(4, 2));

        var fireRating = session.AddParameter("Fire rating", ParameterSpec.Integer, ParameterBinding.Type,
            [ElementCategory.Walls], "Identity");
        var finish = session.AddParameter("Finish", ParameterSpec.Text, ParameterBinding.Instance,
            [ElementCategory.Walls, ElementCategory.Rooms], "Materials");
        var rejected = false;
        try { session.AddParameter("Bad type", ParameterSpec.Text, ParameterBinding.Type, [ElementCategory.Rooms]); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "Type binding is rejected for categories without reusable types");
        rejected = false;
        try { session.AddParameter("finish", ParameterSpec.Number, ParameterBinding.Instance, [ElementCategory.Floors]); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected && session.Document.ParameterDefinitions.Count == 2,
            "Parameter names are unique regardless of case");

        session.SetParameterValue(wallA, fireRating, 90, null);
        var schedule = CategoryScheduleService.Build(session.Document, ElementCategory.Walls);
        check(schedule.Rows.All(row => row.Values[fireRating].Number == 90) &&
              session.Document.ParameterValues.Single().OwnerId == BuiltInWallTypes.Basic200Id,
            "Type-bound values are stored once on the type and reported by every instance");

        rejected = false;
        try { session.SetParameterValue(wallB, fireRating, 1.5, null); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected && session.Document.ParameterValues.Single().Number == 90,
            "Integer parameters reject fractional values atomically");

        session.SetParameterValues([
            new ParameterEdit(wallA, finish, null, "Plaster"),
            new ParameterEdit(roomId, finish, null, "Tile")]);
        check(session.UndoName == "Transaction.SetParameterValues" &&
              session.Document.ParameterValues.Count == 3, "Bulk value edits commit as one transaction");

        session.RemoveWall(wallA);
        check(session.Document.ParameterValues.Count == 2 &&
              session.Document.ParameterValues.All(value => value.OwnerId != wallA),
            "Deleting an element regenerates away its instance values");
        session.Undo();
        check(session.Document.ParameterValues.Any(value => value.OwnerId == wallA && value.Text == "Plaster"),
            "Undo restores an element together with its parameter values");

        session.UpdateParameter(finish, "Surface finish", [ElementCategory.Walls], "Materials");
        check(session.Document.ParameterValues.All(value => value.OwnerId != roomId) &&
              session.Document.ParameterDefinitions.Any(definition => definition.Name == "Surface finish"),
            "Narrowing a binding drops values on removed categories in the same transaction");
        session.RemoveParameter(fireRating);
        check(session.Document.ParameterValues.All(value => value.DefinitionId != fireRating),
            "Removing a definition removes all of its values");
        var csv = CategoryScheduleService.ToCsv(CategoryScheduleService.Build(session.Document, ElementCategory.Walls));
        check(csv.StartsWith("\"Mark\",\"Name\",\"Level\",\"Length (m)\"", StringComparison.Ordinal) &&
              csv.Split('\n')[0].TrimEnd().EndsWith("\"Surface finish\"", StringComparison.Ordinal) &&
              csv.Contains("\"Plaster\"", StringComparison.Ordinal), "Category schedules export parameter columns to CSV");
    }

    /// <summary>Verifies culture-aware parsing and formatting of parameter text.</summary>
    private static void CheckValueParsing(Action<bool, string> check)
    {
        var spanish = CultureInfo.GetCultureInfo("es-ES");
        check(ParameterValueText.TryParse(ParameterSpec.Length, "2,5 m", spanish, out var comma, out _) && comma == 2.5 &&
              ParameterValueText.TryParse(ParameterSpec.Length, "2.5", spanish, out var dot, out _) && dot == 2.5,
            "Length input accepts local and invariant decimals with unit suffix");
        var mexican = CultureInfo.GetCultureInfo("es-MX");
        check(ParameterValueText.TryParse(ParameterSpec.Length, "3,5", mexican, out var lone, out _) && lone == 3.5,
            "A lone decimal comma parses even when the culture uses a decimal point");
        check(ParameterValueText.TryParse(ParameterSpec.YesNo, "Sí", spanish, out var yes, out _) && yes == 1 &&
              !ParameterValueText.TryParse(ParameterSpec.Area, "-3", spanish, out _, out _) &&
              ParameterValueText.TryParse(ParameterSpec.Text, "   ", spanish, out var cleared, out var clearedText) &&
              cleared is null && clearedText is null,
            "Yes/no tokens, negative-area rejection, and empty-means-clear parsing");
        check(ParameterValueText.Format(ParameterSpec.Currency, new ParameterValue(Guid.NewGuid(), Guid.NewGuid(), 1234.5, null),
                CultureInfo.InvariantCulture) == "1,234.50", "Currency values format with two decimals");
    }

    /// <summary>Verifies schema-4 round trip and schema-3 migration.</summary>
    private static void CheckPersistence(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Persistence"));
        var levelId = session.Document.Levels[0].Id;
        var sheetId = session.AddSheet();
        var drawnBy = session.AddParameter("Drawn by", ParameterSpec.Text, ParameterBinding.Instance,
            [ElementCategory.Sheets], "Title block");
        var elevation = session.AddParameter("Finish floor", ParameterSpec.Length, ParameterBinding.Instance,
            [ElementCategory.Levels]);
        session.SetParameterValue(sheetId, drawnBy, null, "YG");
        session.SetParameterValue(levelId, elevation, 0.15, null);
        var path = Path.Combine(Path.GetTempPath(), $"guanacaste-parameters-{Guid.NewGuid():N}.g3");
        try
        {
            ProjectFile.Save(path, session.Document);
            var reloaded = ProjectFile.Load(path);
            check(reloaded.SchemaVersion == BuildingDocument.CurrentSchemaVersion &&
                  reloaded.ParameterDefinitions.Count == 2 &&
                  reloaded.ParameterValues.Any(value => value.OwnerId == sheetId && value.Text == "YG") &&
                  reloaded.ParameterValues.Any(value => value.OwnerId == levelId && value.Number == 0.15),
                "Parameter definitions and values survive a .g3d round trip");

            var legacy = session.Document.Clone();
            legacy.SchemaVersion = 3;
            var json = System.Text.Json.JsonSerializer.Serialize(legacy);
            using (var parsed = System.Text.Json.JsonDocument.Parse(json))
            {
                var fields = parsed.RootElement.EnumerateObject()
                    .Where(field => !field.Name.StartsWith("Parameter", StringComparison.Ordinal))
                    .ToDictionary(field => field.Name, field => field.Value.Clone());
                File.WriteAllText(path, "G3-PROJECT/1\n" + System.Text.Json.JsonSerializer.Serialize(fields));
            }
            var migrated = ProjectFile.Load(path);
            check(migrated.SchemaVersion == BuildingDocument.CurrentSchemaVersion &&
                  migrated.ParameterDefinitions.Count == 0 && migrated.ParameterValues.Count == 0,
                "Schema-3 projects migrate to the current schema with empty parameter catalogs");

            var orphan = session.Document.Clone();
            orphan.ParameterValues.Add(new ParameterValue(drawnBy, Guid.NewGuid(), null, "Ghost"));
            File.WriteAllText(path, "G3-PROJECT/1\n" + System.Text.Json.JsonSerializer.Serialize(orphan));
            var refused = false;
            try { ProjectFile.Load(path); } catch (InvalidDataException) { refused = true; }
            check(refused, "Files with orphaned parameter values are rejected on open");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
        }
    }
}
