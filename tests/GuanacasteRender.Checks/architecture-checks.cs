using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>
/// Checks the architectural element catalog: the type library, pitched roofs, column and beam sections,
/// guard and stair styles, ramps, gutters, downspouts, curtain walls, skylights, and operated doors and windows.
/// </summary>
static class ArchitectureChecks
{
    public static void Run(Action<bool, string> check)
    {
        CheckLibrary(check);
        CheckRoofs(check);
        CheckColumnsAndMembers(check);
        CheckNewElements(check);
        CheckOperatedOpenings(check);
        CheckDraftingAndEditing(check);
        CheckShowcase(check);
        CheckAuthoring(check);
    }

    private static void CheckShowcase(Action<bool, string> check)
    {
        var house = ArchitecturalShowcase.Create();
        house.Validate();
        var kinds = house.StructuralElements.Select(item => item.Kind).Distinct().Count();
        check(Triangles(house) > 2000 && kinds >= 8 && house.Floors.Any(floor => floor.IsPitched) &&
              house.OpeningTypes.Where(type => house.Openings.Any(o => o.OpeningTypeId == type.Id))
                  .Select(type => type.Operation).Distinct().Count() >= 6,
            "The architectural showcase house validates and exercises roofs, 8+ element kinds and 6+ opening operations");
    }

    private static int Triangles(BuildingDocument document) => BuildingMesher.Build(document).TriangleCount;

    private static void CheckLibrary(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Library"));
        var walls = ArchitecturalLibrary.WallTypes();
        var openings = ArchitecturalLibrary.OpeningTypes();
        check(walls.Count >= 20 && walls.Select(type => type.Id).Distinct().Count() == walls.Count &&
              Enum.GetValues<WallFunction>().All(function => walls.Any(type => type.Function == function)),
            "The wall library offers 20+ distinct assemblies across every wall function");
        check(openings.Count(type => type.Kind == OpeningKind.Door) >= 20 &&
              openings.Count(type => type.Kind == OpeningKind.Window) >= 15 &&
              openings.Select(type => type.FamilyName).Distinct().Count() >= 14,
            "The opening library offers 14+ door and window families");
        var added = session.LoadArchitecturalLibrary();
        session.Document.Validate();
        check(added == walls.Count + openings.Count && session.Document.WallTypes.Count == 3 + walls.Count,
            "Loading the library adds every missing type and the project still validates");
        check(session.LoadArchitecturalLibrary() == 0, "Loading the library twice adds nothing");
        session.Undo();
        check(session.Document.WallTypes.Count == 3 && session.Document.OpeningTypes.Count == 6,
            "Loading the library is one undoable transaction");
        session.Redo();

        var path = Path.Combine(Path.GetTempPath(), $"library-{Guid.NewGuid():N}.g3d");
        try
        {
            ProjectFile.Save(path, session.Document);
            var reloaded = ProjectFile.Load(path);
            var sliding = reloaded.OpeningTypes.First(type => type.Operation == OpeningOperation.Sliding &&
                                                              type.Kind == OpeningKind.Door);
            check(reloaded.WallTypes.Count == session.Document.WallTypes.Count && sliding.GlazedLeaf,
                "Library types survive a project round trip with their operation");
        }
        finally { File.Delete(path); }

        // Resizing an instance must keep its family and operation, not fall back to the default family.
        var wall = session.AddWall(session.Document.Levels[0].Id, new Point2(0, 0), new Point2(8, 0));
        var slidingType = session.Document.OpeningTypes.First(type => type.Operation == OpeningOperation.Sliding &&
                                                                       type.Kind == OpeningKind.Door);
        var door = session.AddOpening(wall, OpeningKind.Door, 4.0, openingTypeId: slidingType.Id);
        session.UpdateOpening(door, 4.0, 2.2, 0, 2.1);
        var resized = session.Document.OpeningTypes.Single(type =>
            type.Id == session.Document.Openings.Single(opening => opening.Id == door).OpeningTypeId);
        check(resized.Operation == OpeningOperation.Sliding && resized.FamilyName == slidingType.FamilyName,
            "Resizing a door instance keeps its family and operation");
    }

    private static (DocumentSession Session, Guid Level, Guid Roof) RoofSession(RoofShape shape, double pitch,
        double width = 10, double depth = 6)
    {
        var session = new DocumentSession(BuildingDocument.Create("Roofs"));
        var level = session.Document.Levels[0].Id;
        var roof = session.AddFloor(level, [new(0, 0), new(width, 0), new(width, depth), new(0, depth)], 0.10,
            kind: FloorKind.Roof, elevationOffset: 3.0);
        session.SetRoofShape(roof, shape, pitch);
        return (session, level, roof);
    }

    private static void CheckRoofs(Action<bool, string> check)
    {
        var (flat, _, _) = RoofSession(RoofShape.Flat, 0);
        var flatTriangles = Triangles(flat.Document);
        var (gable, _, gableId) = RoofSession(RoofShape.Gable, 30);
        var gableRoof = gable.Document.Floors.Single(floor => floor.Id == gableId);
        var ridge = RoofGeometry.RidgeRise(gableRoof);
        check(Math.Abs(ridge - 3 * Math.Tan(30 * Math.PI / 180)) < 1e-9 &&
              Math.Abs(RoofGeometry.RiseAt(gableRoof, new Point2(5, 3)) - ridge) < 1e-9 &&
              RoofGeometry.RiseAt(gableRoof, new Point2(5, 0)) == 0,
            "A gable roof rises from the eave to a ridge of span/2 × tan(pitch)");
        check(RoofGeometry.Facets(gableRoof).Count == 2 &&
              RoofGeometry.Facets(gableRoof).Sum(facet => Math.Abs(PolygonTriangulator.SignedDoubleArea(facet.Outline)) / 2)
              is > 59.999 and < 60.001,
            "A gable roof splits into two facets that cover the whole boundary");
        var mesh = BuildingMesher.Build(gable.Document);
        var (min, max) = mesh.Bounds();
        check(Math.Abs((max.Z - 3.0f) - (float)ridge) < 1e-4, "The gable roof mesh reaches the ridge height");
        check(Triangles(gable.Document) > flatTriangles, "A gable roof has more faces than a flat slab");

        var (hip, _, hipId) = RoofSession(RoofShape.Hip, 25);
        var hipRoof = hip.Document.Floors.Single(floor => floor.Id == hipId);
        check(RoofGeometry.Facets(hipRoof).Count == 4 &&
              RoofGeometry.CreaseLines(hipRoof).Count == 5 &&
              Math.Abs(RoofGeometry.Facets(hipRoof).Sum(f => Math.Abs(PolygonTriangulator.SignedDoubleArea(f.Outline)) / 2) - 60) < 1e-6,
            "A hip roof has four facets and a ridge with four hips");
        check(Math.Abs(RoofGeometry.RiseAt(hipRoof, new Point2(5, 3)) - 3 * Math.Tan(25 * Math.PI / 180)) < 1e-9,
            "A hip roof reaches its maximum rise along the ridge");

        var (shed, _, shedId) = RoofSession(RoofShape.Shed, 10);
        var shedRoof = shed.Document.Floors.Single(floor => floor.Id == shedId);
        check(Math.Abs(RoofGeometry.RiseAt(shedRoof, new Point2(2, 6)) - 6 * Math.Tan(10 * Math.PI / 180)) < 1e-9 &&
              RoofGeometry.Facets(shedRoof).Count == 1, "A shed roof is one plane rising across the short side");

        // An L-shaped roof must still triangulate into valid facets covering its area.
        var lShaped = new DocumentSession(BuildingDocument.Create("L"));
        var levelId = lShaped.Document.Levels[0].Id;
        var lRoof = lShaped.AddFloor(levelId, [new(0, 0), new(8, 0), new(8, 3), new(3, 3), new(3, 7), new(0, 7)], 0.1,
            kind: FloorKind.Roof, elevationOffset: 3);
        lShaped.SetRoofShape(lRoof, RoofShape.Hip, 30);
        lShaped.Document.Validate();
        check(Triangles(lShaped.Document) > 20, "A hip roof over an L-shaped boundary meshes without failing");

        var flatFloorFailed = false;
        try
        {
            var plain = new DocumentSession(BuildingDocument.Create("Bad"));
            var floor = plain.AddFloor(plain.Document.Levels[0].Id, [new(0, 0), new(4, 0), new(4, 4), new(0, 4)]);
            plain.SetRoofShape(floor, RoofShape.Gable, 20);
        }
        catch (InvalidDataException) { flatFloorFailed = true; }
        check(flatFloorFailed, "Only roofs may be pitched");

        var steepFailed = false;
        try { gable.SetRoofShape(gableId, RoofShape.Gable, 80); }
        catch (InvalidDataException) { steepFailed = true; }
        check(steepFailed && gable.Document.Floors.Single(f => f.Id == gableId).Pitch == 30,
            "A pitch above 70° is rejected atomically");

        var dormerSession = RoofSession(RoofShape.Gable, 30);
        var dormer = dormerSession.Session.AddDormer(dormerSession.Level, "Dormer", new Point2(1, 0.5), new Point2(2, 1.5));
        var dormerElement = dormerSession.Session.Document.StructuralElements.Single(item => item.Id == dormer);
        check(dormer is not null && dormerElement.Offset >= 3.0 && dormerElement.Offset < 3.0 + ridge,
            "A dormer on a pitched roof rests on the lowest roof height under its footprint");
        var skylight = dormerSession.Session.AddSkylight(dormerSession.Level, "Skylight", new Point2(6, 1), new Point2(7.5, 2));
        check(skylight is not null && dormerSession.Session.AddSkylight(dormerSession.Level, "Outside",
                  new Point2(20, 20), new Point2(21, 21)) is null,
            "A skylight needs a roof under its whole footprint");
        dormerSession.Session.SetRoofShape(dormerSession.Roof, RoofShape.Flat, 0);
        check(dormerSession.Session.Document.StructuralElements.Where(item => item.Kind is
                  StructuralElementKind.Dormer or StructuralElementKind.Skylight).All(item => item.Offset == 3.0),
            "Flattening a roof lowers the dormers and skylights standing on it");
    }

    private static void CheckColumnsAndMembers(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Sections"));
        var level = session.Document.Levels[0].Id;
        foreach (var preset in ArchitecturalLibrary.ColumnPresets)
            session.AddColumnFromPreset(level, new Point2(session.Document.Columns.Count * 2, 0), preset);
        session.Document.Validate();
        var shapes = session.Document.Columns.Select(column => column.Shape).Distinct().Count();
        check(shapes == 5, "Columns come in rectangular, round, octagonal, L-shaped and T-shaped sections");
        var round = session.Document.Columns.First(column => column.Shape == ColumnShape.Round);
        check(round.Outline.Count == 24 && round.Outline.All(point =>
                  Math.Abs(point.DistanceTo(round.Position) - round.Width / 2) < 1e-9),
            "A round column outline lies on its circle");
        var lShape = session.Document.Columns.First(column => column.Shape == ColumnShape.LShaped);
        var lArea = Math.Abs(PolygonTriangulator.SignedDoubleArea(lShape.Outline)) / 2;
        check(lArea < lShape.Width * lShape.Depth * 0.7 && lArea > 0.1, "An L-shaped column removes a quadrant");
        check(PolygonTriangulator.Triangulate(lShape.Outline).Count == 4, "The L-shaped outline triangulates");

        var rectangular = new DocumentSession(BuildingDocument.Create("Rect"));
        rectangular.AddColumn(rectangular.Document.Levels[0].Id, new Point2(0, 0));
        var roundSession = new DocumentSession(BuildingDocument.Create("Round"));
        var roundColumn = roundSession.AddColumn(roundSession.Document.Levels[0].Id, new Point2(0, 0));
        roundSession.SetColumnShape(roundColumn, ColumnShape.Round);
        check(Triangles(roundSession.Document) > Triangles(rectangular.Document),
            "A round column meshes with more faces than a square one");

        var beams = new DocumentSession(BuildingDocument.Create("Beams"));
        var beamLevel = beams.Document.Levels[0].Id;
        var plain = beams.AddStructuralElement(beamLevel, StructuralElementKind.Beam, "Beam", new(0, 0), new(6, 0));
        var plainParts = ElementGeometry.Parts(beams.Document.StructuralElements.Single(item => item.Id == plain)).ToArray();
        beams.SetElementStyle(plain, ElementStyle.IShape);
        var iParts = ElementGeometry.Parts(beams.Document.StructuralElements.Single(item => item.Id == plain)).ToArray();
        beams.SetElementStyle(plain, ElementStyle.Tee);
        var teeParts = ElementGeometry.Parts(beams.Document.StructuralElements.Single(item => item.Id == plain)).ToArray();
        check(plainParts.Length == 1 && iParts.Length == 3 && teeParts.Length == 2 &&
              Math.Abs(iParts.Min(part => part.Bottom) - plainParts[0].Bottom) < 1e-9 &&
              Math.Abs(iParts.Max(part => part.Top) - plainParts[0].Top) < 1e-9,
            "I-shaped and tee beams are built from flanges and a web inside the same envelope");
        beams.Document.Validate();
        var pier = beams.AddStructuralElement(beamLevel, StructuralElementKind.IsolatedFoundation, "Pier", new(2, 2), new(2, 2));
        beams.SetElementStyle(pier, ElementStyle.Round);
        check(ElementGeometry.Parts(beams.Document.StructuralElements.Single(item => item.Id == pier))
                  .Single().Footprint.Count == 24, "A round pier has a circular footprint");

        var badStyleFailed = false;
        try { beams.SetElementStyle(pier, (ElementStyle)99); }
        catch (InvalidDataException) { badStyleFailed = true; }
        check(badStyleFailed, "Unknown element styles are rejected");

        var presets = ArchitecturalLibrary.ElementPresets;
        check(presets.Select(preset => preset.Name).Distinct().Count() == presets.Count &&
              Enum.GetValues<StructuralElementKind>().Where(kind => kind is StructuralElementKind.Railing or
                  StructuralElementKind.Stair or StructuralElementKind.Ramp or StructuralElementKind.Gutter or
                  StructuralElementKind.Downspout or StructuralElementKind.CurtainWall).All(kind =>
                  ArchitecturalLibrary.PresetsFor(kind).Any()),
            "Every circulation and drainage kind has catalog sections");
    }

    private static void CheckNewElements(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Elements"));
        var level = session.Document.Levels[0].Id;
        double Area(IReadOnlyList<Point2> footprint) => Math.Abs(PolygonTriangulator.SignedDoubleArea(footprint)) / 2;

        var railing = session.AddStructuralElement(level, StructuralElementKind.Railing, "Guard", new(0, 0), new(6, 0));
        var counts = new Dictionary<ElementStyle, int>();
        foreach (var style in new[] { ElementStyle.Default, ElementStyle.Pickets, ElementStyle.GlassPanel,
                     ElementStyle.Cable, ElementStyle.SolidParapet })
        {
            session.SetElementStyle(railing, style);
            counts[style] = ElementGeometry.Parts(session.Document.StructuralElements.Single(item => item.Id == railing)).Count();
        }
        check(counts[ElementStyle.Pickets] > 50 && counts[ElementStyle.SolidParapet] == 2 &&
              counts[ElementStyle.GlassPanel] < counts[ElementStyle.Pickets] &&
              counts[ElementStyle.Default] == CirculationGeometry.Parts(
                  session.Document.StructuralElements.Single(item => item.Id == railing) with { Style = ElementStyle.Default }).Count(),
            "Guards offer posts and rails, balusters, glass, cable and solid parapet styles");
        session.SetElementStyle(railing, ElementStyle.GlassPanel);
        check(ElementGeometry.Parts(session.Document.StructuralElements.Single(item => item.Id == railing))
                  .Any(part => part.Kind == SurfaceKind.Glass), "A glass guard carries a glass pane");

        var stair = session.AddStructuralElement(level, StructuralElementKind.Stair, "Stair", new(0, 2), new(5, 2));
        var solid = ElementGeometry.Parts(session.Document.StructuralElements.Single(item => item.Id == stair)).Count();
        session.SetElementStyle(stair, ElementStyle.Open);
        var open = ElementGeometry.Parts(session.Document.StructuralElements.Single(item => item.Id == stair)).ToArray();
        check(solid == 18 && open.Length == 18 * 3 && open.Max(part => part.Top) <= 3.0 + 1e-9,
            "An open stair has a tread and two stringers per step within the same rise");

        var ramp = session.AddStructuralElement(level, StructuralElementKind.Ramp, "Ramp", new(0, 4), new(7.2, 4));
        var rampPart = ElementGeometry.Parts(session.Document.StructuralElements.Single(item => item.Id == ramp)).Single();
        check(rampPart.EndTop == 0.60 && rampPart.Top < rampPart.EndTop && Area(rampPart.Footprint) is > 10.7 and < 10.9,
            "A ramp rises from its toe to its full height over its run");
        var rampMesh = Triangles(session.Document);
        check(rampMesh > 0, "A ramp meshes as a closed wedge");

        var gutter = session.AddStructuralElement(level, StructuralElementKind.Gutter, "Gutter", new(0, 6), new(8, 6));
        var gutterParts = ElementGeometry.Parts(session.Document.StructuralElements.Single(item => item.Id == gutter)).ToArray();
        check(gutterParts.Length == 3 && gutterParts.All(part => part.Bottom == 2.85),
            "A gutter is a channel with a bottom and two sides at the eave");

        var downspout = session.AddStructuralElement(level, StructuralElementKind.Downspout, "Downspout", new(0, 6), new(0, 6));
        var spoutPart = ElementGeometry.Parts(session.Document.StructuralElements.Single(item => item.Id == downspout)).Single();
        check(spoutPart.Top - spoutPart.Bottom == 2.85 && Math.Abs(Area(spoutPart.Footprint) - 0.09 * 0.09) < 1e-9,
            "A downspout is a vertical pipe from the ground to the eave");

        var curtain = session.AddStructuralElement(level, StructuralElementKind.CurtainWall, "Curtain wall", new(0, 8), new(6, 8));
        var curtainParts = ElementGeometry.Parts(session.Document.StructuralElements.Single(item => item.Id == curtain)).ToArray();
        check(curtainParts.Count(part => part.Kind == SurfaceKind.Glass) == 1 && curtainParts.Length == 1 + 5 + 4,
            "A curtain wall has one glass pane, mullions every 1.5 m and transoms every 1.2 m");
        session.Document.Validate();

        var tooShort = false;
        try { session.AddStructuralElement(level, StructuralElementKind.Gutter, "Short", new(0, 0), new(0.01, 0)); session.Document.Validate(); }
        catch (InvalidDataException) { tooShort = true; }
        check(tooShort, "A gutter shorter than 10 cm is rejected");
    }

    private static void CheckOperatedOpenings(Action<bool, string> check)
    {
        int Faces(OpeningKind kind, OpeningOperation operation, double width, double height, double sill, int lights = 0,
            double depth = 0.04, bool glazed = false)
        {
            var document = BuildingDocument.Create("Operated");
            var type = new OpeningType(Guid.NewGuid(), kind, "Test", "Type", width, height, kind == OpeningKind.Door ? 0 : sill)
            { Operation = operation, GlazingLightCount = lights, Depth = kind == OpeningKind.Door ? depth : 0, GlazedLeaf = glazed };
            document.OpeningTypes.Add(type);
            var session = new DocumentSession(document);
            var wall = session.AddWall(document.Levels[0].Id, new Point2(0, 0), new Point2(8, 0));
            session.AddOpening(wall, kind, 2.0, width, kind == OpeningKind.Door ? 0 : sill, height, type.Id);
            session.Document.Validate();
            // A geometry signature, so two operations with the same face count still compare as different.
            var hash = new HashCode();
            var mesh = BuildingMesher.Build(session.Document);
            foreach (var position in mesh.Positions)
                hash.Add((int)Math.Round(position.X * 1000) * 31 + (int)Math.Round(position.Y * 1000) * 17 +
                         (int)Math.Round(position.Z * 1000));
            return hash.ToHashCode() ^ mesh.TriangleCount;
        }

        var swing = Faces(OpeningKind.Door, OpeningOperation.Standard, 1.8, 2.1, 0);
        var glazedSwing = Faces(OpeningKind.Door, OpeningOperation.Standard, 1.8, 2.1, 0, glazed: true);
        var counts = new[]
        {
            swing,
            Faces(OpeningKind.Door, OpeningOperation.Sliding, 1.8, 2.1, 0, glazed: true),
            Faces(OpeningKind.Door, OpeningOperation.Folding, 1.8, 2.1, 0),
            Faces(OpeningKind.Door, OpeningOperation.Overhead, 1.8, 2.1, 0, depth: 0.05),
            Faces(OpeningKind.Door, OpeningOperation.Pocket, 1.8, 2.1, 0),
            Faces(OpeningKind.Door, OpeningOperation.Revolving, 1.8, 2.1, 0, depth: 0.9)
        };
        check(glazedSwing != swing && counts.Distinct().Count() == counts.Length,
            "Every door operation builds its own leaves; a glazed leaf has glass where the upper raised panel was");

        var windows = new[]
        {
            Faces(OpeningKind.Window, OpeningOperation.Standard, 1.2, 1.2, 0.9, 1),
            Faces(OpeningKind.Window, OpeningOperation.Sliding, 1.2, 1.2, 0.9, 2),
            Faces(OpeningKind.Window, OpeningOperation.Hung, 1.2, 1.2, 0.9, 1),
            Faces(OpeningKind.Window, OpeningOperation.Louvered, 1.2, 1.2, 0.9, 1),
            Faces(OpeningKind.Window, OpeningOperation.Casement, 1.2, 1.2, 0.9, 1),
            Faces(OpeningKind.Window, OpeningOperation.Awning, 1.2, 1.2, 0.9, 1)
        };
        check(windows.Distinct().Count() == windows.Length,
            "Window operations differ: sliding tracks, hung sashes, louver blades and handles");

        var invalidDoor = false;
        try
        {
            var document = BuildingDocument.Create("Bad");
            document.OpeningTypes.Add(new OpeningType(Guid.NewGuid(), OpeningKind.Door, "Bad", "Awning door", 0.9, 2.1, 0)
            { Operation = OpeningOperation.Awning });
            document.Validate();
        }
        catch (InvalidDataException) { invalidDoor = true; }
        check(invalidDoor, "A door cannot be an awning and a window cannot be a garage door");

        var symbol = (OpeningOperation operation) => DoorPlanSymbol.Build(0, operation == OpeningOperation.Overhead ? 2.4 : 1.8,
            0.2, 0.04, operation);
        check(symbol(OpeningOperation.Standard).Swings.Count == 2 && symbol(OpeningOperation.Sliding).Swings.Count == 0 &&
              symbol(OpeningOperation.Sliding).Outlines.Count(o => o.Part == DoorPlanPart.Leaf) == 2 &&
              symbol(OpeningOperation.Revolving).Swings.Count == 1 &&
              symbol(OpeningOperation.Folding).Outlines.Count(o => o.Part == DoorPlanPart.Leaf) >= 4 &&
              symbol(OpeningOperation.Pocket).Thresholds.Count == 4,
            "The plan symbol follows the operation: swing arcs, sliding panels, folded leaves, pocket cavity and drum");

        // Existing swing-door symbols are unchanged by the new parameter.
        check(DoorPlanSymbol.Build(3, 3.9, 0.2, 0.04).Outlines.Count ==
              DoorPlanSymbol.Build(3, 3.9, 0.2, 0.04, OpeningOperation.Standard).Outlines.Count,
            "A swing door keeps its original plan symbol");
    }

    private static void CheckDraftingAndEditing(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Drafting"));
        var level = session.Document.Levels[0].Id;

        var area = session.AddArea(level, "Area 1", new Point2(0, 0), new Point2(5, 4));
        var areaElement = session.Document.StructuralElements.Single(item => item.Id == area);
        check(Math.Abs(areaElement.PlanArea - 20) < 1e-9 && !areaElement.HasSolid && areaElement.Footprint.Count == 4 &&
              ElementGeometry.Parts(areaElement).Count() == 0, "An area measures its rectangle and has no solid");
        var thin = false;
        try { session.AddArea(level, "Thin", new Point2(0, 0), new Point2(0.01, 5)); }
        catch (InvalidDataException) { thin = true; }
        check(thin, "An area thinner than 5 cm is rejected");

        var plane = session.AddStructuralElement(level, StructuralElementKind.ReferencePlane, "Axis A", new(0, -1), new(10, -1));
        var separator = session.AddStructuralElement(level, StructuralElementKind.RoomSeparator, "Separator", new(2, 0), new(2, 4));
        var boundary = session.AddStructuralElement(level, StructuralElementKind.AreaBoundary, "Boundary", new(0, 0), new(5, 0));
        session.Document.Validate();
        check(new[] { plane, separator, boundary }.All(id =>
                  session.Document.StructuralElements.Single(item => item.Id == id) is { IsDatumLine: true, HasSolid: false }) &&
              BuildingMesher.Build(session.Document).TriangleCount == 0,
            "Reference planes, room separators and area boundaries are datum lines that add no solids");

        session.RenameStructuralElement(plane, "Axis B");
        check(session.Document.StructuralElements.Single(item => item.Id == plane).Name == "Axis B",
            "A drafting mark can be renamed");
        var blank = false;
        try { session.RenameStructuralElement(plane, "  "); }
        catch (InvalidDataException) { blank = true; }
        check(blank, "A blank element name is rejected");

        var room = session.AddRoom(level, new Point2(0, 0), new Point2(4, 3), "Living");
        session.RenameRoom(room, "Kitchen", "K-1");
        var renamed = session.Document.Rooms.Single(item => item.Id == room);
        check(renamed.Name == "Kitchen" && renamed.Number == "K-1", "A room can be renamed with a new number");
        session.AddRoom(level, new Point2(5, 0), new Point2(8, 3), "Study");
        var duplicate = false;
        try { session.RenameRoom(session.Document.Rooms.Last().Id, "Study", "K-1"); }
        catch (InvalidDataException) { duplicate = true; }
        check(duplicate && session.Document.Rooms.Last().Number != "K-1", "Room numbers stay unique when renaming");

        var floor = session.AddFloor(level, [new(0, 0), new(6, 0), new(6, 4), new(0, 4)], 0.2);
        session.UpdateFloor(floor, "Ground slab", 0.25, 0.05);
        var slab = session.Document.Floors.Single(item => item.Id == floor);
        check(slab.Name == "Ground slab" && slab.Thickness == 0.25 && slab.ElevationOffset == 0.05,
            "A floor's name, thickness and elevation can be edited");
        var tooThick = false;
        try { session.UpdateFloor(floor, "Bad", 9, 0); }
        catch (InvalidDataException) { tooThick = true; }
        check(tooThick && session.Document.Floors.Single(item => item.Id == floor).Thickness == 0.25,
            "An out-of-range slab edit is rejected atomically");

        var column = session.AddColumn(level, new Point2(1, 1));
        session.UpdateColumn(column, 0.5, 0.3, 4, ColumnShape.Octagonal);
        var edited = session.Document.Columns.Single(item => item.Id == column);
        check(edited is { Width: 0.5, Depth: 0.3, Height: 4, Shape: ColumnShape.Octagonal } && edited.Outline.Count == 8,
            "A column's section, height and outline can be edited");

        var beam = session.AddStructuralElement(level, StructuralElementKind.Beam, "Beam", new(0, 0), new(5, 0));
        session.UpdateStructuralElement(beam, "Main beam", 0.25, 0.45, -0.45, ElementStyle.IShape);
        var main = session.Document.StructuralElements.Single(item => item.Id == beam);
        check(main is { Name: "Main beam", Width: 0.25, Height: 0.45, Style: ElementStyle.IShape },
            "A framing member's section and style can be edited");
        var wrongStyle = false;
        try { session.UpdateStructuralElement(beam, "Beam", 0.25, 0.45, -0.45, ElementStyle.Pickets); }
        catch (InvalidDataException) { wrongStyle = true; }
        check(wrongStyle && session.Document.StructuralElements.Single(item => item.Id == beam).Style == ElementStyle.IShape,
            "A style that does not fit the kind is rejected atomically");

        var curtain = session.AddStructuralElement(level, StructuralElementKind.CurtainWall, "Curtain", new(0, 8), new(6, 8));
        var defaultParts = ElementGeometry.Parts(session.Document.StructuralElements.Single(item => item.Id == curtain)).Count();
        session.UpdateCurtainGrid(curtain, 3, 1.5);
        var wide = ElementGeometry.Parts(session.Document.StructuralElements.Single(item => item.Id == curtain)).Count();
        session.UpdateCurtainGrid(curtain, 0, 0);
        check(wide < defaultParts && ElementGeometry.Parts(session.Document.StructuralElements.Single(item => item.Id == curtain)).Count() == defaultParts,
            "A curtain wall's grid spacing changes its mullions and resets to the default");
        var badGrid = false;
        try { session.UpdateCurtainGrid(curtain, 0.05, 1); }
        catch (InvalidDataException) { badGrid = true; }
        check(badGrid, "A curtain grid tighter than 20 cm is rejected");
        var notCurtain = false;
        try { session.UpdateCurtainGrid(beam, 1, 1); }
        catch (InvalidDataException) { notCurtain = true; }
        check(notCurtain, "Only curtain walls have a grid");

        session.Undo();
        check(session.Document.StructuralElements.Single(item => item.Id == curtain).GridWidth == 3,
            "Editing steps are undoable one by one");

        var path = Path.Combine(Path.GetTempPath(), $"drafting-{Guid.NewGuid():N}.g3d");
        try
        {
            ProjectFile.Save(path, session.Document);
            var reloaded = ProjectFile.Load(path);
            check(reloaded.StructuralElements.Single(item => item.Id == beam).Style == ElementStyle.IShape &&
                  reloaded.Columns.Single(item => item.Id == column).Shape == ColumnShape.Octagonal &&
                  reloaded.StructuralElements.Any(item => item.Kind == StructuralElementKind.Area),
                "Styles, shapes and areas survive a project round trip");
        }
        finally { File.Delete(path); }
    }

    private static void CheckAuthoring(Action<bool, string> check)
    {
        var session = new DocumentSession(ArchitecturalLibrary.CreateProject("Authoring"));
        var level = session.Document.Levels[0].Id;
        var wallType = ArchitecturalLibrary.WallTypes().First(type => type.Name == "Concrete Block 150 mm").Id;

        var added = session.AddWallsAroundBoundary(level, [new(0, 0), new(8, 0), new(8, 6), new(0, 6)], wallType);
        session.AddWall(level, new Point2(4, 0), new Point2(4, 6), wallTypeId: wallType);
        check(added == 4 && session.Document.Walls.Count == 5 && session.Document.Walls.All(wall => wall.Height == 3),
            "Walls by face build one wall per side of a boundary in one transaction");
        session.Undo();
        session.Undo();
        check(session.Document.Walls.Count == 0, "A wall loop is a single undo step");
        session.Redo();
        session.Redo();

        var rooms = session.AddAutomaticRooms(level);
        var spaces = session.Document.Rooms.OrderBy(room => room.FirstCorner.X).ToArray();
        check(rooms == 2 && spaces.Length == 2 &&
              spaces.All(room => room.Area is > 20 and < 25),
            "Automatic rooms find both closed spaces and measure the clear floor");
        check(session.AddAutomaticRooms(level) == 0, "Automatic rooms never duplicate an existing room");

        var open = new DocumentSession(BuildingDocument.Create("Open"));
        var openLevel = open.Document.Levels[0].Id;
        open.AddWall(openLevel, new Point2(0, 0), new Point2(8, 0));
        open.AddWall(openLevel, new Point2(8, 0), new Point2(8, 6));
        open.AddWall(openLevel, new Point2(8, 6), new Point2(0, 6));
        check(open.AddAutomaticRooms(openLevel) == 0, "A space that is not closed gets no room");

        var room = session.Document.Rooms.First();
        var gable = ArchitecturalLibrary.SlabPresets.Single(preset => preset.Name == "Gable Roof 30°");
        var roof = session.AddSlabOverRoom(room.Id, gable, 0.6);
        var roofSlab = session.Document.Floors.Single(floor => floor.Id == roof);
        check(roofSlab.IsPitched && roofSlab.ElevationOffset == 3.0 + gable.Thickness &&
              Math.Abs(roofSlab.CalculateArea() - (Math.Abs(room.SecondCorner.X - room.FirstCorner.X) + 1.2) *
                  (Math.Abs(room.SecondCorner.Y - room.FirstCorner.Y) + 1.2)) < 1e-6,
            "A roof by face covers a room with an overhang and bears on the wall tops");
        var floorRecipe = ArchitecturalLibrary.SlabPresets.First(preset => preset.Kind == FloorKind.Floor);
        var floor = session.AddSlabOverRoom(room.Id, floorRecipe);
        var edges = session.AddSlabEdges(floor);
        check(edges == 4 && session.Document.StructuralElements.Count(element => element.Kind == StructuralElementKind.SlabEdge) == 4 &&
              session.Document.StructuralElements.Where(element => element.Kind == StructuralElementKind.SlabEdge)
                  .Select(element => element.GroupId).Distinct().Count() == 1,
            "A slab edge runs along each side of a slab as one group");
        session.Document.Validate();

        var wall = session.Document.Walls.First().Id;
        var sweep = session.AddWallSweep(wall, false);
        var reveal = session.AddWallSweep(wall, true);
        var sweepElement = session.Document.StructuralElements.Single(element => element.Id == sweep);
        check(sweepElement.Width == session.Document.Walls.First().Thickness &&
              ElementGeometry.Parts(sweepElement).Count() == 2 &&
              ElementGeometry.Parts(session.Document.StructuralElements.Single(element => element.Id == reveal)).Count() == 2,
            "A wall sweep and a reveal wrap both faces of their host wall");

        var stair = session.AddStructuralElement(level, StructuralElementKind.Stair, "Stair", new(1, 1), new(1, 5));
        check(session.AddRailingsOnHost(stair) == 2, "A stair receives a guard on each side");
        var guards = session.Document.StructuralElements.Where(element => element.Kind == StructuralElementKind.Railing).ToArray();
        var guardParts = ElementGeometry.Parts(guards[0]).ToArray();
        check(guards.All(guard => guard.Rise == 3.0) && guardParts.Any(part => part.EndTop is not null && part.EndBottom is not null) &&
              guardParts.Max(part => part.Top) > 3.0 && guardParts.Min(part => part.Bottom) < 0.2,
            "A guard on a stair rises with it: level posts and sloped rails");
        session.Document.Validate();
        var guardMesh = Triangles(session.Document);
        check(guardMesh > 0, "Sloped guards mesh");

        var groupA = session.AddStructuralElement(level, StructuralElementKind.ModelLine, "Line", new(10, 0), new(10, 1));
        var groupB = session.AddStructuralElement(level, StructuralElementKind.ModelLine, "Line", new(11, 0), new(11, 1));
        session.AddStructuralGroup(level, new Point2(9, -1), new Point2(12, 2));
        var groupId = session.Document.StructuralElements.Single(element => element.Id == groupA).GroupId!.Value;
        var copied = session.DuplicateStructuralGroup(groupId, 0, 5);
        var copies = session.Document.StructuralElements.Where(element => element.GroupId is not null && element.GroupId != groupId &&
                                                                        element.Kind == StructuralElementKind.ModelLine).ToArray();
        check(copied == 2 && copies.Length == 2 && copies.All(copy => copy.Start.Y >= 5) &&
              session.Document.StructuralElements.Single(element => element.Id == groupB).GroupId == groupId,
            "A model group can be copied as a new group");

        session.AddElementFromPreset(level, ArchitecturalLibrary.ElementPresets.First(preset => preset.Kind == StructuralElementKind.Railing),
            new Point2(0, -3), new Point2(2, -3));
        session.AddStructuralElement(level, StructuralElementKind.AreaBoundary, "B1", new(0, 0), new(8, 0));
        session.AddStructuralElement(level, StructuralElementKind.AreaBoundary, "B2", new(8, 0), new(8, 6));
        var area = session.AddAreaFromBoundaries(level, "Plot");
        check(area is not null && Math.Abs(session.Document.StructuralElements.Single(element => element.Id == area).PlanArea - 48) < 1e-9,
            "An area by boundaries covers the extent of its boundary lines");

        var hole = new DocumentSession(ArchitecturalLibrary.CreateProject("Holes"));
        var hostWall = hole.AddWall(hole.Document.Levels[0].Id, new Point2(0, 0), new Point2(6, 0));
        var voidType = hole.Document.OpeningTypes.First(type => type.Operation == OpeningOperation.Void);
        hole.AddOpening(hostWall, OpeningKind.Window, 3.0, openingTypeId: voidType.Id);
        var withHole = Triangles(hole.Document);
        var solid = new DocumentSession(BuildingDocument.Create("Solid"));
        solid.AddWall(solid.Document.Levels[0].Id, new Point2(0, 0), new Point2(6, 0));
        var framed = new DocumentSession(ArchitecturalLibrary.CreateProject("Framed"));
        var framedWall = framed.AddWall(framed.Document.Levels[0].Id, new Point2(0, 0), new Point2(6, 0));
        framed.AddOpening(framedWall, OpeningKind.Window, 3.0, openingTypeId:
            framed.Document.OpeningTypes.First(type => type.Operation == OpeningOperation.Standard && type.Kind == OpeningKind.Window).Id);
        check(withHole > 0 && withHole < Triangles(framed.Document) && hole.Document.Openings.Count == 1,
            "A wall opening is a plain hole without frame or sash");
        var voidDoor = false;
        try
        {
            var bad = BuildingDocument.Create("Bad");
            bad.OpeningTypes.Add(new OpeningType(Guid.NewGuid(), OpeningKind.Door, "Bad", "Void door", 0.9, 2.1, 0)
            { Operation = OpeningOperation.Void });
            bad.Validate();
        }
        catch (InvalidDataException) { voidDoor = true; }
        check(voidDoor, "Only windows can be plain wall openings");
    }
}
