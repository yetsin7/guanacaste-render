namespace Guanacaste3D.Core;

/// <summary>Level editing and wall top constraints; each public method is one transaction.</summary>
public sealed partial class DocumentSession
{
    /// <summary>
    /// Renames a level and changes its elevation. Walls whose top is
    /// constrained to the level, or whose base sits on it under a constrained
    /// top, regenerate their height in the same transaction; if a hosted
    /// opening would no longer fit, the whole change is rejected. A plan view
    /// still carrying the level's name (or the older "&lt;level&gt; Plan") is renamed along.
    /// </summary>
    public void UpdateLevel(Guid levelId, string name, double elevation)
    {
        var trimmed = name.Trim();
        if (!Document.Levels.Any(level => level.Id == levelId))
            throw new InvalidDataException("The level does not exist.");
        if (Document.Levels.Any(level => level.Id != levelId &&
                string.Equals(level.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Level names must be unique.");
        Commit("Transaction.UpdateLevel", document =>
        {
            var index = document.Levels.FindIndex(level => level.Id == levelId);
            var oldName = document.Levels[index].Name;
            document.Levels[index] = document.Levels[index] with { Name = trimmed, Elevation = elevation };
            for (var viewIndex = 0; viewIndex < document.Views.Count; viewIndex++)
            {
                var view = document.Views[viewIndex];
                if (view.LevelId != levelId) continue;
                if (view.Name == oldName) document.Views[viewIndex] = view with { Name = trimmed };
                else if (view.Name == $"{oldName} Plan") document.Views[viewIndex] = view with { Name = $"{trimmed} Plan" };
            }
        });
    }

    /// <summary>Deletes unused optional levels; the project's permanent floor and roof datums are protected by identity.</summary>
    public void RemoveLevel(Guid levelId)
    {
        if (!Document.Levels.Any(level => level.Id == levelId)) return;
        if (BaseLevels.IsProtected(Document.Levels.Single(level => level.Id == levelId)))
            throw new InvalidDataException("The base floor and roof levels cannot be deleted.");
        if (LevelConstraints.CountDependents(Document, levelId) > 0)
            throw new InvalidDataException("The level still hosts or constrains elements.");
        Commit("Transaction.RemoveLevel", document =>
        {
            document.Views.RemoveAll(view => view.LevelId == levelId);
            document.Levels.RemoveAll(level => level.Id == levelId);
        });
    }

    /// <summary>
    /// Ties a wall's top to a level with an offset, or releases it when
    /// <paramref name="topLevelId"/> is null (the wall keeps its current height
    /// as an unconnected height).
    /// </summary>
    public void SetWallTopConstraint(Guid wallId, Guid? topLevelId, double topOffset = 0)
    {
        if (!Document.Walls.Any(wall => wall.Id == wallId))
            throw new InvalidDataException("The wall does not exist.");
        Commit("Transaction.SetWallTopConstraint", document =>
        {
            var index = document.Walls.FindIndex(wall => wall.Id == wallId);
            document.Walls[index] = document.Walls[index] with
            {
                TopLevelId = topLevelId,
                TopOffset = topLevelId is null ? 0 : topOffset
            };
        });
    }

    /// <summary>
    /// Constrains the top of every wall based on a level to the next level
    /// above, as one undo step. Returns how many walls were constrained; zero
    /// when there is no level above or no walls on the level.
    /// </summary>
    public int AttachWallsToLevelAbove(Guid levelId, double topOffset = 0)
    {
        if (LevelConstraints.LevelAbove(Document, levelId) is not { } above) return 0;
        var walls = Document.Walls.Where(wall => wall.LevelId == levelId).Select(wall => wall.Id).ToHashSet();
        if (walls.Count == 0) return 0;
        Commit("Transaction.AttachWallsToLevelAbove", document =>
        {
            for (var index = 0; index < document.Walls.Count; index++)
                if (walls.Contains(document.Walls[index].Id))
                    document.Walls[index] = document.Walls[index] with { TopLevelId = above.Id, TopOffset = topOffset };
        });
        return walls.Count;
    }

    /// <summary>Releases the top constraint of every wall based on a level, keeping current heights.</summary>
    public int DetachWallsFromLevelAbove(Guid levelId)
    {
        var walls = Document.Walls.Where(wall => wall.LevelId == levelId && wall.TopLevelId is not null)
            .Select(wall => wall.Id).ToHashSet();
        if (walls.Count == 0) return 0;
        Commit("Transaction.DetachWalls", document =>
        {
            for (var index = 0; index < document.Walls.Count; index++)
                if (walls.Contains(document.Walls[index].Id))
                    document.Walls[index] = document.Walls[index] with { TopLevelId = null, TopOffset = 0 };
        });
        return walls.Count;
    }
}
