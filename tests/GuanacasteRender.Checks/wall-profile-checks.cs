using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Checks wall top profiles: what they do to the mesh, the quantities and the openings, and random profiles on the cobogó house.</summary>
static class WallProfileChecks
{
    private static bool Near(double first, double second, double tolerance = 1e-6) => Math.Abs(first - second) < tolerance;

    private static bool Throws(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return true; }
        return false;
    }

    public static void Run(Action<bool, string> check)
    {
        Parsing(check);
        OneWall(check);
        RandomProfiles(check);
    }

    private static void Parsing(Action<bool, string> check)
    {
        check(WallProfiles.TryParse("0 1.8; 4 3; 8 1.8") is { Count: 3 } && WallProfiles.TryParse("0,1.8;8,3") is { Count: 2 },
            "A profile is read from text, with a space or a comma between distance and height");
        check(WallProfiles.TryParse("0 1; 0 2") is null && WallProfiles.TryParse("5 1; 2 2") is null && WallProfiles.TryParse("0 0.01; 1 2") is null &&
              WallProfiles.TryParse("0 1") is null && WallProfiles.TryParse("0 NaN; 1 2") is null && WallProfiles.TryParse("a b; c d") is null &&
              WallProfiles.TryParse("0 1 2; 3 4") is null && WallProfiles.TryParse(null) is null,
            "Profiles with repeated or backwards distances, tiny or invalid heights, one point or garbage are not read");
        var gable = WallProfiles.Gable(8, 3);
        check(WallProfiles.IsValid(gable) && WallProfiles.IsValid(WallProfiles.Shed(8, 3)) && WallProfiles.IsValid(WallProfiles.Stepped(9, 3)) &&
              WallProfiles.TryParse(WallProfiles.ToText(gable)) is { } again && again.Zip(gable).All(pair => Near(pair.First.X, pair.Second.X, 0.001) && Near(pair.First.Y, pair.Second.Y, 0.001)),
            "The gable, shed and stepped presets are valid profiles, and a profile written out is read back the same");
    }

    private static void OneWall(Action<bool, string> check)
    {
        var session = new DocumentSession(BreezeBlockHouseShowcase.Create());
        var level = session.Document.Levels[0].Id;
        var elevation = session.Document.Levels[0].Elevation;
        var id = session.AddWall(level, new Point2(700, 700), new Point2(708, 700), 0.2, 3);
        var flat = session.Document.Walls.Single(wall => wall.Id == id);
        double MaxZ() => BuildingMesher.Build(session.Document).Positions.Where(position => position.X is > 699 and < 709 && position.Y is > 699 and < 701).Max(position => (double)position.Z);
        var flatTriangles = BuildingMesher.Build(session.Document).TriangleCount;
        check(Near(MaxZ(), elevation + 3, 1e-4), "A flat wall of 3 m has its top 3 m above its level");

        session.SetWallProfile(id, WallProfiles.Gable(8, 3));
        var gable = session.Document.Walls.Single(wall => wall.Id == id);
        var mesh = BuildingMesher.Build(session.Document);
        var heights = mesh.Positions.Where(position => position.X is > 699 and < 709 && position.Y is > 699 and < 701).Select(position => position.Z - elevation).ToArray();
        check(Near(gable.ElevationArea, 19.2) && Near(gable.TopHeightAt(0), 1.8) && Near(gable.TopHeightAt(4), 3) && Near(gable.TopHeightAt(2), 2.4),
            $"A gable 8 m wide has an elevation area of {gable.ElevationArea:0.00} m² (a 14.4 m² rectangle and a 4.8 m² triangle)");
        check(Near(heights.Max(), 3, 1e-4) && heights.Any(height => Math.Abs(height - 1.8) < 1e-4) && heights.All(height => height <= 3 + 1e-4) &&
              mesh.TriangleCount > flatTriangles && mesh.Positions.All(position => float.IsFinite(position.X + position.Y + position.Z)),
            "The mesh follows the gable: highest at the ridge, 1.8 m at the ends, nothing above the ridge, all finite");
        check(Near(WallGeometry.NetVolume(gable, []), 19.2 * 0.2) && Near(FinishRules.WallFaceArea(gable, []), 19.2),
            "The volume and the face area use the area under the profile, not the whole rectangle");
        check(Throws(() => session.AddOpening(id, OpeningKind.Door, offset: 0.9)),
            "A door cannot be put where the gable is too low for it");

        var doorWall = session.AddWall(level, new Point2(720, 700), new Point2(728, 700), 0.2, 3);
        session.AddOpening(doorWall, OpeningKind.Door, offset: 4);
        check(Throws(() => session.SetWallProfile(doorWall, [new Point2(0, 3), new Point2(3.9, 1.5), new Point2(8, 3)])),
            "A profile that dips under the door of its wall is refused");
        session.SetWallProfile(doorWall, WallProfiles.Gable(8, 3, 0.1));
        check(session.Document.Openings.Any(opening => opening.WallId == doorWall) && BuildingMesher.Build(session.Document).Positions.All(position => float.IsFinite(position.Z)),
            "A gentle gable over a door is accepted, and the wall with its door still meshes");

        var before = gable.TopHeightAt(0.5);
        session.FlipWalls([id]);
        var flipped = session.Document.Walls.Single(wall => wall.Id == id);
        check(Near(flipped.TopHeightAt(8 - 0.5), before) && Near(flipped.ElevationArea, 19.2),
            "Flipping the wall turns its profile round with it");
        session.FlipWalls([id]);

        var secondId = session.SplitWall(id, new Point2(703, 700));
        var first = session.Document.Walls.Single(wall => wall.Id == id);
        var second = session.Document.Walls.Single(wall => wall.Id == secondId);
        check(Near(first.ElevationArea + second.ElevationArea, 19.2, 1e-5) && Near(second.TopHeightAt(0), gable.TopHeightAt(3)),
            $"Splitting a gable wall keeps both halves on the same slope ({first.ElevationArea:0.00} + {second.ElevationArea:0.00} m²)");

        var file = Path.Combine(Path.GetTempPath(), $"profile-{Guid.NewGuid():N}.g3d");
        ProjectFile.Save(file, session.Document);
        var reopened = ProjectFile.Load(file);
        File.Delete(file);
        check(reopened.Walls.Single(wall => wall.Id == secondId).TopProfile is { Count: > 1 } && reopened.Walls.Count(wall => wall.TopProfile is not null) == 3,
            "The profiles survive saving and opening the project");

        session.SetWallsPinned([doorWall], true);
        check(Throws(() => session.SetWallProfile(doorWall, WallProfiles.Shed(8, 3, 0.05))) && Throws(() => session.ResetWallProfiles([doorWall])),
            "A pinned wall keeps its profile");
        session.SetWallsPinned([doorWall], false);
        check(session.ResetWallProfiles([id, secondId, doorWall]) == 3 && session.ResetWallProfiles([id]) == 0 &&
              session.Document.Walls.All(wall => wall.TopProfile is null) && Near(session.Document.Walls.Single(wall => wall.Id == id).ElevationArea, 3 * 3),
            "Resetting takes the profile off every wall that has one, and walls that have none are left alone");
        session.Undo();
        check(session.Document.Walls.Count(wall => wall.TopProfile is not null) == 3, "Undo brings the profiles back");
    }

    /// <summary>Random profiles on random walls of the house: each is accepted or refused, and the house stays valid and meshes.</summary>
    private static void RandomProfiles(Action<bool, string> check)
    {
        var session = new DocumentSession(BreezeBlockHouseShowcase.Create());
        var random = new Random(20261104);
        var accepted = 0;
        var refused = 0;
        for (var round = 0; round < 60; round++)
        {
            var wall = session.Document.Walls[random.Next(session.Document.Walls.Count)];
            var count = random.Next(2, 7);
            var along = 0.0;
            var points = new List<Point2>();
            for (var index = 0; index < count; index++)
            {
                points.Add(new Point2(along, 0.3 + random.NextDouble() * (wall.Height - 0.3)));
                along += 0.05 + random.NextDouble() * wall.Length / count;
            }
            try { session.SetWallProfile(wall.Id, points); accepted++; }
            catch (InvalidDataException) { refused++; }
            if (round % 15 == 14) session.ResetWallProfiles([wall.Id]);
        }
        var valid = true;
        try { session.Document.Validate(); } catch (InvalidDataException) { valid = false; }
        var mesh = BuildingMesher.Build(session.Document);
        check(accepted > 0 && refused > 0 && valid && mesh.Positions.All(position => float.IsFinite(position.X + position.Y + position.Z)) &&
              session.Document.Walls.All(wall => wall.ElevationArea <= wall.Length * wall.Height + 1e-9 && wall.ElevationArea > 0),
            $"60 random profiles on the cobogó house ({accepted} accepted, {refused} refused for cutting under a door or window) leave it valid and meshing");
    }
}
