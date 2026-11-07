namespace Guanacaste3D.Core;

/// <summary>State of a fixture or a whole system after the hydraulic check.</summary>
public enum PlumbingStatus
{
    Empty,
    Ok,
    Warning,
    Fail
}

/// <summary>A straight piece of pipe between two points (plan position and absolute elevation) with its size.</summary>
public sealed record PipeRun(PlumbingSystemKind Kind, Point2 From, double FromZ, Point2 To, double ToZ, string Nominal,
    double OutsideMillimeters)
{
    public double Length => Math.Sqrt(Math.Pow(To.X - From.X, 2) + Math.Pow(To.Y - From.Y, 2) + Math.Pow(ToZ - FromZ, 2));

    /// <summary>A fitting-scale piece (a trap) modelled in 3D but too small to draw in plan.</summary>
    public bool Detail { get; init; }
}

/// <summary>A vent stack serving the fixtures of a sanitary system: where it stands, from its foot to above the roof.</summary>
public sealed record VentReport(Guid Id, string Name, Point2 Position, double Bottom, double Top, string Nominal,
    double OutsideMillimeters, double Units);

/// <summary>
/// One fixture on one system. Supply systems give the flowing pressure at the fixture's stop and, for hot water, how many
/// seconds hot water takes to arrive; the drain gives the invert under the fixture and its cover below the floor.
/// <see cref="Issue"/> is a localization key, empty when the fixture is fine.
/// </summary>
public sealed record FixtureReport(Guid FixtureId, string Name, double Units, string Nominal, double PressureKPa,
    double RequiredKPa, double WaitSeconds, double Invert, double Cover, double PathLength, PlumbingStatus Status, string Issue)
{
    /// <summary>Drains: the vent stack that protects the fixture's trap, empty when there is none.</summary>
    public string Vent { get; init; } = string.Empty;

    /// <summary>Drains: length of the trap arm to that vent, and the longest allowed for the trap's size, in meters.</summary>
    public double TrapArm { get; init; } = double.NaN;
    public double TrapArmLimit { get; init; } = double.NaN;
}

/// <summary>A system's routed pipes, design flow, main size and the check of every fixture on it.</summary>
public sealed record PlumbingSystemReport(PlumbingSystem System, string SourceName, PlumbingStatus Status,
    IReadOnlyList<PipeRun> Runs, IReadOnlyList<FixtureReport> Fixtures, double Units, double DesignFlow, string MainNominal,
    double LengthMeters)
{
    /// <summary>Sanitary systems: the vent stacks that serve its fixtures.</summary>
    public IReadOnlyList<VentReport> Vents { get; init; } = [];
}

/// <summary>
/// Routes and checks the plumbing. Each system is laid out as a tree from its source, growing toward the nearest
/// fixture first (Prim's minimum spanning tree in the Manhattan metric), with every connection run square to the axes.
/// Every pipe carries the fixture units of all fixtures downstream of it. Supply pipe is sized for the peak flow of
/// those units by Hunter's curve, under a velocity limit. The pressure at each fixture is the street pressure less
/// friction (Hazen–Williams, with an allowance for fittings) and the height it is raised. Hot water also gets the time
/// it takes to arrive, which is the volume of pipe from the heater divided by the fixture's flow. Drains are sized by
/// drainage fixture units and laid at 2 % up from the outlet invert; a fixture whose drain would come up through the
/// floor fails.
/// </summary>
public sealed class PlumbingAnalysis
{
    private readonly Dictionary<Guid, PlumbingSystemReport> _byId;

    private PlumbingAnalysis(List<PlumbingSystemReport> systems, List<(StructuralElement, PlumbingSystemKind)> unserved,
        List<SepticReport> septic, List<RoofDrainageReport> roofs, List<StorageReport> storage)
    {
        Storage = storage;
        Systems = systems;
        Unserved = unserved;
        Septic = septic;
        Roofs = roofs;
        _byId = systems.ToDictionary(report => report.System.Id);
    }

    /// <summary>Every cistern and elevated tank: reserve, tank size and pump.</summary>
    public IReadOnlyList<StorageReport> Storage { get; }

    /// <summary>The design of every septic tank and its drain field.</summary>
    public IReadOnlyList<SepticReport> Septic { get; }

    /// <summary>Every roof's runoff and whether its downspouts carry it.</summary>
    public IReadOnlyList<RoofDrainageReport> Roofs { get; }

    public IReadOnlyList<PlumbingSystemReport> Systems { get; }

    /// <summary>Fixtures that need a system of the given kind and are on none.</summary>
    public IReadOnlyList<(StructuralElement Fixture, PlumbingSystemKind Kind)> Unserved { get; }

    public PlumbingSystemReport? System(Guid systemId) => _byId.GetValueOrDefault(systemId);

    /// <summary>The check of one fixture on its system of the given kind, or null when it is not connected.</summary>
    public FixtureReport? Fixture(Guid fixtureId, PlumbingSystemKind kind) => Systems.Where(report => report.System.Kind == kind)
        .SelectMany(report => report.Fixtures).FirstOrDefault(fixture => fixture.FixtureId == fixtureId);

    private sealed record Node(Guid? Id, string Name, Point2 Point, double Level, double Units, PlumbingFixtureSpec? Spec,
        double InletZ, double OutletZ, Point2 Center);

    public static PlumbingAnalysis Analyze(BuildingDocument document)
    {
        var elevations = document.Levels.ToDictionary(level => level.Id, level => level.Elevation);
        var components = document.StructuralElements.Where(element => element.Kind == StructuralElementKind.Component)
            .ToDictionary(element => element.Id);
        var points = document.PlumbingPoints.ToDictionary(point => point.Id);
        var defaultPressure = document.PlumbingPoints.FirstOrDefault(point => point.Kind == PlumbingPointKind.WaterMeter)?.SupplyPressureKPa ?? 280;
        var vents = document.PlumbingPoints.Where(point => point.Kind == PlumbingPointKind.VentStack).ToList();
        // Vent stacks rise through the roof: above the highest wall top and the roof datum, whichever is higher.
        var wallTop = document.Walls.Where(wall => elevations.ContainsKey(wall.LevelId))
            .Select(wall => elevations[wall.LevelId] + wall.Height).DefaultIfEmpty(double.NegativeInfinity).Max();
        var roofLevel = document.Levels.Where(level => level.Role == LevelRole.BaseRoof).Select(level => level.Elevation)
            .DefaultIfEmpty(double.NegativeInfinity).Max();
        var building = Math.Max(wallTop, roofLevel);
        var roofTop = (double.IsFinite(building) ? building : elevations.Values.Max() + 3) + PlumbingStandards.VentAboveRoof;

        // The cold demand of a water heater is all the hot demand it serves.
        var heaterUnits = new Dictionary<Guid, double>();
        foreach (var system in document.PlumbingSystems.Where(system => system.Kind == PlumbingSystemKind.HotWater))
            heaterUnits[system.SourceId] = heaterUnits.GetValueOrDefault(system.SourceId) + system.Fixtures.Sum(id =>
                components.TryGetValue(id, out var element) && PlumbingStandards.Fixture(element) is { } spec ? spec.HotUnits : 0);
        double UnitsOf(StructuralElement element, PlumbingFixtureSpec spec, PlumbingSystemKind kind) => kind switch
        {
            PlumbingSystemKind.ColdWater when PlumbingStandards.IsWaterHeater(element) =>
                heaterUnits.TryGetValue(element.Id, out var units) && units > 0 ? units : 1.4,
            PlumbingSystemKind.ColdWater => spec.ColdUnits,
            PlumbingSystemKind.HotWater => spec.HotUnits,
            _ => spec.DrainUnits
        };

        var reports = new List<PlumbingSystemReport>();
        var heaterPressure = new Dictionary<Guid, double>();
        // A cistern's pump is an electrical device: it runs only while its circuit and panel are live.
        var electrical = ElectricalAnalysis.Analyze(document);
        bool PumpPowered(PlumbingPoint cistern) =>
            cistern.PumpDeviceId is Guid pump && document.ElectricalDevices.FirstOrDefault(device => device.Id == pump) is { } device &&
            electrical.Energized(device);
        var rainfall = document.PlumbingPoints.FirstOrDefault(point => point.Kind == PlumbingPointKind.StormOutlet)?.RainfallMmPerHour ?? 150;
        var spoutFlows = SiteDrainage.DownspoutFlows(document, rainfall);
        var spouts = document.StructuralElements.Where(element => element.Kind == StructuralElementKind.Downspout).ToDictionary(element => element.Id);
        foreach (var kind in new[] { PlumbingSystemKind.ColdWater, PlumbingSystemKind.HotWater, PlumbingSystemKind.Sanitary, PlumbingSystemKind.StormWater })
            foreach (var system in document.PlumbingSystems.Where(system => system.Kind == kind))
            {
                // The source: a meter, a heater or an outlet, with its position, level and starting condition.
                Node? root = null;
                double start = 0;
                if (kind == PlumbingSystemKind.HotWater)
                {
                    if (components.TryGetValue(system.SourceId, out var heater) && PlumbingStandards.IsWaterHeater(heater) &&
                        elevations.TryGetValue(heater.LevelId, out var level))
                    {
                        root = new Node(heater.Id, heater.Name, PlumbingStandards.Connection(heater), level, 0, null,
                            level + heater.Offset + heater.Height, 0, heater.Start);
                        start = heaterPressure.GetValueOrDefault(heater.Id, defaultPressure);
                    }
                }
                else if (points.TryGetValue(system.SourceId, out var point) && elevations.TryGetValue(point.LevelId, out var level) &&
                         (kind == PlumbingSystemKind.ColdWater ? point.IsColdSource
                             : kind == PlumbingSystemKind.Sanitary ? point.IsSanitaryOutlet
                             : point.Kind == PlumbingPointKind.StormOutlet))
                {
                    // A meter gives the street's pressure at grade; a cistern its pump's pressure, when the pump has power;
                    // an elevated tank nothing but the height of water above each fixture.
                    var entry = point.Kind == PlumbingPointKind.ElevatedTank ? level + point.TankHeight : level;
                    root = new Node(point.Id, point.Name, point.Position, level, 0, null, entry, level - point.InvertDepth, point.Position);
                    start = kind != PlumbingSystemKind.ColdWater ? level - point.InvertDepth
                        : point.Kind switch
                        {
                            PlumbingPointKind.Cistern => PumpPowered(point) ? point.PumpPressureKPa : 0,
                            PlumbingPointKind.ElevatedTank => 0,
                            _ => point.SupplyPressureKPa
                        };
                }
                if (root is null)
                {
                    reports.Add(new PlumbingSystemReport(system, string.Empty, PlumbingStatus.Empty, [], [], 0, 0, string.Empty, 0));
                    continue;
                }

                var nodes = new List<Node> { root };
                if (kind == PlumbingSystemKind.StormWater)
                {
                    // Downspouts carry their share of roof runoff down to the buried storm drain.
                    foreach (var id in system.Fixtures.Distinct())
                        if (spouts.TryGetValue(id, out var spout) && elevations.TryGetValue(spout.LevelId, out var level))
                            nodes.Add(new Node(spout.Id, spout.Name, spout.Start, level, spoutFlows.GetValueOrDefault(spout.Id), null,
                                level, level + spout.Offset, spout.Start));
                    var storm = Storm(system, nodes, start, spouts);
                    reports.Add(storm);
                    continue;
                }
                foreach (var id in system.Fixtures.Distinct())
                {
                    if (!components.TryGetValue(id, out var element) || PlumbingStandards.Fixture(element) is not { } spec ||
                        !elevations.TryGetValue(element.LevelId, out var level))
                        continue;
                    var units = UnitsOf(element, spec, kind);
                    if (units <= 0) continue;
                    // A drain leaves the fixture at its trap: under the basin, below the floor for a shower, or from
                    // the fixture's own outlet when the trap is built in.
                    var outlet = level + Math.Max(0, element.Offset) + (double.IsNaN(spec.TrapHeight) ? 0 : spec.TrapHeight);
                    nodes.Add(new Node(element.Id, element.Name, PlumbingStandards.Connection(element), level, units, spec,
                        level + element.Offset + spec.InletHeight, outlet, element.Start));
                }
                var report = kind == PlumbingSystemKind.Sanitary ? Drain(system, nodes, start, vents, roofTop) : Supply(system, nodes, start);
                if (kind == PlumbingSystemKind.ColdWater && points.TryGetValue(system.SourceId, out var source) &&
                    source.Kind == PlumbingPointKind.Cistern && !PumpPowered(source))
                    report = report with
                    {
                        Status = report.Fixtures.Count == 0 ? PlumbingStatus.Empty : PlumbingStatus.Fail,
                        Fixtures = report.Fixtures.Select(fixture => fixture with { Status = PlumbingStatus.Fail, Issue = "Plumbing.Issue.PumpOff" }).ToList()
                    };
                if (kind == PlumbingSystemKind.ColdWater)
                    foreach (var fixture in report.Fixtures.Where(fixture => components.TryGetValue(fixture.FixtureId, out var element) &&
                                                                             PlumbingStandards.IsWaterHeater(element)))
                        heaterPressure[fixture.FixtureId] = fixture.PressureKPa;
                reports.Add(report);
            }

        // Fixtures left without a system they need.
        var served = reports.Where(report => report.Status != PlumbingStatus.Empty)
            .SelectMany(report => report.Fixtures.Select(fixture => (fixture.FixtureId, report.System.Kind))).ToHashSet();
        var unserved = new List<(StructuralElement, PlumbingSystemKind)>();
        foreach (var element in components.Values)
        {
            if (PlumbingStandards.Fixture(element) is not { } spec) continue;
            var heater = PlumbingStandards.IsWaterHeater(element);
            if ((spec.NeedsCold || heater) && !served.Contains((element.Id, PlumbingSystemKind.ColdWater)))
                unserved.Add((element, PlumbingSystemKind.ColdWater));
            if (spec.NeedsHot && !served.Contains((element.Id, PlumbingSystemKind.HotWater)))
                unserved.Add((element, PlumbingSystemKind.HotWater));
            if (spec.Drains && !served.Contains((element.Id, PlumbingSystemKind.Sanitary)))
                unserved.Add((element, PlumbingSystemKind.Sanitary));
        }
        if (document.PlumbingPoints.Any(point => point.Kind == PlumbingPointKind.StormOutlet))
            foreach (var spout in spouts.Values.Where(spout => !served.Contains((spout.Id, PlumbingSystemKind.StormWater))))
                unserved.Add((spout, PlumbingSystemKind.StormWater));

        var septic = document.PlumbingPoints.Where(point => point.Kind == PlumbingPointKind.SepticTank)
            .Select(point => SiteDrainage.Septic(document, point)).ToList();
        var roofs = SiteDrainage.Roofs(document).Select(item =>
        {
            var draining = spouts.Values.Where(spout => SiteDrainage.Drains(item.Outline, spout.Start)).ToList();
            var flow = SiteDrainage.RoofFlow(item.Area, rainfall);
            var capacity = draining.Sum(SiteDrainage.LeaderCapacity);
            var status = draining.Count == 0 || capacity < flow ? PlumbingStatus.Fail : capacity < flow * 1.2 ? PlumbingStatus.Warning : PlumbingStatus.Ok;
            return new RoofDrainageReport(item.Roof.Id, item.Roof.Name, item.Area, flow, draining.Count, capacity, status);
        }).ToList();
        var storage = document.PlumbingPoints.Where(point => point.Kind is PlumbingPointKind.Cistern or PlumbingPointKind.ElevatedTank)
            .Select(point => WaterStorage.Design(document, point,
                reports.Where(report => report.System.SourceId == point.Id).Select(report => report.DesignFlow).DefaultIfEmpty(0).Max(),
                point.Kind != PlumbingPointKind.Cistern || PumpPowered(point)))
            .ToList();
        return new PlumbingAnalysis(reports, unserved, septic, roofs, storage);
    }

    /// <summary>
    /// The storm drain: tree from the outlet, sized for full flow by Manning at 1 %, with the inverts rising at 1 % away
    /// from the outlet; each downspout drops into it and is checked against its leader's capacity.
    /// </summary>
    private static PlumbingSystemReport Storm(PlumbingSystem system, List<Node> nodes, double outletInvert,
        Dictionary<Guid, StructuralElement> spouts)
    {
        var root = nodes[0];
        var (parent, order) = Tree(nodes);
        var flows = Subtree(nodes, parent, order, node => node.Units);
        var sizes = SiteDrainage.StormSizes;
        var runs = new List<PipeRun>();
        var fixtures = new List<FixtureReport>();
        var invert = new double[nodes.Count];
        var path = new double[nodes.Count];
        invert[0] = outletInvert;
        var mainSize = 0;
        foreach (var index in order.Skip(1))
        {
            var from = parent[index];
            var length = Manhattan(nodes[from].Point, nodes[index].Point);
            invert[index] = invert[from] + SiteDrainage.StormSlope * length;
            path[index] = path[from] + length;
            var size = SiteDrainage.StormSize(flows[index]);
            if (from == 0) mainSize = Math.Max(mainSize, size);
            AddSquareRun(runs, PlumbingSystemKind.StormWater, nodes[from].Point, invert[from], nodes[index].Point, invert[index], sizes[size]);
        }
        for (var index = 1; index < nodes.Count; index++)
        {
            var node = nodes[index];
            var size = SiteDrainage.StormSize(node.Units);
            // The boot at the foot of the downspout, down into the buried drain.
            runs.Add(new PipeRun(PlumbingSystemKind.StormWater, node.Point, node.OutletZ, node.Point, invert[index], sizes[size].Nominal,
                sizes[size].Outside));
            var capacity = SiteDrainage.LeaderCapacity(spouts[node.Id!.Value]);
            var cover = node.Level - invert[index];
            var (status, issue) = node.Units > capacity ? (PlumbingStatus.Fail, "Plumbing.Issue.LeaderTooSmall")
                : cover < 0.10 ? (PlumbingStatus.Fail, "Plumbing.Issue.NoFall")
                : node.Units > capacity * 0.85 ? (PlumbingStatus.Warning, "Plumbing.Issue.LeaderNearlyFull")
                : (PlumbingStatus.Ok, string.Empty);
            fixtures.Add(new FixtureReport(node.Id.Value, node.Name, node.Units, sizes[size].Nominal, double.NaN, capacity, double.NaN,
                invert[index], cover, path[index], status, issue));
        }
        return Report(system, root.Name, runs, fixtures, flows[0], flows[0], sizes[mainSize].Nominal);
    }

    /// <summary>
    /// A cold or hot water system: tree, sizes by peak flow, then the flowing pressure and the hot-water wait at every
    /// fixture. Horizontal mains run just under the floor of the source's level; each fixture is fed by a riser up to
    /// its stop.
    /// </summary>
    private static PlumbingSystemReport Supply(PlumbingSystem system, List<Node> nodes, double sourcePressure)
    {
        var hot = system.Kind == PlumbingSystemKind.HotWater;
        var kind = system.Kind;
        var root = nodes[0];
        var runZ = root.Level - (hot ? 0.14 : 0.10);
        var (parent, order) = Tree(nodes);
        var units = Subtree(nodes, parent, order, node => node.Units);
        var sizes = PlumbingStandards.SupplySizes;
        var runs = new List<PipeRun>();
        var fixtures = new List<FixtureReport>();

        var pressure = new double[nodes.Count];
        var path = new double[nodes.Count];
        var volume = new double[nodes.Count];
        pressure[0] = sourcePressure - 9.81 * (runZ - root.InletZ);
        var mainSize = 0;
        // From the meter (or down from the heater's outlet) to the main under the floor.
        runs.Add(new PipeRun(kind, root.Point, root.InletZ, root.Point, runZ, sizes[0].Nominal, sizes[0].Outside));
        foreach (var index in order.Skip(1))
        {
            var from = parent[index];
            var flow = PlumbingStandards.PeakFlow(units[index]);
            var size = PlumbingStandards.SupplySize(flow, hot);
            // The service from the meter is never smaller than 3/4".
            if (from == 0 && !hot) size = Math.Max(size, 1);
            if (from == 0) mainSize = Math.Max(mainSize, size);
            var length = Manhattan(nodes[from].Point, nodes[index].Point);
            var inside = sizes[size].Inside;
            pressure[index] = pressure[from] - PlumbingStandards.FrictionLoss(flow, inside, length * (1 + PlumbingStandards.FittingAllowance));
            path[index] = path[from] + length;
            volume[index] = volume[from] + length * Area(inside);
            AddSquareRun(runs, kind, nodes[from].Point, runZ, nodes[index].Point, runZ, sizes[size]);
        }
        if (mainSize == 0 && nodes.Count > 1 && !hot) mainSize = 1;
        runs[0] = runs[0] with { Nominal = sizes[mainSize].Nominal, OutsideMillimeters = sizes[mainSize].Outside };

        for (var index = 1; index < nodes.Count; index++)
        {
            var node = nodes[index];
            var spec = node.Spec!;
            var own = PlumbingStandards.PeakFlow(node.Units);
            var size = PlumbingStandards.SupplySize(own, hot);
            var rise = node.InletZ - runZ;
            runs.Add(new PipeRun(kind, node.Point, runZ, node.Point, node.InletZ, sizes[size].Nominal, sizes[size].Outside));
            var atStop = pressure[index] - 9.81 * rise -
                         PlumbingStandards.FrictionLoss(own, sizes[size].Inside, Math.Abs(rise) * (1 + PlumbingStandards.FittingAllowance));
            var wait = hot && own > 0 ? (volume[index] + Math.Abs(rise) * Area(sizes[size].Inside)) / (own / 1000) : double.NaN;
            var (status, issue) = atStop < spec.MinPressureKPa
                ? (PlumbingStatus.Fail, "Plumbing.Issue.LowPressure")
                : atStop < spec.MinPressureKPa + 35
                    ? (PlumbingStatus.Warning, "Plumbing.Issue.MarginalPressure")
                    : hot && wait > PlumbingStandards.HotWaitLimit
                        ? (PlumbingStatus.Warning, "Plumbing.Issue.LongWait")
                        : (PlumbingStatus.Ok, string.Empty);
            fixtures.Add(new FixtureReport(node.Id!.Value, node.Name, node.Units, sizes[size].Nominal, atStop, spec.MinPressureKPa,
                wait, double.NaN, double.NaN, path[index] + Math.Abs(rise), status, issue));
        }
        return Report(system, root.Name, runs, fixtures, units[0], PlumbingStandards.PeakFlow(units[0]), sizes[mainSize].Nominal);
    }

    /// <summary>
    /// The sanitary drain: tree from the outlet, sizes by the drainage fixture units downstream and the largest trap
    /// among them, inverts rising at 2 % away from the outlet, and the cover left under each fixture's floor.
    /// </summary>
    private static PlumbingSystemReport Drain(PlumbingSystem system, List<Node> nodes, double outletInvert, List<PlumbingPoint> vents,
        double roofTop)
    {
        var root = nodes[0];
        var (parent, order) = Tree(nodes);
        var units = Subtree(nodes, parent, order, node => node.Units);
        var minimum = new int[nodes.Count];
        for (var index = 1; index < nodes.Count; index++) minimum[index] = nodes[index].Spec!.MinDrainSize;
        foreach (var index in order.Reverse().Where(index => parent[index] >= 0))
            minimum[parent[index]] = Math.Max(minimum[parent[index]], minimum[index]);
        var sizes = PlumbingStandards.DrainSizes;
        var runs = new List<PipeRun>();
        var fixtures = new List<FixtureReport>();
        var invert = new double[nodes.Count];
        var path = new double[nodes.Count];
        invert[0] = outletInvert;
        var mainSize = 0;
        foreach (var index in order.Skip(1))
        {
            var from = parent[index];
            var length = Manhattan(nodes[from].Point, nodes[index].Point);
            invert[index] = invert[from] + PlumbingStandards.DrainSlope * length;
            path[index] = path[from] + length;
            var size = PlumbingStandards.DrainSize(units[index], minimum[index]);
            if (from == 0) mainSize = Math.Max(mainSize, size);
            var size3 = sizes[size];
            AddSquareRun(runs, PlumbingSystemKind.Sanitary, nodes[from].Point, invert[from], nodes[index].Point, invert[index],
                (size3.Nominal, size3.Inside, size3.Outside));
        }
        // A cleanout riser (registro) at the outlet, up to the floor.
        runs.Add(new PipeRun(PlumbingSystemKind.Sanitary, root.Point, root.InletZ, root.Point, outletInvert, sizes[mainSize].Nominal,
            sizes[mainSize].Outside));
        for (var index = 1; index < nodes.Count; index++)
        {
            var node = nodes[index];
            var spec = node.Spec!;
            var size = PlumbingStandards.DrainSize(node.Units, spec.MinDrainSize);
            runs.Add(new PipeRun(PlumbingSystemKind.Sanitary, node.Point, node.OutletZ, node.Point, invert[index], sizes[size].Nominal,
                sizes[size].Outside));
            if (!double.IsNaN(spec.TrapHeight)) AddTrap(runs, node, spec, sizes[Math.Max(0, spec.MinDrainSize)]);
            var cover = node.Level - invert[index];
            var (status, issue) = cover < 0.10
                ? (PlumbingStatus.Fail, "Plumbing.Issue.NoFall")
                : cover < 0.15
                    ? (PlumbingStatus.Warning, "Plumbing.Issue.ShallowDrain")
                    : (PlumbingStatus.Ok, string.Empty);
            // The trap must be vented within its trap-arm length, or a draining fixture siphons it dry.
            var vent = vents.MinBy(candidate => Manhattan(candidate.Position, node.Point));
            var arm = vent is null ? double.NaN : Manhattan(vent.Position, node.Point);
            var limit = PlumbingStandards.TrapArmLimit(size);
            var (ventStatus, ventIssue) = vent is null
                ? (PlumbingStatus.Fail, "Plumbing.Issue.NoVent")
                : arm > limit ? (PlumbingStatus.Fail, "Plumbing.Issue.TrapArmTooLong") : (PlumbingStatus.Ok, string.Empty);
            if (ventStatus > status) (status, issue) = (ventStatus, ventIssue);
            fixtures.Add(new FixtureReport(node.Id!.Value, node.Name, node.Units, sizes[size].Nominal, double.NaN, double.NaN,
                double.NaN, invert[index], cover, path[index], status, issue)
            {
                Vent = vent?.Name ?? string.Empty, TrapArm = arm, TrapArmLimit = limit
            });
        }
        // Each vent stack carries the fixture units of the traps it vents and stands from the drain to above the roof.
        var ventReports = new List<VentReport>();
        foreach (var group in fixtures.Where(fixture => fixture.Vent.Length > 0)
                     .GroupBy(fixture => vents.MinBy(candidate => Manhattan(candidate.Position,
                         nodes.First(node => node.Id == fixture.FixtureId).Point))!))
        {
            var ventUnits = group.Sum(fixture => fixture.Units);
            var ventSize = sizes[PlumbingStandards.VentSize(ventUnits)];
            ventReports.Add(new VentReport(group.Key.Id, group.Key.Name, group.Key.Position, group.Min(fixture => fixture.Invert),
                roofTop, ventSize.Nominal, ventSize.Outside, ventUnits));
        }
        return Report(system, root.Name, runs, fixtures, units[0], 0, sizes[mainSize].Nominal) with { Vents = ventReports };
    }

    /// <summary>
    /// A P-trap at the fixture: tailpiece down from the fixture, the U bend, and the trap arm back to the wall where the
    /// drain drops. The fixture's front is from its connection toward its centre.
    /// </summary>
    private static void AddTrap(List<PipeRun> runs, Node node, PlumbingFixtureSpec spec,
        (string Nominal, double Inside, double Outside, double Capacity) size)
    {
        var dx = node.Center.X - node.Point.X;
        var dy = node.Center.Y - node.Point.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        var (fx, fy) = length > 1e-6 ? (dx / length, dy / length) : (0.0, -1.0);
        Point2 Out(double distance) => new(node.Point.X + fx * distance, node.Point.Y + fy * distance);
        var arm = node.OutletZ;
        var bottom = arm - 0.08;
        var tail = Out(0.14);
        var weir = Out(0.05);
        void Piece(Point2 from, double fromZ, Point2 to, double toZ) =>
            runs.Add(new PipeRun(PlumbingSystemKind.Sanitary, from, fromZ, to, toZ, size.Nominal, size.Outside) { Detail = true });
        Piece(tail, arm + spec.Tailpiece, tail, bottom);
        Piece(tail, bottom, weir, bottom);
        Piece(weir, bottom, weir, arm);
        Piece(weir, arm, node.Point, arm);
    }

    private static PlumbingSystemReport Report(PlumbingSystem system, string source, List<PipeRun> runs, List<FixtureReport> fixtures,
        double units, double flow, string main)
    {
        var status = fixtures.Count == 0 ? PlumbingStatus.Empty : fixtures.Max(fixture => fixture.Status);
        return new PlumbingSystemReport(system, source, status, runs, fixtures, units, flow, main, runs.Sum(run => run.Length));
    }

    /// <summary>
    /// The run from one node to another along the axes: first along X, then along Y, with the elevation changing in
    /// proportion to the distance covered (for drains, which fall toward the outlet).
    /// </summary>
    private static void AddSquareRun(List<PipeRun> runs, PlumbingSystemKind kind, Point2 from, double fromZ, Point2 to, double toZ,
        (string Nominal, double Inside, double Outside) size)
    {
        var corner = new Point2(to.X, from.Y);
        var total = Manhattan(from, to);
        var cornerZ = total < 1e-9 ? toZ : fromZ + (toZ - fromZ) * Math.Abs(to.X - from.X) / total;
        if (from.DistanceTo(corner) > 1e-6) runs.Add(new PipeRun(kind, from, fromZ, corner, cornerZ, size.Nominal, size.Outside));
        if (corner.DistanceTo(to) > 1e-6) runs.Add(new PipeRun(kind, corner, cornerZ, to, toZ, size.Nominal, size.Outside));
    }

    /// <summary>Prim's tree from node 0: the parent of every node and the order in which nodes joined.</summary>
    private static (int[] Parent, int[] Order) Tree(List<Node> nodes)
    {
        var count = nodes.Count;
        var parent = new int[count];
        var best = new double[count];
        var done = new bool[count];
        var order = new List<int>(count);
        Array.Fill(best, double.MaxValue);
        best[0] = 0;
        parent[0] = -1;
        for (var step = 0; step < count; step++)
        {
            var next = -1;
            for (var index = 0; index < count; index++)
                if (!done[index] && (next < 0 || best[index] < best[next])) next = index;
            done[next] = true;
            order.Add(next);
            for (var index = 0; index < count; index++)
            {
                if (done[index]) continue;
                // Changing level costs extra: it takes a riser, so a fixture prefers a neighbour on its own floor.
                var distance = Manhattan(nodes[next].Point, nodes[index].Point) + Math.Abs(nodes[next].Level - nodes[index].Level) * 2;
                if (distance < best[index])
                {
                    best[index] = distance;
                    parent[index] = next;
                }
            }
        }
        return (parent, order.ToArray());
    }

    /// <summary>The sum of a value over every node and all nodes downstream of it (farther from the source).</summary>
    private static double[] Subtree(List<Node> nodes, int[] parent, int[] order, Func<Node, double> value)
    {
        var total = nodes.Select(value).ToArray();
        foreach (var index in order.Reverse())
            if (parent[index] >= 0) total[parent[index]] += total[index];
        return total;
    }

    private static double Manhattan(Point2 a, Point2 b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    private static double Area(double insideMillimeters) => Math.PI * Math.Pow(insideMillimeters / 1000, 2) / 4;
}
