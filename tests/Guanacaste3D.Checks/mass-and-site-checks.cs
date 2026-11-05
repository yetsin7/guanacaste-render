using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Checks masses (walls and roof built from them, hiding), parking spaces and the sun data of the site.</summary>
static class MassAndSiteChecks
{
    private static bool Near(double first, double second, double tolerance) => Math.Abs(first - second) < tolerance;

    private static bool Throws(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return true; }
        return false;
    }

    public static void Run(Action<bool, string> check)
    {
        Sun(check);
        Masses(check);
    }

    private static void Sun(Action<bool, string> check)
    {
        var days = SiteSolarData.ForLatitude(10.63);
        var june = days.Single(day => day.Name == "JuneSolstice");
        var december = days.Single(day => day.Name == "DecemberSolstice");
        check(Near(june.NoonAltitudeDegrees, 90 - Math.Abs(10.63 - 23.44), 1.5) && Near(december.NoonAltitudeDegrees, 90 - (10.63 + 23.44), 1.5),
            $"At 10.63° north the noon sun climbs to {june.NoonAltitudeDegrees:0.#}° in June and {december.NoonAltitudeDegrees:0.#}° in December");
        check(june.DaylightHours > december.DaylightHours && Near(june.DaylightHours, 12.6, 0.4) && Near(december.DaylightHours, 11.5, 0.4) &&
              Near((june.SunriseHour!.Value + june.SunsetHour!.Value) / 2, 12, 0.05),
            $"Days are {june.DaylightHours:0.0} h long in June and {december.DaylightHours:0.0} h in December, centred on noon");
        var equator = SiteSolarData.OnDay("Equinox", 0, 80);
        check(Near(equator.DaylightHours, 12, 0.2) && Near(equator.NoonAltitudeDegrees, 90, 2), "On the equator at the equinox the day is 12 hours and the sun is overhead");
        var polarSummer = SiteSolarData.OnDay("Midnight sun", 80, 172);
        var polarWinter = SiteSolarData.OnDay("Polar night", 80, 355);
        check(polarSummer.DaylightHours == 24 && polarWinter.DaylightHours == 0, "Beyond the polar circle the sun never sets in summer and never rises in winter");
    }

    private static void Masses(Action<bool, string> check)
    {
        var session = new DocumentSession(BreezeBlockHouseShowcase.Create());
        var level = session.Document.Levels[0];
        var walls = session.Document.Walls.Count;
        var floors = session.Document.Floors.Count;
        var solidsBefore = BuildingMesher.Build(session.Document).Positions.Count;
        var corner1 = new Point2(900, 900);
        var corner2 = new Point2(910, 906);

        var parking = session.AddStructuralElement(level.Id, StructuralElementKind.ParkingSpace, "Plaza", new Point2(930, 900), new Point2(932.5, 905));
        var space = session.Document.StructuralElements.Single(element => element.Id == parking);
        check(!space.HasSolid && Near(space.Footprint.Max(p => p.X) - space.Footprint.Min(p => p.X), 2.5, 1e-9) && BuildingMesher.Build(session.Document).Positions.Count == solidsBefore,
            "A parking space is a 2.5 m by 5 m outline with no solid, and adds nothing to the 3D model");

        var mass = session.AddStructuralElement(level.Id, StructuralElementKind.Mass, "Masa", corner1, corner2);
        var element = session.Document.StructuralElements.Single(item => item.Id == mass);
        session.UpdateStructuralElement(mass, "Masa", 0, 9, 0, ElementStyle.Default);
        var tall = session.Document.StructuralElements.Single(item => item.Id == mass);
        check(element.HasSolid && tall.Height == 9 && Near(tall.Footprint.Max(p => p.X) - tall.Footprint.Min(p => p.X), 10, 1e-9) && BuildingMesher.Build(session.Document).Positions.Count > solidsBefore,
            "A mass is a solid box between its two corners: here 10 m by 6 m and 9 m tall");

        var storeys = session.Document.Levels.Count(l => l.Elevation >= level.Elevation - 1e-6 && l.Elevation < level.Elevation + 9 - 0.1);
        var built = session.AddWallsOnMass(mass);
        var builtWalls = session.Document.Walls.Where(wall => built.Contains(wall.Id)).ToArray();
        var eachStorey = builtWalls.GroupBy(wall => wall.LevelId).ToArray();
        var totalRise = eachStorey.Sum(storey => storey.First().Height);
        check(session.Document.Walls.Count == walls + 4 * storeys && eachStorey.Length == storeys && eachStorey.All(storey => storey.Count() == 4) && Near(totalRise, 9, 1e-6),
            $"Building walls on the mass makes four walls for each of its {storeys} storeys, which together rise its full 9 m");
        check(builtWalls.All(wall => new[] { wall.Start, wall.End }.All(p => p.X >= 900 - 1e-6 && p.X <= 910 + 1e-6 && p.Y >= 900 - 1e-6 && p.Y <= 906 + 1e-6)),
            "The walls stand inside the mass, with their outer faces on its sides");

        var roof = session.AddRoofOnMass(mass);
        var slab = session.Document.Floors.Single(floor => floor.Id == roof);
        check(session.Document.Floors.Count == floors + 1 && slab.Kind == FloorKind.Roof && Near(slab.ElevationOffset, 9, 1e-9) &&
              slab.Boundary.All(p => p.X is >= 900 - 1e-6 and <= 910 + 1e-6), "The roof lies over the mass at its top, with its outline");

        check(Throws(() => session.AddWallsOnMass(Guid.NewGuid())) && Throws(() => session.AddRoofOnMass(parking)) &&
              Throws(() => session.AddWallsOnMass(mass, Guid.NewGuid())), "Walls and a roof can be built only from a mass, with a wall type that exists");

        var plan = session.Document.Views.First(view => view.Kind == ProjectViewKind.Plan);
        session.SetViewHiddenCategories(plan.Id, [ViewCategory.Masses]);
        var hiddenView = ViewVisibility.Apply(session.Document, session.Document.Views.First(view => view.Id == plan.Id));
        check(hiddenView.StructuralElements.All(item => item.Kind != StructuralElementKind.Mass) && hiddenView.Walls.Count == session.Document.Walls.Count &&
              session.Document.StructuralElements.Any(item => item.Kind == StructuralElementKind.Mass),
            "Hiding the masses in a view takes only the masses out of it, and the model keeps them");

        var file = Path.Combine(Path.GetTempPath(), $"mass-{Guid.NewGuid():N}.g3d");
        ProjectFile.Save(file, session.Document);
        var reopened = ProjectFile.Load(file);
        File.Delete(file);
        check(reopened.StructuralElements.Count(item => item.Kind == StructuralElementKind.Mass) == 1 &&
              reopened.StructuralElements.Count(item => item.Kind == StructuralElementKind.ParkingSpace) == 1, "Masses and parking spaces survive saving and opening the project");
        check(BuildingMesher.Build(session.Document).Positions.All(p => float.IsFinite(p.X + p.Y + p.Z)), "The house with a mass, its walls and its roof still meshes");
    }
}
