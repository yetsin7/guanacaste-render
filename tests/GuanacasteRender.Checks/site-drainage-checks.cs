using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Regression checks for the septic tank, roof drainage and storm drains, and the service ranges of the X-ray view.</summary>
static class SiteDrainageChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(Math.Abs(SiteDrainage.ManningCapacity(104, 0.01) - 7.46) < 0.1 && SiteDrainage.StormSize(5) == 1 && SiteDrainage.StormSize(2) == 0,
            "Storm drains are sized for full flow by Manning: a 4\" PVC drain at 1 % carries about 7.5 L/s");
        var round = new StructuralElement(Guid.Empty, Guid.Empty, StructuralElementKind.Downspout, "", default, default, 0.1016, 3, 0)
        {
            Style = ElementStyle.Round
        };
        check(Math.Abs(SiteDrainage.LeaderCapacity(round) - 4.06) < 1e-9 &&
              SiteDrainage.LeaderCapacity(round with { Width = 0.0762 }) < 2.0,
            "A 4\" downspout carries 4.06 L/s, a 3\" one less than half of that");

        // The demonstration house: three rooms, two of them bedrooms, under a gable roof.
        var session = new DocumentSession(SampleProjects.CreateDemonstrationHouse());
        var level = session.Document.Levels[0].Id;
        var gable = ArchitecturalLibrary.SlabPresets.Single(slab => slab.Name == "Gable Roof 30°");
        session.AddSlabFromPreset(level, [new(0, 0), new(12, 0), new(12, 8), new(0, 8)], gable, session.SlabElevation(level, gable));
        var tank = session.AddPlumbingPoint(level, PlumbingPointKind.SepticTank, "Tanque", new Point2(16, 4));
        var septic = PlumbingAnalysis.Analyze(session.Document).Septic.Single();
        check(septic.Occupants == 4 && septic.DailyLiters == 600 && Math.Abs(septic.VolumeLiters - (1000 + 4 * (150 + 57))) < 1e-9 &&
              Math.Abs(septic.Length - 2 * septic.Width) < 1e-9 &&
              Math.Abs(septic.Length * septic.Width * septic.LiquidDepth * 1000 - septic.VolumeLiters) < 1e-6 &&
              Math.Abs(septic.FieldArea - 15) < 1e-9 && septic.Trenches == 2 && septic.FieldDirection.X > 0.9,
            "Two bedrooms make four people: a 1.8 m³ septic tank (NBR 7229) twice as long as wide, and 25 m of trench in two runs away from the house");
        session.SetSepticOccupants(tank, 8);
        session.SetPercolation(tank, 20);
        var bigger = PlumbingAnalysis.Analyze(session.Document).Septic.Single();
        check(bigger.VolumeLiters > septic.VolumeLiters && bigger.FieldArea == 8 * 150 / 20.0,
            "More people and tighter soil take a larger tank and a larger drain field");
        session.SetSepticOccupants(tank, 0);
        session.SetPercolation(tank, 40);

        Guid Place(string name, double x, double y)
        {
            var preset = ResidentialComponentCatalog.Presets.Single(candidate => candidate.Name == name);
            return session.AddElementFromPreset(level, preset, new Point2(x, y), new Point2(x, y), Math.PI);
        }
        var toilet = Place("Toilet", 8, 0.5);
        Place("Pedestal Sink", 9, 0.4);
        var storm = session.AddPlumbingPoint(level, PlumbingPointKind.StormOutlet, "Cuneta", new Point2(-2, -3));
        session.ConnectPlumbing((kind, number) => $"{kind} {number}");
        var analysis = PlumbingAnalysis.Analyze(session.Document);
        var sanitary = analysis.Systems.Single(report => report.System.Kind == PlumbingSystemKind.Sanitary);
        check(sanitary.System.SourceId == tank && sanitary.Fixtures.Any(fixture => fixture.FixtureId == toilet) && sanitary.Status != PlumbingStatus.Fail,
            "Without a public sewer the drains run to the septic tank");
        var spouts = session.Document.StructuralElements.Where(element => element.Kind == StructuralElementKind.Downspout).ToArray();
        var roof = analysis.Roofs.Single();
        var stormReport = analysis.Systems.Single(report => report.System.Kind == PlumbingSystemKind.StormWater);
        check(spouts.Length >= 2 && roof.Downspouts == spouts.Length && roof.Status == PlumbingStatus.Ok &&
              Math.Abs(roof.Flow - SiteDrainage.RoofFlow(roof.Area, 150)) < 1e-9 && roof.Area > 12 * 8 &&
              stormReport.Fixtures.Count == spouts.Length && stormReport.Status == PlumbingStatus.Ok &&
              Math.Abs(stormReport.Units - roof.Flow) < 1e-9,
            "Connecting places downspouts at the eave corners for the roof's runoff and drains them to the storm outlet");
        check(stormReport.Runs.Where(run => run.From.DistanceTo(run.To) > 1e-6)
                  .All(run => Math.Abs(Math.Abs(run.ToZ - run.FromZ) - SiteDrainage.StormSlope * run.From.DistanceTo(run.To)) < 1e-9),
            "Storm drains fall at 1 % to the outlet");

        session.SetRainfall(storm, 400);
        var downpour = PlumbingAnalysis.Analyze(session.Document);
        check(downpour.Roofs.Single().Status == PlumbingStatus.Fail &&
              downpour.Systems.Single(report => report.System.Kind == PlumbingSystemKind.StormWater).Fixtures
                  .All(fixture => fixture.Issue == "Plumbing.Issue.LeaderTooSmall"),
            "At 400 mm/h two downspouts cannot carry the roof");
        session.ConnectPlumbing((kind, number) => $"{kind} {number}");
        var resized = PlumbingAnalysis.Analyze(session.Document);
        check(resized.Roofs.Single().Status != PlumbingStatus.Fail &&
              session.Document.StructuralElements.Count(element => element.Kind == StructuralElementKind.Downspout) > spouts.Length,
            "Connecting again adds the downspouts the heavier rain needs");

        var mesh = BuildingMesher.Build(session.Document, includeGround: true);
        var services = Enumerable.Range(0, mesh.TriangleCount).Where(mesh.IsService).ToArray();
        check(mesh.TriangleKinds.Contains(SurfaceKind.PipeStorm) &&
              Enumerable.Range(0, mesh.TriangleCount).Where(index => mesh.TriangleKinds[index] is SurfaceKind.PipeStorm or SurfaceKind.PipeDrain)
                  .All(mesh.IsService) &&
              services.All(index => mesh.TriangleKinds[index] is not (SurfaceKind.Wall or SurfaceKind.InteriorWall or SurfaceKind.Roof or SurfaceKind.Ground)),
            "Pipes, the septic tank and the storm drains are services for the X-ray view; walls, roof and ground are not");
        // The ground sits where the house alone puts it (under its footings); buried services do not move it.
        float GroundOf(SceneMesh scene) => scene.Positions[scene.Indices[
            Enumerable.Range(0, scene.TriangleCount).First(index => scene.TriangleKinds[index] == SurfaceKind.Ground) * 3]].Z;
        var bare = session.Document.Clone();
        bare.PlumbingPoints.Clear();
        bare.PlumbingSystems.Clear();
        check(mesh.Positions.Any(position => position.Z < -1.2f && position.X > 14) &&
              Math.Abs(GroundOf(mesh) - GroundOf(BuildingMesher.Build(bare, includeGround: true))) < 1e-6,
            "The septic tank is buried beside the house while the ground stays at grade");

        var reloaded = ProjectFile.DeserializeDocument(ProjectFile.SerializeDocument(session.Document));
        var stormPoint = reloaded.PlumbingPoints.Single(point => point.Id == storm);
        check(stormPoint.RainfallMmPerHour == 400 && reloaded.PlumbingSystems.Any(system => system.Kind == PlumbingSystemKind.StormWater),
            "Rainfall, septic data and storm systems persist");
    }
}
