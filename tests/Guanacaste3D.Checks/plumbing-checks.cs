using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Regression checks for plumbing: fixture data, automatic connection, sizing, pressures, drains, and the ceiling fan.</summary>
static class PlumbingChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(Math.Abs(PlumbingStandards.PeakFlow(10) - 14.6 * 0.0630902) < 1e-9 &&
              PlumbingStandards.PeakFlow(15) > PlumbingStandards.PeakFlow(14) && PlumbingStandards.PeakFlow(0) == 0,
            "Peak flow follows Hunter's curve, interpolated between table rows");
        check(PlumbingStandards.SupplySize(PlumbingStandards.PeakFlow(1), false) == 0 &&
              PlumbingStandards.SupplySize(PlumbingStandards.PeakFlow(20), false) >= 1 &&
              PlumbingStandards.SupplySize(PlumbingStandards.PeakFlow(10), true) > PlumbingStandards.SupplySize(PlumbingStandards.PeakFlow(10), false) - 1,
            "Supply pipe grows with demand and hot water is held to a lower velocity");
        check(PlumbingStandards.DrainSize(3, 2) == 2 && PlumbingStandards.DrainSize(5, 0) == 1 && PlumbingStandards.DrainSize(100, 0) == 3,
            "Drains are sized by fixture units and never below the largest trap upstream (3\" for a toilet)");

        var session = new DocumentSession(BuildingDocument.Create("Plumbing"));
        var level = session.Document.Levels[0].Id;
        Guid Place(string name, double x, double y)
        {
            var preset = ResidentialComponentCatalog.Presets.Single(candidate => candidate.Name == name);
            return session.AddElementFromPreset(level, preset, new Point2(x, y), new Point2(x, y));
        }
        var toilet = Place("Toilet", 2, 4);
        var vanity = Place("Bathroom Vanity", 3.2, 4);
        var shower = Place("Shower Enclosure", 4.6, 4);
        var sink = Place("Kitchen Sink Cabinet", 8, 4);
        var heater = Place("Water Heater", 6, 1);
        var washer = Place("Washing Machine", 9, 1);
        Place("Clothes Dryer", 9.8, 1);
        try
        {
            session.ConnectPlumbing((kind, number) => $"{kind} {number}");
            check(false, "Connecting needs a meter or outlet");
        }
        catch (InvalidDataException) { check(true, "Connecting needs a meter or outlet"); }
        var meter = session.AddPlumbingPoint(level, PlumbingPointKind.WaterMeter, "Medidor", new Point2(0, 0));
        var outlet = session.AddPlumbingPoint(level, PlumbingPointKind.SewerOutlet, "Salida", new Point2(0, 5));
        var made = session.ConnectPlumbing((kind, number) => $"{kind} {number}");
        var systems = session.Document.PlumbingSystems;
        var cold = systems.Single(system => system.Kind == PlumbingSystemKind.ColdWater);
        var hot = systems.Single(system => system.Kind == PlumbingSystemKind.HotWater);
        var drain = systems.Single(system => system.Kind == PlumbingSystemKind.Sanitary);
        check(made == 3 && cold.Fixtures.ToHashSet().SetEquals([toilet, vanity, shower, sink, heater, washer]) &&
              hot.Fixtures.ToHashSet().SetEquals([vanity, shower, sink, washer]) && hot.SourceId == heater &&
              drain.Fixtures.ToHashSet().SetEquals([toilet, vanity, shower, sink, washer]),
            "Connecting plumbing feeds every fixture and the heater with cold water, hot water to the fixtures that use it, and drains every fixture; the dryer needs none");

        var analysis = PlumbingAnalysis.Analyze(session.Document);
        var coldReport = analysis.System(cold.Id)!;
        var hotReport = analysis.System(hot.Id)!;
        var drainReport = analysis.System(drain.Id)!;
        check(Math.Abs(coldReport.Units - (2.2 + 0.5 + 1.0 + 1.0 + 1.0 + (0.5 + 1.0 + 1.0 + 1.0))) < 1e-9 &&
              Math.Abs(hotReport.Units - 3.5) < 1e-9 && Math.Abs(drainReport.Units - (3 + 1 + 2 + 2 + 3)) < 1e-9,
            "Fixture units add up through the tree; the heater draws the hot demand it serves from the cold system");
        check(coldReport.MainNominal != "1/2\"" && coldReport.Status == PlumbingStatus.Ok &&
              coldReport.Fixtures.All(fixture => fixture.PressureKPa < 280 && fixture.PressureKPa > fixture.RequiredKPa),
            "At 280 kPa the house is well supplied: every fixture keeps its minimum pressure, from a main of at least 3/4\"");
        check(hotReport.Fixtures.All(fixture => fixture.WaitSeconds > 0 && fixture.PressureKPa < coldReport.Fixtures
                  .Single(candidate => candidate.FixtureId == heater).PressureKPa),
            "Hot water starts from the pressure at the heater and every fixture has a wait for hot water");
        var shower3 = drainReport.Fixtures.Single(fixture => fixture.FixtureId == shower);
        var sink3 = drainReport.Fixtures.Single(fixture => fixture.FixtureId == sink);
        check(drainReport.Fixtures.Single(fixture => fixture.FixtureId == toilet).Nominal == "3\"" &&
              drainReport.MainNominal is "3\"" or "4\"" && sink3.Invert > shower3.Invert && sink3.Cover > 0.1 &&
              drainReport.Runs.Where(run => !run.Detail && run.From.DistanceTo(run.To) > 1e-6)
                  .All(run => Math.Abs(Math.Abs(run.ToZ - run.FromZ) - PlumbingStandards.DrainSlope * run.From.DistanceTo(run.To)) < 1e-9),
            "Drains fall at 2 % toward the outlet, a toilet takes 3\", and the farthest fixture sits highest");

        var vents = session.Document.PlumbingPoints.Where(point => point.Kind == PlumbingPointKind.VentStack).ToArray();
        check(vents.Length >= 1 && drainReport.Fixtures.All(fixture => fixture.Vent.Length > 0 && fixture.TrapArm <= fixture.TrapArmLimit) &&
              drainReport.Status != PlumbingStatus.Fail && drainReport.Vents.Count >= 1 &&
              drainReport.Vents.All(vent => Math.Abs(vent.Top - (3 + PlumbingStandards.VentAboveRoof)) < 1e-9 && vent.Bottom < 0),
            "Connecting plumbing adds vent stacks so every trap is vented within its trap-arm length, rising from the drain through the roof");
        var trapPieces = drainReport.Runs.Where(run => run.Detail).ToArray();
        var washerAt = PlumbingStandards.Connection(session.Document.StructuralElements.Single(element => element.Id == washer));
        var washerTrap = trapPieces.Where(run => run.From.DistanceTo(washerAt) < 0.2).ToArray();
        check(trapPieces.Length == 4 * 4 && washerTrap.Max(run => Math.Max(run.FromZ, run.ToZ)) > 0.85,
            "Every fixture without a built-in trap gets a P-trap (the toilet has its own), and the washer drains into a tall standpipe");
        session.ConnectPlumbing((kind, number) => $"{kind} {number}");
        check(session.Document.PlumbingPoints.Count(point => point.Kind == PlumbingPointKind.VentStack) == vents.Length,
            "Connecting again reuses the vent stacks already placed");
        foreach (var vent in vents) session.RemovePlumbingPoint(vent.Id);
        var unvented = PlumbingAnalysis.Analyze(session.Document).Systems.Single(report => report.System.Kind == PlumbingSystemKind.Sanitary);
        check(unvented.Fixtures.All(fixture => fixture.Issue == "Plumbing.Issue.NoVent") && unvented.Status == PlumbingStatus.Fail,
            "Without vent stacks every trap fails: a draining fixture would siphon it dry");
        var far = session.AddPlumbingPoint(level, PlumbingPointKind.VentStack, "V lejos", new Point2(-20, 0));
        check(PlumbingAnalysis.Analyze(session.Document).Systems.Single(report => report.System.Kind == PlumbingSystemKind.Sanitary)
                .Fixtures.All(fixture => fixture.Issue == "Plumbing.Issue.TrapArmTooLong"),
            "A vent too far from a trap does not protect it");
        session.RemovePlumbingPoint(far);
        session.ConnectPlumbing((kind, number) => $"{kind} {number}");
        cold = session.Document.PlumbingSystems.Single(system => system.Kind == PlumbingSystemKind.ColdWater);
        hot = session.Document.PlumbingSystems.Single(system => system.Kind == PlumbingSystemKind.HotWater);
        drain = session.Document.PlumbingSystems.Single(system => system.Kind == PlumbingSystemKind.Sanitary);

        session.SetSupplyPressure(meter, 150);
        var low = PlumbingAnalysis.Analyze(session.Document).System(cold.Id)!;
        check(low.Fixtures.Single(fixture => fixture.FixtureId == shower).Status == PlumbingStatus.Fail &&
              low.Fixtures.Single(fixture => fixture.FixtureId == sink).Status != PlumbingStatus.Fail &&
              low.Status == PlumbingStatus.Fail,
            "At low street pressure the shower (138 kPa) fails while the kitchen sink (55 kPa) still works");
        session.SetSupplyPressure(meter, 280);
        session.SetOutletDepth(outlet, 0.2);
        var shallow = PlumbingAnalysis.Analyze(session.Document).System(drain.Id)!;
        check(shallow.Fixtures.Single(fixture => fixture.FixtureId == sink).Issue == "Plumbing.Issue.NoFall",
            "With a shallow outlet the farthest drain would come up through the floor and fails");
        session.SetOutletDepth(outlet, 0.6);

        var mesh = BuildingMesher.Build(session.Document);
        check(mesh.TriangleKinds.Contains(SurfaceKind.PipeCold) && mesh.TriangleKinds.Contains(SurfaceKind.PipeHot) &&
              mesh.TriangleKinds.Contains(SurfaceKind.PipeDrain),
            "Cold, hot and drain pipes are modeled in 3D in their system colours");
        var grounded = BuildingMesher.Build(session.Document, includeGround: true);
        var groundZ = Enumerable.Range(0, grounded.TriangleCount).Where(index => grounded.TriangleKinds[index] == SurfaceKind.Ground)
            .Select(index => grounded.Positions[grounded.Indices[index * 3]].Z).First();
        check(groundZ > -0.05f && groundZ < 0.01f, "Buried pipes stay below grade instead of lowering the ground under the house");

        session.TogglePlumbingFixture(cold.Id, toilet);
        check(PlumbingAnalysis.Analyze(session.Document).Unserved.Any(item => item.Fixture.Id == toilet && item.Kind == PlumbingSystemKind.ColdWater),
            "A disconnected fixture is listed as lacking water");
        try
        {
            session.TogglePlumbingFixture(hot.Id, toilet);
            check(false, "A toilet cannot be put on hot water");
        }
        catch (InvalidDataException) { check(true, "A toilet cannot be put on hot water"); }
        session.Undo();

        var path = Path.Combine(Path.GetTempPath(), $"plumbing-{Guid.NewGuid():N}.g3d");
        try
        {
            ProjectFile.Save(path, session.Document);
            var reloaded = ProjectFile.Load(path);
            check(reloaded.PlumbingPoints.Count == session.Document.PlumbingPoints.Count && reloaded.PlumbingPoints.Count > 2 &&
                  reloaded.PlumbingSystems.Count == 3 &&
                  reloaded.PlumbingSystems.Single(system => system.Id == cold.Id).Fixtures.Contains(toilet),
                "Meters, outlets and systems survive a project round trip");
        }
        finally { File.Delete(path); }
        var previous = session.Document.Clone();
        previous.SchemaVersion = 13;
        // A schema 13 file has no plumbing lists at all.
        previous.PlumbingPoints = null!;
        previous.PlumbingSystems = null!;
        var migrated = ProjectFile.DeserializeDocument(ProjectFile.SerializeDocument(previous));
        check(migrated.SchemaVersion == BuildingDocument.CurrentSchemaVersion && migrated.PlumbingPoints.Count == 0 &&
              migrated.PlumbingSystems.Count == 0,
            "Schema 13 projects open with empty plumbing");

        session.RemoveStructuralElement(heater);
        var orphan = PlumbingAnalysis.Analyze(session.Document);
        check(orphan.System(hot.Id)!.Status == PlumbingStatus.Empty && orphan.Unserved.Any(item => item.Kind == PlumbingSystemKind.HotWater),
            "Deleting the heater leaves its hot system empty and the fixtures without hot water, without breaking the project");
        session.RemovePlumbingPoint(meter);
        check(session.Document.PlumbingSystems.All(system => system.Kind != PlumbingSystemKind.ColdWater),
            "Removing a meter removes the systems it fed");

        // Ceiling fan with its light kit: switched like a luminaire, drawing its motor load.
        var fan = session.AddElectricalDevice(level, ElectricalDeviceKind.CeilingFan, "Ventilador", new Point2(4, 2));
        var fanSwitch = session.AddElectricalDevice(level, ElectricalDeviceKind.Switch, "S", new Point2(0.1, 2));
        session.ToggleSwitchLink(fanSwitch, fan);
        var device = session.Document.ElectricalDevices.Single(candidate => candidate.Id == fan);
        var lit = BuildingMesher.Build(session.Document);
        session.ToggleSwitch(fanSwitch);
        check(device.Watts == 75 && device.IsLuminaire && device.Elevation > 2 &&
              lit.TriangleKinds.Count(kind => kind == SurfaceKind.LightEmitter) > 0 &&
              ElectricalAnalysis.Analyze(session.Document).Brightness(fan) == 0,
            "A ceiling fan hangs from the ceiling with its blades and light kit and follows its switch");
    }
}
