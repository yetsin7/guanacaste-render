namespace Guanacaste3D.Core;

/// <summary>How a room's light compares with what its use needs.</summary>
public enum LightingStatus
{
    /// <summary>No luminaire in the room.</summary>
    Dark,
    Ok,
    /// <summary>Below 90 % of the recommended illuminance.</summary>
    Underlit,
    /// <summary>Over three times the recommendation: energy spent for glare.</summary>
    Overlit
}

/// <summary>A room's lighting: its luminaires, the light they give now and the average illuminance on the floor.</summary>
public sealed record RoomLighting(Room Room, double Area, int Luminaires, double Lumens, double Lux, double RequiredLux, LightingStatus Status,
    bool HasSmokeDetector, bool NeedsSmokeDetector);

/// <summary>
/// Room illuminance by the lumen method, E = Φ·UF·MF / A, with a utilisation factor of 0.5 (light walls, ordinary rooms)
/// and a maintenance factor of 0.8. The luminous flux counts each luminaire at its present output, so switching or
/// dimming changes the result at once. Recommended levels follow EN 12464-1 and IES practice for homes: kitchens and
/// studies 300 lx, bathrooms and living rooms 200 lx, bedrooms 150 lx, corridors 100 lx. Bedrooms also need a smoke
/// detector inside, as NFPA 72 asks.
/// </summary>
public static class LightingAnalysis
{
    public const double UtilisationFactor = 0.5;
    public const double MaintenanceFactor = 0.8;

    /// <summary>Lumens of a luminaire at full output: LED efficacy by fitting, the fan counting only its 15 W light kit.</summary>
    public static double Lumens(ElectricalDevice device) => device.Kind switch
    {
        ElectricalDeviceKind.CeilingFixture => device.Watts * 100,
        ElectricalDeviceKind.Downlight => device.Watts * 90,
        ElectricalDeviceKind.WallSconce => device.Watts * 80,
        ElectricalDeviceKind.Pendant => device.Watts * 90,
        ElectricalDeviceKind.CeilingFan => 15 * 100,
        _ => 0
    };

    /// <summary>Lumens of a lighting component (the residential ceiling light and floor lamp families).</summary>
    public static double Lumens(StructuralElement element) => element.ComponentShape switch
    {
        ResidentialComponentShape.CeilingLight => 1800,
        ResidentialComponentShape.FloorLamp => 1200,
        _ => 0
    };

    /// <summary>The recommended average illuminance for a room, from its name.</summary>
    public static double RequiredLux(string name)
    {
        bool Has(params string[] words) => words.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase));
        if (Has("cocina", "kitchen", "estudio", "oficina", "study", "office", "lavander", "laundry")) return 300;
        if (Has("baño", "bano", "bath", "sala", "living", "comedor", "dining")) return 200;
        if (Has("dormitorio", "habitaci", "bedroom", "cuarto")) return 150;
        if (Has("pasillo", "corredor", "hall", "vestíbulo", "garaje", "garage", "terraza", "bodega")) return 100;
        return 200;
    }

    public static bool IsBedroom(string name) =>
        new[] { "dormitorio", "habitaci", "bedroom", "cuarto" }.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<RoomLighting> Analyze(BuildingDocument document, ElectricalAnalysis? electrical = null)
    {
        electrical ??= ElectricalAnalysis.Analyze(document);
        bool Inside(Room room, Point2 point) =>
            point.X >= Math.Min(room.FirstCorner.X, room.SecondCorner.X) - 0.2 && point.X <= Math.Max(room.FirstCorner.X, room.SecondCorner.X) + 0.2 &&
            point.Y >= Math.Min(room.FirstCorner.Y, room.SecondCorner.Y) - 0.2 && point.Y <= Math.Max(room.FirstCorner.Y, room.SecondCorner.Y) + 0.2;
        return document.Rooms.Select(room =>
        {
            var area = CoolingAnalysis.Area(room);
            var devices = document.ElectricalDevices.Where(device => device.LevelId == room.LevelId && Inside(room, device.Position)).ToList();
            var luminaires = devices.Where(device => device.IsLuminaire).ToList();
            var components = document.StructuralElements.Where(element => element.LevelId == room.LevelId &&
                                                                          Lumens(element) > 0 && Inside(room, element.Start)).ToList();
            var lumens = luminaires.Sum(device => Lumens(device) * electrical.Brightness(device.Id)) +
                         components.Sum(element => Lumens(element) * electrical.Brightness(element.Id));
            var lux = area > 0 ? lumens * UtilisationFactor * MaintenanceFactor / area : 0;
            var required = RequiredLux(room.Name);
            var count = luminaires.Count + components.Count;
            var status = count == 0 ? LightingStatus.Dark
                : lux < required * 0.9 ? LightingStatus.Underlit
                : lux > required * 3 ? LightingStatus.Overlit
                : LightingStatus.Ok;
            return new RoomLighting(room, area, count, lumens, lux, required, status,
                devices.Any(device => device.Kind == ElectricalDeviceKind.SmokeDetector), IsBedroom(room.Name));
        }).ToList();
    }
}
