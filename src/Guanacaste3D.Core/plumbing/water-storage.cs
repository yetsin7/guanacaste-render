namespace Guanacaste3D.Core;

/// <summary>
/// A storage tank's design: daily demand of the people it serves, the reserve it must hold and the tank chosen, and for
/// a cistern the power its pump needs to deliver the house's peak flow at its pressure.
/// </summary>
public sealed record StorageReport(Guid Id, string Name, PlumbingPointKind Kind, int Occupants, double DailyLiters,
    double RequiredLiters, double CapacityLiters, double PeakFlow, double PumpRequiredHorsepower, double PumpHorsepower,
    bool PumpPowered, PlumbingStatus Status, string Issue);

/// <summary>
/// Water storage: a day of reserve at 200 L per person (the usual design figure for houses in Costa Rica), rounded up to
/// the standard polyethylene tanks sold, and the pump power P = Q·p/η with a 50 % efficient small centrifugal pump.
/// </summary>
public static class WaterStorage
{
    public const double LitersPerPerson = 200;
    public const double PumpEfficiency = 0.5;

    /// <summary>Standard tank sizes in liters.</summary>
    public static IReadOnlyList<double> TankSizes { get; } = [450, 750, 1100, 2500, 5000, 10000, 20000];

    /// <summary>Standard pump motors in HP.</summary>
    public static IReadOnlyList<double> PumpSizes { get; } = [0.5, 0.75, 1, 1.5, 2, 3];

    /// <summary>Electrical input of a pump motor of the given horsepower (746 W/HP at 70 % motor efficiency), to 10 W.</summary>
    public static double PumpWatts(double horsepower) => Math.Round(horsepower * 746 / 0.7 / 10) * 10;

    public static double TankFor(double liters) => TankSizes.FirstOrDefault(size => size >= liters, TankSizes[^1]);

    /// <summary>Diameter and height of a cylindrical tank of the given capacity, about as tall as it is wide.</summary>
    public static (double Diameter, double Height) TankShape(double liters)
    {
        var volume = liters / 1000;
        var diameter = Math.Cbrt(4 * volume / Math.PI);
        return (diameter, volume / (Math.PI * diameter * diameter / 4));
    }

    public static StorageReport Design(BuildingDocument document, PlumbingPoint tank, double peakFlow, bool pumpPowered)
    {
        var people = SiteDrainage.Occupants(document, tank);
        var daily = people * LitersPerPerson;
        var capacity = tank.CapacityLiters > 0 ? tank.CapacityLiters : TankFor(daily);
        var pumped = tank.Kind == PlumbingPointKind.Cistern;
        var required = pumped ? peakFlow * tank.PumpPressureKPa / PumpEfficiency / 746 : 0;
        var (status, issue) = capacity < daily ? (PlumbingStatus.Warning, "Plumbing.Issue.TankSmall")
            : pumped && !pumpPowered ? (PlumbingStatus.Fail, "Plumbing.Issue.PumpOff")
            : pumped && required > tank.PumpHorsepower ? (PlumbingStatus.Warning, "Plumbing.Issue.PumpSmall")
            : (PlumbingStatus.Ok, string.Empty);
        return new StorageReport(tank.Id, tank.Name, tank.Kind, people, daily, daily, capacity, peakFlow, required,
            pumped ? tank.PumpHorsepower : 0, pumpPowered, status, issue);
    }
}
