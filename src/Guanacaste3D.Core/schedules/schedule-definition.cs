namespace Guanacaste3D.Core;

/// <summary>Comparison applied by a schedule filter to one field.</summary>
public enum ScheduleFilterOperator
{
    Equal,
    NotEqual,
    GreaterThan,
    GreaterOrEqual,
    LessThan,
    LessOrEqual,
    Contains,
    NotContains,
    BeginsWith,
    EndsWith,
    HasValue,
    HasNoValue
}

/// <summary>
/// Keeps elements whose field satisfies a comparison. <see cref="Value"/> is
/// invariant text: numbers use '.' and metric base units (m, m², m³).
/// </summary>
public sealed record ScheduleFilter(string FieldKey, ScheduleFilterOperator Operator, string Value);

/// <summary>Sorts rows by a field and optionally groups them with header and footer rows.</summary>
public sealed record ScheduleSortGroup(string FieldKey, bool Descending = false, bool GroupHeader = false,
    bool GroupFooter = false);

/// <summary>
/// A saved, user-designed schedule over one category. Field keys are the
/// category schedule column keys: built-in keys (Mark, Name, Level, Width,
/// Area…) or a parameter definition id in "N" format. When
/// <see cref="Itemize"/> is false, rows with equal sort-field values are merged
/// into one row with a count, like a door schedule grouped by type.
/// </summary>
public sealed record ScheduleDefinition(
    Guid Id,
    string Name,
    ElementCategory Category,
    List<string> Fields,
    List<ScheduleFilter> Filters,
    List<ScheduleSortGroup> SortGroups,
    bool Itemize = true,
    bool GrandTotals = true);

/// <summary>Validation of saved schedule definitions against the current document.</summary>
public static class ScheduleDefinitionRules
{
    public const int MaximumFields = 50;
    public const int MaximumFilters = 8;
    public const int MaximumSortGroups = 4;

    /// <summary>Rejects invalid schedules, including fields that no longer exist for the category.</summary>
    public static void Validate(BuildingDocument document)
    {
        var schedules = document.Schedules;
        if (schedules is null || schedules.Count > 10000 || schedules.Any(schedule => schedule is null))
            throw new InvalidDataException("The schedule collection is invalid.");
        foreach (var schedule in schedules)
        {
            if (schedule.Id == Guid.Empty || string.IsNullOrWhiteSpace(schedule.Name) || schedule.Name.Length > 120 ||
                !Enum.IsDefined(schedule.Category) || schedule.Fields is null || schedule.Filters is null ||
                schedule.SortGroups is null || schedule.Fields.Count is < 1 or > MaximumFields ||
                schedule.Filters.Count > MaximumFilters || schedule.SortGroups.Count > MaximumSortGroups ||
                schedule.Fields.Distinct(StringComparer.Ordinal).Count() != schedule.Fields.Count)
                throw new InvalidDataException("A schedule definition is invalid.");
            var available = AvailableFields(document, schedule.Category);
            if (schedule.Fields.Any(field => !available.Contains(field)) ||
                schedule.Filters.Any(filter => filter is null || !available.Contains(filter.FieldKey) ||
                    !Enum.IsDefined(filter.Operator) || filter.Value is null || filter.Value.Length > 1000) ||
                schedule.SortGroups.Any(sort => sort is null || !schedule.Fields.Contains(sort.FieldKey)))
                throw new InvalidDataException("A schedule references a field that is not available.");
        }
        if (schedules.Select(schedule => schedule.Id).Distinct().Count() != schedules.Count ||
            schedules.Select(schedule => schedule.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != schedules.Count)
            throw new InvalidDataException("Schedule identifiers and names must be unique.");
    }

    /// <summary>Returns every field key a schedule over the category can use, in display order.</summary>
    public static IReadOnlyList<string> AvailableFields(BuildingDocument document, ElementCategory category) =>
        new[] { CategoryScheduleService.MarkColumn, CategoryScheduleService.NameColumn, CategoryScheduleService.LevelColumn }
            .Concat(ScheduleQuantities.ColumnsFor(category).Select(column => column.Key))
            .Concat(ParameterRules.ForCategory(document, category).Select(definition => definition.Id.ToString("N")))
            .ToArray();

    /// <summary>Regeneration: removes schedule fields, filters, and sorts whose parameter was deleted or unbound.</summary>
    public static void RemoveMissingFields(BuildingDocument document)
    {
        for (var index = 0; index < document.Schedules.Count; index++)
        {
            var schedule = document.Schedules[index];
            var available = AvailableFields(document, schedule.Category).ToHashSet(StringComparer.Ordinal);
            if (schedule.Fields.All(available.Contains) && schedule.Filters.All(f => available.Contains(f.FieldKey)) &&
                schedule.SortGroups.All(s => available.Contains(s.FieldKey))) continue;
            var fields = schedule.Fields.Where(available.Contains).ToList();
            if (fields.Count == 0) fields.Add(CategoryScheduleService.MarkColumn);
            document.Schedules[index] = schedule with
            {
                Fields = fields,
                Filters = schedule.Filters.Where(filter => available.Contains(filter.FieldKey)).ToList(),
                SortGroups = schedule.SortGroups.Where(sort => fields.Contains(sort.FieldKey)).ToList()
            };
        }
    }
}
