namespace Guanacaste3D.Core;

/// <summary>Separates permanent floor/roof datums from optional storeys without depending on editable names.</summary>
public enum LevelRole { Additional, BaseFloor, BaseRoof }

/// <summary>Maintains the two foundational datums across new projects, templates, and older project migrations.</summary>
public static class BaseLevels
{
    /// <summary>Returns whether a level belongs to the nondeletable floor/roof pair.</summary>
    public static bool IsProtected(Level level) => level.Role is LevelRole.BaseFloor or LevelRole.BaseRoof;

    /// <summary>Adopts existing levels before adding missing datums; existing elevations and hosted geometry never move.</summary>
    public static void Ensure(BuildingDocument document)
    {
        if (document.Levels is null || document.Levels.Count == 0) return;
        var floor = document.Levels.FirstOrDefault(level => level.Role == LevelRole.BaseFloor) ?? document.Levels.FirstOrDefault(level =>
            string.Equals(level.Name, DefaultLabels.Get("LevelName", 1), StringComparison.OrdinalIgnoreCase)) ??
            document.Levels.OrderBy(level => level.Elevation).First();
        var roof = document.Levels.FirstOrDefault(level => level.Role == LevelRole.BaseRoof) ??
            document.Levels.FirstOrDefault(level => level.Id != floor.Id && level.Elevation > floor.Elevation + 0.1 &&
                string.Equals(level.Name, DefaultLabels.Get("LevelName", 2), StringComparison.OrdinalIgnoreCase)) ??
            document.Levels.Where(level => level.Id != floor.Id && level.Elevation > floor.Elevation + 0.1)
                .OrderBy(level => level.Elevation).FirstOrDefault();
        if (roof is null)
        {
            var number = 2;
            while (document.Levels.Any(level => string.Equals(level.Name, DefaultLabels.Get("LevelName", number), StringComparison.OrdinalIgnoreCase)) ||
                document.Views.Any(view => string.Equals(view.Name, DefaultLabels.Get("LevelName", number), StringComparison.OrdinalIgnoreCase))) number++;
            roof = new(Guid.NewGuid(), DefaultLabels.Get("LevelName", number), floor.Elevation + 3);
            document.Levels.Add(roof);
        }
        for (var index = 0; index < document.Levels.Count; index++)
        {
            var level = document.Levels[index];
            document.Levels[index] = level with { Role = level.Id == floor.Id ? LevelRole.BaseFloor :
                level.Id == roof.Id ? LevelRole.BaseRoof : LevelRole.Additional };
            if (!document.Views.Any(view => view.Kind == ProjectViewKind.Plan && view.LevelId == level.Id))
                document.Views.Add(BuiltInProjectViews.CreatePlan(level));
        }
    }

    /// <summary>Rejects missing, duplicate, or inverted base datums before committing an edit or saving a project.</summary>
    public static void Validate(BuildingDocument document)
    {
        var floor = document.Levels.Where(level => level.Role == LevelRole.BaseFloor).ToArray();
        var roof = document.Levels.Where(level => level.Role == LevelRole.BaseRoof).ToArray();
        if (document.Levels.Any(level => !Enum.IsDefined(level.Role)) || floor.Length != 1 || roof.Length != 1 ||
            roof[0].Elevation <= floor[0].Elevation + 0.1)
            throw new InvalidDataException("A project requires distinct protected floor and roof levels.");
    }
}
