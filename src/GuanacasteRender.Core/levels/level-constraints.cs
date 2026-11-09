namespace Guanacaste3D.Core;

/// <summary>
/// Rules that tie walls to levels. A wall with a top constraint has its height
/// derived from the levels: (top level elevation + top offset) minus its base
/// level elevation. Moving a level therefore regenerates the height of every
/// wall constrained to it, and validation then re-checks hosted openings.
/// </summary>
public static class LevelConstraints
{
    /// <summary>Largest allowed top offset in meters, above or below the top level.</summary>
    public const double MaximumOffset = 100;

    /// <summary>Tolerance between a stored wall height and the height its constraint implies.</summary>
    public const double HeightTolerance = 0.0000005;

    /// <summary>Returns the height implied by a wall's top constraint, or null when it is unconnected.</summary>
    public static double? ConstrainedHeight(Wall wall, IReadOnlyDictionary<Guid, Level> levels) =>
        wall.TopLevelId is Guid topId && levels.TryGetValue(topId, out var top) &&
        levels.TryGetValue(wall.LevelId, out var baseLevel)
            ? top.Elevation + wall.TopOffset - baseLevel.Elevation
            : null;

    /// <summary>Regeneration: recomputes the height of every top-constrained wall.</summary>
    public static void Apply(BuildingDocument document)
    {
        var levels = document.Levels.ToDictionary(level => level.Id);
        for (var index = 0; index < document.Walls.Count; index++)
        {
            var wall = document.Walls[index];
            if (ConstrainedHeight(wall, levels) is double height && height != wall.Height)
                document.Walls[index] = wall with { Height = height };
        }
    }

    /// <summary>Rejects dangling constraints, constraints to the base level, and stale heights.</summary>
    public static void Validate(BuildingDocument document)
    {
        var levels = document.Levels.ToDictionary(level => level.Id);
        foreach (var wall in document.Walls)
        {
            if (!double.IsFinite(wall.TopOffset) || Math.Abs(wall.TopOffset) > MaximumOffset ||
                (wall.TopLevelId is null && wall.TopOffset != 0))
                throw new InvalidDataException("A wall top constraint is invalid.");
            if (wall.TopLevelId is not Guid topId) continue;
            if (topId == wall.LevelId || !levels.ContainsKey(topId))
                throw new InvalidDataException("A wall top constraint is invalid.");
            var height = ConstrainedHeight(wall, levels)!.Value;
            if (height < 0.1)
                throw new InvalidDataException("A wall top constraint is below its base level.");
            if (Math.Abs(height - wall.Height) > HeightTolerance)
                throw new InvalidDataException("A wall height does not match its top constraint.");
        }
    }

    /// <summary>Returns the next level above the given one by elevation, if any.</summary>
    public static Level? LevelAbove(BuildingDocument document, Guid levelId)
    {
        var level = document.Levels.FirstOrDefault(candidate => candidate.Id == levelId);
        return level is null ? null : document.Levels
            .Where(candidate => candidate.Elevation > level.Elevation)
            .OrderBy(candidate => candidate.Elevation).FirstOrDefault();
    }

    /// <summary>Counts the elements that depend on a level as base or top constraint.</summary>
    public static int CountDependents(BuildingDocument document, Guid levelId) =>
        document.Walls.Count(wall => wall.LevelId == levelId || wall.TopLevelId == levelId) +
        document.Rooms.Count(room => room.LevelId == levelId) +
        document.Floors.Count(floor => floor.LevelId == levelId) +
        document.Columns.Count(column => column.LevelId == levelId) +
        document.StructuralElements.Count(element => element.LevelId == levelId) +
        document.ElectricalDevices.Count(device => device.LevelId == levelId) +
        document.PlumbingPoints.Count(point => point.LevelId == levelId) +
        document.Sheets.SelectMany(sheet => sheet.Viewports).Count(viewport => document.Views.Any(view =>
            view.Id == viewport.ViewId && view.LevelId == levelId));
}
