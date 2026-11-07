namespace Guanacaste3D.Core;

/// <summary>The kinds of element that can carry an automatic tag in plan.</summary>
public enum TagCategory { Door, Window, Wall, Column, Slab, Room, Area, Material }

/// <summary>
/// A computed label for one element: where to draw it, a short mark (P1, V2, C1...) and a longer description.
/// Tags are derived from the model on demand, so they can never be stale and need no storage.
/// </summary>
public sealed record ElementTag(Guid ElementId, TagCategory Category, Point2 Position, string Mark, string Description);

/// <summary>Builds the tags of one level. Marks are numbered by type across the whole project, so they stay stable between levels.</summary>
public static class ElementTags
{
    /// <summary>The categories tagged by default: the ones a plan is normally annotated with.</summary>
    public static readonly IReadOnlySet<TagCategory> Default = new HashSet<TagCategory>
    {
        TagCategory.Door, TagCategory.Window, TagCategory.Column
    };

    /// <summary>Returns the tags of the requested categories on a level, or the default categories when none are given.</summary>
    public static IReadOnlyList<ElementTag> Build(BuildingDocument document, Guid levelId, IReadOnlySet<TagCategory>? categories = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        categories ??= Default;
        var tags = new List<ElementTag>();
        var walls = document.Walls.ToDictionary(wall => wall.Id);

        if (categories.Contains(TagCategory.Door) || categories.Contains(TagCategory.Window))
        {
            var doorMarks = OpeningTypeMarks(document, OpeningKind.Door);
            var windowMarks = OpeningTypeMarks(document, OpeningKind.Window);
            foreach (var opening in document.Openings)
            {
                if (!walls.TryGetValue(opening.WallId, out var host) || host.LevelId != levelId) continue;
                var door = opening.Kind == OpeningKind.Door;
                if (!categories.Contains(door ? TagCategory.Door : TagCategory.Window)) continue;
                var mark = (door ? doorMarks : windowMarks)[opening.OpeningTypeId];
                var type = document.OpeningTypes.FirstOrDefault(candidate => candidate.Id == opening.OpeningTypeId);
                var description = $"{type?.Name ?? (door ? "Puerta" : "Ventana")} {opening.Width:0.##} × {opening.Height:0.##} m";
                tags.Add(new ElementTag(opening.Id, door ? TagCategory.Door : TagCategory.Window,
                    AlongWall(host, opening.Offset, host.Thickness / 2 + 0.35), mark, description));
            }
        }

        if (categories.Contains(TagCategory.Wall))
            foreach (var wall in document.Walls.Where(wall => wall.LevelId == levelId))
            {
                var typeName = document.WallTypes.FirstOrDefault(type => type.Id == wall.WallTypeId)?.Name ?? "Muro";
                tags.Add(new ElementTag(wall.Id, TagCategory.Wall, AlongWall(wall, wall.Length / 2, 0),
                    typeName, $"{typeName}, {wall.Length:0.##} m"));
            }

        if (categories.Contains(TagCategory.Material))
            foreach (var wall in document.Walls.Where(wall => wall.LevelId == levelId))
            {
                var type = document.WallTypes.FirstOrDefault(candidate => candidate.Id == wall.WallTypeId);
                if (type is null) continue;
                tags.Add(new ElementTag(wall.Id, TagCategory.Material, AlongWall(wall, wall.Length / 2, wall.Thickness / 2 + 0.2),
                    type.MaterialName, $"{type.MaterialName}, {wall.Thickness * 1000:0} mm"));
            }

        if (categories.Contains(TagCategory.Column))
        {
            var sizes = document.Columns.Select(column => (column.Width, column.Depth)).Distinct().ToList();
            foreach (var column in document.Columns.Where(column => column.LevelId == levelId))
                tags.Add(new ElementTag(column.Id, TagCategory.Column, column.Position,
                    $"C{sizes.IndexOf((column.Width, column.Depth)) + 1}", $"Columna {column.Width:0.##} × {column.Depth:0.##} m"));
        }

        if (categories.Contains(TagCategory.Slab))
            foreach (var floor in document.Floors.Where(floor => floor.LevelId == levelId && floor.Boundary.Count >= 3))
                tags.Add(new ElementTag(floor.Id, TagCategory.Slab,
                    new Point2(floor.Boundary.Average(p => p.X), floor.Boundary.Average(p => p.Y)), floor.Name,
                    $"{floor.Name}, {floor.CalculateArea():0.##} m², {floor.Thickness * 1000:0} mm"));

        if (categories.Contains(TagCategory.Room))
            foreach (var room in document.Rooms.Where(room => room.LevelId == levelId))
                tags.Add(new ElementTag(room.Id, TagCategory.Room, room.Center, room.Number, $"{room.Name}, {room.Area:0.##} m²"));

        if (categories.Contains(TagCategory.Area))
            foreach (var area in document.StructuralElements.Where(element =>
                         element.LevelId == levelId && element.Kind == StructuralElementKind.Area))
            {
                var center = new Point2((area.Start.X + area.End.X) / 2,
                    (area.Start.Y + area.End.Y) / 2);
                tags.Add(new ElementTag(area.Id, TagCategory.Area, center, area.Name,
                    $"{area.Name}, {area.PlanArea:0.##} m²"));
            }

        return tags;
    }

    /// <summary>
    /// Numbers each distinct opening type of one kind in order of first appearance: P1, P2... for doors, V1, V2... for
    /// windows. Tags and schedules share these type marks, so a plan's tags key into its door and window tables.
    /// </summary>
    internal static Dictionary<Guid, string> OpeningTypeMarks(BuildingDocument document, OpeningKind kind)
    {
        var prefix = kind == OpeningKind.Door ? "P" : "V";
        var marks = new Dictionary<Guid, string>();
        foreach (var opening in document.Openings.Where(opening => opening.Kind == kind))
            marks.TryAdd(opening.OpeningTypeId, prefix + (marks.Count + 1));
        return marks;
    }

    /// <summary>A point at a distance along the wall, pushed sideways by <paramref name="sideOffset"/> to its left (positive) or right (negative).</summary>
    private static Point2 AlongWall(Wall wall, double distance, double sideOffset)
    {
        var dx = wall.End.X - wall.Start.X;
        var dy = wall.End.Y - wall.Start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-9) return wall.Start;
        var (ux, uy) = (dx / length, dy / length);
        return new Point2(wall.Start.X + ux * distance - uy * sideOffset, wall.Start.Y + uy * distance + ux * sideOffset);
    }
}
