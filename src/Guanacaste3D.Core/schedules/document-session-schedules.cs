namespace Guanacaste3D.Core;

/// <summary>Saved schedule management; each public method is one transaction.</summary>
public sealed partial class DocumentSession
{
    /// <summary>
    /// Creates a schedule over a category. Without explicit fields it starts with
    /// identity columns plus the category's model quantities.
    /// </summary>
    public Guid AddSchedule(string name, ElementCategory category, IEnumerable<string>? fields = null)
    {
        var id = Guid.NewGuid();
        var selected = fields?.ToList() ?? DefaultFields(category);
        var available = ScheduleDefinitionRules.AvailableFields(Document, category).ToHashSet(StringComparer.Ordinal);
        if (selected.Any(field => !available.Contains(field)))
            throw new InvalidDataException("A schedule references a field that is not available.");
        Commit("Transaction.AddSchedule", document => document.Schedules.Add(
            new ScheduleDefinition(id, name.Trim(), category, selected, [], [])));
        return id;
    }

    /// <summary>Replaces a schedule's name, fields, filters, sorting, grouping, and totals options.</summary>
    public void UpdateSchedule(ScheduleDefinition definition)
    {
        var existing = Document.Schedules.FirstOrDefault(schedule => schedule.Id == definition.Id)
            ?? throw new InvalidDataException("The schedule does not exist.");
        // Checked before the transaction: regeneration prunes fields that vanish
        // as a consequence of other edits, but must never mask invalid input.
        var available = ScheduleDefinitionRules.AvailableFields(Document, existing.Category).ToHashSet(StringComparer.Ordinal);
        if (definition.Fields.Any(field => !available.Contains(field)) ||
            definition.Filters.Any(filter => !available.Contains(filter.FieldKey)) ||
            definition.SortGroups.Any(sort => !definition.Fields.Contains(sort.FieldKey)))
            throw new InvalidDataException("A schedule references a field that is not available.");
        var copy = definition with
        {
            Name = definition.Name.Trim(),
            Fields = [.. definition.Fields],
            Filters = [.. definition.Filters],
            SortGroups = [.. definition.SortGroups]
        };
        Commit("Transaction.UpdateSchedule", document =>
        {
            var index = document.Schedules.FindIndex(schedule => schedule.Id == definition.Id);
            document.Schedules[index] = copy with { Category = document.Schedules[index].Category };
        });
    }

    /// <summary>Copies a schedule under a new name, for variants such as "Doors by level".</summary>
    public Guid DuplicateSchedule(Guid scheduleId, string newName)
    {
        var source = Document.Schedules.FirstOrDefault(schedule => schedule.Id == scheduleId)
            ?? throw new InvalidDataException("The schedule does not exist.");
        var id = Guid.NewGuid();
        Commit("Transaction.DuplicateSchedule", document => document.Schedules.Add(source with
        {
            Id = id, Name = newName.Trim(), Fields = [.. source.Fields], Filters = [.. source.Filters],
            SortGroups = [.. source.SortGroups]
        }));
        return id;
    }

    /// <summary>Deletes a saved schedule; model elements are never affected.</summary>
    public void RemoveSchedule(Guid scheduleId)
    {
        if (Document.Schedules.Any(schedule => schedule.Id == scheduleId))
            Commit("Transaction.RemoveSchedule", document => document.Schedules.RemoveAll(schedule => schedule.Id == scheduleId));
    }

    /// <summary>Returns the starting fields for a new schedule of a category.</summary>
    public static List<string> DefaultFields(ElementCategory category) =>
    [
        CategoryScheduleService.MarkColumn, CategoryScheduleService.NameColumn, CategoryScheduleService.LevelColumn,
        .. ScheduleQuantities.ColumnsFor(category).Select(column => column.Key)
    ];
}
