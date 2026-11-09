namespace Guanacaste3D.Core;

/// <summary>Authoring commands that build elements from other elements: by face, by host, automatic and copied groups.</summary>
public sealed partial class DocumentSession
{
    /// <summary>
    /// Adds a sweep (skirting) or reveal (groove line) along both faces of a wall. The element runs the wall's
    /// length and takes its thickness, so it follows the wall's identity when the wall is deleted.
    /// </summary>
    public Guid AddWallSweep(Guid wallId, bool reveal)
    {
        var wall = Document.Walls.FirstOrDefault(candidate => candidate.Id == wallId)
            ?? throw new InvalidDataException("The wall does not exist.");
        var kind = reveal ? StructuralElementKind.WallReveal : StructuralElementKind.WallSweep;
        var id = Guid.NewGuid();
        Commit("Transaction.AddStructuralElement", document =>
            document.StructuralElements.Add(CreateStructuralElement(id, document, wall.LevelId, kind,
                reveal ? "Wall reveal" : "Wall sweep", wall.Start, wall.End) with { Width = wall.Thickness }));
        return id;
    }

    /// <summary>Adds a slab edge along every side of a floor, roof or ceiling boundary as one model group.</summary>
    public int AddSlabEdges(Guid floorId)
    {
        var floor = Document.Floors.FirstOrDefault(candidate => candidate.Id == floorId)
            ?? throw new InvalidDataException("The floor slab does not exist.");
        var group = Guid.NewGuid();
        var count = 0;
        Commit("Transaction.AddSlabEdges", document =>
        {
            var top = floor.ElevationOffset;
            for (var index = 0; index < floor.Boundary.Count; index++)
            {
                var from = floor.Boundary[index];
                var to = floor.Boundary[(index + 1) % floor.Boundary.Count];
                if (from.DistanceTo(to) < 0.05) continue;
                document.StructuralElements.Add(CreateStructuralElement(Guid.NewGuid(), document, floor.LevelId,
                    StructuralElementKind.SlabEdge, "Slab edge", from, to) with
                {
                    GroupId = group, Height = Math.Max(0.1, floor.Thickness + 0.1), Offset = top - Math.Max(0.1, floor.Thickness + 0.1)
                });
                count++;
            }
        });
        return count;
    }

    /// <summary>
    /// Adds a guard along each side of a stair or ramp, rising with it. Returns how many guards were added.
    /// </summary>
    public int AddRailingsOnHost(Guid hostId)
    {
        var host = Document.StructuralElements.FirstOrDefault(candidate => candidate.Id == hostId &&
                candidate.Kind is StructuralElementKind.Stair or StructuralElementKind.Ramp)
            ?? throw new InvalidDataException("The element does not exist.");
        if (host.Kind == StructuralElementKind.Stair)
        {
            // Every flight gets a guard on each side, the landing on its open sides, a spiral along its outer edge.
            var lines = StairGeometry.GuardLines(host);
            Commit("Transaction.AddRailingsOnHost", document =>
            {
                foreach (var (start, end, offset, rise) in lines)
                    document.StructuralElements.Add(CreateStructuralElement(Guid.NewGuid(), document, host.LevelId,
                        StructuralElementKind.Railing, "Guard", start, end) with { Rise = rise, Offset = offset, RailingHostId = host.Id });
            });
            return lines.Count;
        }
        var length = host.Length;
        var normalX = -(host.End.Y - host.Start.Y) / length;
        var normalY = (host.End.X - host.Start.X) / length;
        var lateral = Math.Max(0.03, host.Width / 2 - 0.03);
        Commit("Transaction.AddRailingsOnHost", document =>
        {
            foreach (var side in new[] { -1, 1 })
                document.StructuralElements.Add(CreateStructuralElement(Guid.NewGuid(), document, host.LevelId,
                    StructuralElementKind.Railing, "Guard",
                    new Point2(host.Start.X + normalX * lateral * side, host.Start.Y + normalY * lateral * side),
                    new Point2(host.End.X + normalX * lateral * side, host.End.Y + normalY * lateral * side)) with
                {
                    Rise = host.Height, Offset = host.Offset, RailingHostId = host.Id
                });
        });
        return 2;
    }

    /// <summary>The top elevation of the tallest wall on a level, which roofs and ceilings bear on.</summary>
    public double WallTopOf(Guid levelId) =>
        Document.Walls.Where(wall => wall.LevelId == levelId).Select(wall => wall.Height).DefaultIfEmpty(3.0).Max();

    /// <summary>Adds a slab over a room's rectangle, grown on every side by <paramref name="margin"/> meters.</summary>
    public Guid AddSlabOverRoom(Guid roomId, SlabPreset preset, double margin = 0)
    {
        var room = Document.Rooms.FirstOrDefault(candidate => candidate.Id == roomId)
            ?? throw new InvalidDataException("The room does not exist.");
        // Face-derived floors, ceilings and roofs preserve L-shaped rooms instead of
        // silently filling their unused bounding-box corners.
        var footprint = RoomRegionGeometry.Outline(room);
        var boundary = RoomRegionGeometry.OffsetOrthogonal(footprint, margin);
        return AddSlabFromPreset(room.LevelId, boundary,
            preset, SlabElevation(room.LevelId, preset));
    }

    /// <summary>Adds a slab with the footprint of another slab, grown outward by <paramref name="margin"/> meters.</summary>
    public Guid AddSlabOverSlab(Guid floorId, SlabPreset preset, double margin = 0)
    {
        var floor = Document.Floors.FirstOrDefault(candidate => candidate.Id == floorId)
            ?? throw new InvalidDataException("The floor slab does not exist.");
        var (minX, maxX, minY, maxY) = RoofGeometry.Extent(floor.Boundary);
        // The footprint is grown through its bounding box so the margin is exact for rectangular slabs.
        Point2[] boundary = margin == 0
            ? [.. floor.Boundary]
            : [new(minX - margin, minY - margin), new(maxX + margin, minY - margin), new(maxX + margin, maxY + margin), new(minX - margin, maxY + margin)];
        return AddSlabFromPreset(floor.LevelId, boundary, preset, SlabElevation(floor.LevelId, preset));
    }

    /// <summary>The elevation a new slab of the recipe bears at: floors on the level, ceilings and roofs on the walls.</summary>
    public double SlabElevation(Guid levelId, SlabPreset preset) => preset.Kind switch
    {
        FloorKind.Ceiling => WallTopOf(levelId),
        FloorKind.Roof => WallTopOf(levelId) + preset.Thickness,
        // Terrain sits just under the ground floor, so a slab on the level never shares its plane.
        FloorKind.Toposolid => -0.10,
        // The water surface sits below the surrounding paving, as in a real reflecting pool.
        FloorKind.Water => -0.15,
        _ => 0
    };

    /// <summary>
    /// Adds a wall along each side of a closed boundary as one transaction; the whole set is rejected when any wall
    /// is invalid. Returns the number of walls added.
    /// </summary>
    public int AddWallsAroundBoundary(Guid levelId, IReadOnlyList<Point2> boundary, Guid wallTypeId, double height = 3)
    {
        if (boundary.Count < 3) throw new InvalidDataException("A wall loop needs at least three corners.");
        var type = Document.WallTypes.FirstOrDefault(candidate => candidate.Id == wallTypeId)
            ?? throw new InvalidDataException("The selected wall type does not exist.");
        Commit("Transaction.AddWallsAroundBoundary", document =>
        {
            for (var index = 0; index < boundary.Count; index++)
                document.Walls.Add(new Wall(Guid.NewGuid(), levelId, boundary[index], boundary[(index + 1) % boundary.Count],
                    type.Thickness, height) { WallTypeId = wallTypeId });
            RefreshRoomBoundariesIfNeeded(document, levelId);
        });
        return boundary.Count;
    }

    /// <summary>Discovers and refreshes rectangular automatic rooms without affecting manual ones.</summary>
    public int AddAutomaticRooms(Guid levelId) => SynchronizeAutomaticRooms(levelId);

    /// <summary>Adds an area covering the bounding rectangle of every area boundary line on a level; null when there are none.</summary>
    public Guid? AddAreaFromBoundaries(Guid levelId, string name)
    {
        var points = Document.StructuralElements
            .Where(element => element.LevelId == levelId && element.Kind == StructuralElementKind.AreaBoundary)
            .SelectMany(element => new[] { element.Start, element.End }).ToArray();
        if (points.Length < 2) return null;
        var first = new Point2(points.Min(point => point.X), points.Min(point => point.Y));
        var second = new Point2(points.Max(point => point.X), points.Max(point => point.Y));
        return AddArea(levelId, name, first, second);
    }

    /// <summary>Copies a model group, translated, as a new group. Returns the number of elements copied.</summary>
    public int DuplicateStructuralGroup(Guid groupId, double deltaX, double deltaY)
    {
        var members = Document.StructuralElements.Where(element => element.GroupId == groupId).ToArray();
        if (members.Length == 0) throw new InvalidDataException("The element does not exist.");
        var copy = Guid.NewGuid();
        var copiedIds = members.ToDictionary(member => member.Id, _ => Guid.NewGuid());
        Commit("Transaction.DuplicateStructuralGroup", document =>
        {
            foreach (var member in members)
                document.StructuralElements.Add(member with
                {
                    Id = copiedIds[member.Id], GroupId = copy,
                    RailingHostId = member.RailingHostId is Guid hostId && copiedIds.TryGetValue(hostId, out var copiedHost)
                        ? copiedHost : null,
                    Start = new Point2(member.Start.X + deltaX, member.Start.Y + deltaY),
                    End = new Point2(member.End.X + deltaX, member.End.Y + deltaY)
                });
        });
        return members.Length;
    }
}

/// <summary>Finds closed rectangular spaces bounded by orthogonal walls and non-solid room separators.</summary>
public static class RoomDetection
{
    private const double Tolerance = 1e-3;

    /// <summary>Tests whether a room's rectangle overlaps another rectangle.</summary>
    public static bool Overlaps(Room room, Point2 minimum, Point2 maximum)
    {
        var (x0, x1) = (Math.Min(room.FirstCorner.X, room.SecondCorner.X), Math.Max(room.FirstCorner.X, room.SecondCorner.X));
        var (y0, y1) = (Math.Min(room.FirstCorner.Y, room.SecondCorner.Y), Math.Max(room.FirstCorner.Y, room.SecondCorner.Y));
        return x0 < maximum.X && x1 > minimum.X && y0 < maximum.Y && y1 > minimum.Y;
    }

    /// <summary>
    /// Splits the plan into the cells of the grid that the wall lines form, floods cells that no wall separates, and
    /// keeps the groups that cannot reach the outside and fill a whole rectangle. Each rectangle is reduced by half
    /// the thickness of the walls on its sides, so it measures the clear floor.
    /// </summary>
    public static IReadOnlyList<(Point2 Min, Point2 Max)> Detect(BuildingDocument document, Guid levelId)
    {
        var walls = document.Walls.Where(wall => wall.LevelId == levelId &&
            (Math.Abs(wall.Start.X - wall.End.X) < Tolerance || Math.Abs(wall.Start.Y - wall.End.Y) < Tolerance)).ToArray();
        if (walls.Length < 4) return [];
        // Room separators are drafting/reference boundaries, never solids. Include their ends as
        // partition coordinates so the flood-fill can detect distinct adjacent rooms.
        var separators = document.StructuralElements.Where(element =>
            element.LevelId == levelId && element.Kind == StructuralElementKind.RoomSeparator &&
            (Math.Abs(element.Start.X - element.End.X) < Tolerance ||
             Math.Abs(element.Start.Y - element.End.Y) < Tolerance)).ToArray();
        var xs = Distinct(walls.SelectMany(wall => new[] { wall.Start.X, wall.End.X })
            .Concat(separators.SelectMany(separator => new[] { separator.Start.X, separator.End.X })));
        var ys = Distinct(walls.SelectMany(wall => new[] { wall.Start.Y, wall.End.Y })
            .Concat(separators.SelectMany(separator => new[] { separator.Start.Y, separator.End.Y })));
        if (xs.Count < 2 || ys.Count < 2) return [];
        var (nx, ny) = (xs.Count - 1, ys.Count - 1);

        bool VerticalWallCovers(double x, double y0, double y1) =>
            walls.Any(wall => Math.Abs(wall.Start.X - wall.End.X) < Tolerance &&
                Math.Abs(wall.Start.X - x) < Tolerance &&
                Math.Min(wall.Start.Y, wall.End.Y) <= y0 + Tolerance &&
                Math.Max(wall.Start.Y, wall.End.Y) >= y1 - Tolerance) ||
            separators.Any(separator => Math.Abs(separator.Start.X - separator.End.X) < Tolerance &&
                Math.Abs(separator.Start.X - x) < Tolerance &&
                Math.Min(separator.Start.Y, separator.End.Y) <= y0 + Tolerance &&
                Math.Max(separator.Start.Y, separator.End.Y) >= y1 - Tolerance);
        bool HorizontalWallCovers(double y, double x0, double x1) =>
            walls.Any(wall => Math.Abs(wall.Start.Y - wall.End.Y) < Tolerance &&
                Math.Abs(wall.Start.Y - y) < Tolerance &&
                Math.Min(wall.Start.X, wall.End.X) <= x0 + Tolerance &&
                Math.Max(wall.Start.X, wall.End.X) >= x1 - Tolerance) ||
            separators.Any(separator => Math.Abs(separator.Start.Y - separator.End.Y) < Tolerance &&
                Math.Abs(separator.Start.Y - y) < Tolerance &&
                Math.Min(separator.Start.X, separator.End.X) <= x0 + Tolerance &&
                Math.Max(separator.Start.X, separator.End.X) >= x1 - Tolerance);

        var cluster = new int[nx, ny];
        var clusters = new List<(bool Outside, List<(int I, int J)> Cells)>();
        for (var i = 0; i < nx; i++)
        for (var j = 0; j < ny; j++)
        {
            if (cluster[i, j] != 0) continue;
            var cells = new List<(int, int)>();
            var outside = false;
            var queue = new Queue<(int I, int J)>();
            queue.Enqueue((i, j));
            cluster[i, j] = clusters.Count + 1;
            while (queue.Count > 0)
            {
                var (ci, cj) = queue.Dequeue();
                cells.Add((ci, cj));
                // Each side is either blocked by a wall, a neighbouring cell to flood, or the outside of the grid.
                foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var (ni, nj) = (ci + di, cj + dj);
                    var blocked = di != 0
                        ? VerticalWallCovers(xs[di > 0 ? ci + 1 : ci], ys[cj], ys[cj + 1])
                        : HorizontalWallCovers(ys[dj > 0 ? cj + 1 : cj], xs[ci], xs[ci + 1]);
                    if (blocked) continue;
                    if (ni < 0 || nj < 0 || ni >= nx || nj >= ny) { outside = true; continue; }
                    if (cluster[ni, nj] != 0) continue;
                    cluster[ni, nj] = clusters.Count + 1;
                    queue.Enqueue((ni, nj));
                }
            }
            clusters.Add((outside, cells.Select(cell => (cell.Item1, cell.Item2)).ToList()));
        }

        var spaces = new List<(Point2, Point2)>();
        foreach (var (outside, cells) in clusters)
        {
            if (outside) continue;
            var (i0, i1) = (cells.Min(cell => cell.I), cells.Max(cell => cell.I));
            var (j0, j1) = (cells.Min(cell => cell.J), cells.Max(cell => cell.J));
            if (cells.Count != (i1 - i0 + 1) * (j1 - j0 + 1)) continue;
            double Half(Func<Wall, bool> match) => walls.Where(match).Select(wall => wall.Thickness / 2).DefaultIfEmpty(0).Max();
            var minimum = new Point2(xs[i0] + Half(wall => Math.Abs(wall.Start.X - wall.End.X) < Tolerance && Math.Abs(wall.Start.X - xs[i0]) < Tolerance),
                ys[j0] + Half(wall => Math.Abs(wall.Start.Y - wall.End.Y) < Tolerance && Math.Abs(wall.Start.Y - ys[j0]) < Tolerance));
            var maximum = new Point2(xs[i1 + 1] - Half(wall => Math.Abs(wall.Start.X - wall.End.X) < Tolerance && Math.Abs(wall.Start.X - xs[i1 + 1]) < Tolerance),
                ys[j1 + 1] - Half(wall => Math.Abs(wall.Start.Y - wall.End.Y) < Tolerance && Math.Abs(wall.Start.Y - ys[j1 + 1]) < Tolerance));
            if (maximum.X - minimum.X < 0.3 || maximum.Y - minimum.Y < 0.3) continue;
            spaces.Add((minimum, maximum));
        }
        return spaces;
    }

    private static List<double> Distinct(IEnumerable<double> values)
    {
        var result = new List<double>();
        foreach (var value in values.OrderBy(value => value))
            if (result.Count == 0 || value - result[^1] > Tolerance) result.Add(value);
        return result;
    }
}
