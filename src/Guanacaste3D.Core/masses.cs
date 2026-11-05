using System.Numerics;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Core;

/// <summary>When the sun rises and sets at a place on a day, and how high it climbs at noon.</summary>
public sealed record SolarDay(string Name, int DayOfYear, double? SunriseHour, double? SunsetHour, double NoonAltitudeDegrees)
{
    /// <summary>Hours between sunrise and sunset; 0 in polar night and 24 in midnight sun.</summary>
    public double DaylightHours => SunriseHour is { } rise && SunsetHour is { } set ? set - rise : NoonAltitudeDegrees > 0 ? 24 : 0;
}

/// <summary>The sun's course at the project's latitude on the four days that frame the year.</summary>
public static class SiteSolarData
{
    /// <summary>March and September equinoxes and June and December solstices, as days of the year.</summary>
    public static readonly (string Key, int Day)[] KeyDays = [("MarchEquinox", 80), ("JuneSolstice", 172), ("SeptemberEquinox", 266), ("DecemberSolstice", 355)];

    public static IReadOnlyList<SolarDay> ForLatitude(double latitude) => KeyDays.Select(day => OnDay(day.Key, latitude, day.Day)).ToArray();

    /// <summary>Sunrise and sunset as the local solar hours at which the sun crosses the horizon, found minute by minute.</summary>
    public static SolarDay OnDay(string name, double latitude, int dayOfYear)
    {
        double Altitude(double hour) => SolarPosition.AltitudeDegrees(SolarPosition.Direction(latitude, dayOfYear, hour));
        double? rise = null;
        double? set = null;
        var previous = Altitude(0);
        for (var minute = 1; minute <= 24 * 60; minute++)
        {
            var hour = minute / 60.0;
            var current = Altitude(hour);
            if (previous <= 0 && current > 0 && rise is null) rise = hour - 1 / 60.0;
            if (previous > 0 && current <= 0) set = hour - 1 / 60.0;
            previous = current;
        }
        return new SolarDay(name, dayOfYear, rise, set, Altitude(12));
    }
}

public sealed partial class DocumentSession
{
    private StructuralElement RequireMass(Guid massId) =>
        Document.StructuralElements.FirstOrDefault(element => element.Id == massId && element.Kind == StructuralElementKind.Mass)
        ?? throw new InvalidDataException("The mass does not exist.");

    /// <summary>
    /// Builds the walls of a mass on its sides: one set of four joined walls for every storey the mass spans, from the
    /// level the mass stands on up to its top. Each storey reaches the next level, and the last one the top of the mass.
    /// The walls lie inside the mass with their outer face on its sides. Returns their identifiers.
    /// </summary>
    public IReadOnlyList<Guid> AddWallsOnMass(Guid massId, Guid? wallTypeId = null)
    {
        var mass = RequireMass(massId);
        var typeId = wallTypeId ?? BuiltInWallTypes.Basic200Id;
        var type = Document.WallTypes.FirstOrDefault(item => item.Id == typeId) ?? throw new InvalidDataException("The wall type does not exist.");
        var baseLevel = Document.Levels.First(level => level.Id == mass.LevelId);
        var bottom = baseLevel.Elevation + mass.Offset;
        var top = bottom + mass.Height;
        var corners = mass.Footprint;
        var first = corners[0];
        var opposite = corners[2];
        // The storeys: each level that lies within the mass starts one, and the next level (or the top) ends it.
        var starts = Document.Levels.Where(level => level.Elevation >= bottom - 1e-6 && level.Elevation < top - 0.1).Select(level => level).OrderBy(level => level.Elevation).ToList();
        if (starts.Count == 0 || starts[0].Id != baseLevel.Id) starts.Insert(0, baseLevel);
        var created = new List<Guid>();
        using var group = BeginGroup("Transaction.AddWallsOnMass");
        for (var index = 0; index < starts.Count; index++)
        {
            var from = Math.Max(starts[index].Elevation, bottom);
            var to = index + 1 < starts.Count ? starts[index + 1].Elevation : top;
            var height = Math.Round(to - from, 4);
            if (height < 0.1) continue;
            foreach (var centerline in WallPlacementGeometry.Rectangle(first, opposite, type.Thickness, 0, WallLocationReference.ExteriorFace))
                created.Add(AddWall(starts[index].Id, centerline.Start, centerline.End, type.Thickness, height, typeId, WallLocationReference.ExteriorFace));
        }
        if (created.Count == 0) throw new InvalidDataException("The mass is too small for walls.");
        group.Assimilate();
        return created;
    }

    /// <summary>Puts a roof slab over a mass, at its top, with the outline of the mass. Returns the roof.</summary>
    public Guid AddRoofOnMass(Guid massId, double thickness = 0.20)
    {
        var mass = RequireMass(massId);
        if (mass.Height < thickness + 0.1) throw new InvalidDataException("The mass is too low for a roof.");
        return AddFloor(mass.LevelId, mass.Footprint, thickness, null, FloorKind.Roof, mass.Offset + mass.Height);
    }
}
