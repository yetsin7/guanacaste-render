namespace Guanacaste3D.Core;

/// <summary>Commands that act on many elements at once: scale, renumber rooms, find and replace text.</summary>
public sealed partial class DocumentSession
{
    /// <summary>Smallest and largest scale factor one command accepts.</summary>
    public const double MinimumScale = 0.1;
    public const double MaximumScale = 10;

    /// <summary>
    /// Scales the position of walls about a point: their ends move toward or away from it by the factor, so a wall is
    /// longer or shorter and farther or nearer. Thickness and height are the wall type's and do not change, and doors
    /// and windows keep their size and their relative place along the wall. Walls that met a scaled wall stretch to stay
    /// joined, so scaling a whole connected set keeps every corner.
    /// </summary>
    public void ScaleWalls(IReadOnlyCollection<Guid> wallIds, Point2 origin, double factor)
    {
        if (!double.IsFinite(factor) || factor is < MinimumScale or > MaximumScale)
            throw new InvalidDataException("The scale factor is invalid.");
        if (!double.IsFinite(origin.X) || !double.IsFinite(origin.Y))
            throw new InvalidDataException("The scale origin is invalid.");
        Commit("Transaction.ScaleWalls", document =>
        {
            var ids = RequireWalls(document, wallIds);
            foreach (var id in ids) EnsureNotPinned(document, id);
            var before = WallJoinIndex.Build(document.Walls);
            Point2 Scaled(Point2 point) => new(origin.X + (point.X - origin.X) * factor, origin.Y + (point.Y - origin.Y) * factor);
            foreach (var id in ids)
            {
                var index = document.Walls.FindIndex(wall => wall.Id == id);
                var wall = document.Walls[index];
                document.Walls[index] = wall with
                {
                    Start = Scaled(wall.Start), End = Scaled(wall.End), GridAxisId = null, GridAxisAssociationDisabled = true
                };
                for (var opening = 0; opening < document.Openings.Count; opening++)
                    if (document.Openings[opening].WallId == id)
                        document.Openings[opening] = document.Openings[opening] with { Offset = document.Openings[opening].Offset * factor };
            }
            WallReferenceLine.ReconnectCorners(document, WallReferenceLine.IndexOf(document.Walls), before, ids, null);
            foreach (var level in ids.Select(id => document.Walls.Single(wall => wall.Id == id).LevelId).Distinct())
                RefreshRoomBoundariesIfNeeded(document, level);
        });
    }

    /// <summary>
    /// Gives every room a number made of its level's position and its order on that level, from south to north and
    /// west to east: the first room of the second level becomes 201. Numbers are unique across the project. Returns how
    /// many rooms changed number.
    /// </summary>
    public int RenumberRooms()
    {
        var levelOrder = Document.Levels.OrderBy(level => level.Elevation).Select((level, index) => (level.Id, index))
            .ToDictionary(pair => pair.Id, pair => pair.index);
        var counters = new Dictionary<Guid, int>();
        var renumbered = new Dictionary<Guid, string>();
        foreach (var room in Document.Rooms.Select((room, index) => (room, index))
                     .OrderBy(entry => levelOrder.GetValueOrDefault(entry.room.LevelId, int.MaxValue))
                     .ThenBy(entry => Math.Round(entry.room.Center.Y, 3)).ThenBy(entry => Math.Round(entry.room.Center.X, 3))
                     .ThenBy(entry => entry.index).Select(entry => entry.room))
        {
            counters[room.LevelId] = counters.GetValueOrDefault(room.LevelId) + 1;
            var number = $"{levelOrder.GetValueOrDefault(room.LevelId) + 1}{counters[room.LevelId]:00}";
            if (room.Number != number) renumbered[room.Id] = number;
        }
        if (renumbered.Count == 0) return 0;
        Commit("Transaction.RenumberRooms", document =>
        {
            for (var index = 0; index < document.Rooms.Count; index++)
                if (renumbered.TryGetValue(document.Rooms[index].Id, out var number))
                    document.Rooms[index] = document.Rooms[index] with { Number = number };
        });
        return renumbered.Count;
    }

    /// <summary>
    /// Replaces text in the names of rooms and sheets and in the model text of the plan. Returns how many places
    /// changed. A replacement that would leave a name empty is refused and changes nothing.
    /// </summary>
    public int ReplaceText(string find, string replacement, bool matchCase = false)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (string.IsNullOrEmpty(find)) throw new InvalidDataException("There is no text to find.");
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var changed = 0;
        string Replace(string text)
        {
            var result = text.Replace(find, replacement, comparison);
            if (result != text) changed++;
            return result;
        }

        bool Contains(string text) => text.Contains(find, comparison);
        if (!Document.Rooms.Any(room => Contains(room.Name)) && !Document.Sheets.Any(sheet => Contains(sheet.Name)) &&
            !Document.StructuralElements.Any(element => element.Kind == StructuralElementKind.ModelText && Contains(element.Name)))
            return 0;

        Commit("Transaction.ReplaceText", document =>
        {
            for (var index = 0; index < document.Rooms.Count; index++)
                document.Rooms[index] = document.Rooms[index] with { Name = Replace(document.Rooms[index].Name) };
            for (var index = 0; index < document.Sheets.Count; index++)
                document.Sheets[index] = document.Sheets[index] with { Name = Replace(document.Sheets[index].Name) };
            for (var index = 0; index < document.StructuralElements.Count; index++)
                if (document.StructuralElements[index].Kind == StructuralElementKind.ModelText)
                {
                    var text = Replace(document.StructuralElements[index].Name);
                    if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("Model text cannot be blank.");
                    document.StructuralElements[index] = document.StructuralElements[index] with { Name = text };
                }
            if (document.Rooms.Any(room => room.Name.Length > 120) || document.Sheets.Any(sheet => string.IsNullOrWhiteSpace(sheet.Name)))
                throw new InvalidDataException("The replacement leaves a name that is empty or too long.");
        });
        return changed;
    }
}

public sealed partial class DocumentSession
{
    /// <summary>
    /// Ties the top of a wall to the underside of the flat slab, ceiling or roof that lies directly above the middle of
    /// the wall, nearest first. The wall keeps following that slab's level when the level moves; pitched roofs are not
    /// followed along their slope. Returns the slab the wall was attached to.
    /// </summary>
    public Guid AttachWallTopToSlabAbove(Guid wallId)
    {
        var attachedTo = Guid.Empty;
        Commit("Transaction.AttachWallTop", document =>
        {
            EnsureNotPinned(document, wallId);
            var index = document.Walls.FindIndex(wall => wall.Id == wallId);
            if (index < 0) throw new InvalidDataException("The wall does not exist.");
            var wall = document.Walls[index];
            var levels = document.Levels.ToDictionary(level => level.Id);
            var baseElevation = levels[wall.LevelId].Elevation;
            var middle = new Point2((wall.Start.X + wall.End.X) / 2, (wall.Start.Y + wall.End.Y) / 2);

            var candidates = document.Floors
                .Where(slab => !slab.IsToposolid && !slab.IsPitched && slab.Kind != FloorKind.Water && slab.Boundary.Count >= 3 &&
                               RoomRegionGeometry.Contains(slab.Boundary, middle))
                .Select(slab => (Slab: slab, Underside: levels[slab.LevelId].Elevation + slab.ElevationOffset - slab.Thickness))
                .Where(candidate => candidate.Underside > baseElevation + 0.1)
                .OrderBy(candidate => candidate.Underside)
                .ToArray();
            if (candidates.Length == 0) throw new InvalidDataException("There is no slab above the wall to attach it to.");

            var (slab, underside) = candidates[0];
            document.Walls[index] = wall with
            {
                TopLevelId = slab.LevelId,
                TopOffset = underside - levels[slab.LevelId].Elevation
            };
            attachedTo = slab.Id;
        });
        return attachedTo;
    }
}
