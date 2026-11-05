namespace Guanacaste3D.Core;

/// <summary>Vertical transport and fire protection; each public method is one undoable transaction.</summary>
public sealed partial class DocumentSession
{
    /// <summary>
    /// Places an elevator in the hoistway between two corners, from <paramref name="levelId"/> up to
    /// <paramref name="topLevelId"/>, with a landing at every level between them. Its doors face the side
    /// <paramref name="quarterTurns"/> picks (0 = -Y, 1 = +X, 2 = +Y, 3 = -X). Landing elevations must be
    /// evenly spaced because the elevator geometry currently stores a single spacing value.
    /// </summary>
    public Guid AddElevator(Guid levelId, Guid topLevelId, Point2 first, Point2 opposite, string name, int quarterTurns = 0)
    {
        var bottom = Document.Levels.FirstOrDefault(level => level.Id == levelId)
            ?? throw new InvalidDataException("The level does not exist.");
        var top = Document.Levels.FirstOrDefault(level => level.Id == topLevelId)
            ?? throw new InvalidDataException("The level does not exist.");
        var travel = top.Elevation - bottom.Elevation;
        if (travel < 2) throw new InvalidDataException("An elevator must rise to a higher level.");
        var landingElevations = Document.Levels.Where(level => level.Elevation >= bottom.Elevation - 1e-6 &&
                level.Elevation <= top.Elevation + 1e-6).Select(level => level.Elevation).Order().ToArray();
        var spacing = travel / (landingElevations.Length - 1);
        if (landingElevations.Select((elevation, index) => Math.Abs(elevation - (bottom.Elevation + index * spacing)))
            .Any(deviation => deviation > 1e-6))
            throw new InvalidDataException("Elevator landing levels must be evenly spaced.");
        var extent = Geometry.PlanRectangle.Of([first, opposite]);
        if (Math.Min(extent.MaxX - extent.MinX, extent.MaxY - extent.MinY) < 1)
            throw new InvalidDataException("The elevator hoistway is too small.");
        var id = Guid.NewGuid();
        Commit("Transaction.AddElevator", document => document.StructuralElements.Add(
            new StructuralElement(id, levelId, StructuralElementKind.Elevator, name, first, opposite, 0.90,
                ElevatorGeometry.Pit + travel + ElevatorGeometry.Overhead, -ElevatorGeometry.Pit)
            {
                GridHeight = spacing,
                Rotation = quarterTurns * Math.PI / 2
            }));
        return id;
    }

    /// <summary>
    /// Suggests fire-protection elements using the limited thresholds in <see cref="LifeSafetyAnalysis"/>: a sprinkler grid
    /// in every room of a sprinklered building, smoke alarms in bedrooms, extinguishers and fire-hose cabinets on walls
    /// until every room is within reach, and at each exit stair an exit sign, a pull station, an emergency light and a
    /// horn-strobe, with more emergency lights and horn-strobes along the storey as its area asks. Devices that the
    /// storey already has count toward each threshold. Placement is preliminary and requires professional review.
    /// Returns the number of devices placed.
    /// </summary>
    public int AddFireProtection(Guid levelId, Func<ElectricalDeviceKind, int, string> name)
    {
        var level = Document.Levels.FirstOrDefault(candidate => candidate.Id == levelId)
            ?? throw new InvalidDataException("The level does not exist.");
        var report = LifeSafetyAnalysis.Analyze(Document);
        var rooms = Document.Rooms.Where(room => room.LevelId == levelId).ToArray();
        if (rooms.Length == 0) return 0;
        var existing = Document.ElectricalDevices.Where(device => device.LevelId == levelId).ToList();
        var placed = new List<ElectricalDevice>();
        var ceiling = WallTopOf(levelId);
        void Place(ElectricalDeviceKind kind, Point2 position, double facing = 0)
        {
            var number = existing.Concat(placed).Count(device => device.Kind == kind) + 1;
            var elevation = ElectricalStandards.MountsOnWall(kind) ? ElectricalStandards.MountingHeight(kind) : ceiling;
            placed.Add(new ElectricalDevice(Guid.NewGuid(), levelId, kind, name(kind, number), position, elevation)
            {
                Facing = facing,
                Watts = ElectricalStandards.DefaultWatts(kind)
            });
        }
        IEnumerable<ElectricalDevice> All(ElectricalDeviceKind kind) => existing.Concat(placed).Where(device => device.Kind == kind);

        foreach (var room in rooms)
        {
            if (report.SuggestsSprinklers)
            {
                var have = All(ElectricalDeviceKind.Sprinkler).Count(device => LifeSafetyAnalysis.Inside(room, device.Position));
                if (have < LifeSafetyAnalysis.SprinklersFor(room))
                {
                    // An even grid, each head at the centre of its own cell, so no head is more than half a cell from a wall.
                    var (columns, rows) = LifeSafetyAnalysis.SprinklerGrid(room);
                    while (columns * rows < LifeSafetyAnalysis.SprinklersFor(room))
                        if (columns <= rows) columns++; else rows++;
                    var (minX, minY) = (Math.Min(room.FirstCorner.X, room.SecondCorner.X), Math.Min(room.FirstCorner.Y, room.SecondCorner.Y));
                    var (width, depth) = (Math.Abs(room.SecondCorner.X - room.FirstCorner.X), Math.Abs(room.SecondCorner.Y - room.FirstCorner.Y));
                    for (var column = 0; column < columns; column++)
                        for (var row = 0; row < rows; row++)
                        {
                            var at = new Point2(minX + width * (column + 0.5) / columns, minY + depth * (row + 0.5) / rows);
                            if (All(ElectricalDeviceKind.Sprinkler).All(device => device.Position.DistanceTo(at) > 0.5))
                                Place(ElectricalDeviceKind.Sprinkler, at);
                        }
                }
            }
            if (LightingAnalysis.IsBedroom(room.Name) &&
                !All(ElectricalDeviceKind.SmokeDetector).Any(device => LifeSafetyAnalysis.Inside(room, device.Position)))
                Place(ElectricalDeviceKind.SmokeDetector, new Point2(room.Center.X + 0.6, room.Center.Y + 0.6));
        }

        var elevations = Document.Levels.ToDictionary(candidate => candidate.Id, candidate => candidate.Elevation);
        var stairs = Document.StructuralElements.Where(element => element.Kind == StructuralElementKind.Stair &&
                (element.LevelId == levelId || Math.Abs(elevations[element.LevelId] + element.Offset + element.Height - level.Elevation) < 0.3))
            .Select(stair => Geometry.PlanRectangle.Of(stair.Footprint))
            .Select(extent => new Point2((extent.MinX + extent.MaxX) / 2, (extent.MinY + extent.MaxY) / 2))
            .Aggregate(new List<Point2>(), (list, point) => { if (list.All(other => other.DistanceTo(point) > 3)) list.Add(point); return list; });
        // At every exit stair: the sign over the way out, a pull station, an emergency light, an extinguisher and the
        // hose cabinet, on the nearest wall face. Without stairs the storey's central room stands in for the exit.
        var exits = stairs.Count > 0 ? stairs : [rooms.MinBy(room => room.Center.DistanceTo(Centroid(rooms)))!.Center];
        foreach (var exit in exits)
        {
            void AtExit(ElectricalDeviceKind kind, double slide)
            {
                if (All(kind).Any(device => device.Position.DistanceTo(exit) < 4.5)) return;
                if (NearestWallFace(levelId, exit, slide) is { } face) Place(kind, face.Position, face.Facing);
            }
            AtExit(ElectricalDeviceKind.ExitSign, 0);
            AtExit(ElectricalDeviceKind.FireAlarmPullStation, 0.45);
            AtExit(ElectricalDeviceKind.EmergencyLight, -0.45);
            AtExit(ElectricalDeviceKind.FireExtinguisher, 0.9);
            if (report.Storeys > LifeSafetyAnalysis.MultiStoreyThreshold) AtExit(ElectricalDeviceKind.FireHoseCabinet, -1.4);
            AtExit(ElectricalDeviceKind.HornStrobe, 1.5);
        }

        // Extinguishers and hose cabinets until every room is in reach: each new one goes on the wall nearest the room
        // that is worst served.
        void Cover(ElectricalDeviceKind kind, double reach)
        {
            for (var guard = 0; guard < rooms.Length; guard++)
            {
                var points = All(kind).Select(device => device.Position).ToArray();
                var worst = rooms.Select(room => (room, distance: points.Length == 0 ? double.PositiveInfinity
                        : Corners(room).Max(corner => points.Min(point => point.DistanceTo(corner)))))
                    .Where(item => item.distance > reach).OrderByDescending(item => item.distance).FirstOrDefault();
                if (worst.room is null || NearestWallFace(levelId, worst.room.Center, 0) is not { } face) return;
                Place(kind, face.Position, face.Facing);
            }
        }
        Cover(ElectricalDeviceKind.FireExtinguisher, ElectricalStandards.ExtinguisherTravel);
        if (report.Storeys > LifeSafetyAnalysis.MultiStoreyThreshold) Cover(ElectricalDeviceKind.FireHoseCabinet, ElectricalStandards.HoseReach);

        // Emergency lights and horn-strobes for the area, spread over the largest rooms that have none.
        var area = rooms.Sum(room => room.Area);
        void Spread(ElectricalDeviceKind kind, double perDevice)
        {
            var needed = Math.Max(1, (int)Math.Ceiling(area / perDevice));
            foreach (var room in rooms.OrderByDescending(room => room.Area))
            {
                if (All(kind).Count() >= needed) return;
                if (All(kind).Any(device => LifeSafetyAnalysis.Inside(room, device.Position))) continue;
                if (NearestWallFace(levelId, room.Center, 0) is { } face) Place(kind, face.Position, face.Facing);
            }
        }
        Spread(ElectricalDeviceKind.EmergencyLight, LifeSafetyAnalysis.EmergencyLightArea);
        Spread(ElectricalDeviceKind.HornStrobe, LifeSafetyAnalysis.NotificationArea);

        if (placed.Count > 0)
            Commit("Transaction.AddFireProtection", document => document.ElectricalDevices.AddRange(placed));
        return placed.Count;
    }

    /// <summary>
    /// The face of the wall of a level nearest a point, slid <paramref name="slide"/> meters along the wall: where a
    /// wall-mounted device goes, and the direction it faces (toward the point's side of the wall).
    /// </summary>
    public (Point2 Position, double Facing)? NearestWallFace(Guid levelId, Point2 point, double slide)
    {
        var best = Document.Walls.Where(wall => wall.LevelId == levelId && wall.Length > 0.3)
            .Select(wall => (wall, distance: WallDistance(wall, point))).OrderBy(item => item.distance).FirstOrDefault();
        if (best.wall is null) return null;
        var wall = best.wall;
        var length = wall.Length;
        var (dx, dy) = ((wall.End.X - wall.Start.X) / length, (wall.End.Y - wall.Start.Y) / length);
        var along = Math.Clamp((point.X - wall.Start.X) * dx + (point.Y - wall.Start.Y) * dy + slide, 0.15, length - 0.15);
        var foot = new Point2(wall.Start.X + dx * along, wall.Start.Y + dy * along);
        var (nx, ny) = (-dy, dx);
        if ((point.X - foot.X) * nx + (point.Y - foot.Y) * ny < 0) (nx, ny) = (-nx, -ny);
        var half = wall.Thickness / 2;
        return (new Point2(foot.X + nx * half, foot.Y + ny * half), Math.Atan2(ny, nx));
    }

    private static Point2 Centroid(IReadOnlyList<Room> rooms) =>
        new(rooms.Average(room => room.Center.X), rooms.Average(room => room.Center.Y));

    private static Point2[] Corners(Room room) =>
    [
        room.FirstCorner, room.SecondCorner, new(room.FirstCorner.X, room.SecondCorner.Y), new(room.SecondCorner.X, room.FirstCorner.Y)
    ];
}
