using Guanacaste3D.Core;

/// <summary>Checks the project's place and true north: shared coordinates both ways, validity, undo and saving.</summary>
static class ProjectSiteChecks
{
    private static bool Near(double first, double second, double tolerance = 1e-9) => Math.Abs(first - second) < tolerance;

    private static bool Throws(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return true; }
        return false;
    }

    public static void Run(Action<bool, string> check)
    {
        var straight = new ProjectSite(10.6, -85.4, 100, 0, 500000, 1170000);
        var shared = straight.ToShared(new Point2(12, 5));
        check(Near(shared.X, 500012) && Near(shared.Y, 1170005), "With the project facing true north, a point 12 m east and 5 m north of the origin is that far from the survey origin");

        var east = new ProjectSite(10.6, -85.4, 0, 90, 0, 0);
        var projectNorthIsEast = east.ToShared(new Point2(0, 1));
        var projectEastIsSouth = east.ToShared(new Point2(1, 0));
        check(Near(projectNorthIsEast.X, 1) && Near(projectNorthIsEast.Y, 0, 1e-12) && Near(projectEastIsSouth.X, 0, 1e-12) && Near(projectEastIsSouth.Y, -1),
            "With true north at 90° from the project's Y axis, project north is true east and project east is true south");

        // Whatever the place and the angle, a point goes to shared coordinates and comes back to itself.
        var random = new Random(20261106);
        var worst = 0.0;
        for (var round = 0; round < 500; round++)
        {
            var site = new ProjectSite(random.NextDouble() * 180 - 90, random.NextDouble() * 360 - 180, random.NextDouble() * 3000,
                random.NextDouble() * 720 - 360, random.NextDouble() * 2e6 - 1e6, random.NextDouble() * 2e6 - 1e6);
            var point = new Point2(random.NextDouble() * 2000 - 1000, random.NextDouble() * 2000 - 1000);
            var back = site.ToProject(site.ToShared(point));
            worst = Math.Max(worst, point.DistanceTo(back));
            if (!site.IsValid) worst = double.MaxValue;
        }
        check(worst < 1e-6, $"500 random sites: converting a point to shared coordinates and back lands within {worst:0.0e+0} m of where it began");

        var trueNorthRight = new ProjectSite(0, 0, 0, 30, 0, 0);
        var distanceBefore = new Point2(0, 0).DistanceTo(new Point2(3, 4));
        var turned = trueNorthRight.ToShared(new Point2(3, 4)).DistanceTo(trueNorthRight.ToShared(new Point2(0, 0)));
        check(Near(turned, distanceBefore), "Turning the project to true north never changes the distance between two points");
        var (sunX, sunY) = new ProjectSite(0, 0, 0, 90, 0, 0).TrueToProject(1, 0);
        check(Near(sunX, 0, 1e-12) && Near(sunY, 1), "A direction toward true east is project north when true north is at 90°, which is how the sun follows the turn");

        check(new ProjectSite(91, 0, 0, 0, 0, 0).IsValid == false && new ProjectSite(0, 181, 0, 0, 0, 0).IsValid == false &&
              new ProjectSite(0, 0, 0, 400, 0, 0).IsValid == false && new ProjectSite(0, 0, 0, 0, double.NaN, 0).IsValid == false &&
              new ProjectSite(0, 0, 99999, 0, 0, 0).IsValid == false && ProjectSite.Default.IsValid,
            "A latitude, longitude, altitude, angle or coordinate out of range is not a site");

        var session = new DocumentSession(BreezeBlockHouseShowcase.Create());
        var walls = session.Document.Walls.Select(wall => (wall.Id, wall.Start, wall.End)).ToArray();
        var steps = session.UndoNames.Count;
        session.SetProjectSite(new ProjectSite(10.0, -85.0, 50, 25, 600000, 1100000));
        check(session.UndoNames.Count == steps + 1 && session.Document.Walls.Select(wall => (wall.Id, wall.Start, wall.End)).SequenceEqual(walls),
            "Setting the site is one undoable step and does not move a wall");
        check(Throws(() => session.SetProjectSite(new ProjectSite(100, 0, 0, 0, 0, 0))), "A site out of range is refused");

        var file = Path.Combine(Path.GetTempPath(), $"site-{Guid.NewGuid():N}.g3d");
        ProjectFile.Save(file, session.Document);
        var reopened = ProjectFile.Load(file);
        File.Delete(file);
        check(reopened.Site == new ProjectSite(10.0, -85.0, 50, 25, 600000, 1100000), "The site survives saving and opening the project");
        session.Undo();
        check(session.Document.Site == ProjectSite.Default, "Undo brings the old site back");
    }
}
