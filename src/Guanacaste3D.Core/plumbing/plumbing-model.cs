using System.Text.Json.Serialization;

namespace Guanacaste3D.Core;

/// <summary>The three piped systems of a house: cold water, hot water and the sanitary drain.</summary>
public enum PlumbingSystemKind
{
    ColdWater,
    HotWater,
    Sanitary,
    /// <summary>Rainwater: roof downspouts to the storm outlet, kept apart from the sanitary drain.</summary>
    StormWater
}

/// <summary>The points where the building meets the public networks.</summary>
public enum PlumbingPointKind
{
    /// <summary>Water meter and service entry: the source of every cold-water system.</summary>
    WaterMeter,
    /// <summary>Building sewer outlet: where the drain leaves for the street sewer or the septic tank.</summary>
    SewerOutlet,
    /// <summary>
    /// Vent stack: a pipe from the drain up through the roof that lets air into the drains, so a draining fixture does
    /// not siphon the water seal out of the traps near it.
    /// </summary>
    VentStack,
    /// <summary>Septic tank with its drain field, where there is no public sewer: a sanitary outlet that treats on site.</summary>
    SepticTank,
    /// <summary>Storm outlet: where the rainwater drain discharges to the street gutter (cuneta) or a soakaway.</summary>
    StormOutlet,
    /// <summary>
    /// Cistern with a pressure pump: a storage tank on the ground, filled by the meter, whose pump (an electrical device)
    /// pressurises the house. Without power the pump stops and the house has no pressure.
    /// </summary>
    Cistern,
    /// <summary>Elevated tank on a tower: feeds the house by gravity, with the pressure its height gives.</summary>
    ElevatedTank
}

/// <summary>A water meter or sewer outlet on a level, at the edge of the building.</summary>
public sealed record PlumbingPoint(Guid Id, Guid LevelId, PlumbingPointKind Kind, string Name, Point2 Position)
{
    /// <summary>Meters: static pressure of the street main in kilopascals (280 kPa is about 40 psi).</summary>
    public double SupplyPressureKPa { get; init; } = 280;

    /// <summary>Sewer outlets: depth of the drain invert below the level, in meters, where it meets the public sewer.</summary>
    public double InvertDepth { get; init; } = 0.60;

    /// <summary>Septic tanks: people served; zero takes two per bedroom.</summary>
    public int Occupants { get; init; }

    /// <summary>Septic tanks: what the soil absorbs, in liters per square meter of trench bottom per day.</summary>
    public double Percolation { get; init; } = 40;

    /// <summary>Storm outlets: design rainfall intensity in mm/h (a short tropical downpour; 150 mm/h in Guanacaste).</summary>
    public double RainfallMmPerHour { get; init; } = 150;

    /// <summary>Storage tanks: nominal capacity in liters; zero takes the standard tank the occupants need.</summary>
    public double CapacityLiters { get; init; }

    /// <summary>Cisterns: the pump's cut-out pressure in kPa (a 40 psi pressure switch) and its motor power in HP.</summary>
    public double PumpPressureKPa { get; init; } = 280;
    public double PumpHorsepower { get; init; } = 1;

    /// <summary>Cisterns: the electrical device that is the pump.</summary>
    public Guid? PumpDeviceId { get; init; }

    /// <summary>Elevated tanks: height of the tank's floor above the level, in meters.</summary>
    public double TankHeight { get; init; } = 6;

    /// <summary>Whether the point feeds a cold-water system: the meter, a cistern's pump or an elevated tank.</summary>
    [JsonIgnore] public bool IsColdSource => Kind is PlumbingPointKind.WaterMeter or PlumbingPointKind.Cistern or PlumbingPointKind.ElevatedTank;

    /// <summary>Whether the point receives the sanitary drain: a sewer connection or a septic tank.</summary>
    [JsonIgnore] public bool IsSanitaryOutlet => Kind is PlumbingPointKind.SewerOutlet or PlumbingPointKind.SepticTank;
}

/// <summary>
/// One piped system from its source to the fixtures it serves. Cold water flows from a water meter, hot water from a
/// water heater (a component), and the sanitary drain runs from the fixtures to a sewer outlet.
/// </summary>
public sealed record PlumbingSystem(Guid Id, PlumbingSystemKind Kind, Guid SourceId, string Name)
{
    /// <summary>The fixtures (residential components) connected to the system.</summary>
    public List<Guid> Fixtures { get; init; } = [];
}

/// <summary>
/// What a fixture draws and discharges: water supply fixture units (WSFU, IPC table E103.3(2), private use) for cold
/// and hot water, drainage fixture units (DFU, IPC table 709.1), the smallest trap and drain, the height of its supply
/// stop above the floor, and the minimum flowing pressure it needs (IPC table 604.3).
/// </summary>
public sealed record PlumbingFixtureSpec(double ColdUnits, double HotUnits, double DrainUnits, int MinDrainSize,
    double InletHeight, double MinPressureKPa)
{
    /// <summary>
    /// Height of the trap's outlet (the trap arm) above the floor; negative for a trap under the floor (showers, tubs),
    /// NaN for a fixture with an integral trap (toilets, urinals).
    /// </summary>
    public double TrapHeight { get; init; } = double.NaN;

    /// <summary>Length of the tailpiece above the trap; a washing machine's standpipe is much longer.</summary>
    public double Tailpiece { get; init; } = 0.15;

    public bool NeedsCold => ColdUnits > 0;
    public bool NeedsHot => HotUnits > 0;
    public bool Drains => DrainUnits > 0;
}

/// <summary>Fixture data, pipe sizes and hydraulic formulas used by the plumbing analysis.</summary>
public static class PlumbingStandards
{
    /// <summary>Copper-tube-size supply pipe (PEX, CPVC or copper): nominal size, inside and outside diameters in mm.</summary>
    public static IReadOnlyList<(string Nominal, double Inside, double Outside)> SupplySizes { get; } =
    [
        ("1/2\"", 13.8, 15.9), ("3/4\"", 19.9, 22.2), ("1\"", 26.0, 28.6), ("1 1/4\"", 32.1, 34.9),
        ("1 1/2\"", 38.2, 41.3), ("2\"", 50.4, 54.0)
    ];

    /// <summary>
    /// PVC drain pipe: nominal size, inside and outside diameters in mm, and the drainage fixture units a horizontal
    /// drain of that size carries at 2 % slope (IPC tables 710.1(1) and 710.1(2)).
    /// </summary>
    public static IReadOnlyList<(string Nominal, double Inside, double Outside, double Capacity)> DrainSizes { get; } =
    [
        ("1 1/2\"", 38, 48, 3), ("2\"", 47, 60, 6), ("3\"", 72, 89, 42), ("4\"", 104, 114, 216), ("6\"", 152, 168, 840)
    ];

    /// <summary>The slope of every horizontal drain: 2 %, a quarter inch per foot.</summary>
    public const double DrainSlope = 0.02;

    /// <summary>Highest velocity allowed in cold-water pipe, m/s; hot water is held lower to limit erosion and noise.</summary>
    public const double ColdVelocity = 2.4;
    public const double HotVelocity = 1.5;

    /// <summary>Hazen–Williams coefficient for plastic and copper tube.</summary>
    public const double HazenWilliams = 150;

    /// <summary>Equivalent length added for fittings and valves, as a fraction of the straight length.</summary>
    public const double FittingAllowance = 0.25;

    /// <summary>Hot water should reach a fixture within this many seconds (ASPE: up to 10 s good, 11–30 s marginal).</summary>
    public const double HotWaitLimit = 30;

    /// <summary>Data of a residential component as a plumbing fixture, or null when it has no water or drain.</summary>
    public static PlumbingFixtureSpec? Fixture(StructuralElement element)
    {
        if (element.Kind != StructuralElementKind.Component) return null;
        return element.ComponentShape switch
        {
            ResidentialComponentShape.Toilet => new(2.2, 0, 3, 2, 0.20, 138),
            ResidentialComponentShape.Vanity when element.Width >= 1.3 => new(1.0, 1.0, 2, 0, 0.50, 55) { TrapHeight = 0.45 },
            ResidentialComponentShape.Vanity or ResidentialComponentShape.PedestalSink => new(0.5, 0.5, 1, 0, 0.50, 55) { TrapHeight = 0.45 },
            ResidentialComponentShape.KitchenSink => new(1.0, 1.0, 2, 0, 0.50, 55) { TrapHeight = 0.45 },
            ResidentialComponentShape.Bathtub => new(1.0, 1.0, 2, 0, 0.60, 138) { TrapHeight = -0.15 },
            ResidentialComponentShape.Shower => new(1.0, 1.0, 2, 1, 1.00, 138) { TrapHeight = -0.15 },
            ResidentialComponentShape.Dishwasher => new(0, 1.4, 2, 0, 0.15, 55) { TrapHeight = 0.30 },
            // A washer drains into a 2" standpipe that rises about 60 cm above its trap.
            ResidentialComponentShape.WasherDryer => new(1.0, 1.0, 3, 1, 1.10, 55) { TrapHeight = 0.30, Tailpiece = 0.60 },
            ResidentialComponentShape.Bidet => new(0.5, 0.5, 1, 0, 0.20, 138) { TrapHeight = 0.10 },
            ResidentialComponentShape.Urinal => new(2.2, 0, 2, 1, 1.10, 55),
            // The heater's cold demand is the hot demand it serves; the analysis fills it in.
            ResidentialComponentShape.WaterHeater => new(0, 0, 0, 0, Math.Max(0.3, element.Height - 0.1), 55),
            _ => null
        };
    }

    public static bool IsWaterHeater(StructuralElement element) =>
        element.Kind == StructuralElementKind.Component && element.ComponentShape == ResidentialComponentShape.WaterHeater;

    /// <summary>
    /// Where a fixture's pipes connect: the middle of its back, which stands against the wall where the stub-outs come
    /// through. Families are authored with their front toward -Y, so the back is +Y turned by the instance's rotation.
    /// </summary>
    public static Point2 Connection(StructuralElement element)
    {
        var depth = element.Depth > 0 ? element.Depth : element.Width;
        var back = Math.Max(0, depth / 2 - 0.06);
        return new Point2(element.Start.X - Math.Sin(element.Rotation) * back, element.Start.Y + Math.Cos(element.Rotation) * back);
    }

    /// <summary>
    /// Probable peak flow in liters per second for a number of water supply fixture units, by Hunter's curve for
    /// flush-tank systems (IPC table E103.3(3)), interpolated.
    /// </summary>
    public static double PeakFlow(double units)
    {
        if (units <= 0) return 0;
        double[] wsfu = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 14, 16, 18, 20, 25, 30, 35, 40, 45, 50, 60, 70, 80, 90, 100];
        double[] gpm = [3.0, 5.0, 6.5, 8.0, 9.4, 10.7, 11.8, 12.8, 13.7, 14.6, 16.0, 17.0, 18.0, 18.8, 19.6, 21.5, 23.3, 24.9, 26.3,
            27.7, 29.1, 32.0, 35.0, 38.0, 41.0, 43.5];
        double flow;
        if (units <= 1) flow = 3.0 * units;
        else if (units >= 100) flow = 43.5 + (units - 100) * 0.25;
        else
        {
            var index = Array.FindIndex(wsfu, value => value >= units);
            var t = (units - wsfu[index - 1]) / (wsfu[index] - wsfu[index - 1]);
            flow = gpm[index - 1] + (gpm[index] - gpm[index - 1]) * t;
        }
        return flow * 0.0630902;
    }

    /// <summary>The smallest supply size that keeps the velocity of <paramref name="flow"/> (L/s) under the limit.</summary>
    public static int SupplySize(double flow, bool hot)
    {
        var limit = hot ? HotVelocity : ColdVelocity;
        for (var index = 0; index < SupplySizes.Count; index++)
            if (Velocity(flow, SupplySizes[index].Inside) <= limit) return index;
        return SupplySizes.Count - 1;
    }

    /// <summary>Mean velocity in m/s of <paramref name="flow"/> L/s in a pipe of the given inside diameter in mm.</summary>
    public static double Velocity(double flow, double insideMillimeters)
    {
        var diameter = insideMillimeters / 1000;
        return flow / 1000 / (Math.PI * diameter * diameter / 4);
    }

    /// <summary>Friction loss in kPa over <paramref name="length"/> m of pipe, by Hazen–Williams.</summary>
    public static double FrictionLoss(double flow, double insideMillimeters, double length)
    {
        if (flow <= 0 || length <= 0) return 0;
        var q = flow / 1000;
        var d = insideMillimeters / 1000;
        var head = 10.67 * length * Math.Pow(q, 1.852) / (Math.Pow(HazenWilliams, 1.852) * Math.Pow(d, 4.8704));
        return head * 9.81;
    }

    /// <summary>
    /// Longest trap arm, from the trap weir to its vent, for a drain size (IPC table 909.1): 6 ft for 1 1/2", 8 ft for
    /// 2", 12 ft for 3", 16 ft for 4", in meters. A longer arm lets the trap siphon dry.
    /// </summary>
    public static double TrapArmLimit(int drainSize) => drainSize switch
    {
        <= 0 => 1.8,
        1 => 2.4,
        2 => 3.6,
        _ => 4.9
    };

    /// <summary>Vent stack size (an index into <see cref="DrainSizes"/>) for the drainage fixture units it vents.</summary>
    public static int VentSize(double units) => units <= 8 ? 0 : units <= 24 ? 1 : 2;

    /// <summary>A vent stack rises this far above the highest wall top, through the roof.</summary>
    public const double VentAboveRoof = 0.30;

    /// <summary>The smallest drain that carries <paramref name="units"/> DFU and is at least <paramref name="minimum"/>.</summary>
    public static int DrainSize(double units, int minimum)
    {
        for (var index = Math.Max(0, minimum); index < DrainSizes.Count; index++)
            if (DrainSizes[index].Capacity >= units) return index;
        return DrainSizes.Count - 1;
    }
}
