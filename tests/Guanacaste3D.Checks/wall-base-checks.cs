using Guanacaste3D.Core;

/// <summary>Checks the base of a wall: lifted or taken down, what that does to its solids and area, and attaching it to a slab.</summary>
static class WallBaseChecks
{
    private static bool Near(double first, double second, double tolerance = 1e-6) => Math.Abs(first - second) < tolerance;

    private static bool Throws(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return true; }
        return false;
    }

    private static double Area(IEnumerable<WallSolid> solids) => solids.Sum(part => (part.To - part.From) * (part.Top - part.Bottom));

    public static void Run(Action<bool, string> check)
    {
        Extensions(check);
        var session = new DocumentSession(BuildingDocument.Create("Base"));
        var level = session.Document.Levels[0].Id;
        var wall = session.AddWall(level, new Point2(0, 0), new Point2(10, 0), 0.2, 3, BuiltInWallTypes.Exterior300Id);
        var window = session.AddOpening(wall, OpeningKind.Window, 5, 1.0, 1.0, 1.2);
        var openings = () => session.Document.Openings.Where(item => item.WallId == wall).ToList();
        var glass = openings().Sum(item => item.Width * item.Height);

        session.SetWallBaseOffset(wall, 0.3);
        var lifted = session.Document.Walls.Single();
        var solids = WallGeometry.Solids(lifted, openings());
        check(Near(lifted.NetHeight, 2.7) && Near(lifted.ElevationArea, 27) && solids.All(part => part.Bottom >= 0.3 - 1e-9) && Near(Area(solids), 27 - glass, 1e-6),
            $"A base lifted 30 cm leaves 2.7 m of wall: 27 m² before the window and {Area(solids):0.00} m² of solid after it");

        session.SetWallBaseOffset(wall, -0.5);
        var lowered = session.Document.Walls.Single();
        var below = WallGeometry.Solids(lowered, openings());
        check(Near(lowered.NetHeight, 3.5) && Near(lowered.ElevationArea, 35) && below.Min(part => part.Bottom) <= -0.5 + 1e-9 && Near(Area(below), 35 - glass, 1e-6),
            "A base taken 50 cm below the level lengthens every piece that stood on it, to 35 m²");
        session.Undo();
        check(Near(session.Document.Walls.Single().BaseOffset, 0.3), "Undo gives back the base it had");
        session.SetWallBaseOffset(wall, 0);
        check(Near(session.Document.Walls.Single().ElevationArea, 30) && WallGeometry.Solids(session.Document.Walls.Single(), openings()).All(part => part.Bottom >= 0 - 1e-9), "A base at the level is the wall as it was");

        check(Throws(() => session.SetWallBaseOffset(wall, double.NaN)) && Throws(() => session.SetWallBaseOffset(wall, -60)) && Throws(() => session.SetWallBaseOffset(wall, 2.95)) && Throws(() => session.SetWallBaseOffset(Guid.NewGuid(), 0.1)),
            "A base that is not a number, absurdly low, almost at the top, or on a wall that is not there is refused");

        // A door cannot start below a raised base; a profile cannot dip under it.
        var other = session.AddWall(level, new Point2(0, 5), new Point2(10, 5), 0.2, 3, BuiltInWallTypes.Exterior300Id);
        session.AddOpening(other, OpeningKind.Door, 2, 0.9, 0, 2.1);
        check(Throws(() => session.SetWallBaseOffset(other, 0.2)) && session.Document.Walls.Single(item => item.Id == other).BaseOffset == 0, "A raised base that would cut below a door is refused");
        session.SetWallBaseOffset(other, -0.4);
        check(Near(session.Document.Walls.Single(item => item.Id == other).BaseOffset, -0.4), "A lowered base is fine beside a door");
        var gable = session.AddWall(level, new Point2(0, 9), new Point2(10, 9), 0.2, 4, BuiltInWallTypes.Exterior300Id);
        session.SetWallProfile(gable, WallProfiles.Gable(10, 4, 0.2));
        var lowest = session.Document.Walls.Single(item => item.Id == gable).TopProfile!.Min(point => point.Y);
        check(Throws(() => session.SetWallBaseOffset(gable, lowest)), "A base above the lowest point of the wall's profile is refused");
        session.SetWallBaseOffset(gable, 0.5);
        check(Throws(() => session.SetWallProfile(gable, [new Point2(0, 0.4), new Point2(10, 3)])), "A profile that dips below a raised base is refused");

        // Attaching to slabs.
        var site = new DocumentSession(BuildingDocument.Create("Losas"));
        var siteLevel = site.Document.Levels[0].Id;
        var plain = site.AddWall(siteLevel, new Point2(0, 0), new Point2(10, 0), 0.2, 3, BuiltInWallTypes.Exterior300Id);
        check(Throws(() => site.AttachWallBaseToSlab(plain)), "With no slab under the wall there is nothing to attach the base to");
        var footing = site.AddFloor(siteLevel, [new Point2(-1, -1), new Point2(11, -1), new Point2(11, 1), new Point2(-1, 1)], 0.3);
        site.UpdateFloor(footing, "Zapata", 0.3, -0.6);
        var attached = site.AttachWallBaseToSlab(plain);
        check(attached == footing && Near(site.Document.Walls.Single().BaseOffset, -0.6), "A wall goes down to the footing under it");
        var plinth = site.AddFloor(siteLevel, [new Point2(-1, -1), new Point2(11, -1), new Point2(11, 1), new Point2(-1, 1)], 0.2);
        site.UpdateFloor(plinth, "Plinto", 0.2, 0.4);
        site.SetWallBaseOffset(plain, 0);
        var up = site.AttachWallBaseToSlab(plain);
        check(up == footing || up == plinth, "From the level, the nearest slab below comes before one above");
        check(Near(site.Document.Walls.Single().BaseOffset, -0.6), $"The footing below wins over the plinth above: {site.Document.Walls.Single().BaseOffset:0.00}");
        var json = ProjectFile.SerializeDocument(site.Document);
        check(Near(ProjectFile.DeserializeDocument(json).Walls.Single().BaseOffset, -0.6), "The base is saved with the project");

        // Lowering the wall's height below a lifted base is caught when the wall is regenerated.
        var squat = new DocumentSession(BuildingDocument.Create("Bajo"));
        var squatLevel = squat.Document.Levels[0].Id;
        var tall = squat.AddWall(squatLevel, new Point2(0, 0), new Point2(10, 0), 0.2, 3, BuiltInWallTypes.Exterior300Id);
        squat.SetWallBaseOffset(tall, 2.0);
        squat.UpdateWall(tall, 0.2, 2.05);
        check(squat.Document.Walls.Single().BaseOffset <= squat.Document.Walls.Single().Height - 0.1 + 1e-9, "Shortening a wall below its lifted base brings the base down with it");
    }

    public static void Extensions(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Extensiones"));
        check(session.GetExtension("render.layers") is null, "A project starts with no extensions");
        session.SetExtension("render.layers", "{\"keys\":[1,2,3]}");
        check(session.GetExtension("render.layers") == "{\"keys\":[1,2,3]}", "A feature saves its text under a key");
        var steps = session.CanUndo;
        session.SetExtension("render.layers", "{\"keys\":[1,2,3]}");
        session.Undo();
        check(steps && session.GetExtension("render.layers") is null && !session.CanUndo, "Saving the same text again adds no step, and undo removes the one that was saved");
        session.Redo();
        var back = ProjectFile.DeserializeDocument(ProjectFile.SerializeDocument(session.Document));
        check(back.Extensions["render.layers"] == "{\"keys\":[1,2,3]}" && session.Document.Clone().Extensions is { Count: 1 }, "Extensions are saved with the project and copied with it");
        var copy = session.Document.Clone();
        copy.Extensions["render.other"] = "x";
        check(session.Document.Extensions.Count == 1, "A copy of the project does not share its extensions");
        session.SetExtension("render.layers", null);
        check(session.Document.Extensions.Count == 0, "Saving nothing removes the key");
        check(Throws(() => session.SetExtension("Render Layers", "x")) && Throws(() => session.SetExtension("", "x")) && Throws(() => session.SetExtension("a.b.c.d.e", "x")) && Throws(() => session.SetExtension("render.big", new string('x', 500_001))),
            "Keys that are not lowercase dotted names and values that are too long are refused");
        for (var number = 0; number < ProjectExtensions.MaximumKeys; number++) session.SetExtension("clave.k" + number, "v");
        check(Throws(() => session.SetExtension("clave.una.mas", "v")) && session.Document.Extensions.Count == ProjectExtensions.MaximumKeys, "A project holds at most 50 keys");
    }
}
