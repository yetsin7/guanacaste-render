namespace Guanacaste3D.Core;

public sealed partial class DocumentSession
{
    /// <summary>
    /// Removes walls (with their openings), openings, slabs, columns and structural elements (with their groups and
    /// railings) as one undoable step. Returns how many of the given ids were found.
    /// </summary>
    public int RemoveElements(IReadOnlyCollection<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var chosen = ids.ToHashSet();
        // Deleting one member of a group deletes the whole group, as it does for the structural groups.
        foreach (var group in Document.ElementGroups.Where(item => item.Kind != ElementGroupKind.Assembly && item.MemberIds.Any(chosen.Contains)).ToArray())
            chosen.UnionWith(group.MemberIds);
        var found = Document.Walls.Count(item => chosen.Contains(item.Id)) +
                    Document.Openings.Count(item => chosen.Contains(item.Id)) +
                    Document.Floors.Count(item => chosen.Contains(item.Id)) +
                    Document.Columns.Count(item => chosen.Contains(item.Id)) +
                    Document.StructuralElements.Count(item => chosen.Contains(item.Id));
        if (found == 0) return 0;
        Commit("Transaction.RemoveElements", document =>
        {
            var levels = document.Walls.Where(item => chosen.Contains(item.Id)).Select(item => item.LevelId).ToHashSet();
            var groups = document.StructuralElements.Where(item => chosen.Contains(item.Id) && item.GroupId is not null)
                .Select(item => item.GroupId!.Value).ToHashSet();
            var removedStructural = document.StructuralElements
                .Where(item => chosen.Contains(item.Id) || (item.GroupId is Guid group && groups.Contains(group)))
                .Select(item => item.Id).ToHashSet();
            levels.UnionWith(document.StructuralElements
                .Where(item => removedStructural.Contains(item.Id) && item.Kind == StructuralElementKind.RoomSeparator)
                .Select(item => item.LevelId));

            document.Openings.RemoveAll(item => chosen.Contains(item.Id) || chosen.Contains(item.WallId));
            document.Walls.RemoveAll(item => chosen.Contains(item.Id));
            document.Floors.RemoveAll(item => chosen.Contains(item.Id));
            document.Columns.RemoveAll(item => chosen.Contains(item.Id));
            document.StructuralElements.RemoveAll(item => removedStructural.Contains(item.Id) ||
                (item.RailingHostId is Guid host && removedStructural.Contains(host)));
            foreach (var level in levels) RefreshRoomBoundariesIfNeeded(document, level);
        });
        return found;
    }
}
