using System.Text.Json.Serialization;

namespace Guanacaste3D.Core;

/// <summary>Kinds of electrical devices placed in a building, grouped as receptacles, switches, luminaires and equipment.</summary>
public enum ElectricalDeviceKind
{
    /// <summary>Duplex receptacle, 120 V 15 A.</summary>
    Outlet,
    /// <summary>Ground-fault protected receptacle for kitchens, baths and outdoors.</summary>
    GfciOutlet,
    /// <summary>Duplex receptacle with USB charging ports.</summary>
    UsbOutlet,
    /// <summary>240 V receptacle for an air conditioner, range or dryer.</summary>
    Outlet240,
    /// <summary>Single-pole switch.</summary>
    Switch,
    /// <summary>Three-way switch: two of them control the same lights from two places.</summary>
    ThreeWaySwitch,
    /// <summary>Dimmer: switches and dims the lights it controls.</summary>
    Dimmer,
    /// <summary>Surface-mounted ceiling fixture.</summary>
    CeilingFixture,
    /// <summary>Recessed downlight in the ceiling.</summary>
    Downlight,
    /// <summary>Wall sconce.</summary>
    WallSconce,
    /// <summary>Pendant hanging from the ceiling.</summary>
    Pendant,
    /// <summary>Load center (breaker panel).</summary>
    Panel,
    /// <summary>Ceiling fan with a light kit: switched and dimmed like a luminaire, it also draws its motor load.</summary>
    CeilingFan,
    /// <summary>
    /// Ductless split air conditioner: the wall-mounted indoor unit, with its outdoor condenser outside the wall and the
    /// refrigerant line set between them. It runs on 240 V.
    /// </summary>
    MiniSplit,
    /// <summary>A cistern's pressure pump: motor, pump and pressure tank on a pad; a 120 V motor load.</summary>
    WaterPump,
    /// <summary>A photovoltaic array: rows of panels on a roof slope (or on a ground rack), wired to an inverter.</summary>
    SolarArray,
    /// <summary>
    /// Grid-tie inverter: turns the arrays' direct current into 240 V alternating current fed back through a two-pole
    /// breaker in the panel. It shuts down whenever the grid side is dead (anti-islanding).
    /// </summary>
    Inverter,
    /// <summary>Smoke alarm on the ceiling, 120 V with battery back-up, interconnected with the others.</summary>
    SmokeDetector,
    /// <summary>Emergency light: twin heads on the wall with a 90-minute battery that light the way out when power fails.</summary>
    EmergencyLight,
    /// <summary>Illuminated EXIT sign over a door on the way out, with battery back-up.</summary>
    ExitSign,
    /// <summary>Manual fire-alarm pull station beside an exit, wired to the fire-alarm panel.</summary>
    FireAlarmPullStation,
    /// <summary>Fire-alarm horn and strobe on the wall: the audible and visible notification of an alarm.</summary>
    HornStrobe,
    /// <summary>
    /// Automatic sprinkler head on the ceiling (pendent, 68 °C bulb). It is piped to the fire-protection riser, not
    /// wired: it has no circuit and no load.
    /// </summary>
    Sprinkler,
    /// <summary>Portable extinguisher (ABC, 4.5 kg) hung on the wall in its cabinet; a fire-protection item without power.</summary>
    FireExtinguisher,
    /// <summary>Fire-hose cabinet on the standpipe: 30 m of 38 mm hose and a 64 mm valve for the fire brigade.</summary>
    FireHoseCabinet
}

/// <summary>
/// One electrical device. Wall devices stand on a wall face at <see cref="Elevation"/> above their level and look out
/// along <see cref="Facing"/> (radians, plan); luminaires hang from <see cref="Elevation"/> (the ceiling). The device is
/// fed by the circuit <see cref="CircuitId"/>; a switch controls the luminaires (or lighting components) listed in
/// <see cref="Controls"/>.
/// </summary>
public sealed record ElectricalDevice(
    Guid Id,
    Guid LevelId,
    ElectricalDeviceKind Kind,
    string Name,
    Point2 Position,
    double Elevation)
{
    public double Facing { get; init; }

    public Guid? CircuitId { get; init; }

    /// <summary>Connected load in watts (volt-amperes): lamp power for a luminaire, planned load for a receptacle.</summary>
    public double Watts { get; init; }

    /// <summary>Switches: closed (on) or open. Panel: main breaker closed.</summary>
    public bool IsOn { get; init; } = true;

    /// <summary>Dimmers: output from 0.1 to 1.</summary>
    public double DimLevel { get; init; } = 1;

    /// <summary>Switches: the luminaires and lighting components they control.</summary>
    public List<Guid> Controls { get; init; } = [];

    /// <summary>Panels: rating of the main breaker in amperes.</summary>
    public double MainBreakerAmps { get; init; }

    /// <summary>Air conditioners: nominal cooling capacity in BTU per hour.</summary>
    public double CoolingBtu { get; init; }

    /// <summary>Air conditioners: where the outdoor condenser stands, outside the wall behind the indoor unit.</summary>
    public Point2? OutdoorPosition { get; init; }

    /// <summary>Solar arrays: number of panels and the rated power of each, in watts.</summary>
    public int PanelCount { get; init; }
    public double PanelWatts { get; init; }

    /// <summary>Solar arrays: tilt of the panels from horizontal in degrees; they face along <see cref="Facing"/>.</summary>
    public double Tilt { get; init; }

    /// <summary>Equipment that runs only on 240 V; everything else is a 120 V device.</summary>
    [JsonIgnore] public bool Needs240 => Kind is ElectricalDeviceKind.Outlet240 or ElectricalDeviceKind.MiniSplit or ElectricalDeviceKind.Inverter;

    /// <summary>A source of power on its circuit (its <see cref="Watts"/> is its output) rather than a load.</summary>
    [JsonIgnore] public bool IsGenerator => Kind == ElectricalDeviceKind.Inverter;

    /// <summary>Equipment wired outside the branch-circuit logic: arrays connect to their inverter, not to a breaker.</summary>
    [JsonIgnore] public bool IsDirectCurrent => Kind == ElectricalDeviceKind.SolarArray;

    [JsonIgnore] public bool IsSwitch => Kind is ElectricalDeviceKind.Switch or ElectricalDeviceKind.ThreeWaySwitch or ElectricalDeviceKind.Dimmer;

    [JsonIgnore] public bool IsLuminaire => Kind is ElectricalDeviceKind.CeilingFixture or ElectricalDeviceKind.Downlight
        or ElectricalDeviceKind.WallSconce or ElectricalDeviceKind.Pendant or ElectricalDeviceKind.CeilingFan;

    /// <summary>Life-safety equipment: alarms, emergency lighting, exit signs and the fire-protection items.</summary>
    [JsonIgnore] public bool IsLifeSafety => Kind is ElectricalDeviceKind.SmokeDetector or ElectricalDeviceKind.EmergencyLight
        or ElectricalDeviceKind.ExitSign or ElectricalDeviceKind.FireAlarmPullStation or ElectricalDeviceKind.HornStrobe
        || IsFireProtection;

    /// <summary>Fire-protection items that are piped or portable rather than wired: they never take a circuit.</summary>
    [JsonIgnore] public bool IsFireProtection => Kind is ElectricalDeviceKind.Sprinkler or ElectricalDeviceKind.FireExtinguisher
        or ElectricalDeviceKind.FireHoseCabinet;

    [JsonIgnore] public bool IsReceptacle => Kind is ElectricalDeviceKind.Outlet or ElectricalDeviceKind.GfciOutlet
        or ElectricalDeviceKind.UsbOutlet or ElectricalDeviceKind.Outlet240;
}

/// <summary>A branch circuit of a panel, protected by one breaker.</summary>
public sealed record ElectricalCircuit(Guid Id, Guid PanelId, int Number, string Name)
{
    public double BreakerAmps { get; init; } = 15;

    /// <summary>120 V for lighting and receptacles, 240 V (two poles) for large appliances.</summary>
    public double Voltage { get; init; } = 120;

    /// <summary>The breaker handle: off when the user opens it. A breaker also trips by itself when overloaded.</summary>
    public bool BreakerOn { get; init; } = true;
}

/// <summary>Default sizes and loads of electrical devices.</summary>
public static class ElectricalStandards
{
    /// <summary>Mounting height above the floor (or ceiling height for luminaires, resolved by the caller).</summary>
    public static double MountingHeight(ElectricalDeviceKind kind) => kind switch
    {
        ElectricalDeviceKind.Outlet or ElectricalDeviceKind.GfciOutlet or ElectricalDeviceKind.UsbOutlet => 0.30,
        ElectricalDeviceKind.Outlet240 => 0.60,
        ElectricalDeviceKind.Switch or ElectricalDeviceKind.ThreeWaySwitch or ElectricalDeviceKind.Dimmer => 1.20,
        ElectricalDeviceKind.WallSconce => 2.00,
        // The centre of a minisplit's indoor unit, a little below a 2.4–2.7 m ceiling.
        ElectricalDeviceKind.MiniSplit => 2.15,
        ElectricalDeviceKind.Inverter => 1.40,
        ElectricalDeviceKind.Panel => 1.50,
        // NFPA 72: manual stations 1.1–1.37 m, notification appliances 2.03–2.44 m; NFPA 10: extinguisher top ≤ 1.53 m.
        ElectricalDeviceKind.FireAlarmPullStation => 1.20,
        ElectricalDeviceKind.HornStrobe => 2.20,
        ElectricalDeviceKind.EmergencyLight => 2.30,
        ElectricalDeviceKind.ExitSign => 2.25,
        ElectricalDeviceKind.FireExtinguisher => 1.00,
        ElectricalDeviceKind.FireHoseCabinet => 0.90,
        _ => 0
    };

    /// <summary>Default connected load: lamp watts for LED luminaires, 180 VA per receptacle (NEC 220.14(I)), 3.5 kW for a 240 V outlet.</summary>
    public static double DefaultWatts(ElectricalDeviceKind kind) => kind switch
    {
        ElectricalDeviceKind.CeilingFixture => 18,
        ElectricalDeviceKind.Downlight => 12,
        ElectricalDeviceKind.WallSconce => 9,
        ElectricalDeviceKind.Pendant => 15,
        // A 52" fan draws about 60 W at high speed, plus its 15 W LED light kit.
        ElectricalDeviceKind.CeilingFan => 75,
        ElectricalDeviceKind.Outlet or ElectricalDeviceKind.GfciOutlet or ElectricalDeviceKind.UsbOutlet => 180,
        ElectricalDeviceKind.Outlet240 => 3500,
        ElectricalDeviceKind.MiniSplit => CoolingWatts(12000),
        ElectricalDeviceKind.Inverter => 5000,
        ElectricalDeviceKind.SmokeDetector => 1,
        // LED heads and signs trickle-charge their batteries; a horn-strobe draws little until it sounds.
        ElectricalDeviceKind.EmergencyLight => 4,
        ElectricalDeviceKind.ExitSign => 3,
        ElectricalDeviceKind.HornStrobe => 2,
        _ => 0
    };

    /// <summary>Rated power of a modern monocrystalline module, in watts, and its size (2.28 × 1.13 m).</summary>
    public const double PanelRating = 550;
    public const double PanelLength = 2.28;
    public const double PanelWidth = 1.13;

    /// <summary>Panel counts a selected array steps through.</summary>
    public static IReadOnlyList<int> PanelCounts { get; } = [4, 6, 8, 10, 12, 16, 20, 24, 30];

    /// <summary>AC ratings in which residential grid-tie inverters are sold, in watts.</summary>
    public static IReadOnlyList<double> InverterRatings { get; } = [3000, 3800, 5000, 6000, 7600, 10000, 11400];

    /// <summary>Capacities in which minisplits are sold, in BTU/h (9000 is ¾ ton, 12000 one ton).</summary>
    public static IReadOnlyList<double> CoolingCapacities { get; } = [9000, 12000, 18000, 24000, 36000];

    /// <summary>
    /// Cooling a hot, humid lowland room (Guanacaste, the coasts) takes about 600 BTU/h per square meter of floor:
    /// one ton (12000 BTU/h) for 20 m².
    /// </summary>
    public const double CoolingPerSquareMeter = 600;

    /// <summary>Electrical input of an air conditioner at an EER of 11 BTU/Wh, rounded to 10 W.</summary>
    public static double CoolingWatts(double btu) => Math.Round(btu / 11 / 10) * 10;

    /// <summary>The smallest standard capacity that cools a room of the given area, or the largest one.</summary>
    public static double CoolingCapacityFor(double area) =>
        CoolingCapacities.FirstOrDefault(capacity => capacity >= area * CoolingPerSquareMeter, CoolingCapacities[^1]);

    public static bool MountsOnWall(ElectricalDeviceKind kind) =>
        kind is not (ElectricalDeviceKind.CeilingFixture or ElectricalDeviceKind.Downlight or ElectricalDeviceKind.Pendant
            or ElectricalDeviceKind.CeilingFan or ElectricalDeviceKind.WaterPump or ElectricalDeviceKind.SolarArray
            or ElectricalDeviceKind.SmokeDetector or ElectricalDeviceKind.Sprinkler);

    /// <summary>NFPA 13, light hazard: the most floor one sprinkler protects (20.9 m², 225 ft²) and the widest spacing.</summary>
    public const double SprinklerCoverage = 20.9;
    public const double SprinklerSpacing = 4.6;

    /// <summary>NFPA 10, class A ordinary hazard: the farthest anyone may walk to reach an extinguisher (22.9 m, 75 ft).</summary>
    public const double ExtinguisherTravel = 22.9;

    /// <summary>NFPA 14, class III standpipe: a 30 m hose plus a 9 m stream must reach every point of the storey.</summary>
    public const double HoseReach = 39;

    /// <summary>Breaker ratings offered for branch circuits.</summary>
    public static IReadOnlyList<double> BreakerRatings { get; } = [15, 20, 30, 40, 50, 60];

    /// <summary>
    /// Smallest copper conductor (THHN, 75 °C) allowed for a breaker rating, by NEC 240.4(D) and table 310.16: its AWG
    /// size, cross-section in mm² and resistance in ohms per kilometer.
    /// </summary>
    public static (string Awg, double SquareMillimeters, double OhmsPerKilometer) Conductor(double breakerAmps) => breakerAmps switch
    {
        <= 15 => ("14", 2.08, 8.28),
        <= 20 => ("12", 3.31, 5.21),
        <= 30 => ("10", 5.26, 3.28),
        <= 40 => ("8", 8.37, 2.06),
        <= 55 => ("6", 13.3, 1.30),
        _ => ("4", 21.2, 0.82)
    };
}
