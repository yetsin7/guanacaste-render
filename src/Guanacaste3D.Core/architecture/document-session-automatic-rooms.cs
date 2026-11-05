namespace Guanacaste3D.Core;

/// <summary>
/// Keeps automatically discovered rectangular rooms in sync with their current boundaries.
/// Manually placed rooms and older, unmarked project rooms are never changed by this service.
/// </summary>
public sealed partial class DocumentSession
{
    /// <summary>
    /// Discover new rooms and reconcile existing automatic rooms in one undoable transaction.
    /// Matching uses the largest shared floor area to preserve a room's GUID, number and edited name
    /// when a separator moves. When two rooms merge, the best-matching identity survives.
    /// </summary>
    public int SynchronizeAutomaticRooms(Guid levelId)
    {
        if (!Document.Levels.Any(level => level.Id == levelId))
            throw new InvalidDataException("The room level does not exist.");

        // Build a complete proposed result first, so an idempotent refresh adds no undo step.
        var proposed = Document.Clone();
        var created = RebuildAutomaticRooms(proposed, levelId);
        if (Document.Rooms.Count == proposed.Rooms.Count &&
            Document.Rooms.Zip(proposed.Rooms).All(pair => SameRoom(pair.First, pair.Second)))
            return 0;

        Commit("Transaction.SyncAutomaticRooms", document =>
        {
            document.Rooms = proposed.Rooms;
        });
        return created;
    }

    /// <summary>Compares point contents so deep-cloned outlines do not create spurious undo history.</summary>
    private static bool SameRoom(Room a, Room b) =>
        a.Id == b.Id && a.LevelId == b.LevelId &&
        a.Number == b.Number && a.Name == b.Name &&
        a.FirstCorner == b.FirstCorner && a.SecondCorner == b.SecondCorner &&
        a.IsAutomatic == b.IsAutomatic &&
        (a.Boundary is null
            ? b.Boundary is null
            : b.Boundary is not null && a.Boundary.SequenceEqual(b.Boundary)) &&
        (a.Holes is null || a.Holes.Count == 0
            ? b.Holes is null || b.Holes.Count == 0
            : b.Holes is not null && a.Holes.Count == b.Holes.Count &&
              a.Holes.Zip(b.Holes).All(pair => pair.First.SequenceEqual(pair.Second)));

    /// <summary>
    /// Called by all geometry-changing wall transactions. An automatic room follows its
    /// wall boundaries; manually drawn rooms and older project annotations never move.
    /// </summary>
    private static void RefreshRoomBoundariesIfNeeded(BuildingDocument document, Guid levelId)
    {
        if (document.Rooms.Any(room => room.IsAutomatic && room.LevelId == levelId))
            RebuildAutomaticRooms(document, levelId);
    }

    /// <summary>
    /// Reconciles automatic rooms directly within an already open atomic transaction, for changes
    /// to room separators and other enclosing elements. Never call Commit from this helper.
    /// </summary>
    private static int RebuildAutomaticRooms(BuildingDocument document, Guid levelId)
    {
        var protectedRooms = document.Rooms.Where(room => room.LevelId == levelId && !room.IsAutomatic).ToArray();
        var automatic = document.Rooms.Where(room => room.LevelId == levelId && room.IsAutomatic).ToArray();
        // Keep nested detected faces available for matching an EXISTING room. Adding another
        // wall loop inside a room must not silently erase its identity, name and schedule
        // simply because the new topology marks that face as a nested courtyard.
        // An unmatched nested face is still a courtyard by default, not a new automatic room.
        var regions = RoomPolygonDetection.Detect(document, levelId)
            .Where(region => protectedRooms.All(room =>
                RoomRegionGeometry.IntersectionArea(room, region) < 1e-8))
            .ToArray();

        var candidates = (
            from oldIndex in Enumerable.Range(0, automatic.Length)
            from regionIndex in Enumerable.Range(0, regions.Length)
            let area = RoomRegionGeometry.IntersectionArea(automatic[oldIndex], regions[regionIndex])
            where area > 1e-8
            orderby area descending, oldIndex, regionIndex
            select (oldIndex, regionIndex)
        ).ToArray();

        var usedRooms = new HashSet<int>();
        var usedRegions = new HashSet<int>();
        var replacements = new Dictionary<Guid, Room>();
        foreach (var (oldIndex, regionIndex) in candidates)
        {
            if (!usedRooms.Add(oldIndex)) continue;
            if (!usedRegions.Add(regionIndex))
            {
                usedRooms.Remove(oldIndex);
                continue;
            }
            var old = automatic[oldIndex];
            replacements.Add(old.Id, old with
            {
                FirstCorner = regions[regionIndex].Min,
                SecondCorner = regions[regionIndex].Max,
                Boundary = [.. regions[regionIndex].Boundary],
                Holes = regions[regionIndex].Holes.Select(hole => hole.ToList()).ToList()
            });
        }

        // Only automatic rooms without a valid enclosure disappear. Manual room annotations stay.
        var kept = document.Rooms
            .Where(room => room.LevelId != levelId || !room.IsAutomatic || replacements.ContainsKey(room.Id))
            .Select(room => replacements.TryGetValue(room.Id, out var updated) ? updated : room)
            .ToList();
        var usedNumbers = kept.Select(room => room.Number).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nextNumber = 1;
        var created = 0;
        for (var index = 0; index < regions.Length; index++)
        {
            if (usedRegions.Contains(index) || regions[index].IsCourtyard) continue;
            string number;
            do
            {
                number = nextNumber.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
                nextNumber++;
            } while (!usedNumbers.Add(number));

            var region = regions[index];
            kept.Add(new Room(Guid.NewGuid(), levelId, number, $"Room {number}", region.Min, region.Max)
            {
                IsAutomatic = true,
                Boundary = [.. region.Boundary],
                Holes = region.Holes.Select(hole => hole.ToList()).ToList()
            });
            created++;
        }
        document.Rooms = kept;
        return created;
    }
}
