using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core;

/// <summary>A bar of the analytical model: the axis of a beam, brace, truss or column.</summary>
public sealed record AnalyticalMember(Guid Id, string Kind, Point3 A, Point3 B);

/// <summary>A surface of the analytical model: a wall or a slab, by the corners of its middle surface.</summary>
public sealed record AnalyticalPanel(Guid Id, string Kind, IReadOnlyList<Point3> Corners);

/// <summary>A hole in an analytical panel, by its corners.</summary>
public sealed record AnalyticalOpening(Guid Id, Guid HostId, IReadOnlyList<Point3> Corners);

/// <summary>A point where the structure rests on the ground and how it is held there: fixed or pinned.</summary>
public sealed record AnalyticalSupport(Guid Id, Point3 At, string Condition);

/// <summary>A node where two or more bars meet.</summary>
public sealed record AnalyticalLink(Point3 At, IReadOnlyList<Guid> Members);

/// <summary>
/// The structure of the building as an engineer's model: bars along the axes of beams and columns, panels for walls and slabs,
/// the openings in them, the supports on the ground and the nodes where bars meet. It is read from the building, not stored.
/// </summary>
public sealed record AnalyticalModel(IReadOnlyList<AnalyticalMember> Members, IReadOnlyList<AnalyticalPanel> Panels, IReadOnlyList<AnalyticalOpening> Openings,
    IReadOnlyList<AnalyticalSupport> Supports, IReadOnlyList<AnalyticalLink> Links)
{
    private const double NodeTolerance = 0.05;

    public static AnalyticalModel Build(BuildingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var elevations = document.Levels.ToDictionary(level => level.Id, level => level.Elevation);
        var lowest = elevations.Count == 0 ? 0 : elevations.Values.Min();
        var members = new List<AnalyticalMember>();
        var panels = new List<AnalyticalPanel>();
        var openings = new List<AnalyticalOpening>();
        var supports = new List<AnalyticalSupport>();

        foreach (var beam in document.StructuralElements.Where(item => item.Kind is StructuralElementKind.Beam or StructuralElementKind.Brace or StructuralElementKind.Truss && elevations.ContainsKey(item.LevelId)))
        {
            var z = elevations[beam.LevelId] + beam.Offset + beam.Height / 2;
            members.Add(new AnalyticalMember(beam.Id, beam.Kind.ToString(), new Point3(beam.Start.X, beam.Start.Y, z), new Point3(beam.End.X, beam.End.Y, z)));
        }
        foreach (var column in document.Columns.Where(item => elevations.ContainsKey(item.LevelId)))
        {
            var z = elevations[column.LevelId];
            members.Add(new AnalyticalMember(column.Id, "Column", new Point3(column.Position.X, column.Position.Y, z), new Point3(column.Position.X, column.Position.Y, z + column.Height)));
            if (z <= lowest + 1e-6) supports.Add(new AnalyticalSupport(column.Id, new Point3(column.Position.X, column.Position.Y, z), "Fixed"));
        }
        foreach (var footing in document.StructuralElements.Where(item => item.Kind is StructuralElementKind.IsolatedFoundation or StructuralElementKind.WallFoundation && elevations.ContainsKey(item.LevelId)))
            supports.Add(new AnalyticalSupport(footing.Id, new Point3((footing.Start.X + footing.End.X) / 2, (footing.Start.Y + footing.End.Y) / 2, elevations[footing.LevelId] + footing.Offset), "Fixed"));
        foreach (var wall in document.Walls.Where(item => elevations.ContainsKey(item.LevelId)))
        {
            var z = elevations[wall.LevelId];
            panels.Add(new AnalyticalPanel(wall.Id, "Wall",
                [new(wall.Start.X, wall.Start.Y, z), new(wall.End.X, wall.End.Y, z), new(wall.End.X, wall.End.Y, z + wall.Height), new(wall.Start.X, wall.Start.Y, z + wall.Height)]));
            if (z <= lowest + 1e-6) supports.Add(new AnalyticalSupport(wall.Id, new Point3((wall.Start.X + wall.End.X) / 2, (wall.Start.Y + wall.End.Y) / 2, z), "Pinned"));
            var dx = wall.Length < 1e-9 ? 0 : (wall.End.X - wall.Start.X) / wall.Length;
            var dy = wall.Length < 1e-9 ? 0 : (wall.End.Y - wall.Start.Y) / wall.Length;
            foreach (var opening in document.Openings.Where(item => item.WallId == wall.Id))
            {
                Point3 At(double along, double height) => new(wall.Start.X + dx * along, wall.Start.Y + dy * along, z + height);
                openings.Add(new AnalyticalOpening(opening.Id, wall.Id, [At(opening.Begin, opening.Sill), At(opening.End, opening.Sill), At(opening.End, opening.Sill + opening.Height), At(opening.Begin, opening.Sill + opening.Height)]));
            }
        }
        foreach (var slab in document.Floors.Where(item => item.Kind is FloorKind.Floor or FloorKind.Roof && elevations.ContainsKey(item.LevelId)))
        {
            var z = elevations[slab.LevelId] + slab.ElevationOffset;
            panels.Add(new AnalyticalPanel(slab.Id, slab.Kind.ToString(), slab.Boundary.Select(point => new Point3(point.X, point.Y, z)).ToList()));
        }

        // Nodes: the ends of bars that fall on the same place within 5 cm.
        var nodes = new Dictionary<(long, long, long), (Point3 At, List<Guid> Members)>();
        void Touch(Point3 point, Guid id)
        {
            var key = ((long)Math.Round(point.X / NodeTolerance), (long)Math.Round(point.Y / NodeTolerance), (long)Math.Round(point.Z / NodeTolerance));
            if (!nodes.TryGetValue(key, out var node)) nodes[key] = node = (point, []);
            if (!node.Members.Contains(id)) node.Members.Add(id);
        }
        foreach (var member in members)
        {
            Touch(member.A, member.Id);
            Touch(member.B, member.Id);
        }
        var links = nodes.Values.Where(node => node.Members.Count >= 2).Select(node => new AnalyticalLink(node.At, node.Members)).ToList();
        return new AnalyticalModel(members, panels, openings, supports, links);
    }
}

/// <summary>The weight of one storey, split into what holds the building up and the load people and use add.</summary>
public sealed record LevelWeight(Guid LevelId, string Name, double Walls, double Slabs, double Frames, double Live)
{
    public double Dead => Walls + Slabs + Frames;
}

/// <summary>What a column carries: the slab area that drains to it on its storey, and the load of this storey and every storey above, in kilograms.</summary>
public sealed record ColumnLoad(Guid ColumnId, double TributaryArea, double Load, double Accumulated);

/// <summary>A simple hand check of the structure: the weight of each storey and the load each column takes.</summary>
public sealed record StructuralSummary(IReadOnlyList<LevelWeight> Levels, IReadOnlyList<ColumnLoad> Columns, double SlabLoad, double UnassignedSlabLoad)
{
    public const double LiveLoadFloor = 200;
    public const double LiveLoadRoof = 100;
    private const double SampleStep = 0.25;

    public double TotalDead => Levels.Sum(level => level.Dead);
    public double TotalLive => Levels.Sum(level => level.Live);

    private static double DensityOf(WallConstruction construction) => construction switch
    {
        WallConstruction.Brick => 1800, WallConstruction.Block => 1400, WallConstruction.Concrete => 2400, WallConstruction.Stone => 2600,
        WallConstruction.Wood => 600, WallConstruction.Metal => 7850, WallConstruction.Glass => 2500, WallConstruction.Gypsum => 800, _ => 1600
    };

    public static StructuralSummary Build(BuildingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var unit = document.Concrete.UnitWeight;
        var types = document.WallTypes.ToDictionary(type => type.Id);
        var levels = new List<LevelWeight>();
        var columnLoads = new Dictionary<Guid, (double Area, double Load)>();
        double slabLoadTotal = 0, unassigned = 0;

        foreach (var level in document.Levels.OrderByDescending(item => item.Elevation))
        {
            double walls = 0, slabs = 0, frames = 0, live = 0;
            foreach (var wall in document.Walls.Where(item => item.LevelId == level.Id))
            {
                var density = DensityOf(types.TryGetValue(wall.WallTypeId, out var type) ? type.Construction : WallConstruction.Plaster);
                walls += WallGeometry.NetVolume(wall, document.Openings.Where(opening => opening.WallId == wall.Id)) * density;
            }
            foreach (var column in document.Columns.Where(item => item.LevelId == level.Id)) frames += column.Width * column.Depth * column.Height * unit;
            foreach (var member in document.StructuralElements.Where(item => item.LevelId == level.Id && item.Kind is StructuralElementKind.Beam or StructuralElementKind.Brace or StructuralElementKind.Truss))
                frames += member.Length * member.Width * member.Height * (member.Style == ElementStyle.Default ? unit : 7850 * 0.15);
            var slabsHere = document.Floors.Where(item => item.LevelId == level.Id && item.Kind is FloorKind.Floor or FloorKind.Roof).ToList();
            var columnsHere = document.Columns.Where(item => item.LevelId == level.Id).ToList();
            foreach (var slab in slabsHere)
            {
                var area = slab.CalculateArea();
                var perArea = slab.Thickness * unit;
                var liveLoad = slab.Kind == FloorKind.Roof ? LiveLoadRoof : LiveLoadFloor;
                slabs += area * perArea;
                live += area * liveLoad;
                slabLoadTotal += area * (perArea + liveLoad);
                // Each patch of slab drains to its nearest column.
                var share = Share(slab.Boundary, columnsHere.Select(column => column.Position).ToList());
                for (var index = 0; index < columnsHere.Count; index++)
                {
                    var tributary = share.Areas[index];
                    var (previousArea, previousLoad) = columnLoads.GetValueOrDefault(columnsHere[index].Id);
                    columnLoads[columnsHere[index].Id] = (previousArea + tributary, previousLoad + tributary * (perArea + liveLoad));
                }
                unassigned += share.Unassigned * (perArea + liveLoad);
            }
            levels.Add(new LevelWeight(level.Id, level.Name, walls, slabs, frames, live));
        }

        // Down the building: a column carries its own storey's load plus the loads of the columns stacked above it.
        var ordered = document.Columns.OrderByDescending(column => document.Levels.First(level => level.Id == column.LevelId).Elevation).ToList();
        var accumulated = new Dictionary<Guid, double>();
        foreach (var column in ordered)
        {
            var own = columnLoads.GetValueOrDefault(column.Id).Load + column.Width * column.Depth * column.Height * unit;
            var above = ordered.Where(other => other.Id != column.Id && accumulated.ContainsKey(other.Id) && other.Position.DistanceTo(column.Position) < 0.3 &&
                                               document.Levels.First(l => l.Id == other.LevelId).Elevation > document.Levels.First(l => l.Id == column.LevelId).Elevation)
                .Sum(other => accumulated[other.Id]);
            accumulated[column.Id] = own + above;
        }
        var columns = document.Columns.Select(column => new ColumnLoad(column.Id, columnLoads.GetValueOrDefault(column.Id).Area, columnLoads.GetValueOrDefault(column.Id).Load, accumulated.GetValueOrDefault(column.Id))).ToList();
        levels.Reverse();
        return new StructuralSummary(levels, columns, slabLoadTotal, unassigned);
    }

    /// <summary>Splits a slab among columns by sampling it every 25 cm and giving each sample to the nearest column; with no columns it all goes unassigned.</summary>
    private static (double[] Areas, double Unassigned) Share(IReadOnlyList<Point2> boundary, IReadOnlyList<Point2> columns)
    {
        var areas = new double[columns.Count];
        var area = new FloorSlab(Guid.Empty, Guid.Empty, "x", [.. boundary], 0.1).CalculateArea();
        if (columns.Count == 0) return (areas, area);
        double minX = boundary.Min(p => p.X), maxX = boundary.Max(p => p.X), minY = boundary.Min(p => p.Y), maxY = boundary.Max(p => p.Y);
        var counts = new int[columns.Count];
        var total = 0;
        for (var x = minX + SampleStep / 2; x < maxX; x += SampleStep)
            for (var y = minY + SampleStep / 2; y < maxY; y += SampleStep)
            {
                if (!TopographyGeometry.Contains(boundary, new Point2(x, y))) continue;
                var nearest = 0;
                var best = double.MaxValue;
                for (var index = 0; index < columns.Count; index++)
                {
                    var distance = (columns[index].X - x) * (columns[index].X - x) + (columns[index].Y - y) * (columns[index].Y - y);
                    if (distance < best) { best = distance; nearest = index; }
                }
                counts[nearest]++;
                total++;
            }
        // The samples are scaled to the true area of the slab, so the shares always add up to it.
        if (total == 0) return (areas, area);
        for (var index = 0; index < columns.Count; index++) areas[index] = area * counts[index] / total;
        return (areas, 0);
    }
}

/// <summary>The envelope of one orientation: the area of its exterior walls, of the windows in them and their ratio.</summary>
public sealed record FacadeSummary(string Orientation, double WallArea, double WindowArea)
{
    public double WindowRatio => WallArea < 1e-9 ? 0 : WindowArea / WallArea;
}

/// <summary>A first look at the building's energy: how much glass faces each way, and what to change.</summary>
public sealed record EnergySummary(IReadOnlyList<FacadeSummary> Facades, IReadOnlyList<string> Advice)
{
    public double TotalWallArea => Facades.Sum(item => item.WallArea);
    public double TotalWindowArea => Facades.Sum(item => item.WindowArea);
    public double TotalRatio => TotalWallArea < 1e-9 ? 0 : TotalWindowArea / TotalWallArea;

    public static EnergySummary Build(BuildingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var types = document.WallTypes.ToDictionary(type => type.Id);
        var exterior = document.Walls.Where(wall => !types.TryGetValue(wall.WallTypeId, out var type) || type.Function != WallFunction.Interior).ToList();
        string[] names = ["N", "E", "S", "W"];
        var wallArea = new double[4];
        var windowArea = new double[4];
        if (exterior.Count > 0)
        {
            var centreX = exterior.Average(wall => (wall.Start.X + wall.End.X) / 2);
            var centreY = exterior.Average(wall => (wall.Start.Y + wall.End.Y) / 2);
            var north = document.Site.NorthDirection;
            var east = document.Site.EastDirection;
            foreach (var wall in exterior.Where(wall => wall.Length > 1e-6))
            {
                var dx = (wall.End.X - wall.Start.X) / wall.Length;
                var dy = (wall.End.Y - wall.Start.Y) / wall.Length;
                var nx = -dy;
                var ny = dx;
                if (((wall.Start.X + wall.End.X) / 2 - centreX) * nx + ((wall.Start.Y + wall.End.Y) / 2 - centreY) * ny < 0) { nx = -nx; ny = -ny; }
                var azimuth = Math.Atan2(nx * east.X + ny * east.Y, nx * north.X + ny * north.Y) * 180 / Math.PI;
                var sector = (int)Math.Floor(((azimuth % 360 + 360) % 360 + 45) / 90) % 4;
                wallArea[sector] += wall.Length * wall.Height;
                windowArea[sector] += document.Openings.Where(opening => opening.WallId == wall.Id && opening.Kind == OpeningKind.Window).Sum(opening => opening.Width * opening.Height);
            }
        }
        var facades = names.Select((name, index) => new FacadeSummary(name, wallArea[index], windowArea[index])).ToList();
        var advice = new List<string>();
        if (facades.Sum(item => item.WallArea) < 1e-9) advice.Add("NoWalls");
        else
        {
            var summary = new EnergySummary(facades, []);
            if (summary.TotalWindowArea < 1e-9) advice.Add("NoWindows");
            if (summary.TotalRatio > 0.4) advice.Add("HighGlazing");
            if (facades[3].WindowRatio > 0.25) advice.Add("ReduceWest");
            if (facades[1].WindowRatio > 0.25) advice.Add("ReduceEast");
            if (facades[0].WindowArea + facades[2].WindowArea < (facades[1].WindowArea + facades[3].WindowArea) && summary.TotalWindowArea > 0) advice.Add("MoveGlazingNorthSouth");
            if (advice.Count == 0) advice.Add("Balanced");
        }
        return new EnergySummary(facades, advice);
    }
}

/// <summary>A summary of the plumbing and the electricity: what is served, what is wrong and the load.</summary>
public sealed record SystemsSummary(int PlumbingSystems, int FixturesServed, int FixturesUnserved, int PlumbingWarnings, int PlumbingFailures,
    int Panels, int Circuits, double ConnectedWatts, int CircuitsOverloaded, int CircuitsWarning)
{
    public static SystemsSummary Build(BuildingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var plumbing = PlumbingAnalysis.Analyze(document);
        var electrical = ElectricalAnalysis.Analyze(document);
        var circuits = electrical.Panels.SelectMany(panel => panel.Circuits).ToList();
        return new SystemsSummary(plumbing.Systems.Count, plumbing.Systems.Sum(system => system.Fixtures.Count), plumbing.Unserved.Count,
            plumbing.Systems.Count(system => system.Status == PlumbingStatus.Warning), plumbing.Systems.Count(system => system.Status == PlumbingStatus.Fail),
            electrical.Panels.Count, circuits.Count, circuits.Sum(circuit => circuit.LoadWatts),
            circuits.Count(circuit => circuit.Status is not (CircuitStatus.Ok or CircuitStatus.Warning)), circuits.Count(circuit => circuit.Status == CircuitStatus.Warning));
    }
}
