using System.Text.Json;
using System.Text.Json.Nodes;
using Guanacaste3D.Core;

/// <summary>
/// Regression checks for door and window types (schema 5): defaults, size-based
/// type resolution, type propagation with atomic rejection, type management,
/// type-bound parameters on openings, schedule quantities, and migration.
/// </summary>
static class OpeningTypeChecks
{
    /// <summary>Runs every opening-type check in sequence.</summary>
    public static void Run(Action<bool, string> check)
    {
        CheckPlacementAndResolution(check);
        CheckTypePropagation(check);
        CheckTypeManagement(check);
        CheckTypeParametersAndSchedules(check);
        CheckMigrationAndRoundTrip(check);
    }

    /// <summary>Creates a session with one 8 m wall and returns the session and wall id.</summary>
    private static (DocumentSession Session, Guid WallId) NewWall()
    {
        var session = new DocumentSession(BuildingDocument.Create("Openings"));
        var wallId = session.AddWall(session.Document.Levels[0].Id, new Point2(0, 0), new Point2(8, 0));
        return (session, wallId);
    }

    /// <summary>Verifies default types and size-based reuse or creation of types.</summary>
    private static void CheckPlacementAndResolution(Action<bool, string> check)
    {
        var (session, wallId) = NewWall();
        check(session.Document.OpeningTypes.Count == 6, "New projects include the built-in door and window catalog");
        var sliding = session.Document.OpeningTypes.Single(type => type.Id == BuiltInOpeningTypes.SlidingWindow1600Id);
        check(sliding.Operation == OpeningOperation.Sliding && sliding.GlazingLightCount == 2,
            "The built-in sliding window uses two coordinated sliding sashes");
        var door = session.AddOpening(wallId, OpeningKind.Door, offset: 1.0);
        var window = session.AddOpening(wallId, OpeningKind.Window, offset: 3.0);
        var placedDoor = session.Document.Openings.Single(o => o.Id == door);
        var placedWindow = session.Document.Openings.Single(o => o.Id == window);
        check(placedDoor.OpeningTypeId == BuiltInOpeningTypes.SingleDoor900Id && placedDoor.Width == 0.9 &&
              placedWindow.OpeningTypeId == BuiltInOpeningTypes.FixedWindow1200Id && placedWindow.Sill == 0.9,
            "Openings without a type use the default type and its default sill");
        session.AddOpening(wallId, OpeningKind.Door, offset: 5.0, width: 1.0, height: 2.2);
        session.AddOpening(wallId, OpeningKind.Door, offset: 7.0, width: 1.0, height: 2.2);
        var custom = session.Document.OpeningTypes.Where(type => type.Width == 1.0 && type.Height == 2.2).ToArray();
        check(custom.Length == 1 && custom[0].DisplayName == "Single Door: 1000 × 2200 mm" &&
              session.Document.Openings.Count(o => o.OpeningTypeId == custom[0].Id) == 2,
            "A new size creates one named type that later placements reuse");
        var rejected = false;
        try { session.AddOpening(wallId, OpeningKind.Window, offset: 6, width: 0.5, openingTypeId: BuiltInOpeningTypes.FixedWindow1200Id); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "An explicit type rejects a conflicting explicit size");
    }

    /// <summary>Verifies that type edits drive instances and that overflow rejects the whole edit.</summary>
    private static void CheckTypePropagation(Action<bool, string> check)
    {
        var (session, wallId) = NewWall();
        var first = session.AddOpening(wallId, OpeningKind.Door, offset: 1.5);
        var second = session.AddOpening(wallId, OpeningKind.Door, offset: 4.5);
        session.UpdateOpeningType(BuiltInOpeningTypes.SingleDoor900Id, "Single Door", "1000 × 2100 mm", 1.0, 2.1, 0);
        check(session.Document.Openings.All(o => o.Width == 1.0) && session.UndoName == "Transaction.UpdateOpeningType",
            "Editing a type resizes every instance in one transaction");
        session.Undo();
        check(session.Document.Openings.All(o => o.Width == 0.9), "Undo restores the type and every instance size");
        var rejected = false;
        try { session.UpdateOpeningType(BuiltInOpeningTypes.SingleDoor900Id, "Single Door", "Too wide", 3.5, 2.1, 0); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected && session.Document.OpeningTypes.Single(t => t.Id == BuiltInOpeningTypes.SingleDoor900Id).Width == 0.9,
            "A type edit that makes instances overlap or overflow is rejected atomically");
        session.UpdateOpening(first, 1.5, 0.8, 0, 2.1);
        check(session.Document.Openings.Single(o => o.Id == first).OpeningTypeId == BuiltInOpeningTypes.SingleDoor800Id &&
              session.Document.Openings.Single(o => o.Id == second).OpeningTypeId == BuiltInOpeningTypes.SingleDoor900Id,
            "Resizing one instance rebinds only that instance to a matching type");
        session.SetOpeningType(second, BuiltInOpeningTypes.DoubleDoor1500Id);
        check(session.Document.Openings.Single(o => o.Id == second).Width == 1.5, "Assigning a type resizes the instance");
        rejected = false;
        try { session.SetOpeningType(second, BuiltInOpeningTypes.FixedWindow600Id); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "A door cannot take a window type");
    }

    /// <summary>Verifies duplicate, rename uniqueness, protected delete, and purge.</summary>
    private static void CheckTypeManagement(Action<bool, string> check)
    {
        var (session, wallId) = NewWall();
        var copy = session.DuplicateOpeningType(BuiltInOpeningTypes.FixedWindow1200Id, "1200 × 1200 mm laminated");
        check(session.Document.OpeningTypes.Single(t => t.Id == copy).FamilyName == "Fixed Window",
            "Duplicating keeps the family and copies the parameters");
        var rejected = false;
        try { session.DuplicateOpeningType(copy, "1200 × 1200 mm"); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "Type names are unique within a family");
        var opening = session.AddOpening(wallId, OpeningKind.Window, offset: 2, openingTypeId: copy);
        rejected = false;
        try { session.RemoveOpeningType(copy); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected && session.Document.OpeningTypes.Any(t => t.Id == copy), "Types in use cannot be deleted");
        var purged = session.PurgeUnusedOpeningTypes();
        check(purged == 6 && session.Document.OpeningTypes.Single().Id == copy, "Purge removes every unused type");
        session.RemoveOpening(opening);
        var door = session.AddOpening(wallId, OpeningKind.Door, offset: 5);
        check(session.Document.Openings.Single(o => o.Id == door).Width == 0.9,
            "Placement still works after the default type was purged");
    }

    /// <summary>Verifies type-bound opening parameters and door/window schedule quantities.</summary>
    private static void CheckTypeParametersAndSchedules(Action<bool, string> check)
    {
        var (session, wallId) = NewWall();
        session.AddOpening(wallId, OpeningKind.Door, offset: 1.5);
        session.AddOpening(wallId, OpeningKind.Door, offset: 4.5);
        var rating = session.AddParameter("Fire rating", ParameterSpec.Integer, ParameterBinding.Type, [ElementCategory.Doors]);
        session.SetParameterValue(session.Document.Openings[0].Id, rating, 30, null);
        var schedule = CategoryScheduleService.Build(session.Document, ElementCategory.Doors);
        var widthColumn = schedule.Columns.Single(c => c.Key == ScheduleQuantities.Width);
        check(schedule.Rows.Count == 2 && schedule.Rows.All(r => r.Values[rating].Number == 30) &&
              schedule.Rows.All(r => CategoryScheduleService.FormatCell(r, widthColumn,
                  System.Globalization.CultureInfo.InvariantCulture) == "0.9") &&
              schedule.Rows.All(r => r.Element.Name == "Single Door: 900 × 2100 mm"),
            "Door schedules show type names, sizes, and type-bound values shared by the type");
        foreach (var id in session.Document.Openings.Select(o => o.Id).ToArray()) session.RemoveOpening(id);
        session.RemoveOpeningType(BuiltInOpeningTypes.SingleDoor900Id);
        check(session.Document.ParameterValues.Count == 0, "Deleting a type regenerates away its type-bound values");
    }

    /// <summary>Verifies schema-4 migration and schema-5 round trip.</summary>
    private static void CheckMigrationAndRoundTrip(Action<bool, string> check)
    {
        var house = SampleProjects.CreateDemonstrationHouse();
        var path = Path.Combine(Path.GetTempPath(), $"guanacaste-opening-types-{Guid.NewGuid():N}.g3");
        try
        {
            var node = JsonNode.Parse(JsonSerializer.Serialize(house))!.AsObject();
            node["SchemaVersion"] = 4;
            node.Remove("OpeningTypes");
            foreach (var opening in node["Openings"]!.AsArray()) opening!.AsObject().Remove("OpeningTypeId");
            File.WriteAllText(path, "G3-PROJECT/1\n" + node.ToJsonString());
            var migrated = ProjectFile.Load(path);
            var sizesKept = migrated.Openings.Zip(house.Openings).All(pair =>
                pair.First.Width == pair.Second.Width && pair.First.Height == pair.Second.Height);
            var standardDoors = migrated.Openings.Where(o => o.Kind == OpeningKind.Door && o.Width == 0.9).ToArray();
            check(migrated.SchemaVersion == BuildingDocument.CurrentSchemaVersion && sizesKept &&
                  standardDoors.Length > 0 && standardDoors.All(o => o.OpeningTypeId == BuiltInOpeningTypes.SingleDoor900Id),
                "Schema-4 openings migrate to built-in or generated types with identical sizes");
            ProjectFile.Save(path, migrated);
            var reloaded = ProjectFile.Load(path);
            check(reloaded.OpeningTypes.Count == migrated.OpeningTypes.Count &&
                  reloaded.Openings.Select(o => o.OpeningTypeId).SequenceEqual(migrated.Openings.Select(o => o.OpeningTypeId)),
                "Door and window types survive a schema-5 round trip");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
        }
    }
}
