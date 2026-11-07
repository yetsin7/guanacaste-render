using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core;

/// <summary>
/// A septic tank's design for the people it serves: daily sewage, the tank volume and its inside dimensions, and the
/// infiltration trenches of its drain field, which run away from the house along <see cref="FieldDirection"/>.
/// </summary>
public sealed record SepticReport(Guid Id, string Name, Point2 Position, int Occupants, double DailyLiters, double VolumeLiters,
    double Length, double Width, double LiquidDepth, double FieldArea, double FieldLength, int Trenches, Point2 FieldDirection);

/// <summary>A roof's runoff: its plan area with the eaves, the design flow, and the downspouts that drain it.</summary>
public sealed record RoofDrainageReport(Guid RoofId, string Name, double Area, double Flow, int Downspouts, double Capacity,
    PlumbingStatus Status);

/// <summary>
/// Rainwater and septic design. Roof runoff is the rational method, Q = C·i·A with C = 0.95 for a roof, at the design
/// rainfall of the storm outlet. A downspout carries what a vertical leader of its section carries (UPC 1101.8, scaled
/// as d^8/3). Buried storm drains are sized for full flow by Manning (n = 0.010, PVC) at 1 % slope. The septic tank
/// follows the method of NBR 7229, used across Latin America: V = 1000 + N·(C·T + K·Lf). Its drain field is sized by
/// the soil's infiltration rate.
/// </summary>
public static class SiteDrainage
{
    public const double RoofRunoff = 0.95;
    public const double StormSlope = 0.01;
    public const double ManningN = 0.010;

    /// <summary>PVC storm drain: nominal size, inside and outside diameters in mm.</summary>
    public static IReadOnlyList<(string Nominal, double Inside, double Outside)> StormSizes { get; } =
    [
        ("3\"", 72, 89), ("4\"", 104, 114), ("6\"", 152, 168), ("8\"", 200, 219), ("10\"", 250, 273)
    ];

    /// <summary>Peak runoff in L/s from a roof of <paramref name="area"/> m² under <paramref name="rainfall"/> mm/h.</summary>
    public static double RoofFlow(double area, double rainfall) => RoofRunoff * rainfall * area / 3600;

    /// <summary>
    /// What a downspout carries, in L/s. A 4" round leader drains 576 m² at 25.4 mm/h (UPC 1101.8), 4.06 L/s. Other sizes
    /// scale as the 8/3 power of the diameter; a square section counts as the circle of equal area.
    /// </summary>
    public static double LeaderCapacity(StructuralElement downspout)
    {
        var diameter = downspout.Style == ElementStyle.Round ? downspout.Width : downspout.Width * 1.128;
        return 4.06 * Math.Pow(Math.Max(0.02, diameter) / 0.1016, 8.0 / 3);
    }

    /// <summary>Full-bore capacity in L/s of a pipe of the given inside diameter (mm) at a slope, by Manning.</summary>
    public static double ManningCapacity(double insideMillimeters, double slope)
    {
        var d = insideMillimeters / 1000;
        var area = Math.PI * d * d / 4;
        return area / ManningN * Math.Pow(d / 4, 2.0 / 3) * Math.Sqrt(slope) * 1000;
    }

    /// <summary>The smallest storm drain that carries <paramref name="flow"/> L/s at 1 %.</summary>
    public static int StormSize(double flow)
    {
        for (var index = 0; index < StormSizes.Count; index++)
            if (ManningCapacity(StormSizes[index].Inside, StormSlope) >= flow) return index;
        return StormSizes.Count - 1;
    }

    /// <summary>Roofs of a project: every roof slab, with its plan outline including the eaves.</summary>
    public static IEnumerable<(FloorSlab Roof, IReadOnlyList<Point2> Outline, double Area)> Roofs(BuildingDocument document) =>
        document.Floors.Where(floor => floor.Kind == FloorKind.Roof).Select(floor =>
        {
            var outline = RoofGeometry.WithOverhang(floor).Boundary;
            return (floor, (IReadOnlyList<Point2>)outline, Math.Abs(SignedArea(outline)));
        });

    /// <summary>Whether a downspout drains a roof: it stands within half a meter of the roof's outline.</summary>
    public static bool Drains(IReadOnlyList<Point2> outline, Point2 point)
    {
        var (minX, maxX) = (outline.Min(p => p.X) - 0.5, outline.Max(p => p.X) + 0.5);
        var (minY, maxY) = (outline.Min(p => p.Y) - 0.5, outline.Max(p => p.Y) + 0.5);
        return point.X >= minX && point.X <= maxX && point.Y >= minY && point.Y <= maxY;
    }

    /// <summary>The flow each downspout takes: the runoff of the roofs it drains, shared equally with their other downspouts.</summary>
    public static Dictionary<Guid, double> DownspoutFlows(BuildingDocument document, double rainfall)
    {
        var spouts = document.StructuralElements.Where(element => element.Kind == StructuralElementKind.Downspout).ToList();
        var flows = spouts.ToDictionary(spout => spout.Id, _ => 0.0);
        foreach (var (_, outline, area) in Roofs(document))
        {
            var draining = spouts.Where(spout => Drains(outline, spout.Start)).ToList();
            foreach (var spout in draining) flows[spout.Id] += RoofFlow(area, rainfall) / draining.Count;
        }
        return flows;
    }

    // ------------------------------------------------------------------ septic

    /// <summary>Sewage per person and day (medium standard), in liters.</summary>
    public const double SewagePerPerson = 150;

    /// <summary>Useful liquid depth of a small septic tank, in meters (NBR 7229: 1.2 to 2.2 m up to 6 m³).</summary>
    public const double SepticDepth = 1.2;

    /// <summary>Bottom width of an infiltration trench, in meters.</summary>
    public const double TrenchWidth = 0.6;

    /// <summary>Longest single trench before the field is split into parallel ones, in meters.</summary>
    public const double TrenchMaxLength = 20;

    /// <summary>
    /// People served: those set on the tank, otherwise two per bedroom (rooms named dormitorio, habitación or bedroom),
    /// otherwise four.
    /// </summary>
    public static int Occupants(BuildingDocument document, PlumbingPoint tank)
    {
        if (tank.Occupants > 0) return tank.Occupants;
        var bedrooms = document.Rooms.Count(room => room.Name.Contains("dormitorio", StringComparison.OrdinalIgnoreCase) ||
                                                    room.Name.Contains("habitaci", StringComparison.OrdinalIgnoreCase) ||
                                                    room.Name.Contains("bedroom", StringComparison.OrdinalIgnoreCase));
        return bedrooms > 0 ? 2 * bedrooms : 4;
    }

    /// <summary>The tank and drain field for a septic tank point.</summary>
    public static SepticReport Septic(BuildingDocument document, PlumbingPoint tank)
    {
        var people = Occupants(document, tank);
        var daily = people * SewagePerPerson;
        // Detention time falls as the daily flow grows (NBR 7229 table 2); K = 57 for yearly emptying above 20 °C.
        var detention = daily <= 1500 ? 1.0 : daily <= 3000 ? 0.92 : daily <= 4500 ? 0.83 : 0.75;
        var volume = 1000 + people * (SewagePerPerson * detention + 57 * 1.0);
        // A rectangular tank twice as long as it is wide.
        var width = Math.Sqrt(volume / 1000 / (2 * SepticDepth));
        var area = daily / Math.Max(1, tank.Percolation);
        var length = area / TrenchWidth;
        var trenches = Math.Max(1, (int)Math.Ceiling(length / TrenchMaxLength));
        // The field runs away from the house: from the middle of the walls through the tank.
        var walls = document.Walls.Where(wall => wall.LevelId == tank.LevelId).ToList();
        var (cx, cy) = walls.Count > 0
            ? (walls.Average(wall => (wall.Start.X + wall.End.X) / 2), walls.Average(wall => (wall.Start.Y + wall.End.Y) / 2))
            : (tank.Position.X - 1, tank.Position.Y);
        var (dx, dy) = (tank.Position.X - cx, tank.Position.Y - cy);
        var norm = Math.Sqrt(dx * dx + dy * dy);
        var direction = norm > 1e-6 ? new Point2(dx / norm, dy / norm) : new Point2(1, 0);
        return new SepticReport(tank.Id, tank.Name, tank.Position, people, daily, volume, 2 * width, width, SepticDepth, area, length,
            trenches, direction);
    }

    private static double SignedArea(IReadOnlyList<Point2> outline)
    {
        var sum = 0.0;
        for (var index = 0; index < outline.Count; index++)
        {
            var a = outline[index];
            var b = outline[(index + 1) % outline.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return sum / 2;
    }
}
