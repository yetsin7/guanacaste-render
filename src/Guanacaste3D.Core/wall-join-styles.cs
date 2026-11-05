namespace Guanacaste3D.Core;

/// <summary>How one end of a wall treats the wall it meets.</summary>
public enum WallJoinSetting
{
    /// <summary>Both faces meet on one line at the corner. This is the default.</summary>
    Miter,

    /// <summary>This wall stops against the face of the other wall, which runs through the corner.</summary>
    Butt,

    /// <summary>The end stays square at the centerline corner and the walls overlap there.</summary>
    SquareOff,

    /// <summary>The end never joins: not at a corner and not at a tee.</summary>
    Disallowed
}

public sealed partial class WallJoinIndex
{
    /// <summary>The part a wall end plays in a butt corner.</summary>
    private enum EndRole
    {
        /// <summary>The end stops at the near face of the other wall.</summary>
        Abut,

        /// <summary>The end runs on to the far face of the other wall, covering the corner.</summary>
        Through
    }

    private readonly Dictionary<(Guid WallId, WallEnd End), EndRole> _roles = [];
    private readonly Dictionary<(Guid WallId, WallEnd End), Wall> _tees = [];

    /// <summary>Wall ends that rest on the body of another wall, with the wall they rest on.</summary>
    public IEnumerable<(Guid WallId, WallEnd End, Wall Host)> Tees =>
        _tees.Select(entry => (entry.Key.WallId, entry.Key.End, entry.Value));

    /// <summary>Returns the wall whose body a free wall end rests on, if any.</summary>
    public Wall? TeeHostAt(Guid wallId, WallEnd end) => _tees.TryGetValue((wallId, end), out var host) ? host : null;

    /// <summary>Returns the join setting stored on one end of a wall.</summary>
    public static WallJoinSetting SettingAt(Wall wall, WallEnd end) => end == WallEnd.Start ? wall.StartJoin : wall.EndJoin;

    /// <summary>
    /// Applies the per-end settings to the corners found by endpoint matching, then finds the tees: free ends
    /// that rest on the body of one other wall.
    /// </summary>
    private void ResolveStyles(IReadOnlyList<Wall> walls, HashSet<(Guid WallId, WallEnd End)> touching)
    {
        var byId = new Dictionary<Guid, Wall>(walls.Count);
        foreach (var wall in walls) byId.TryAdd(wall.Id, wall);

        foreach (var entry in _joins.ToArray())
        {
            var own = SettingAt(byId[entry.Key.WallId], entry.Key.End);
            var other = SettingAt(entry.Value.Other, entry.Value.OtherEnd);
            if (own is WallJoinSetting.SquareOff or WallJoinSetting.Disallowed ||
                other is WallJoinSetting.SquareOff or WallJoinSetting.Disallowed)
            {
                _joins.Remove(entry.Key);
                continue;
            }
            // When both ends ask to butt, neither is the one that stops, so the corner stays mitered.
            if (own == WallJoinSetting.Butt && other != WallJoinSetting.Butt)
            {
                _roles[entry.Key] = EndRole.Abut;
                _roles[(entry.Value.Other.Id, entry.Value.OtherEnd)] = EndRole.Through;
            }
        }

        SegmentGrid? grid = null;
        foreach (var wall in walls)
        foreach (var end in new[] { WallEnd.Start, WallEnd.End })
        {
            if (wall.Length < Tolerance || touching.Contains((wall.Id, end)) ||
                SettingAt(wall, end) is WallJoinSetting.SquareOff or WallJoinSetting.Disallowed)
                continue;
            grid ??= SegmentGrid.Build(walls);
            if (FindHost(wall, end, grid) is { } host) _tees[(wall.Id, end)] = host;
        }
    }

    /// <summary>Returns the only wall whose body a free end rests on, or null when there is none or more than one.</summary>
    private static Wall? FindHost(Wall wall, WallEnd end, SegmentGrid grid)
    {
        var corner = CornerAt(wall, end);
        var (directionX, directionY) = Direction(wall);
        Wall? found = null;
        foreach (var other in grid.Near(wall.LevelId, corner))
        {
            if (other.Id == wall.Id || other.Length < Tolerance || found?.Id == other.Id) continue;
            var (otherX, otherY) = Direction(other);
            var along = (corner.X - other.Start.X) * otherX + (corner.Y - other.Start.Y) * otherY;
            if (along <= Tolerance || along >= other.Length - Tolerance) continue;
            var across = -(corner.X - other.Start.X) * otherY + (corner.Y - other.Start.Y) * otherX;
            if (Math.Abs(across) > Tolerance || Math.Abs(directionX * otherY - directionY * otherX) < 1e-3) continue;
            if (found is not null) return null;
            found = other;
        }
        return found;
    }

    /// <summary>Returns the faces of a butt or tee end, or false when the end is plain or mitered.</summary>
    private bool TryStyledFaces(Wall wall, WallEnd end, out (Point2 Left, Point2 Right) faces)
    {
        var key = (wall.Id, end);
        Wall? target;
        var near = true;
        if (_tees.TryGetValue(key, out var host))
            target = host;
        else if (_roles.TryGetValue(key, out var role) && _joins.TryGetValue(key, out var join))
        {
            target = join.Other;
            near = role == EndRole.Abut;
        }
        else
        {
            faces = default;
            return false;
        }

        if (CutAgainst(wall, end, target, near) is { } cut)
        {
            faces = cut;
            return true;
        }
        var (normalX, normalY) = LeftNormal(wall);
        faces = Square(CornerAt(wall, end), normalX * wall.Thickness / 2, normalY * wall.Thickness / 2);
        return true;
    }

    /// <summary>
    /// Ends a wall on one face of another: the face on the side of the wall's own body (near), or the one on the
    /// far side (the wall runs on to cover the corner). Returns null when the walls are almost parallel or the cut
    /// would reach far along the wall.
    /// </summary>
    private static (Point2 Left, Point2 Right)? CutAgainst(Wall wall, WallEnd end, Wall other, bool nearFace)
    {
        var corner = CornerAt(wall, end);
        var (directionX, directionY) = Direction(wall);
        var (otherX, otherY) = Direction(other);
        var (otherNormalX, otherNormalY) = LeftNormal(other);
        // The wall's body lies on the side of "other" that its inward direction points to.
        var inwardX = end == WallEnd.Start ? directionX : -directionX;
        var inwardY = end == WallEnd.Start ? directionY : -directionY;
        var side = Math.Sign(inwardX * otherNormalX + inwardY * otherNormalY);
        if (side == 0) return null;
        var faceSide = nearFace ? side : -side;
        var otherHalf = other.Thickness / 2;
        var face = new Point2(corner.X + otherNormalX * faceSide * otherHalf, corner.Y + otherNormalY * faceSide * otherHalf);

        var (normalX, normalY) = LeftNormal(wall);
        var half = wall.Thickness / 2;
        var limit = Math.Min(MaximumMiterFactor * Math.Max(half, otherHalf), Math.Min(wall.Length, other.Length) / 2);
        var left = FaceCut(wall, corner, normalX * half, normalY * half, face, otherX, otherY, limit);
        var right = FaceCut(wall, corner, -normalX * half, -normalY * half, face, otherX, otherY, limit);
        return left is { } l && right is { } r ? (l, r) : null;
    }

    /// <summary>Intersects one face line of a wall with a line through <paramref name="face"/>.</summary>
    private static Point2? FaceCut(Wall wall, Point2 corner, double offsetX, double offsetY, Point2 face,
        double faceX, double faceY, double limit)
    {
        var (directionX, directionY) = Direction(wall);
        var cross = directionX * faceY - directionY * faceX;
        if (Math.Abs(cross) < 1e-6) return null;
        var originX = corner.X + offsetX;
        var originY = corner.Y + offsetY;
        var t = ((face.X - originX) * faceY - (face.Y - originY) * faceX) / cross;
        if (Math.Abs(t) > limit) return null;
        return new Point2(originX + directionX * t, originY + directionY * t);
    }

    /// <summary>A coarse grid of wall segments, so a tee search only looks at walls near the point.</summary>
    private sealed class SegmentGrid
    {
        private const double CellSize = 4;
        private const int MaximumSamples = 4096;
        private readonly Dictionary<(Guid LevelId, long X, long Y), List<Wall>> _cells = [];
        private readonly Dictionary<Guid, List<Wall>> _longWalls = [];

        public static SegmentGrid Build(IReadOnlyList<Wall> walls)
        {
            var grid = new SegmentGrid();
            foreach (var wall in walls) grid.Add(wall);
            return grid;
        }

        /// <summary>Registers a wall in every cell its centerline passes through.</summary>
        private void Add(Wall wall)
        {
            var steps = (int)Math.Ceiling(wall.Length / (CellSize / 2));
            if (steps > MaximumSamples)
            {
                if (!_longWalls.TryGetValue(wall.LevelId, out var list)) _longWalls[wall.LevelId] = list = [];
                list.Add(wall);
                return;
            }
            (long, long)? last = null;
            for (var step = 0; step <= Math.Max(steps, 1); step++)
            {
                var fraction = steps == 0 ? 0 : (double)step / steps;
                var cell = CellOf(wall.Start.X + (wall.End.X - wall.Start.X) * fraction,
                    wall.Start.Y + (wall.End.Y - wall.Start.Y) * fraction);
                if (last == cell) continue;
                last = cell;
                var key = (wall.LevelId, cell.Item1, cell.Item2);
                if (!_cells.TryGetValue(key, out var bucket)) _cells[key] = bucket = [];
                bucket.Add(wall);
            }
        }

        /// <summary>Returns the walls registered around a point; a wall may appear more than once.</summary>
        public IEnumerable<Wall> Near(Guid levelId, Point2 point)
        {
            var (cellX, cellY) = CellOf(point.X, point.Y);
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
                if (_cells.TryGetValue((levelId, cellX + dx, cellY + dy), out var bucket))
                    foreach (var wall in bucket) yield return wall;
            if (_longWalls.TryGetValue(levelId, out var longWalls))
                foreach (var wall in longWalls) yield return wall;
        }

        private static (long, long) CellOf(double x, double y) =>
            ((long)Math.Floor(x / CellSize), (long)Math.Floor(y / CellSize));
    }
}
