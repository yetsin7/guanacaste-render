namespace Guanacaste3D.Core;

/// <summary>
/// Read-only model quantities shown as built-in schedule columns. Values are
/// metric doubles (m, m², m³) or text, keyed by column key.
/// </summary>
public static class ScheduleQuantities
{
    public const string Length = "Length";
    public const string Height = "Height";
    public const string Thickness = "Thickness";
    public const string NetVolume = "NetVolume";
    public const string Width = "Width";
    public const string Sill = "Sill";
    public const string Host = "Host";
    public const string Area = "Area";
    public const string Elevation = "Elevation";
    public const string PaperSize = "PaperSize";
    public const string TopConstraint = "TopConstraint";
    public const string TopOffset = "TopOffset";
    public const string NetHeight = "NetHeight";
    public const string BaseOffset = "BaseOffset";

    /// <summary>Returns the built-in quantity columns for a category, in display order.</summary>
    public static IReadOnlyList<CategoryScheduleColumn> ColumnsFor(ElementCategory category) => category switch
    {
        ElementCategory.Walls =>
        [
            new(Length, null, "m"), new(Height, null, "m"), new(Thickness, null, "m"), new(NetVolume, null, "m³"),
            new(TopConstraint, null), new(TopOffset, null, "m"), new(NetHeight, null, "m"), new(Area, null, "m²"), new(BaseOffset, null, "m")
        ],
        ElementCategory.Doors or ElementCategory.Windows =>
        [
            new(Width, null, "m"), new(Height, null, "m"), new(Sill, null, "m"), new(Host, null)
        ],
        ElementCategory.Rooms => [new(Area, null, "m²")],
        ElementCategory.Floors => [new(Area, null, "m²"), new(Thickness, null, "m")],
        ElementCategory.Levels => [new(Elevation, null, "m")],
        ElementCategory.Sheets => [new(PaperSize, null)],
        _ => []
    };

    /// <summary>Computes quantities for every element of a category, keyed by element identifier.</summary>
    public static Dictionary<Guid, IReadOnlyDictionary<string, object>> Compute(BuildingDocument document,
        ElementCategory category)
    {
        var result = new Dictionary<Guid, IReadOnlyDictionary<string, object>>();
        switch (category)
        {
            case ElementCategory.Walls:
                var hosted = document.Openings.ToLookup(opening => opening.WallId);
                var levelNames = document.Levels.ToDictionary(level => level.Id, level => level.Name);
                foreach (var wall in document.Walls)
                {
                    var values = new Dictionary<string, object>
                    {
                        [Length] = wall.Length, [Height] = wall.Height, [Thickness] = wall.Thickness,
                        [NetVolume] = WallGeometry.NetVolume(wall, hosted[wall.Id]),
                        [NetHeight] = wall.NetHeight, [BaseOffset] = wall.BaseOffset,
                        [Area] = FinishRules.WallFaceArea(wall, hosted[wall.Id])
                    };
                    if (wall.TopLevelId is Guid topId)
                    {
                        values[TopConstraint] = levelNames.GetValueOrDefault(topId, string.Empty);
                        values[TopOffset] = wall.TopOffset;
                    }
                    result[wall.Id] = values;
                }
                break;
            case ElementCategory.Doors or ElementCategory.Windows:
                var kind = category == ElementCategory.Doors ? OpeningKind.Door : OpeningKind.Window;
                // The host reads as its wall type, as a door schedule names the wall a door sits in.
                var hostTypes = document.Walls.ToDictionary(wall => wall.Id, wall =>
                    document.WallTypes.FirstOrDefault(type => type.Id == wall.WallTypeId)?.Name ?? ElementCatalog.ShortMark(wall.Id));
                foreach (var opening in document.Openings.Where(opening => opening.Kind == kind))
                    result[opening.Id] = new Dictionary<string, object>
                    {
                        [Width] = opening.Width, [Height] = opening.Height, [Sill] = opening.Sill,
                        [Host] = hostTypes.GetValueOrDefault(opening.WallId, ElementCatalog.ShortMark(opening.WallId))
                    };
                break;
            case ElementCategory.Rooms:
                foreach (var room in document.Rooms)
                    result[room.Id] = new Dictionary<string, object> { [Area] = room.Area };
                break;
            case ElementCategory.Floors:
                foreach (var floor in document.Floors)
                    result[floor.Id] = new Dictionary<string, object>
                    {
                        [Area] = floor.CalculateArea(), [Thickness] = floor.Thickness
                    };
                break;
            case ElementCategory.Levels:
                foreach (var level in document.Levels)
                    result[level.Id] = new Dictionary<string, object> { [Elevation] = level.Elevation };
                break;
            case ElementCategory.Sheets:
                foreach (var sheet in document.Sheets)
                    result[sheet.Id] = new Dictionary<string, object> { [PaperSize] = sheet.PaperSize.ToString() };
                break;
        }
        return result;
    }
}
