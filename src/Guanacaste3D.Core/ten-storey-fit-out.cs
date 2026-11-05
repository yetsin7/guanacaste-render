namespace Guanacaste3D.Core;

/// <summary>
/// The fit-out of <see cref="TenStoreyBuilding"/>: the kitchens, bathrooms and furniture of the six apartments of a
/// typical storey with their electrical installation, and the common services of the ground floor.
/// </summary>
internal static class ApartmentFitOut
{
    private const double Back = 0;
    private const double Front = Math.PI;
    private const double FacingWest = 3 * Math.PI / 2;

    /// <summary>One room to light and power: its rectangle, the point beside its door for the switch, and its use.</summary>
    private sealed record Space(string Name, Point2 Min, Point2 Max, Point2 SwitchAt, bool Wet);

    /// <summary>One apartment: its spaces, where its panel hangs (a wall face and the direction it looks), its air conditioner.</summary>
    private sealed record Apartment(string Name, Space[] Spaces, Point2 PanelNear, Point2 CoolingNear);

    /// <summary>
    /// The main panel and the circuits of the common areas: corridors and lobby, the pump and the elevator. Returns the
    /// common lighting circuit, which the corridor lights of every storey join.
    /// </summary>
    public static Guid AddCommonAreas(DocumentSession session, Guid ground)
    {
        var face = session.NearestWallFace(ground, new(25.5, 15.5), 0)!.Value;
        var main = session.AddElectricalDevice(ground, ElectricalDeviceKind.Panel, "Tablero de áreas comunes", face.Position, face.Facing);
        var commonLighting = session.AddCircuit(main, "Iluminación áreas comunes", 20);
        var services = session.AddCircuit(main, "Tomas de servicio", 20);
        var pump = session.AddCircuit(main, "Bomba hidroneumática", 30);
        var elevator = session.AddCircuit(main, "Ascensor", 50, 240);
        var pumpDevice = session.Document.ElectricalDevices.Single(device => device.Kind == ElectricalDeviceKind.WaterPump).Id;
        session.AssignCircuit(pumpDevice, pump);

        var lift = Wall(session, ground, new(16, 12.0), 0, ElectricalDeviceKind.Outlet240, "Alimentación ascensor");
        session.SetDeviceLoad(lift, 7500);
        session.AssignCircuit(lift, elevator);

        var spaces = new[]
        {
            new Space("Vestíbulo", new(3.0, 6.8), new(29.0, 9.2), new(15.0, 7.1), false),
            new Space("Recepción", new(13.5, 0.15), new(18.5, 6.8), new(14.0, 6.5), false),
            new Space("Local comercial 1", new(0.15, 0.15), new(13.5, 6.8), new(13.2, 2.4), false),
            new Space("Local comercial 2", new(18.5, 0.15), new(31.85, 6.8), new(18.8, 2.4), false),
            new Space("Cuarto de bombas", new(3.0, 9.2), new(9.0, 15.85), new(5.5, 9.5), true),
            new Space("Administración", new(9.0, 11.6), new(22.5, 15.85), new(12.5, 11.9), false),
            new Space("Cuarto eléctrico", new(22.5, 9.2), new(29.0, 15.85), new(25.0, 9.5), false)
        };
        foreach (var space in spaces) Light(session, ground, space, commonLighting, services, services);
        return commonLighting;
    }

    /// <summary>The six apartments of a typical storey, with their corridor lights on the common lighting circuit.</summary>
    public static void Add(DocumentSession session, Guid level, Guid commonLighting)
    {
        for (var unit = 0; unit < 4; unit++)
        {
            var a = unit * 8.0;
            var number = unit + 1;
            Place(session, level, "Queen Bed", new(a + 1.9, 2.3), Front);
            Place(session, level, "Toilet", new(a + 1.6, 6.39), Back);
            Place(session, level, "Bathroom Vanity", new(a + 2.7, 6.47), Back);
            Place(session, level, "Shower Enclosure", new(a + 0.65, 4.7), Back);
            Place(session, level, "Kitchen Sink Cabinet", new(a + 7.55, 5.6), FacingWest);
            Place(session, level, "Cooking Range", new(a + 7.61, 4.6), FacingWest);
            Place(session, level, "Refrigerator", new(a + 7.47, 3.55), FacingWest);
            Place(session, level, "Water Heater", new(a + 4.0, 6.5), Back);
            Place(session, level, "Washing Machine", new(a + 4.75, 6.4), Back);
            Place(session, level, "Two Seat Sofa", new(a + 5.4, 0.8), Front);
            Place(session, level, "Dining Table 4 Seats", new(a + 5.4, 3.6), Back);
            Fit(session, level, new Apartment($"{number}A",
            [
                new($"Dormitorio {number}A", new(a + 0.15, 0.15), new(a + 3.6, 4.2), new(a + 3.4, 2.9), false),
                new($"Baño {number}A", new(a + 0.15, 4.2), new(a + 3.6, 6.8), new(a + 3.4, 6.1), true),
                new($"Sala y cocina {number}A", new(a + 3.6, 0.15), new(a + 8, 6.8), new(a + 6.5, 6.6), false)
            ], new(a + 6.9, 6.6), new(a + 5.8, 0.3)));
        }

        // North apartments, mirror images either side of the core.
        foreach (var (name, mirror) in new[] { ("5B", false), ("6B", true) })
        {
            double X(double x) => mirror ? 32 - x : x;
            var bathMin = mirror ? 18.3 : 9.5;
            Place(session, level, "Toilet", new(X(10.3), 9.61), Front);
            Place(session, level, "Bathroom Vanity", new(X(11.4), 9.53), Front);
            Place(session, level, "Shower Enclosure", new(mirror ? 18.8 : 14.3, 11.05), Back);
            Place(session, level, "Kitchen Sink Cabinet", new(X(4.0), 15.5), Back);
            Place(session, level, "Cooking Range", new(X(5.0), 15.51), Back);
            Place(session, level, "Refrigerator", new(X(6.0), 15.45), Back);
            Place(session, level, "Water Heater", new(X(3.5), 9.5), Front);
            Place(session, level, "Washing Machine", new(X(4.3), 9.6), Front);
            Place(session, level, "Three Seat Sofa", new(X(6.25), 11.2), Back);
            Place(session, level, "Queen Bed", new(X(12.75), 14.8), Back);
            Place(session, level, "Single Bed", new(X(1.6), 14.8), Back);
            Fit(session, level, new Apartment(name,
            [
                new($"Sala y cocina {name}", new(Math.Min(X(3.0), X(9.5)), 9.2), new(Math.Max(X(3.0), X(9.5)), 15.85), new(X(7.0), 9.5), false),
                new($"Dormitorio {name}", new(Math.Min(X(9.5), X(16.0)), 11.6), new(Math.Max(X(9.5), X(16.0)), 15.85), new(X(9.7), 14.4), false),
                new($"Baño {name}", new(bathMin, 9.2), new(bathMin + (mirror ? 4.2 : 5.4), 11.6), new(X(9.7), 10.9), true),
                new($"Dormitorio {name}-2", new(Math.Min(X(0.15), X(3.0)), 11.0), new(Math.Max(X(0.15), X(3.0)), 15.85), new(X(2.8), 11.3), false)
            ], new(X(7.2), 9.4), new(X(6.25), 15.6)));
        }

        // The corridor lights of every storey hang on the common circuit fed from the ground floor.
        foreach (var x in new[] { 5.0, 10.0, 16.0, 22.0, 27.0 })
        {
            var light = session.AddElectricalDevice(level, ElectricalDeviceKind.Downlight, "Luz de pasillo", new(x, 8.0));
            session.AssignCircuit(light, commonLighting);
        }
    }

    /// <summary>An apartment's panel, its four circuits, and the lights, switches, receptacles and air conditioner of each space.</summary>
    private static void Fit(DocumentSession session, Guid level, Apartment apartment)
    {
        var face = session.NearestWallFace(level, apartment.PanelNear, 0)!.Value;
        var panel = session.AddElectricalDevice(level, ElectricalDeviceKind.Panel, $"Tablero {apartment.Name}", face.Position, face.Facing);
        var lighting = session.AddCircuit(panel, "Iluminación", 15);
        var receptacles = session.AddCircuit(panel, "Tomacorrientes", 20);
        var wet = session.AddCircuit(panel, "Cocina y baño", 20);
        var cooling = session.AddCircuit(panel, "Aire acondicionado", 20, 240);
        foreach (var space in apartment.Spaces) Light(session, level, space, lighting, receptacles, wet);

        var at = session.NearestWallFace(level, apartment.CoolingNear, 0)!.Value;
        var split = session.AddElectricalDevice(level, ElectricalDeviceKind.MiniSplit, $"Minisplit {apartment.Name}", at.Position, at.Facing);
        session.AssignCircuit(split, cooling);
    }

    /// <summary>A ceiling light at the centre of a space on its switch, and two receptacles on opposite walls.</summary>
    private static void Light(DocumentSession session, Guid level, Space space, Guid lighting, Guid receptacles, Guid wet)
    {
        var center = new Point2((space.Min.X + space.Max.X) / 2, (space.Min.Y + space.Max.Y) / 2);
        var area = Math.Abs(space.Max.X - space.Min.X) * Math.Abs(space.Max.Y - space.Min.Y);
        var lights = new List<Guid>();
        // Large spaces get a row of fixtures: about one 18 W fixture (1800 lm) per 9 m² gives 200 lx.
        var count = Math.Max(1, (int)Math.Round(area / 9));
        var alongX = Math.Abs(space.Max.X - space.Min.X) >= Math.Abs(space.Max.Y - space.Min.Y);
        for (var index = 0; index < count; index++)
        {
            var t = (index + 0.5) / count;
            var position = alongX
                ? new Point2(space.Min.X + (space.Max.X - space.Min.X) * t, center.Y)
                : new Point2(center.X, space.Min.Y + (space.Max.Y - space.Min.Y) * t);
            var light = session.AddElectricalDevice(level, ElectricalDeviceKind.CeilingFixture, $"Luminaria {space.Name}", position);
            session.AssignCircuit(light, lighting);
            lights.Add(light);
        }
        var toggle = Wall(session, level, space.SwitchAt, 0, ElectricalDeviceKind.Switch, $"Apagador {space.Name}");
        session.AssignCircuit(toggle, lighting);
        foreach (var light in lights) session.ToggleSwitchLink(toggle, light);

        var kind = space.Wet ? ElectricalDeviceKind.GfciOutlet : ElectricalDeviceKind.Outlet;
        var near = new Point2(center.X, Math.Min(space.Min.Y, space.Max.Y) + 0.3);
        var far = new Point2(center.X, Math.Max(space.Min.Y, space.Max.Y) - 0.3);
        foreach (var point in new[] { near, far })
        {
            var outlet = Wall(session, level, point, 0.4, kind, $"Tomacorriente {space.Name}");
            session.AssignCircuit(outlet, space.Wet ? wet : receptacles);
        }
    }

    private static Guid Wall(DocumentSession session, Guid level, Point2 near, double slide, ElectricalDeviceKind kind, string name)
    {
        var face = session.NearestWallFace(level, near, slide)
            ?? throw new InvalidDataException("A wall device needs a wall.");
        return session.AddElectricalDevice(level, kind, name, face.Position, face.Facing);
    }

    private static void Place(DocumentSession session, Guid level, string preset, Point2 at, double rotation) =>
        Preset(session, level, preset, at, rotation);

    private static Guid Preset(DocumentSession session, Guid level, string name, Point2 at, double rotation) =>
        session.AddElementFromPreset(level, ArchitecturalLibrary.ElementPresets.Single(preset => preset.Name == name), at, at, rotation);
}
