using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core;

/// <summary>Whether a photovoltaic system is producing, and why not.</summary>
public enum SolarStatus
{
    /// <summary>No array reaches this inverter.</summary>
    NoArrays,
    Ok,
    /// <summary>Producing, but the array and inverter are poorly matched or the panel breaks the 120 % rule.</summary>
    Warning,
    /// <summary>The inverter's circuit or panel is dead, so it shuts down by anti-islanding and produces nothing.</summary>
    Offline
}

/// <summary>
/// One inverter with the arrays wired to it: DC and AC power, their ratio, the daily energy the system yields, the house's
/// estimated daily use and the share of it the sun covers.
/// </summary>
public sealed record SolarReport(ElectricalDevice Inverter, IReadOnlyList<ElectricalDevice> Arrays, double DcWatts, double AcWatts,
    double DcToAc, double DailyKilowattHours, double ConsumptionKilowattHours, double Coverage, bool Producing, SolarStatus Status,
    string Issue);

/// <summary>
/// Photovoltaic yield and fit. Daily energy is kWp × peak sun hours × performance ratio × orientation factor: 5.2 peak
/// sun hours (Guanacaste's yearly mean), 0.80 for heat, wiring and inverter losses. Panels facing south at about 10°
/// yield most at this latitude; facing north or steep loses part of it. A DC/AC ratio between 1.0 and 1.35 is the usual
/// sizing. The house's daily use is estimated from its connected loads and typical hours of use.
/// </summary>
public static class SolarAnalysis
{
    public const double PeakSunHours = 5.2;
    public const double PerformanceRatio = 0.80;
    public const double OptimalTilt = 10;

    /// <summary>Share of the best yield an array keeps, by its tilt and how far it faces from south (plan +Y is north).</summary>
    public static double OrientationFactor(double tiltDegrees, double facing)
    {
        var south = -Math.PI / 2;
        var away = 1 - Math.Cos(facing - south);
        return Math.Clamp(1 - away / 2 * Math.Sin(tiltDegrees * Math.PI / 180) * 0.6 - Math.Abs(tiltDegrees - OptimalTilt) * 0.002, 0.3, 1);
    }

    /// <summary>Daily energy in kWh of an array.</summary>
    public static double DailyYield(ElectricalDevice array) =>
        array.PanelCount * array.PanelWatts / 1000 * PeakSunHours * PerformanceRatio * OrientationFactor(array.Tilt, array.Facing);

    /// <summary>Typical hours of use per day of each kind of load, for the daily energy estimate.</summary>
    public static double HoursOfUse(ElectricalDeviceKind kind) => kind switch
    {
        ElectricalDeviceKind.CeilingFixture or ElectricalDeviceKind.Downlight or ElectricalDeviceKind.WallSconce or
            ElectricalDeviceKind.Pendant => 5,
        ElectricalDeviceKind.CeilingFan => 8,
        ElectricalDeviceKind.MiniSplit => 8,
        ElectricalDeviceKind.WaterPump => 1,
        ElectricalDeviceKind.Outlet240 => 1,
        ElectricalDeviceKind.Outlet or ElectricalDeviceKind.GfciOutlet or ElectricalDeviceKind.UsbOutlet => 2,
        _ => 0
    };

    /// <summary>The house's estimated daily use in kWh: every load times its typical hours.</summary>
    public static double DailyConsumption(BuildingDocument document) =>
        document.ElectricalDevices.Sum(device => device.IsGenerator || device.IsDirectCurrent ? 0 : device.Watts * HoursOfUse(device.Kind)) / 1000;

    public static IReadOnlyList<SolarReport> Analyze(BuildingDocument document, ElectricalAnalysis? electrical = null)
    {
        electrical ??= ElectricalAnalysis.Analyze(document);
        var inverters = document.ElectricalDevices.Where(device => device.Kind == ElectricalDeviceKind.Inverter).ToList();
        var arrays = document.ElectricalDevices.Where(device => device.Kind == ElectricalDeviceKind.SolarArray).ToList();
        var consumption = DailyConsumption(document);
        return inverters.Select(inverter =>
        {
            // Each array is wired to the nearest inverter.
            var mine = arrays.Where(array => inverters.MinBy(candidate => candidate.Position.DistanceTo(array.Position))!.Id == inverter.Id).ToList();
            var dc = mine.Sum(array => array.PanelCount * array.PanelWatts);
            var ac = inverter.Watts;
            var ratio = ac > 0 ? dc / ac : 0;
            // A grid-tie inverter needs a live grid connection: on a dead circuit it stops (anti-islanding).
            var producing = mine.Count > 0 && inverter.CircuitId is not null && electrical.Energized(inverter);
            var daily = producing ? Math.Min(mine.Sum(DailyYield), ac / 1000 * PeakSunHours) : 0;
            var panel = electrical.Panels.FirstOrDefault(report => report.Circuits.Any(circuit => circuit.Circuit.Id == inverter.CircuitId));
            var (status, issue) = mine.Count == 0 ? (SolarStatus.NoArrays, "Solar.Issue.NoArrays")
                : !producing ? (SolarStatus.Offline, inverter.CircuitId is null ? "Solar.Issue.NotConnected" : "Solar.Issue.GridDown")
                : ratio > 1.35 ? (SolarStatus.Warning, "Solar.Issue.Clipping")
                : ratio < 0.8 ? (SolarStatus.Warning, "Solar.Issue.InverterOversized")
                : panel is { BackfeedWithinLimit: false } ? (SolarStatus.Warning, "Solar.Issue.Backfeed")
                : (SolarStatus.Ok, string.Empty);
            return new SolarReport(inverter, mine, dc, ac, ratio, daily, consumption, consumption > 0 ? daily / consumption : 0, producing,
                status, issue);
        }).ToList();
    }
}

/// <summary>Where a solar array sits and how it is tilted: on the roof under it, or on a ground rack.</summary>
public static class SolarGeometry
{
    /// <summary>Elevation above the level (top of the panels' plane at the centre), tilt in degrees and facing in radians.</summary>
    public static (double Elevation, double Tilt, double Facing) Mounting(BuildingDocument document, Guid levelId, Point2 position)
    {
        foreach (var roof in document.Floors.Where(floor => floor.Kind == FloorKind.Roof))
        {
            var built = RoofGeometry.WithOverhang(roof);
            if (!Inside(built.Boundary, position)) continue;
            var level = document.Levels.FirstOrDefault(candidate => candidate.Id == roof.LevelId)?.Elevation ?? 0;
            var here = document.Levels.FirstOrDefault(candidate => candidate.Id == levelId)?.Elevation ?? 0;
            var surface = level + built.ElevationOffset + RoofGeometry.RiseAt(built, position) - here;
            if (!roof.IsPitched) return (surface + 0.35, OptimalTilt, -Math.PI / 2);
            // Downhill is against the gradient of the roof surface.
            const double step = 0.05;
            var gx = RoofGeometry.RiseAt(built, new Point2(position.X + step, position.Y)) - RoofGeometry.RiseAt(built, new Point2(position.X - step, position.Y));
            var gy = RoofGeometry.RiseAt(built, new Point2(position.X, position.Y + step)) - RoofGeometry.RiseAt(built, new Point2(position.X, position.Y - step));
            var facing = Math.Abs(gx) + Math.Abs(gy) < 1e-9 ? -Math.PI / 2 : Math.Atan2(-gy, -gx);
            return (surface + 0.10, roof.Pitch, facing);
        }
        return (1.0, OptimalTilt, -Math.PI / 2);
    }

    private const double OptimalTilt = SolarAnalysis.OptimalTilt;

    private static bool Inside(IReadOnlyList<Point2> polygon, Point2 point)
    {
        var inside = false;
        for (int index = 0, previous = polygon.Count - 1; index < polygon.Count; previous = index++)
        {
            var (a, b) = (polygon[index], polygon[previous]);
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    /// <summary>The panels' layout: rows and columns of portrait modules filling the array.</summary>
    public static (int Rows, int Columns) Layout(int count)
    {
        var rows = Math.Max(1, (int)Math.Round(Math.Sqrt(count / 2.5)));
        return (rows, (int)Math.Ceiling(count / (double)rows));
    }
}
