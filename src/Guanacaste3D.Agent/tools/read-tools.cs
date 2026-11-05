using System.Text.Json;
using System.Text.Json.Nodes;
using Guanacaste3D.Core;

namespace Guanacaste3D.Agent;

/// <summary>Tools that inspect the project without changing it.</summary>
internal static class ReadTools
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static string Json(object value) => JsonSerializer.Serialize(value, Options);

    public static IEnumerable<AgentTool> Create()
    {
        yield return new("get_project_summary",
            "Returns the project name, levels with their ids and elevations, wall types, element counts and the plot. " +
            "Call this first: every other tool needs ids or level names from here.",
            Schema.Object(), ToolRisk.Read, (context, _) =>
            {
                var d = context.Session.Document;
                return Json(new
                {
                    name = d.Name,
                    units = "meters; Z up; +Y is project north",
                    levels = d.Levels.OrderBy(l => l.Elevation).Select(l => new { id = l.Id, name = l.Name, elevation = l.Elevation }),
                    wallTypes = d.WallTypes.Select(t => new { id = t.Id, name = t.Name, thickness = t.Thickness }),
                    openingTypes = d.OpeningTypes.Select(t => new { id = t.Id, name = t.Name, kind = t.Kind.ToString(), width = t.Width, height = t.Height }),
                    callouts = d.Callouts.Select(c => new { id = c.Id, levelId = c.LevelId, name = c.Name }),
                    viewTemplates = d.ViewTemplates.Select(t => t.Name),
                    counts = new
                    {
                        walls = d.Walls.Count, openings = d.Openings.Count, rooms = d.Rooms.Count,
                        floors = d.Floors.Count, columns = d.Columns.Count, sheets = d.Sheets.Count
                    }
                });
            });

        yield return new("list_elements",
            "Lists elements of one kind with their ids and key dimensions, optionally restricted to a level.",
            Schema.Object(
                ("kind", Schema.Str("Element kind.", "walls", "openings", "rooms", "floors", "columns"), true),
                ("level", Schema.Str("Level id or name. Optional."), false)),
            ToolRisk.Read, (context, args) =>
            {
                var d = context.Session.Document;
                Guid? level = args.Raw["level"] is null ? null : args.Level();
                return args.Text("kind") switch
                {
                    "walls" => Json(d.Walls.Where(w => level is null || w.LevelId == level).Select(w => new
                    {
                        id = w.Id, levelId = w.LevelId, start = new[] { w.Start.X, w.Start.Y }, end = new[] { w.End.X, w.End.Y },
                        length = w.Length, thickness = w.Thickness, height = w.Height
                    })),
                    "openings" => Json(d.Openings.Where(o => level is null ||
                        d.Walls.Any(w => w.Id == o.WallId && w.LevelId == level)).Select(o => new
                    {
                        id = o.Id, wallId = o.WallId, kind = o.Kind.ToString(), offset = o.Offset, width = o.Width, sill = o.Sill, height = o.Height
                    })),
                    "rooms" => Json(d.Rooms.Where(r => level is null || r.LevelId == level).Select(r => new
                    {
                        id = r.Id, levelId = r.LevelId, number = r.Number, name = r.Name, area = r.Area
                    })),
                    "floors" => Json(d.Floors.Where(f => level is null || f.LevelId == level).Select(f => new
                    {
                        id = f.Id, levelId = f.LevelId, name = f.Name, kind = f.Kind.ToString(), area = f.CalculateArea(), thickness = f.Thickness
                    })),
                    "columns" => Json(d.Columns.Where(c => level is null || c.LevelId == level).Select(c => new
                    {
                        id = c.Id, levelId = c.LevelId, position = new[] { c.Position.X, c.Position.Y }, height = c.Height
                    })),
                    var other => throw new InvalidDataException($"Unknown element kind '{other}'.")
                };
            });

        yield return new("get_schedule",
            "Returns the coordinated project schedule: walls, rooms, floors and sheets with quantities and units.",
            Schema.Object(), ToolRisk.Read,
            (context, _) => Json(ProjectScheduleService.Build(context.Session.Document)));

        yield return new("get_quantities_csv",
            "Returns the wall quantity takeoff as CSV: lengths, volumes, doors, windows and finish areas per wall.",
            Schema.Object(), ToolRisk.Read,
            (context, _) => QuantitySchedule.ToCsv(context.Session.Document));

        yield return new("run_model_audit",
            "Checks the model for wall gaps, overlapping rooms and low opening head clearance.",
            Schema.Object(), ToolRisk.Read, (context, _) =>
            {
                var findings = ModelAuditReporting.Describe(ModelAudit.Analyze(context.Session.Document));
                return findings.Count == 0 ? "No issues found." : Json(findings.Select(f => new
                {
                    severity = f.Severity.ToString(), elementId = f.Issue.ElementId, description = f.Description, suggestion = f.SuggestedAction
                }));
            });

        yield return new("get_budget",
            "Prices the model quantities with the current unit price list and returns lines, subtotal, contingency and total. " +
            "Default prices are placeholders: tell the user to confirm local prices.",
            Schema.Object(("contingency_percent", Schema.Num("Contingency as a percentage of the subtotal. Default 10."), false)),
            ToolRisk.Read, (context, args) =>
            {
                var rate = (decimal)args.Number("contingency_percent", 10) / 100m;
                var budget = BudgetCalculator.Build(context.Session.Document, context.Prices, rate);
                return Json(budget);
            });

        yield return new("get_tags",
            "Returns the automatic tags of a level: door marks (P1...), window marks (V1...), columns (C1...) and optionally " +
            "walls, slabs and rooms, each with its position and description. The marks are stable across levels.",
            Schema.Object(
                ("level", Schema.Str("Level id or name."), true),
                ("categories", Schema.Str("Comma list of door, window, column, wall, slab, room. Default: door, window, column."), false)),
            ToolRisk.Read, (context, args) =>
            {
                var requested = args.Text("categories", string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(name => Enum.TryParse<TagCategory>(name, true, out var category)
                        ? category : throw new InvalidDataException($"Unknown tag category '{name}'."))
                    .ToHashSet();
                var tags = ElementTags.Build(context.Session.Document, args.Level(), requested.Count == 0 ? null : requested);
                return Json(tags.Select(tag => new
                {
                    elementId = tag.ElementId, category = tag.Category.ToString(), mark = tag.Mark,
                    description = tag.Description, x = Math.Round(tag.Position.X, 3), y = Math.Round(tag.Position.Y, 3)
                }));
            });

        yield return new("get_area_schedule",
            "Returns the area table: every room grouped by level with a subtotal per level and a grand total, in square meters.",
            Schema.Object(), ToolRisk.Read, (context, _) => Json(AreaSchedule.Rows(context.Session.Document)
                .Select(row => new { kind = row.Kind.ToString(), number = row.Number, name = row.Name, area = Math.Round(row.Area, 2) })));

        yield return new("get_unit_prices", "Returns the unit price list used by get_budget.",
            Schema.Object(), ToolRisk.Read,
            (context, _) => Json(new { currency = context.Prices.Currency, prices = context.Prices.Prices }));
    }
}
