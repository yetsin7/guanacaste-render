using System.Reflection;
using System.Text.Json;

namespace Guanacaste3D.Core;

/// <summary>One thing that two people changed in different ways, and what was done about it.</summary>
public sealed record MergeConflict(string Collection, Guid Id, string Description, string Resolution);

/// <summary>What a merge did: how many elements came from the other copy and how many of mine were kept, and the conflicts.</summary>
public sealed record MergeReport(int TakenFromCentral, int KeptFromMine, int Conflicts, IReadOnlyList<MergeConflict> Details);

/// <summary>The result of merging two copies of a project that grew apart from a common one.</summary>
public sealed record MergeResult(BuildingDocument Document, MergeReport Report);

/// <summary>
/// Three-way merge of two copies of a project that started from the same one (<c>baseline</c>): what only one side changed
/// is taken, and what both changed in different ways is a conflict. It works list by list, element by element, by
/// identifier, so two people who edit different walls both keep their work.
/// </summary>
public static class ModelMerge
{
    private static readonly JsonSerializerOptions Json = new();

    private static string Text(object? value) => JsonSerializer.Serialize(value, value?.GetType() ?? typeof(object), Json);

    /// <summary>The lists of the document whose elements carry an <c>Id</c>: the ones merged element by element.</summary>
    private static IEnumerable<PropertyInfo> KeyedLists() => typeof(BuildingDocument).GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(List<>) &&
                           !property.PropertyType.GetGenericArguments()[0].IsValueType && KeyOf(property.PropertyType.GetGenericArguments()[0]) is not null);

    private static PropertyInfo? KeyOf(Type element)
    {
        var key = element.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
        return key?.PropertyType == typeof(Guid) ? key : null;
    }

    private static Dictionary<Guid, (object Item, string Text)> Index(object document, PropertyInfo list, PropertyInfo key)
    {
        var result = new Dictionary<Guid, (object, string)>();
        if (list.GetValue(document) is System.Collections.IEnumerable items)
            foreach (var item in items) result[(Guid)key.GetValue(item)!] = (item, Text(item));
        return result;
    }

    /// <summary>The elements that differ between two copies of the project: the ids added, changed and removed, by list.</summary>
    public static IReadOnlyDictionary<string, (HashSet<Guid> Added, HashSet<Guid> Changed, HashSet<Guid> Removed)> Differences(BuildingDocument from, BuildingDocument to)
    {
        var result = new Dictionary<string, (HashSet<Guid>, HashSet<Guid>, HashSet<Guid>)>();
        foreach (var list in KeyedLists())
        {
            var key = KeyOf(list.PropertyType.GetGenericArguments()[0])!;
            var before = Index(from, list, key);
            var after = Index(to, list, key);
            var added = after.Keys.Where(id => !before.ContainsKey(id)).ToHashSet();
            var removed = before.Keys.Where(id => !after.ContainsKey(id)).ToHashSet();
            var changed = after.Keys.Where(id => before.TryGetValue(id, out var old) && old.Item2 != after[id].Item2).ToHashSet();
            if (added.Count + removed.Count + changed.Count > 0) result[list.Name] = (added, changed, removed);
        }
        return result;
    }

    /// <summary>
    /// Merges <paramref name="mine"/> and <paramref name="theirs"/>, both descended from <paramref name="baseline"/>. When both
    /// changed the same element, mine is kept, unless <paramref name="theirsWin"/> says the other copy owns it.
    /// </summary>
    public static MergeResult Merge(BuildingDocument baseline, BuildingDocument mine, BuildingDocument theirs, Func<string, Guid, bool>? theirsWin = null)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(mine);
        ArgumentNullException.ThrowIfNull(theirs);
        var merged = theirs.Clone();
        var conflicts = new List<MergeConflict>();
        int fromCentral = 0, fromMine = 0;

        foreach (var list in KeyedLists())
        {
            var elementType = list.PropertyType.GetGenericArguments()[0];
            var key = KeyOf(elementType)!;
            var baseIndex = Index(baseline, list, key);
            var mineIndex = Index(mine, list, key);
            var theirIndex = Index(theirs, list, key);
            var result = (System.Collections.IList)Activator.CreateInstance(list.PropertyType)!;
            var order = theirIndex.Keys.Concat(mineIndex.Keys.Where(id => !theirIndex.ContainsKey(id))).Concat(baseIndex.Keys.Where(id => !theirIndex.ContainsKey(id) && !mineIndex.ContainsKey(id))).Distinct();
            foreach (var id in order)
            {
                baseIndex.TryGetValue(id, out var b);
                mineIndex.TryGetValue(id, out var m);
                theirIndex.TryGetValue(id, out var t);
                var inBase = b.Item is not null;
                var mineChanged = inBase ? m.Item is null || m.Text != b.Text : m.Item is not null;
                var theirsChanged = inBase ? t.Item is null || t.Text != b.Text : t.Item is not null;
                object? chosen;
                if (!mineChanged && !theirsChanged) chosen = b.Item ?? t.Item ?? m.Item;
                else if (mineChanged && !theirsChanged) { chosen = m.Item; fromMine++; }
                else if (!mineChanged) { chosen = t.Item; fromCentral++; }
                else if ((m.Item is null && t.Item is null) || (m.Item is not null && t.Item is not null && m.Text == t.Text)) chosen = t.Item;
                else
                {
                    // Both changed it differently.
                    var wins = theirsWin?.Invoke(list.Name, id) == true;
                    chosen = wins ? t.Item : t.Item is null ? m.Item : m.Item is null ? t.Item : m.Item;
                    var keptMine = ReferenceEquals(chosen, m.Item);
                    conflicts.Add(new MergeConflict(list.Name, id,
                        m.Item is null ? "DeletedHere" : t.Item is null ? "DeletedThere" : "EditedBoth", keptMine ? "KeptMine" : "KeptCentral"));
                    if (keptMine) fromMine++; else fromCentral++;
                }
                if (chosen is not null) result.Add(chosen);
            }
            list.SetValue(merged, result);
        }

        // Everything else is taken whole: mine when I changed it since the common copy, the other copy's otherwise.
        foreach (var property in typeof(BuildingDocument).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite || property.Name is "Id" or "SchemaVersion" || KeyedLists().Any(list => list.Name == property.Name)) continue;
            var mineText = Text(property.GetValue(mine));
            if (mineText != Text(property.GetValue(baseline)) && mineText != Text(property.GetValue(theirs)))
            {
                property.SetValue(merged, property.GetValue(mine));
                fromMine++;
            }
        }

        // Clean what the merge can leave dangling: doors and windows whose wall was removed on the other side.
        merged.Openings.RemoveAll(opening => merged.Walls.All(wall => wall.Id != opening.WallId));
        merged.Dimensions.RemoveAll(dimension => PlanDimensions.Resolve(merged, dimension) is null);
        ElementGroups.Prune(merged);
        return new MergeResult(merged, new MergeReport(fromCentral, fromMine, conflicts.Count, conflicts));
    }
}
