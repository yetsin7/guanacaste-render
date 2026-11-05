namespace Guanacaste3D.Core.Rendering;

/// <summary>A level of the building appears (<paramref name="Visible"/>) or disappears at a time of the movie.</summary>
public sealed record LayerKey(Guid LevelId, float Seconds, bool Visible);

/// <summary>
/// Layer animation, as Lumion's layer effect does it: a level is shown or hidden at chosen moments of the movie, so a
/// building can rise floor by floor or lift its roof to show the inside. A level with keys is the opposite of its first
/// key until that key arrives (a level that appears at 3 s is not there before 3 s), and then it follows its last key.
/// Levels without keys are always shown.
/// </summary>
public sealed class LayerAnimation
{
    private const float SameMoment = 0.05f;
    private readonly List<LayerKey> _keys = [];

    public IReadOnlyList<LayerKey> Keys => _keys;

    public bool IsEmpty => _keys.Count == 0;

    /// <summary>Sets what a level does at a time; a key of the same level within 0.05 s is replaced.</summary>
    public void Set(Guid levelId, float seconds, bool visible)
    {
        if (!float.IsFinite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
        seconds = Math.Clamp(seconds, 0f, 36000f);
        _keys.RemoveAll(key => key.LevelId == levelId && Math.Abs(key.Seconds - seconds) < SameMoment);
        _keys.Add(new LayerKey(levelId, seconds, visible));
        _keys.Sort((a, b) => a.Seconds.CompareTo(b.Seconds));
    }

    public void Remove(LayerKey key) => _keys.Remove(key);

    public void RemoveLevel(Guid levelId) => _keys.RemoveAll(key => key.LevelId == levelId);

    public void Clear() => _keys.Clear();

    /// <summary>Forgets the keys of levels the project no longer has.</summary>
    public void KeepLevels(IReadOnlyCollection<Guid> levelIds) => _keys.RemoveAll(key => !levelIds.Contains(key.LevelId));

    /// <summary>The levels that are not shown at a time of the movie.</summary>
    public HashSet<Guid> HiddenAt(float seconds)
    {
        var hidden = new HashSet<Guid>();
        foreach (var level in _keys.GroupBy(key => key.LevelId))
        {
            var keys = level.OrderBy(key => key.Seconds).ToList();
            var visible = !keys[0].Visible;
            foreach (var key in keys)
            {
                if (key.Seconds > seconds) break;
                visible = key.Visible;
            }
            if (!visible) hidden.Add(level.Key);
        }
        return hidden;
    }

    /// <summary>A name for a set of hidden levels, equal for equal sets, so frames with the same look share one scene.</summary>
    public static string StateKey(IEnumerable<Guid> hidden) =>
        string.Join(',', hidden.Order().Select(id => id.ToString("N")));
}

/// <summary>Takes whole levels out of a document, for the frames of a movie in which those levels are hidden.</summary>
public static class LevelVisibility
{
    /// <summary>
    /// Removes what belongs to the hidden levels from <paramref name="document"/> itself (pass a copy): walls and their
    /// openings, floors, roofs, columns, structural elements and rooms. Site, terrain and building services stay.
    /// </summary>
    public static void Hide(BuildingDocument document, IReadOnlySet<Guid> hiddenLevels)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (hiddenLevels.Count == 0) return;
        document.Walls.RemoveAll(wall => hiddenLevels.Contains(wall.LevelId));
        var walls = document.Walls.Select(wall => wall.Id).ToHashSet();
        document.Openings.RemoveAll(opening => !walls.Contains(opening.WallId));
        document.Floors.RemoveAll(floor => hiddenLevels.Contains(floor.LevelId));
        document.Columns.RemoveAll(column => hiddenLevels.Contains(column.LevelId));
        document.StructuralElements.RemoveAll(element => hiddenLevels.Contains(element.LevelId));
        document.Rooms.RemoveAll(room => hiddenLevels.Contains(room.LevelId));
    }
}
