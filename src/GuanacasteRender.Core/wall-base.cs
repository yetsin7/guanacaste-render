namespace Guanacaste3D.Core;

public sealed partial class DocumentSession
{
    /// <summary>
    /// Sets where the base of a wall is, in meters from its level: positive lifts it (a wall on a plinth), negative takes it down
    /// (a wall that goes to its footing). A door or window may not start below the base, and the top may not come lower than it.
    /// </summary>
    public void SetWallBaseOffset(Guid wallId, double offset)
    {
        var wall = Document.Walls.FirstOrDefault(item => item.Id == wallId) ?? throw new InvalidDataException("The wall does not exist.");
        if (!double.IsFinite(offset) || offset < -50 || offset > wall.Height - 0.1) throw new InvalidDataException("The wall base is invalid.");
        if (Math.Abs(offset - wall.BaseOffset) < 1e-9) return;
        if (offset > 0 && Document.Openings.Any(opening => opening.WallId == wallId && opening.Sill < offset - 1e-9))
            throw new InvalidDataException("The base would cut below a door or window of the wall.");
        if (offset > 0 && wall.TopProfile is { } profile && profile.Min(point => point.Y) < offset + WallProfiles.MinimumHeight)
            throw new InvalidDataException("The profile cuts below the base of the wall.");
        Commit("Transaction.SetWallBaseOffset", document =>
        {
            EnsureNotPinned(document, wallId);
            var index = document.Walls.FindIndex(item => item.Id == wallId);
            document.Walls[index] = document.Walls[index] with { BaseOffset = offset };
        });
    }

    /// <summary>
    /// Takes the base of a wall to the top of a flat slab under the middle of the wall: the nearest one below the base first (a
    /// footing the wall must go down to), otherwise the nearest one above it that is still inside the wall. Returns that slab.
    /// </summary>
    public Guid AttachWallBaseToSlab(Guid wallId)
    {
        var wall = Document.Walls.FirstOrDefault(item => item.Id == wallId) ?? throw new InvalidDataException("The wall does not exist.");
        var levels = Document.Levels.ToDictionary(level => level.Id);
        var baseElevation = levels[wall.LevelId].Elevation;
        var middle = new Point2((wall.Start.X + wall.End.X) / 2, (wall.Start.Y + wall.End.Y) / 2);
        var tops = Document.Floors
            .Where(slab => !slab.IsToposolid && !slab.IsPitched && slab.Kind != FloorKind.Water && slab.Boundary.Count >= 3 && RoomRegionGeometry.Contains(slab.Boundary, middle))
            .Select(slab => (Slab: slab, Top: levels[slab.LevelId].Elevation + slab.ElevationOffset - baseElevation))
            .Where(item => Math.Abs(item.Top - wall.BaseOffset) > 0.005 && item.Top >= -50 && item.Top <= wall.Height - 0.1)
            .ToList();
        var below = tops.Where(item => item.Top < wall.BaseOffset).OrderByDescending(item => item.Top).ToList();
        var chosen = below.Count > 0 ? below[0] : tops.OrderBy(item => item.Top).FirstOrDefault();
        if (chosen.Slab is null) throw new InvalidDataException("There is no slab to attach the base of the wall to.");
        SetWallBaseOffset(wallId, chosen.Top);
        return chosen.Slab.Id;
    }
}
