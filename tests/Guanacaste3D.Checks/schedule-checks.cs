using System.Globalization;
using Guanacaste3D.Core;

/// <summary>
/// Regression checks for saved schedules (schema 8): filters, sorting,
/// grouping with headers and footers, non-itemized counts, totals,
/// regeneration when a parameter disappears, CSV, and persistence.
/// </summary>
static class ScheduleChecks
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Runs every schedule check in sequence.</summary>
    public static void Run(Action<bool, string> check)
    {
        var (session, cost) = BuildHouse();
        CheckDoorSchedule(check, session, cost);
        CheckWallSchedule(check, session);
        CheckMaintenanceAndPersistence(check, session, cost);
    }

    /// <summary>Uses the demonstration house plus a cost parameter on its doors.</summary>
    private static (DocumentSession Session, Guid Cost) BuildHouse()
    {
        var session = new DocumentSession(SampleProjects.CreateDemonstrationHouse());
        var cost = session.AddParameter("Cost", ParameterSpec.Currency, ParameterBinding.Instance, [ElementCategory.Doors]);
        session.SetParameterValues(session.Document.Openings.Where(o => o.Kind == OpeningKind.Door)
            .Select(o => new ParameterEdit(o.Id, cost, 100, null)).ToArray());
        return (session, cost);
    }

    /// <summary>Verifies a door schedule grouped by type, not itemized, with counts and totals.</summary>
    private static void CheckDoorSchedule(Action<bool, string> check, DocumentSession session, Guid cost)
    {
        var doorCount = session.Document.Openings.Count(o => o.Kind == OpeningKind.Door);
        var id = session.AddSchedule("Doors by type", ElementCategory.Doors,
            [CategoryScheduleService.NameColumn, ScheduleQuantities.Width, cost.ToString("N")]);
        var definition = session.Document.Schedules.Single(s => s.Id == id) with
        {
            SortGroups = [new ScheduleSortGroup(CategoryScheduleService.NameColumn)],
            Itemize = false
        };
        session.UpdateSchedule(definition);
        var table = ScheduleEvaluator.Evaluate(session.Document, session.Document.Schedules.Single(s => s.Id == id), Invariant);
        var elements = table.Rows.Where(r => r.Kind == ScheduleRowKind.Element).ToArray();
        var types = session.Document.Openings.Where(o => o.Kind == OpeningKind.Door).Select(o => o.OpeningTypeId).Distinct().Count();
        var total = table.Rows.Single(r => r.Kind == ScheduleRowKind.GrandTotal);
        check(table.HasCountColumn && elements.Length == types && elements.Sum(r => r.Count) == doorCount &&
              elements.All(r => r.Cells[^1] == r.Count.ToString(Invariant)),
            "A non-itemized schedule merges identical rows into one row per type with a count");
        check(total.Count == doorCount && total.Cells[2] == (doorCount * 100.0).ToString("N2", Invariant),
            "The grand total counts every element and sums currency");
        var filtered = definition with
        {
            Itemize = true,
            Filters = [new ScheduleFilter(ScheduleQuantities.Width, ScheduleFilterOperator.GreaterOrEqual, "0.9")]
        };
        session.UpdateSchedule(filtered);
        var wide = ScheduleEvaluator.Evaluate(session.Document, session.Document.Schedules.Single(s => s.Id == id), Invariant);
        check(wide.ElementCount == session.Document.Openings.Count(o => o.Kind == OpeningKind.Door && o.Width >= 0.9 - 1e-9) &&
              wide.ElementCount < doorCount, "Numeric filters compare in meters and exclude narrower doors");
    }

    /// <summary>Verifies grouping headers/footers, descending sorts, and text filters on walls.</summary>
    private static void CheckWallSchedule(Action<bool, string> check, DocumentSession session)
    {
        var id = session.AddSchedule("Walls by type", ElementCategory.Walls,
            [CategoryScheduleService.NameColumn, ScheduleQuantities.Length, ScheduleQuantities.Height]);
        session.UpdateSchedule(session.Document.Schedules.Single(s => s.Id == id) with
        {
            SortGroups = [new ScheduleSortGroup(CategoryScheduleService.NameColumn, GroupHeader: true, GroupFooter: true),
                          new ScheduleSortGroup(ScheduleQuantities.Length, Descending: true)]
        });
        var table = ScheduleEvaluator.Evaluate(session.Document, session.Document.Schedules.Single(s => s.Id == id), Invariant);
        var headers = table.Rows.Count(r => r.Kind == ScheduleRowKind.GroupHeader);
        var footers = table.Rows.Where(r => r.Kind == ScheduleRowKind.GroupFooter).ToArray();
        var wallTypes = session.Document.Walls.Select(w => w.WallTypeId).Distinct().Count();
        var lengthSum = session.Document.Walls.Sum(w => w.Length);
        var footerSum = footers.Sum(f => double.Parse(f.Cells[1], Invariant));
        check(headers == wallTypes && footers.Length == wallTypes && Math.Abs(footerSum - lengthSum) < 0.01 &&
              string.IsNullOrEmpty(footers[0].Cells[2]),
            "Group footers sum lengths per type but never sum heights");
        var firstGroup = table.Rows.SkipWhile(r => r.Kind != ScheduleRowKind.Element)
            .TakeWhile(r => r.Kind == ScheduleRowKind.Element).Select(r => double.Parse(r.Cells[1], Invariant)).ToArray();
        check(firstGroup.SequenceEqual(firstGroup.OrderByDescending(v => v)), "Secondary sorts apply inside each group");
        session.UpdateSchedule(session.Document.Schedules.Single(s => s.Id == id) with
        {
            Filters = [new ScheduleFilter(CategoryScheduleService.NameColumn, ScheduleFilterOperator.Contains, "exterior")]
        });
        var exterior = ScheduleEvaluator.Evaluate(session.Document, session.Document.Schedules.Single(s => s.Id == id), Invariant);
        check(exterior.ElementCount == session.Document.Walls.Count(w => w.WallTypeId == BuiltInWallTypes.Exterior300Id),
            "Text filters are case-insensitive");
    }

    /// <summary>Verifies regeneration, validation, duplication, CSV, undo, and the .g3d round trip.</summary>
    private static void CheckMaintenanceAndPersistence(Action<bool, string> check, DocumentSession session, Guid cost)
    {
        var doors = session.Document.Schedules.Single(s => s.Name == "Doors by type");
        session.RemoveParameter(cost);
        var regenerated = session.Document.Schedules.Single(s => s.Id == doors.Id);
        check(!regenerated.Fields.Contains(cost.ToString("N")) && regenerated.Fields.Count == 2,
            "Deleting a parameter removes it from every schedule in the same transaction");
        var rejected = false;
        try { session.UpdateSchedule(regenerated with { Fields = ["NoSuchField"] }); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "Schedules cannot reference unavailable fields");
        rejected = false;
        try { session.AddSchedule("doors BY type", ElementCategory.Doors); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "Schedule names are unique");
        var copy = session.DuplicateSchedule(doors.Id, "Doors copy");
        var csv = ScheduleEvaluator.ToCsv(ScheduleEvaluator.Evaluate(session.Document,
            session.Document.Schedules.Single(s => s.Id == copy), Invariant));
        check(csv.StartsWith("\"Row\",\"Name\",\"Width (m)\"", StringComparison.Ordinal) && csv.Contains("\"GrandTotal\"", StringComparison.Ordinal),
            "Evaluated schedules export to CSV with row kinds");
        var evaluated = ScheduleEvaluator.Evaluate(session.Document,
            session.Document.Schedules.Single(s => s.Id == copy), Invariant);
        var guardedCsv = ScheduleEvaluator.ToCsv(evaluated with
        {
            Rows = [new ScheduleTableRow(ScheduleRowKind.Element, [" \t=2+2", "-12.5"], 1, 0, [])]
        });
        check(guardedCsv.Contains("\"' \t=2+2\"", StringComparison.Ordinal) &&
              guardedCsv.Contains("\"-12.5\"", StringComparison.Ordinal),
            "Schedule CSV neutralizes whitespace-prefixed formulas while preserving numeric cells");
        session.Undo();
        check(session.Document.Schedules.All(s => s.Id != copy), "Undo removes a duplicated schedule");
        var path = Path.Combine(Path.GetTempPath(), $"guanacaste-schedules-{Guid.NewGuid():N}.g3d");
        try
        {
            ProjectFile.Save(path, session.Document);
            var reloaded = ProjectFile.Load(path);
            var walls = reloaded.Schedules.Single(s => s.Name == "Walls by type");
            check(reloaded.SchemaVersion == BuildingDocument.CurrentSchemaVersion && reloaded.Schedules.Count == 2 &&
                  walls.SortGroups.Count == 2 && walls.SortGroups[0].GroupFooter && walls.Filters.Single().Value == "exterior",
                "Saved schedules survive a .g3d round trip with sorting, grouping, and filters");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
        }
    }
}
