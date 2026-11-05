namespace Guanacaste3D.Core;

public sealed partial class DocumentSession
{
    /// <summary>
    /// Sets how the ends of the given walls join the walls they meet, as one undoable step. Without
    /// <paramref name="end"/> both ends of each wall change. A wall that butts stops against its neighbour, so the
    /// neighbour's end stops butting and runs through instead.
    /// </summary>
    public void SetWallJoins(IReadOnlyCollection<Guid> wallIds, WallJoinSetting setting, WallEnd? end = null)
    {
        ArgumentNullException.ThrowIfNull(wallIds);
        if (!Enum.IsDefined(setting)) throw new InvalidDataException("The wall join setting is invalid.");
        var ids = wallIds.Distinct().ToArray();
        if (ids.Length == 0) return;
        var known = Document.Walls.Select(wall => wall.Id).ToHashSet();
        if (ids.Any(id => !known.Contains(id))) throw new InvalidDataException("The wall does not exist.");

        WallEnd[] ends = end is { } only ? [only] : [WallEnd.Start, WallEnd.End];
        Commit("Transaction.SetWallJoin", document =>
        {
            // Neighbours are resolved before the change, while the corner still exists.
            var corners = WallJoinIndex.Build(document.Walls);
            foreach (var id in ids)
            foreach (var wallEnd in ends)
            {
                SetJoin(document, id, wallEnd, setting);
                if (setting == WallJoinSetting.Butt && corners.JoinAt(id, wallEnd) is { } neighbour &&
                    WallJoinIndex.SettingAt(neighbour.Other, neighbour.OtherEnd) == WallJoinSetting.Butt)
                    SetJoin(document, neighbour.Other.Id, neighbour.OtherEnd, WallJoinSetting.Miter);
            }
        });
    }

    /// <summary>Stores one join setting on one wall end.</summary>
    private static void SetJoin(BuildingDocument document, Guid wallId, WallEnd end, WallJoinSetting setting)
    {
        var index = document.Walls.FindIndex(wall => wall.Id == wallId);
        var wall = document.Walls[index];
        document.Walls[index] = end == WallEnd.Start ? wall with { StartJoin = setting } : wall with { EndJoin = setting };
    }
}
