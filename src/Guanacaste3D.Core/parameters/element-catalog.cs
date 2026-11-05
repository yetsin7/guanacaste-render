namespace Guanacaste3D.Core;

/// <summary>
/// A category-neutral description of one model element, used by parameters,
/// schedules, and property panels. <see cref="TypeId"/> is set when the element
/// references a reusable type that can hold type-bound parameter values.
/// </summary>
public sealed record ElementEntry(
    Guid Id,
    ElementCategory Category,
    string Mark,
    string Name,
    Guid? LevelId,
    Guid? TypeId);

/// <summary>Enumerates document elements by category without exposing storage lists.</summary>
public static class ElementCatalog
{
    /// <summary>Returns every element of a category in a deterministic display order.</summary>
    public static IReadOnlyList<ElementEntry> List(BuildingDocument document, ElementCategory category)
    {
        var wallTypeNames = document.WallTypes.ToDictionary(type => type.Id, type => type.Name);
        IEnumerable<ElementEntry> entries = category switch
        {
            ElementCategory.Walls => document.Walls.Select(wall => new ElementEntry(
                wall.Id, category, ShortMark(wall.Id),
                wallTypeNames.GetValueOrDefault(wall.WallTypeId, string.Empty), wall.LevelId, wall.WallTypeId)),
            ElementCategory.Doors or ElementCategory.Windows => ListOpenings(document, category),
            ElementCategory.Rooms => document.Rooms.Select(room => new ElementEntry(
                room.Id, category, room.Number, room.Name, room.LevelId, null)),
            ElementCategory.Floors => document.Floors.Select(floor => new ElementEntry(
                floor.Id, category, ShortMark(floor.Id), floor.Name, floor.LevelId, null)),
            ElementCategory.Levels => document.Levels.OrderBy(level => level.Elevation).Select(level =>
                new ElementEntry(level.Id, category, ShortMark(level.Id), level.Name, level.Id, null)),
            ElementCategory.Sheets => document.Sheets.Select(sheet => new ElementEntry(
                sheet.Id, category, sheet.Number, sheet.Name, null, null)),
            _ => []
        };
        return category == ElementCategory.Levels
            ? entries.ToArray()
            : entries.OrderBy(entry => entry.Mark, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Finds an element instance by identifier across all categories.</summary>
    public static ElementEntry? Find(BuildingDocument document, Guid elementId)
    {
        foreach (var category in Enum.GetValues<ElementCategory>())
        {
            var match = List(document, category).FirstOrDefault(entry => entry.Id == elementId);
            if (match is not null) return match;
        }
        return null;
    }

    /// <summary>Builds an identifier-to-category map used by bulk validation.</summary>
    public static Dictionary<Guid, ElementEntry> Index(BuildingDocument document)
    {
        var index = new Dictionary<Guid, ElementEntry>();
        foreach (var category in Enum.GetValues<ElementCategory>())
            foreach (var entry in List(document, category))
                index[entry.Id] = entry;
        return index;
    }

    /// <summary>Returns the eight-character mark used for elements without user numbering.</summary>
    public static string ShortMark(Guid id) => id.ToString("N")[..8];

    /// <summary>Lists doors or windows with their type mark (as the plan tags show it) and their host-wall level.</summary>
    private static IEnumerable<ElementEntry> ListOpenings(BuildingDocument document, ElementCategory category)
    {
        var kind = category == ElementCategory.Doors ? OpeningKind.Door : OpeningKind.Window;
        var hostLevels = document.Walls.ToDictionary(wall => wall.Id, wall => wall.LevelId);
        var typeNames = document.OpeningTypes.ToDictionary(type => type.Id, type => type.DisplayName);
        var marks = ElementTags.OpeningTypeMarks(document, kind);
        return document.Openings.Where(opening => opening.Kind == kind).Select(opening => new ElementEntry(
            opening.Id, category, marks[opening.OpeningTypeId],
            typeNames.GetValueOrDefault(opening.OpeningTypeId, string.Empty),
            hostLevels.TryGetValue(opening.WallId, out var levelId) ? levelId : null, opening.OpeningTypeId));
    }
}
