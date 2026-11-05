namespace Guanacaste3D.Core;

/// <summary>A snapshot of walls and their hosted openings, taken to paste later, even on another level.</summary>
public sealed record WallClipboard(IReadOnlyList<Wall> Walls, IReadOnlyList<WallOpening> Openings)
{
    /// <summary>Copies the listed walls and the openings they host out of a document.</summary>
    public static WallClipboard Capture(BuildingDocument document, IReadOnlyCollection<Guid> wallIds)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(wallIds);
        var ids = wallIds.ToHashSet();
        var walls = document.Walls.Where(wall => ids.Contains(wall.Id)).ToArray();
        if (walls.Length == 0) throw new InvalidDataException("No walls were selected.");
        return new WallClipboard(walls, document.Openings.Where(opening => ids.Contains(opening.WallId)).ToArray());
    }
}

public sealed partial class DocumentSession
{
    /// <summary>
    /// Pastes clipboard walls and their openings as new elements, moved by a distance and placed on a level. Pasting
    /// to another level drops the top constraint, which pointed at a level of the source, and keeps the wall height.
    /// Returns the new walls.
    /// </summary>
    public IReadOnlyList<Guid> PasteWalls(WallClipboard clipboard, Guid targetLevelId, double deltaX, double deltaY)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY) || Math.Abs(deltaX) > 100000 || Math.Abs(deltaY) > 100000)
            throw new InvalidDataException("The paste distance is invalid.");
        var created = new List<Guid>();
        Commit("Transaction.PasteWalls", document =>
        {
            if (clipboard.Walls.Count == 0) throw new InvalidDataException("No walls were selected.");
            if (document.Levels.All(level => level.Id != targetLevelId)) throw new InvalidDataException("The level does not exist.");
            if (document.Walls.Count + clipboard.Walls.Count > 100000)
                throw new InvalidDataException("The document exceeds the supported element limits.");
            var types = document.WallTypes.Select(type => type.Id).ToHashSet();
            if (clipboard.Walls.Any(wall => !types.Contains(wall.WallTypeId)))
                throw new InvalidDataException("The selected wall type does not exist.");

            var newIds = clipboard.Walls.ToDictionary(wall => wall.Id, _ => Guid.NewGuid());
            var curves = new Dictionary<Guid, Guid>();
            foreach (var wall in clipboard.Walls)
            {
                var sameLevel = wall.LevelId == targetLevelId;
                document.Walls.Add(CurveGroups.Remap(wall, curves) with
                {
                    Id = newIds[wall.Id], LevelId = targetLevelId,
                    Start = new Point2(wall.Start.X + deltaX, wall.Start.Y + deltaY),
                    End = new Point2(wall.End.X + deltaX, wall.End.Y + deltaY),
                    TopLevelId = sameLevel ? wall.TopLevelId : null,
                    TopOffset = sameLevel ? wall.TopOffset : 0,
                    GridAxisId = null, GridAxisAssociationDisabled = true, Pinned = false
                });
                created.Add(newIds[wall.Id]);
            }
            foreach (var opening in clipboard.Openings.Where(opening => newIds.ContainsKey(opening.WallId)))
                document.Openings.Add(opening with { Id = Guid.NewGuid(), WallId = newIds[opening.WallId] });
            RefreshRoomBoundariesIfNeeded(document, targetLevelId);
        });
        return created;
    }
}
