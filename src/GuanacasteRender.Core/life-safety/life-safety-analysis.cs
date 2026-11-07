namespace Guanacaste3D.Core;

/// <summary>What a life-safety finding is about.</summary>
public enum LifeSafetyCode
{
    /// <summary>The count of modeled stairs is below the preliminary threshold.</summary>
    MissingExit,
    /// <summary>The preliminary review found no modeled elevator serving this storey.</summary>
    NoElevator,
    /// <summary>An elevator's hoistway cannot take an accessible car.</summary>
    ElevatorTooSmall,
    /// <summary>The number of sprinklers is below the preliminary area threshold.</summary>
    SprinklerCoverage,
    /// <summary>A room is farther than the preliminary straight-line extinguisher threshold.</summary>
    ExtinguisherTravel,
    /// <summary>A room is beyond the preliminary straight-line hose threshold.</summary>
    HoseReach,
    /// <summary>A storey has fewer illuminated exit signs than exits.</summary>
    ExitSigns,
    /// <summary>A storey has too few emergency lights for its floor area.</summary>
    EmergencyLighting,
    /// <summary>A storey has fewer manual pull stations than modeled exit stairs.</summary>
    PullStations,
    /// <summary>A storey has fewer horn-strobes than the preliminary area threshold.</summary>
    Notification
}

/// <summary>One finding: where it is, the element or room it concerns, and how much is missing (or the distance).</summary>
public sealed record LifeSafetyIssue(LifeSafetyCode Code, Guid LevelId, Guid? ElementId, Point2? Position, double Value);

/// <summary>The preliminary screening flags and its findings, storey by storey.</summary>
public sealed record LifeSafetyReport(int Storeys, bool HighRise, bool SuggestsSprinklers, bool SuggestsElevator,
    IReadOnlyList<LifeSafetyIssue> Issues)
{
    /// <summary>Whether this limited screening found any issues; it does not establish code compliance.</summary>
    public bool HasFindings => Issues.Count != 0;
}

/// <summary>
/// Screens selected modeled elements using fixed preliminary thresholds. Distances are straight lines, not travel
/// paths, and the analysis does not determine applicable codes, occupancy, coverage, accessibility, or compliance.
/// Buildings with fewer than three occupied storeys are not screened here.
/// </summary>
public static class LifeSafetyAnalysis
{
    /// <summary>Number of occupied storeys from which the preliminary screening runs.</summary>
    public const int MultiStoreyThreshold = 3;

    /// <summary>Height used by the preliminary high-rise flag, measured from the lowest occupied level.</summary>
    public const double HighRiseHeight = 22.86;

    /// <summary>Floor area covered by one emergency light and by one horn-strobe, in square meters.</summary>
    public const double EmergencyLightArea = 150;
    public const double NotificationArea = 250;

    /// <summary>The occupied storeys: levels with rooms, lowest first.</summary>
    public static IReadOnlyList<Level> OccupiedStoreys(BuildingDocument document) =>
        document.Levels.Where(level => document.Rooms.Any(room => room.LevelId == level.Id))
            .OrderBy(level => level.Elevation).ToArray();

    /// <summary>Runs fixed screening checks without determining applicable codes or regulatory compliance.</summary>
    public static LifeSafetyReport Analyze(BuildingDocument document)
    {
        var storeys = OccupiedStoreys(document);
        if (storeys.Count == 0) return new(0, false, false, false, []);
        var grade = storeys[0].Elevation;
        var highRise = storeys[^1].Elevation - grade > HighRiseHeight;
        var multiStorey = storeys.Count >= MultiStoreyThreshold;
        var sprinklers = storeys.Count > MultiStoreyThreshold || highRise;
        var elevatorRequired = storeys.Count > MultiStoreyThreshold;
        var issues = new List<LifeSafetyIssue>();
        if (!multiStorey) return new(storeys.Count, highRise, sprinklers, elevatorRequired, issues);

        var elevations = document.Levels.ToDictionary(level => level.Id, level => level.Elevation);
        var elevators = document.StructuralElements.Where(element => element.Kind == StructuralElementKind.Elevator).ToArray();
        foreach (var elevator in elevators)
        {
            var extent = elevator.PlanExtent;
            var (width, depth) = (extent.MaxX - extent.MinX, extent.MaxY - extent.MinY);
            if (Math.Min(width, depth) < ElevatorGeometry.MinimumWidth - 1e-6 || Math.Max(width, depth) < ElevatorGeometry.MinimumDepth - 1e-6)
                issues.Add(new(LifeSafetyCode.ElevatorTooSmall, elevator.LevelId, elevator.Id, Center(extent), Math.Min(width, depth)));
        }

        for (var index = 0; index < storeys.Count; index++)
        {
            var level = storeys[index];
            var rooms = document.Rooms.Where(room => room.LevelId == level.Id).ToArray();
            var devices = document.ElectricalDevices.Where(device => device.LevelId == level.Id).ToArray();
            var area = Math.Max(rooms.Sum(room => room.Area), document.Floors
                .Where(floor => floor.LevelId == level.Id && floor.Kind == FloorKind.Floor).Sum(floor => floor.CalculateArea()));
            var exits = ExitStairs(document, level, elevations);

            if (index > 0)
            {
                var needed = storeys.Count > MultiStoreyThreshold || area > 500 ? 2 : 1;
                if (exits < needed) issues.Add(new(LifeSafetyCode.MissingExit, level.Id, null, null, needed - exits));
                if (elevatorRequired && !elevators.Any(elevator => Serves(elevator, level, elevations)))
                    issues.Add(new(LifeSafetyCode.NoElevator, level.Id, null, null, 1));
            }

            if (sprinklers)
                foreach (var room in rooms)
                {
                    var heads = devices.Count(device => device.Kind == ElectricalDeviceKind.Sprinkler && Inside(room, device.Position));
                    var required = SprinklersFor(room);
                    if (heads < required) issues.Add(new(LifeSafetyCode.SprinklerCoverage, level.Id, room.Id, room.Center, required - heads));
                }

            var extinguishers = devices.Where(device => device.Kind == ElectricalDeviceKind.FireExtinguisher).Select(device => device.Position).ToArray();
            foreach (var room in rooms)
            {
                var reach = Farthest(room, extinguishers);
                if (reach > ElectricalStandards.ExtinguisherTravel)
                    issues.Add(new(LifeSafetyCode.ExtinguisherTravel, level.Id, room.Id, room.Center, reach));
            }

            if (storeys.Count > MultiStoreyThreshold)
            {
                var cabinets = devices.Where(device => device.Kind == ElectricalDeviceKind.FireHoseCabinet).Select(device => device.Position).ToArray();
                foreach (var room in rooms)
                {
                    var reach = Farthest(room, cabinets);
                    if (reach > ElectricalStandards.HoseReach) issues.Add(new(LifeSafetyCode.HoseReach, level.Id, room.Id, room.Center, reach));
                }
            }

            var exitsNeeded = Math.Max(1, exits);
            void Require(LifeSafetyCode code, ElectricalDeviceKind kind, int needed)
            {
                var have = devices.Count(device => device.Kind == kind);
                if (have < needed) issues.Add(new(code, level.Id, null, null, needed - have));
            }
            Require(LifeSafetyCode.ExitSigns, ElectricalDeviceKind.ExitSign, exitsNeeded);
            Require(LifeSafetyCode.EmergencyLighting, ElectricalDeviceKind.EmergencyLight, Math.Max(1, (int)Math.Ceiling(area / EmergencyLightArea)));
            Require(LifeSafetyCode.PullStations, ElectricalDeviceKind.FireAlarmPullStation, exitsNeeded);
            Require(LifeSafetyCode.Notification, ElectricalDeviceKind.HornStrobe, Math.Max(1, (int)Math.Ceiling(area / NotificationArea)));
        }
        return new(storeys.Count, highRise, sprinklers, elevatorRequired, issues);
    }

    /// <summary>Returns a preliminary sprinkler count using room area and a fixed spacing heuristic.</summary>
    public static int SprinklersFor(Room room)
    {
        var (columns, rows) = SprinklerGrid(room);
        return Math.Max((int)Math.Ceiling(room.Area / ElectricalStandards.SprinklerCoverage), columns * rows);
    }

    /// <summary>The grid of heads that keeps the spacing in each direction, at least one by one.</summary>
    public static (int Columns, int Rows) SprinklerGrid(Room room)
    {
        var width = Math.Abs(room.SecondCorner.X - room.FirstCorner.X);
        var depth = Math.Abs(room.SecondCorner.Y - room.FirstCorner.Y);
        return (Math.Max(1, (int)Math.Ceiling(width / ElectricalStandards.SprinklerSpacing)),
            Math.Max(1, (int)Math.Ceiling(depth / ElectricalStandards.SprinklerSpacing)));
    }

    /// <summary>Distinct exit stairs that reach a storey: flights leaving it and flights arriving from the storey below.</summary>
    public static int ExitStairs(BuildingDocument document, Level level, IReadOnlyDictionary<Guid, double> elevations)
    {
        var stairs = document.StructuralElements.Where(element => element.Kind == StructuralElementKind.Stair &&
            elevations.TryGetValue(element.LevelId, out var from) &&
            (element.LevelId == level.Id || Math.Abs(from + element.Offset + element.Height - level.Elevation) < 0.3))
            .Select(stair => Center(Geometry.PlanRectangle.Of(stair.Footprint))).ToList();
        var distinct = new List<Point2>();
        foreach (var stair in stairs)
            if (distinct.All(other => other.DistanceTo(stair) > 3)) distinct.Add(stair);
        return distinct.Count;
    }

    /// <summary>Whether an elevator has a landing at a level.</summary>
    public static bool Serves(StructuralElement elevator, Level level, IReadOnlyDictionary<Guid, double> elevations)
    {
        if (!elevations.TryGetValue(elevator.LevelId, out var bottom)) return false;
        return level.Elevation >= bottom - 1e-6 && level.Elevation <= bottom + ElevatorGeometry.Travel(elevator) + 0.05;
    }

    /// <summary>Checks whether the point lies inside the rectangular room annotation.</summary>
    public static bool Inside(Room room, Point2 point) =>
        point.X >= Math.Min(room.FirstCorner.X, room.SecondCorner.X) - 1e-6 && point.X <= Math.Max(room.FirstCorner.X, room.SecondCorner.X) + 1e-6 &&
        point.Y >= Math.Min(room.FirstCorner.Y, room.SecondCorner.Y) - 1e-6 && point.Y <= Math.Max(room.FirstCorner.Y, room.SecondCorner.Y) + 1e-6;

    /// <summary>Distance from the farthest corner of a room to the nearest of the given points; infinity when there is none.</summary>
    private static double Farthest(Room room, IReadOnlyList<Point2> points)
    {
        if (points.Count == 0) return double.PositiveInfinity;
        Point2[] corners =
        [
            room.FirstCorner, room.SecondCorner, new(room.FirstCorner.X, room.SecondCorner.Y), new(room.SecondCorner.X, room.FirstCorner.Y)
        ];
        return corners.Max(corner => points.Min(point => point.DistanceTo(corner)));
    }

    private static Point2 Center(Geometry.PlanRectangle extent) => new((extent.MinX + extent.MaxX) / 2, (extent.MinY + extent.MaxY) / 2);
}
