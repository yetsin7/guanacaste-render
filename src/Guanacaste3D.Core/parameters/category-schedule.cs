using System.Globalization;
using System.Text;

namespace Guanacaste3D.Core;

/// <summary>
/// Identifies a schedule column. Built-in columns have no parameter definition
/// and are computed from the model; <see cref="Unit"/> is the display unit suffix.
/// </summary>
public sealed record CategoryScheduleColumn(string Key, ParameterDefinition? Definition, string Unit = "")
{
    /// <summary>Returns whether cells in this column edit a stored parameter value.</summary>
    public bool IsEditable => Definition is not null && !FormulaEngine.IsCalculated(Definition);
}

/// <summary>
/// One schedule row: the element, computed built-in values (numbers in metric
/// units or text), and the effective value for each parameter column.
/// </summary>
public sealed record CategoryScheduleRow(ElementEntry Element, string LevelName,
    IReadOnlyDictionary<string, object> BuiltIns, IReadOnlyDictionary<Guid, ParameterValue> Values);

/// <summary>A category schedule computed from the current document state.</summary>
public sealed record CategorySchedule(ElementCategory Category,
    IReadOnlyList<CategoryScheduleColumn> Columns, IReadOnlyList<CategoryScheduleRow> Rows);

/// <summary>
/// Builds element-per-row schedules for one category: identity columns, model
/// quantities for the category, then one column per bound project parameter.
/// Rows are derived from the document, so every edit, undo, or redo is
/// reflected on the next build.
/// </summary>
public static class CategoryScheduleService
{
    public const string MarkColumn = "Mark";
    public const string NameColumn = "Name";
    public const string LevelColumn = "Level";

    /// <summary>Builds the schedule for a category, resolving type-bound values through each element's type.</summary>
    public static CategorySchedule Build(BuildingDocument document, ElementCategory category)
    {
        var definitions = ParameterRules.ForCategory(document, category);
        var columns = new List<CategoryScheduleColumn>
        {
            new(MarkColumn, null), new(NameColumn, null), new(LevelColumn, null)
        };
        columns.AddRange(ScheduleQuantities.ColumnsFor(category));
        columns.AddRange(definitions.Select(definition => new CategoryScheduleColumn(
            definition.Id.ToString("N"), definition, ParameterValueText.UnitSuffix(definition.Spec))));
        var stored = document.ParameterValues.ToDictionary(value => (value.DefinitionId, value.OwnerId));
        var levelNames = document.Levels.ToDictionary(level => level.Id, level => level.Name);
        var quantities = ScheduleQuantities.Compute(document, category);
        var calculated = FormulaEngine.EvaluateCategory(document, category, quantities);
        var rows = ElementCatalog.List(document, category).Select(element =>
        {
            var values = new Dictionary<Guid, ParameterValue>();
            foreach (var definition in definitions)
            {
                if (ParameterRules.ResolveOwner(definition, element) is Guid ownerId &&
                    stored.TryGetValue((definition.Id, ownerId), out var value))
                    values[definition.Id] = value;
            }
            if (calculated.TryGetValue(element.Id, out var computed))
                foreach (var (definitionId, value) in computed) values[definitionId] = value;
            var levelName = element.LevelId is Guid levelId ? levelNames.GetValueOrDefault(levelId, "") : "";
            return new CategoryScheduleRow(element, levelName,
                quantities.GetValueOrDefault(element.Id) ?? new Dictionary<string, object>(), values);
        }).ToArray();
        return new CategorySchedule(category, columns, rows);
    }

    /// <summary>Formats one cell for display or export; yes/no labels come from the caller's localization.</summary>
    public static string FormatCell(CategoryScheduleRow row, CategoryScheduleColumn column, CultureInfo culture,
        string yesLabel = "Yes", string noLabel = "No", ProjectUnits? units = null)
    {
        if (units is not null && column.Unit.Length > 0)
        {
            // Lengths, areas and volumes show in the project's units; the stored values stay metric.
            var metric = column.Definition is { } spec ? row.Values.GetValueOrDefault(spec.Id)?.Number : row.BuiltIns.GetValueOrDefault(column.Key) as double?;
            if (metric is double value && units.NumberFor(column.Unit, value) is { } shown) return shown;
        }
        if (column.Definition is { } definition)
            return ParameterValueText.Format(definition.Spec, row.Values.GetValueOrDefault(definition.Id),
                culture, yesLabel, noLabel);
        return column.Key switch
        {
            MarkColumn => row.Element.Mark,
            NameColumn => row.Element.Name,
            LevelColumn => row.LevelName,
            _ => row.BuiltIns.GetValueOrDefault(column.Key) switch
            {
                double number => number.ToString("0.###", culture),
                string text => text,
                _ => string.Empty
            }
        };
    }

    /// <summary>Exports a schedule as RFC 4180 CSV with invariant numbers and unit-labelled headers.</summary>
    public static string ToCsv(CategorySchedule schedule)
    {
        var csv = new StringBuilder();
        csv.AppendJoin(',', schedule.Columns.Select(column => Escape(HeaderWithUnit(
            column.Definition?.Name ?? column.Key, column.Unit)))).AppendLine();
        foreach (var row in schedule.Rows)
            csv.AppendJoin(',', schedule.Columns.Select(column =>
                Escape(FormatCell(row, column, CultureInfo.InvariantCulture)))).AppendLine();
        return csv.ToString();
    }

    /// <summary>Appends the display unit to a header, such as "Finish height (m)".</summary>
    public static string HeaderWithUnit(string header, string unit) =>
        unit.Length == 0 ? header : $"{header} ({unit})";

    /// <summary>Quotes a CSV cell and neutralizes spreadsheet formula prefixes.</summary>
    private static string Escape(string value)
    {
        var safe = value.Length > 0 && "=+-@".Contains(value[0]) && !double.TryParse(value,
            NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? "'" + value : value;
        return "\"" + safe.Replace("\"", "\"\"") + "\"";
    }
}
