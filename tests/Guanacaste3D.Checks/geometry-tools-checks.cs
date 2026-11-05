using Guanacaste3D.Core;

/// <summary>Checks Cut Geometry on walls (their top follows the underside of a solid that passes over them) and masses placed at an elevation.</summary>
static class GeometryToolsChecks
{
    private static bool Near(double first, double second, double tolerance = 1e-6) => Math.Abs(first - second) < tolerance;

    private static bool Throws(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return true; }
        return false;
    }

    private static Guid Beam(DocumentSession session, Guid level, double x, double offset, double height)
    {
        var id = session.AddStructuralElement(level, StructuralElementKind.Beam, "Viga", new Point2(x, -2), new Point2(x, 2));
        var beam = session.Document.StructuralElements.First(item => item.Id == id);
        session.UpdateStructuralElement(id, beam.Name, beam.Width, height, offset, ElementStyle.Default);
        return id;
    }

    public static void Run(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Corte"));
        var level = session.Document.Levels[0].Id;
        var wall = session.AddWall(level, new Point2(0, 0), new Point2(10, 0), 0.2, 3, BuiltInWallTypes.Exterior300Id);
        var beam = Beam(session, level, 5, 2.4, 0.6);
        session.CutWallBy(wall, beam);
        var cut = session.Document.Walls.Single();
        check(cut.TopProfile is not null && Near(cut.TopHeightAt(5), 2.4) && Near(cut.TopHeightAt(1), 3) && Near(cut.TopHeightAt(9), 3) && cut.ElevationArea < 30 && cut.ElevationArea > 29,
            $"A beam over a wall brings the wall's top down to its underside only where it crosses: {cut.ElevationArea:0.00} m² left of 30");
        session.Undo();
        check(session.Document.Walls.Single().TopProfile is null, "Undoing the cut leaves the wall's top flat again");
        session.Redo();

        // A second solid keeps the first cut.
        var second = Beam(session, level, 8, 2.0, 1.0);
        session.CutWallBy(wall, second);
        var twice = session.Document.Walls.Single();
        check(Near(twice.TopHeightAt(5), 2.4) && Near(twice.TopHeightAt(8), 2.0) && Near(twice.TopHeightAt(3), 3), "Two solids over the same wall each cut their own stretch");

        // A solid that cuts a wall that already has a pitched top only lowers it where it is lower.
        var gable = new DocumentSession(BuildingDocument.Create("Gablete"));
        var gableLevel = gable.Document.Levels[0].Id;
        var gableWall = gable.AddWall(gableLevel, new Point2(0, 0), new Point2(10, 0), 0.2, 4, BuiltInWallTypes.Exterior300Id);
        gable.SetWallProfile(gableWall, WallProfiles.Gable(10, 4, 0.2));
        var before = gable.Document.Walls.Single().TopHeightAt(9);
        gable.CutWallBy(gableWall, Beam(gable, gableLevel, 5, 3.4, 0.6));
        var pitched = gable.Document.Walls.Single();
        check(Near(pitched.TopHeightAt(9), before) && Near(pitched.TopHeightAt(5), 3.4) && pitched.TopHeightAt(5) < 4, "A solid over a pitched wall lowers only the stretch it covers");

        // What cannot be cut.
        var fresh = new DocumentSession(BuildingDocument.Create("Rechazos"));
        var freshLevel = fresh.Document.Levels[0].Id;
        var freshWall = fresh.AddWall(freshLevel, new Point2(0, 0), new Point2(10, 0), 0.2, 3, BuiltInWallTypes.Exterior300Id);
        var low = Beam(fresh, freshLevel, 5, 1.0, 0.5);
        var far = Beam(fresh, freshLevel, 50, 2.4, 0.6);
        check(Throws(() => fresh.CutWallBy(freshWall, low)) && Throws(() => fresh.CutWallBy(freshWall, far)) && Throws(() => fresh.CutWallBy(freshWall, Guid.NewGuid())) && Throws(() => fresh.CutWallBy(Guid.NewGuid(), low)),
            "A solid that does not reach the top, one far away, and elements that do not exist are all refused");
        fresh.AddOpening(freshWall, OpeningKind.Window, 5, 1.0, 1.0, 1.6);
        var over = Beam(fresh, freshLevel, 5, 2.2, 0.8);
        check(Throws(() => fresh.CutWallBy(freshWall, over)) && fresh.Document.Walls.Single().TopProfile is null, "A cut that would go below a window is refused and changes nothing");
        var upper = fresh.AddLevel();
        var upstairs = Beam(fresh, upper, 5, 2.4, 0.6);
        check(Throws(() => fresh.CutWallBy(freshWall, upstairs)), "A solid of another level cannot cut the wall");

        // Masses at an elevation.
        var site = new DocumentSession(BuildingDocument.Create("Sitio"));
        var siteLevel = site.Document.Levels[0].Id;
        var massesBefore = site.Document.StructuralElements.Count;
        var mass = site.AddMassAtElevation(siteLevel, new Point2(0, 0), new Point2(6, 4), 3, 3, "Pasarela");
        var floating = site.Document.StructuralElements.Single(item => item.Id == mass);
        check(floating.Kind == StructuralElementKind.Mass && Near(floating.Offset, 3) && Near(floating.Height, 3) && floating.Name == "Pasarela" && site.Document.StructuralElements.Count == massesBefore + 1,
            "A 3D sketch places a mass that starts 3 m above its level and is 3 m tall");
        site.Undo();
        check(site.Document.StructuralElements.Count == massesBefore, "The whole mass, with its elevation, is one undoable step");
        check(Throws(() => site.AddMassAtElevation(siteLevel, new Point2(0, 0), new Point2(6, 4), double.NaN, 3, "x")) && Throws(() => site.AddMassAtElevation(siteLevel, new Point2(0, 0), new Point2(6, 4), 0, 0.1, "x")) &&
              Throws(() => site.AddMassAtElevation(siteLevel, new Point2(1, 1), new Point2(1, 4), 0, 3, "x")) && Throws(() => site.AddMassAtElevation(siteLevel, new Point2(0, 0), new Point2(6, 4), 5000, 3, "x")) &&
              site.Document.StructuralElements.Count == massesBefore, "Sketches with no thickness, no sense or an absurd elevation are refused");
    }
}
