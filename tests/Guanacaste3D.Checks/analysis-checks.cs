using Guanacaste3D.Core;

/// <summary>Checks the analytical model, the structural, energy and systems summaries, zones, mass shapes and the results fill.</summary>
static class AnalysisChecks
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
        Analytical(check);
        Structural(check);
        Energy(check);
        Tools(check);
    }

    private static void Analytical(Action<bool, string> check)
    {
        var house = new DocumentSession(BreezeBlockHouseShowcase.Create());
        var model = AnalyticalModel.Build(house.Document);
        var document = house.Document;
        check(model.Members.Count == document.Columns.Count + document.StructuralElements.Count(item => item.Kind is StructuralElementKind.Beam or StructuralElementKind.Brace or StructuralElementKind.Truss) &&
              model.Panels.Count == document.Walls.Count + document.Floors.Count(item => item.Kind is FloorKind.Floor or FloorKind.Roof) && model.Openings.Count == document.Openings.Count &&
              model.Members.All(member => double.IsFinite(member.A.X + member.A.Y + member.A.Z + member.B.X + member.B.Y + member.B.Z)),
            $"The house's analytical model has {model.Members.Count} bars, {model.Panels.Count} panels and {model.Openings.Count} openings: one for each beam, column, wall, slab and door or window");
        check(model.Supports.Count >= document.Walls.Count(wall => document.Levels.First(level => level.Id == wall.LevelId).Elevation <= document.Levels.Min(level => level.Elevation) + 1e-6),
            $"Every wall on the lowest level is a support ({model.Supports.Count} supports)");

        var frame = new DocumentSession(BuildingDocument.Create("Pórtico"));
        var level = frame.Document.Levels[0].Id;
        var corners = new[] { new Point2(0, 0), new Point2(6, 0), new Point2(6, 6), new Point2(0, 6) };
        foreach (var corner in corners) frame.AddColumn(level, corner, 0.3, 0.3, 3.0);
        for (var index = 0; index < 4; index++)
        {
            var beam = frame.AddStructuralElement(level, StructuralElementKind.Beam, "Viga", corners[index], corners[(index + 1) % 4]);
            frame.UpdateStructuralElement(beam, "Viga", 0.3, 0.3, 2.85, ElementStyle.Default);
        }
        var framed = AnalyticalModel.Build(frame.Document);
        check(framed.Members.Count == 8 && framed.Links.Count == 4 && framed.Links.All(link => link.Members.Count == 3) && framed.Supports.Count == 4 && framed.Supports.All(support => support.Condition == "Fixed"),
            "A frame of 4 columns and 4 beams has 8 bars, 4 nodes where a column and two beams meet, and 4 fixed supports");
    }

    private static void Structural(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Losa"));
        var level = session.Document.Levels[0].Id;
        session.AddFloor(level, [new Point2(0, 0), new Point2(6, 0), new Point2(6, 6), new Point2(0, 6)], 0.2, "Losa", FloorKind.Floor);
        foreach (var corner in new[] { new Point2(0.5, 0.5), new Point2(5.5, 0.5), new Point2(5.5, 5.5), new Point2(0.5, 5.5) }) session.AddColumn(level, corner, 0.3, 0.3, 3.0);
        var summary = StructuralSummary.Build(session.Document);
        var perColumn = 36.0 / 4 * (0.2 * 2400 + 200);
        check(summary.Columns.Count == 4 && summary.Columns.All(column => Near(column.TributaryArea, 9, 1e-9) && Near(column.Load, perColumn, 1e-6)) && Near(summary.SlabLoad, 36 * (0.2 * 2400 + 200), 1e-6),
            $"A 6 by 6 m slab on four columns gives each 9 m² and {perColumn:0} kg: its quarter of a 480 kg/m² slab plus 200 kg/m² of live load");
        check(Near(summary.Columns.Sum(column => column.Load) + summary.UnassignedSlabLoad, summary.SlabLoad, 1e-6) && Near(summary.TotalLive, 36 * 200, 1e-6) &&
              Near(summary.Levels.Sum(item => item.Slabs), 36 * 0.2 * 2400, 1e-6) && Near(summary.Levels.Sum(item => item.Frames), 4 * 0.3 * 0.3 * 3 * 2400, 1e-6),
            "What the columns carry adds up to the load of the slab, and the weights per storey are volume times density");
        check(summary.Columns.All(column => column.Accumulated > column.Load), "A column's accumulated load includes its own weight");

        var bare = new DocumentSession(BuildingDocument.Create("Sin columnas"));
        bare.AddFloor(bare.Document.Levels[0].Id, [new Point2(0, 0), new Point2(4, 0), new Point2(4, 4), new Point2(0, 4)], 0.2);
        check(Near(StructuralSummary.Build(bare.Document).UnassignedSlabLoad, 16 * 680, 1e-6), "A slab with no columns leaves all its load unassigned");

        var house = StructuralSummary.Build(new DocumentSession(BreezeBlockHouseShowcase.Create()).Document);
        check(house.Levels.All(item => item.Dead >= 0 && double.IsFinite(item.Dead + item.Live)) && house.TotalDead > 0, $"The cobogó house weighs {house.TotalDead / 1000:0} t dead and {house.TotalLive / 1000:0} t live");
    }

    private static void Energy(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Caja"));
        var level = session.Document.Levels[0].Id;
        var south = session.AddWall(level, new Point2(0, 0), new Point2(10, 0), 0.2, 3, BuiltInWallTypes.Exterior300Id);
        session.AddWall(level, new Point2(10, 0), new Point2(10, 10), 0.2, 3, BuiltInWallTypes.Exterior300Id);
        session.AddWall(level, new Point2(10, 10), new Point2(0, 10), 0.2, 3, BuiltInWallTypes.Exterior300Id);
        session.AddWall(level, new Point2(0, 10), new Point2(0, 0), 0.2, 3, BuiltInWallTypes.Exterior300Id);
        session.AddOpening(south, OpeningKind.Window, 3, 1.5, 0.9, 1.2);
        session.AddOpening(south, OpeningKind.Window, 7, 1.5, 0.9, 1.2);
        var energy = EnergySummary.Build(session.Document);
        var southFacade = energy.Facades.Single(item => item.Orientation == "S");
        var glass = session.Document.Openings.Where(opening => opening.WallId == south).Sum(opening => opening.Width * opening.Height);
        check(Near(southFacade.WallArea, 30) && glass > 1 && Near(southFacade.WindowArea, glass, 1e-9) && Near(southFacade.WindowRatio, glass / 30, 1e-9) && energy.Facades.Where(item => item.Orientation != "S").All(item => item.WindowArea == 0) &&
              Near(energy.TotalWallArea, 120),
            $"A box with two windows on its south wall has {glass:0.0} m² of glass on 30 m² of south wall: {glass / 30 * 100:0} %");
        check(!energy.Advice.Contains("ReduceWest") && !energy.Advice.Contains("ReduceEast") && !energy.Advice.Contains("MoveGlazingNorthSouth"), "…and the advice does not complain about glass that faces south");

        session.SetProjectSite(session.Document.Site with { TrueNorthDegrees = 90 });
        var turned = EnergySummary.Build(session.Document);
        check(turned.Facades.Single(item => item.Orientation == "W").WindowArea > 3 && turned.Facades.Single(item => item.Orientation == "S").WindowArea == 0,
            "With true north turned 90° the same wall faces west");

        var hot = new DocumentSession(BuildingDocument.Create("Oeste"));
        var hotLevel = hot.Document.Levels[0].Id;
        var west = hot.AddWall(hotLevel, new Point2(0, 10), new Point2(0, 0), 0.2, 3, BuiltInWallTypes.Exterior300Id);
        hot.AddWall(hotLevel, new Point2(0, 0), new Point2(10, 0), 0.2, 3, BuiltInWallTypes.Exterior300Id);
        hot.AddWall(hotLevel, new Point2(10, 0), new Point2(10, 10), 0.2, 3, BuiltInWallTypes.Exterior300Id);
        hot.AddWall(hotLevel, new Point2(10, 10), new Point2(0, 10), 0.2, 3, BuiltInWallTypes.Exterior300Id);
        for (var index = 0; index < 3; index++) hot.AddOpening(west, OpeningKind.Window, 1.8 + index * 3.0, 2.4, 0.9, 2.0);
        var advice = EnergySummary.Build(hot.Document).Advice;
        check(advice.Contains("ReduceWest") && advice.Contains("MoveGlazingNorthSouth"), "Half the west wall in glass brings the advice to reduce it and to move glass to the north and south");

        check(EnergySummary.Build(BuildingDocument.Create("Vacío")).Advice.SequenceEqual(["NoWalls"]), "A project without walls only says so");

        var systems = SystemsSummary.Build(new DocumentSession(BreezeBlockHouseShowcase.Create()).Document);
        check(systems.PlumbingSystems >= 0 && systems.Circuits >= 0 && systems.ConnectedWatts >= 0 && systems.CircuitsOverloaded <= systems.Circuits, "The systems summary of the house adds up");
    }

    private static void Tools(Action<bool, string> check)
    {
        // Zones.
        var session = new DocumentSession(BuildingDocument.Create("Zonas"));
        var level = session.Document.Levels[0].Id;
        var a = session.AddRoom(level, new Point2(0, 0), new Point2(4, 3), "Sala");
        var b = session.AddRoom(level, new Point2(4, 0), new Point2(8, 3), "Cocina");
        var c = session.AddRoom(level, new Point2(0, 3), new Point2(4, 6), "Dormitorio");
        var zone = session.CreateZone("Zona social", [a, b]);
        var summary = session.SummarizeZones().Single();
        check(summary.Rooms == 2 && Near(summary.Area, 24, 1e-6) && summary.Volume > 24 && summary.ZoneId == zone, $"A zone of two rooms has 24 m² and {summary.Volume:0} m³");
        check(Throws(() => session.CreateZone("Otra", [b, c])) && Throws(() => session.CreateZone("zona SOCIAL", [c])) && Throws(() => session.CreateZone("Vacía", [])) && Throws(() => session.CreateZone("Fantasma", [Guid.NewGuid()])),
            "A room cannot be in two zones, names cannot repeat, and a zone needs rooms that exist");
        session.RemoveRoom(a);
        check(session.SummarizeZones().Single().Rooms == 1, "A room that is deleted leaves its zone");
        session.RemoveRoom(b);
        check(session.Document.Zones.Count == 0, "A zone with no rooms left disappears");
        session.Undo();

        // Results fill.
        check(ResultsFill.ColorAt(0) == (52, 120, 220) && ResultsFill.ColorAt(1) == (230, 60, 50) && ResultsFill.ColorAt(0.5) == (90, 200, 90) && ResultsFill.ColorAt(-3) == ResultsFill.ColorAt(0) && ResultsFill.ColorAt(9) == ResultsFill.ColorAt(1),
            "The fill goes from blue through green to red and stays at its ends beyond them");
        var small = session.Document.Rooms.OrderBy(room => room.Area).First();
        var large = session.Document.Rooms.OrderBy(room => room.Area).Last();
        session.SetResultsMetric(ResultsMetric.Perimeter);
        check(session.Document.ResultsMetric == ResultsMetric.Perimeter && Near(ResultsFill.ValueOf(session.Document, large, ResultsMetric.Area), large.Area) &&
              ResultsFill.Position(session.Document, small, ResultsMetric.Area) <= ResultsFill.Position(session.Document, large, ResultsMetric.Area) && Throws(() => session.SetResultsMetric((ResultsMetric)9)),
            "A metric is chosen and kept; a room with a greater value sits no lower on the scale, and a metric that does not exist is refused");
        var even = new DocumentSession(BuildingDocument.Create("Iguales"));
        even.AddRoom(even.Document.Levels[0].Id, new Point2(0, 0), new Point2(3, 3), "A");
        check(Near(ResultsFill.Position(even.Document, even.Document.Rooms[0], ResultsMetric.Area), 0.5), "A level with one value sits in the middle of the scale");

        // Mass shapes.
        var forms = new DocumentSession(BreezeBlockHouseShowcase.Create());
        var lv = forms.Document.Levels[0].Id;
        var mass = forms.AddStructuralElement(lv, StructuralElementKind.Mass, "Masa", new Point2(900, 900), new Point2(910, 906));
        forms.UpdateStructuralElement(mass, "Masa", 0, 9, 0, ElementStyle.Default);
        var variants = forms.GenerateMassVariants(mass, [1, 2, 3]);
        var masses = forms.Document.StructuralElements.Where(item => item.Kind == StructuralElementKind.Mass).ToArray();
        double Plan(StructuralElement item) => (item.Footprint.Max(p => p.X) - item.Footprint.Min(p => p.X)) * (item.Footprint.Max(p => p.Y) - item.Footprint.Min(p => p.Y));
        check(variants.Count == 4 && masses.Length == 4 && masses.All(item => Near(Plan(item), 60, 1e-6) && Near(item.Height, 9)),
            "Three shapes are made beside the mass, each with its 60 m² of plan and 9 m of height, so the same 540 m³");
        forms.Undo();
        var undone = forms.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.Mass) == 1 && forms.Document.OptionSets.Count == 0;
        forms.Redo();
        check(undone && forms.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.Mass) == 4, "One undo takes all the shapes and the option set away, and redo brings them back");
        var square = variants.Single(item => item.Name == "1:1");
        var long3 = variants.Single(item => item.Name == "3:1");
        check(Near(square.Compactness, 1, 1e-9) && long3.Compactness < square.Compactness && long3.Perimeter > square.Perimeter, "The square is the most compact shape and the long one has the most wall");
        var set = forms.Document.OptionSets.Single(item => item.Name.StartsWith("Formas", StringComparison.Ordinal));
        var model3D = forms.Document.Views.First(view => view.Kind == ProjectViewKind.Model3D);
        var visible = ViewVisibility.Apply(forms.Document, forms.Document.Views.First(view => view.Id == model3D.Id)).StructuralElements.Count(item => item.Kind == StructuralElementKind.Mass);
        check(set.Options.Count == 4 && visible == 1, "They are four options of one set, and a view shows only the original unless it chooses another");
        check(Throws(() => forms.GenerateMassVariants(mass, [])) && Throws(() => forms.GenerateMassVariants(mass, [0.5])) && Throws(() => forms.GenerateMassVariants(Guid.NewGuid(), [2])),
            "No proportions, a proportion under 1 and a mass that does not exist are refused");
    }
}
