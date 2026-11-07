namespace Guanacaste3D.Core;

/// <summary>
/// A ten-storey residential tower built only through the validated authoring commands, as a designer would: the
/// structure and the architecture first (<see cref="CreateArchitecture"/>), then every building service the audits ask
/// for (<see cref="Create"/>). It is 32 × 16 m with a 3 m storey: a ground floor with the lobby, two shops and the pump
/// and electrical rooms, nine typical floors of six apartments each, and a flat roof with a parapet and solar arrays.
/// A central core holds the elevator and a services shaft; two U-shaped exit stairs close the ends of the corridor.
/// </summary>
public static class TenStoreyBuilding
{
    public const int Storeys = 10;
    public const double StoreyHeight = 3.0;
    public const double Length = 32;
    public const double Depth = 16;

    private static readonly Point2[] Footprint = [new(0, 0), new(Length, 0), new(Length, Depth), new(0, Depth)];

    /// <summary>The finished tower: architecture, structure, electrical, plumbing and fire protection, coordinated.</summary>
    public static BuildingDocument Create()
    {
        var session = new DocumentSession(CreateArchitecture());
        AddServices(session);
        return session.Document.Clone();
    }

    /// <summary>
    /// Structure and architecture only: levels, foundations, columns, beams, slabs, walls, doors, windows, rooms,
    /// stairs, the elevator, the roof and the drawing sheets. This is the model before its building services.
    /// </summary>
    public static BuildingDocument CreateArchitecture()
    {
        var session = new DocumentSession(ArchitecturalLibrary.CreateProject("Torre residencial de 10 pisos"));
        var levels = AddLevels(session);
        var floor = ArchitecturalLibrary.SlabPresets.Single(preset => preset.Name == "Ceramic Tile Floor 200 mm");

        // The typical floor is modelled once on the second storey and copied to the eight above it, as Revit's
        // "paste aligned to selected levels"; the ground floor and the roof are their own.
        AddGroundFloor(session, levels[0], floor);
        AddTypicalFloor(session, levels[1], floor);
        session.CopyLevelContent(levels[1], levels[2..Storeys]);

        // Exit stairs rise storey by storey from the ground to the top floor, the elevator serves every storey.
        var stair = ArchitecturalLibrary.ElementPresets.Single(preset => preset.Name == "U-Shaped Stair 1100 mm");
        for (var index = 0; index < Storeys - 1; index++)
        {
            var west = session.AddElementFromPreset(levels[index], stair, new(2.05, 7.0), new(2.05, 10.62));
            var east = session.AddElementFromPreset(levels[index], stair, new(31.15, 7.0), new(31.15, 10.62));
            session.AddRailingsOnHost(west);
            session.AddRailingsOnHost(east);
        }
        session.AddElevator(levels[0], levels[Storeys - 1], new(14.9, 9.2), new(17.1, 11.6), "Ascensor 1");
        if (session.AddOpening(levels[0], StructuralElementKind.Shaft, "Ducto de instalaciones", new(17.1, 9.2), new(18.3, 10.6)) is null)
            throw new InvalidDataException("The services shaft does not fit the floors.");

        AddRoof(session, levels[Storeys]);
        AddSheets(session, levels);
        return session.Document.Clone();
    }

    /// <summary>
    /// Adds what the architecture lacks, in the order the audits reveal it: each apartment's kitchen, bathroom and
    /// furniture, its panel, circuits, lights, switches, receptacles and air conditioning; the water service with a
    /// cistern and its pump, the sewer and storm outlets and every pipe; the main electrical service; and on every
    /// storey the fire protection the life-safety rules require.
    /// </summary>
    public static void AddServices(DocumentSession session)
    {
        var levels = session.Document.Levels.OrderBy(level => level.Elevation).Select(level => level.Id).ToArray();
        // The apartments' fit-out is modelled on the second storey and that selection is copied up, so the copies
        // arrive already wired; the ground floor has its own services.
        var commonLighting = AddGroundServices(session, levels[0]);
        var before = Ids(session.Document);
        ApartmentFitOut.Add(session, levels[1], commonLighting);
        var fitOut = Ids(session.Document);
        fitOut.ExceptWith(before);
        session.CopyLevelContent(levels[1], levels[2..Storeys], fitOut);

        var cistern = session.Document.PlumbingPoints.Single(point => point.Kind == PlumbingPointKind.Cistern).Id;
        // Nine storeys of 3 m above the pump: 27 m of static head (265 kPa) plus 150 kPa at the highest fixture.
        session.SetPump(cistern, 3, 550);
        session.ConnectPlumbing((kind, number) => $"{PlumbingName(kind)} {number}", number => $"Ventilación {number}",
            number => $"Bajante pluvial {number}");

        for (var index = 0; index < Storeys; index++)
            session.AddFireProtection(levels[index], (kind, number) => $"{FireName(kind)} {number}");

        var solar = session.AddElectricalDevice(levels[Storeys], ElectricalDeviceKind.SolarArray, "Arreglo FV 1", new(10, 8));
        var solar2 = session.AddElectricalDevice(levels[Storeys], ElectricalDeviceKind.SolarArray, "Arreglo FV 2", new(22, 8));
        session.SetPanelCount(solar, 20);
        session.SetPanelCount(solar2, 20);
    }

    /// <summary>Ground floor, the roof, and nine more: the levels from the ground up, the roof level last.</summary>
    private static Guid[] AddLevels(DocumentSession session)
    {
        var ground = session.Document.Levels.Single(level => level.Role == LevelRole.BaseFloor).Id;
        var roof = session.Document.Levels.Single(level => level.Role == LevelRole.BaseRoof).Id;
        session.UpdateLevel(ground, "Planta baja", 0);
        session.UpdateLevel(roof, "Azotea", Storeys * StoreyHeight);
        var levels = new List<Guid> { ground };
        for (var storey = 2; storey <= Storeys; storey++)
        {
            var id = session.AddLevel();
            session.UpdateLevel(id, $"Piso {storey}", (storey - 1) * StoreyHeight);
            levels.Add(id);
        }
        levels.Add(roof);
        return [.. levels];
    }

    /// <summary>The slab, frame and core every storey shares: columns, beams, the core and stair walls.</summary>
    private static CoreWalls AddStructureAndCore(DocumentSession session, Guid level, SlabPreset floor, bool ground)
    {
        session.AddSlabFromPreset(level, Footprint, floor);
        foreach (var x in new[] { 0.25, 8, 16, 24, 31.75 })
            foreach (var y in new[] { 0.25, 6.8, 15.75 })
            {
                session.AddColumn(level, new(x, y), 0.50, 0.50, StoreyHeight);
                if (ground) session.AddStructuralElement(level, StructuralElementKind.IsolatedFoundation, "Zapata", new(x, y), new(x, y));
            }
        if (!ground)
        {
            var beam = ArchitecturalLibrary.ElementPresets.Single(preset => preset.Name == "Concrete Beam 300 × 500 mm");
            foreach (var y in new[] { 0.15, 6.8, 9.2, 15.85 }) session.AddElementFromPreset(level, beam, new(0.15, y), new(31.85, y));
            foreach (var x in new[] { 0.15, 8, 16, 24, 31.85 }) session.AddElementFromPreset(level, beam, new(x, 0.15), new(x, 15.85));
            // The stair wells are open to the storey below.
            Hole(session, level, new(0.2, 6.9), new(2.8, 10.9));
            Hole(session, level, new(29.2, 6.9), new(31.8, 10.9));
        }

        var exterior = BuiltInWallTypes.Exterior300Id;
        var interior = BuiltInWallTypes.Interior100Id;
        var south = session.AddWall(level, new(0, 0), new(Length, 0), wallTypeId: exterior);
        var east = session.AddWall(level, new(Length, 0), new(Length, Depth), wallTypeId: exterior);
        var north = session.AddWall(level, new(Length, Depth), new(0, Depth), wallTypeId: exterior);
        var west = session.AddWall(level, new(0, Depth), new(0, 0), wallTypeId: exterior);

        // Corridor, stair wells with their doors, the elevator hoistway and the services shaft.
        var corridorSouth = session.AddWall(level, new(0.15, 6.8), new(31.85, 6.8), wallTypeId: interior);
        var corridorWest = session.AddWall(level, new(3.0, 9.2), new(14.9, 9.2), wallTypeId: interior);
        var corridorEast = session.AddWall(level, new(17.1, 9.2), new(29.0, 9.2), wallTypeId: interior);
        var westStair = session.AddWall(level, new(3.0, 6.8), new(3.0, 11.0), wallTypeId: interior);
        session.AddWall(level, new(0.15, 11.0), new(3.0, 11.0), wallTypeId: interior);
        var eastStair = session.AddWall(level, new(29.0, 6.8), new(29.0, 11.0), wallTypeId: interior);
        session.AddWall(level, new(29.0, 11.0), new(31.85, 11.0), wallTypeId: interior);
        session.AddWall(level, new(14.9, 9.2), new(14.9, 11.6), wallTypeId: interior);
        session.AddWall(level, new(17.1, 9.2), new(17.1, 11.6), wallTypeId: interior);
        session.AddWall(level, new(14.9, 11.6), new(17.1, 11.6), wallTypeId: interior);
        session.AddWall(level, new(18.3, 9.2), new(18.3, 10.6), wallTypeId: interior);
        session.AddWall(level, new(17.1, 10.6), new(18.3, 10.6), wallTypeId: interior);
        session.AddOpening(westStair, OpeningKind.Door, 0.8, width: 0.9, height: 2.1);
        session.AddOpening(eastStair, OpeningKind.Door, 0.8, width: 0.9, height: 2.1);
        session.AddRoom(level, new(3.0, 6.8), new(29.0, 9.2), ground ? "Vestíbulo" : "Pasillo");
        session.AddRoom(level, new(0.15, 6.8), new(3.0, 11.0), "Escalera oeste");
        session.AddRoom(level, new(29.0, 6.8), new(31.85, 11.0), "Escalera este");
        return new(south, east, north, west, corridorSouth, corridorWest, corridorEast);
    }

    /// <summary>The walls of the shared frame that the storeys hang their doors and windows on.</summary>
    private sealed record CoreWalls(Guid South, Guid East, Guid North, Guid West, Guid CorridorSouth, Guid CorridorWest,
        Guid CorridorEast);

    /// <summary>Lobby with the main entrance, two shops on the street front, reception, and the service rooms.</summary>
    private static void AddGroundFloor(DocumentSession session, Guid level, SlabPreset floor)
    {
        var core = AddStructureAndCore(session, level, floor, ground: true);
        var (south, north, corridorSouth) = (core.South, core.North, core.CorridorSouth);
        var interior = BuiltInWallTypes.Interior100Id;

        // The street front: glazed shops either side of the entrance hall that leads to the corridor.
        var hallWest = session.AddWall(level, new(13.5, 0.15), new(13.5, 6.8), wallTypeId: interior);
        var hallEast = session.AddWall(level, new(18.5, 0.15), new(18.5, 6.8), wallTypeId: interior);
        session.AddOpening(south, OpeningKind.Door, 16, width: 1.8, height: 2.4);
        foreach (var x in new[] { 3.5, 9.5, 22.5, 28.5 }) session.AddOpening(south, OpeningKind.Window, x, width: 3.0, height: 2.2, sill: 0.4);
        session.AddOpening(hallWest, OpeningKind.Door, 3.0, width: 0.9, height: 2.1);
        session.AddOpening(hallEast, OpeningKind.Door, 3.0, width: 0.9, height: 2.1);
        session.AddOpening(corridorSouth, OpeningKind.Door, 15.85, width: 1.6, height: 2.4);
        session.AddRoom(level, new(0.15, 0.15), new(13.5, 6.8), "Local comercial 1");
        session.AddRoom(level, new(13.5, 0.15), new(18.5, 6.8), "Recepción");
        session.AddRoom(level, new(18.5, 0.15), new(31.85, 6.8), "Local comercial 2");

        // North of the corridor: the pump room and the main electrical room either side of the core, and the
        // management office.
        var pumpRoom = session.AddWall(level, new(9.0, 9.2), new(9.0, 15.85), wallTypeId: interior);
        var electricalRoom = session.AddWall(level, new(22.5, 9.2), new(22.5, 15.85), wallTypeId: interior);
        session.AddWall(level, new(3.0, 11.0), new(3.0, 15.85), wallTypeId: interior);
        session.AddWall(level, new(29.0, 11.0), new(29.0, 15.85), wallTypeId: interior);
        session.AddOpening(core.CorridorWest, OpeningKind.Door, 3.0, width: 1.0, height: 2.1);
        session.AddOpening(core.CorridorWest, OpeningKind.Door, 9.0, width: 0.9, height: 2.1);
        session.AddOpening(core.CorridorEast, OpeningKind.Door, 8.0, width: 1.0, height: 2.1);
        foreach (var x in new[] { 6, 26 }) session.AddOpening(north, OpeningKind.Window, x, width: 1.2, height: 1.0, sill: 1.4);
        session.AddRoom(level, new(3.0, 9.2), new(9.0, 15.85), "Cuarto de bombas");
        session.AddRoom(level, new(9.0, 11.6), new(22.5, 15.85), "Administración");
        session.AddRoom(level, new(22.5, 9.2), new(29.0, 15.85), "Cuarto eléctrico");
        session.AddRoom(level, new(0.15, 11.0), new(3.0, 15.85), "Bodega 1");
        session.AddRoom(level, new(29.0, 11.0), new(31.85, 15.85), "Bodega 2");
        _ = (pumpRoom, electricalRoom);
    }

    /// <summary>Six apartments around the corridor: four of one bedroom on the south front, two of two bedrooms north.</summary>
    private static void AddTypicalFloor(DocumentSession session, Guid level, SlabPreset floor)
    {
        var core = AddStructureAndCore(session, level, floor, ground: false);
        var (south, north, east, west) = (core.South, core.North, core.East, core.West);
        var (corridorSouth, corridorWest, corridorEast) = (core.CorridorSouth, core.CorridorWest, core.CorridorEast);
        var interior = BuiltInWallTypes.Interior100Id;

        // South: four one-bedroom apartments of 8 × 6.8 m, each with its bedroom and bathroom on the west side.
        for (var unit = 0; unit < 4; unit++)
        {
            var a = unit * 8.0;
            if (unit > 0) session.AddWall(level, new(a, 0.15), new(a, 6.8), wallTypeId: interior);
            var split = session.AddWall(level, new(a + 3.6, 0.15), new(a + 3.6, 6.8), wallTypeId: interior);
            session.AddWall(level, new(a + 0.15, 4.2), new(a + 3.6, 4.2), wallTypeId: interior);
            session.AddOpening(split, OpeningKind.Door, 2.2, width: 0.8, height: 2.1);
            session.AddOpening(split, OpeningKind.Door, 5.4, width: 0.7, height: 2.1);
            session.AddOpening(corridorSouth, OpeningKind.Door, a + 5.8 - 0.15, width: 0.9, height: 2.1);
            session.AddOpening(south, OpeningKind.Window, a + 1.9, width: 1.6, height: 1.4, sill: 0.9);
            session.AddOpening(south, OpeningKind.Window, a + 5.8, width: 2.4, height: 1.8, sill: 0.6);
            var number = unit + 1;
            session.AddRoom(level, new(a + 0.15, 0.15), new(a + 3.6, 4.2), $"Dormitorio {number}A");
            session.AddRoom(level, new(a + 0.15, 4.2), new(a + 3.6, 6.8), $"Baño {number}A");
            session.AddRoom(level, new(a + 3.6, 0.15), new(a + 8, 6.8), $"Sala y cocina {number}A");
        }
        foreach (var y in new[] { 2.0 }) { session.AddOpening(west, OpeningKind.Window, Depth - y, width: 1.2, height: 1.2, sill: 1.0); }
        session.AddOpening(east, OpeningKind.Window, 2.0, width: 1.2, height: 1.2, sill: 1.0);

        // North: two two-bedroom apartments, mirror images either side of the core.
        var westBedroom2 = session.AddWall(level, new(3.0, 11.0), new(3.0, 15.85), wallTypeId: interior);
        var eastBedroom2 = session.AddWall(level, new(29.0, 11.0), new(29.0, 15.85), wallTypeId: interior);
        var westSplit = session.AddWall(level, new(9.5, 9.2), new(9.5, 15.85), wallTypeId: interior);
        var eastSplit = session.AddWall(level, new(22.5, 9.2), new(22.5, 15.85), wallTypeId: interior);
        session.AddWall(level, new(9.5, 11.6), new(14.9, 11.6), wallTypeId: interior);
        session.AddWall(level, new(17.1, 11.6), new(22.5, 11.6), wallTypeId: interior);
        session.AddWall(level, new(16.0, 11.6), new(16.0, 15.85), wallTypeId: interior);
        session.AddOpening(corridorWest, OpeningKind.Door, 3.0, width: 0.9, height: 2.1);
        session.AddOpening(corridorEast, OpeningKind.Door, 9.0, width: 0.9, height: 2.1);
        session.AddOpening(westSplit, OpeningKind.Door, 1.2, width: 0.7, height: 2.1);
        session.AddOpening(westSplit, OpeningKind.Door, 4.6, width: 0.8, height: 2.1);
        session.AddOpening(eastSplit, OpeningKind.Door, 1.2, width: 0.7, height: 2.1);
        session.AddOpening(eastSplit, OpeningKind.Door, 4.6, width: 0.8, height: 2.1);
        session.AddOpening(westBedroom2, OpeningKind.Door, 1.0, width: 0.8, height: 2.1);
        session.AddOpening(eastBedroom2, OpeningKind.Door, 1.0, width: 0.8, height: 2.1);
        // The north wall runs east to west.
        foreach (var x in new[] { 1.5, 6.25, 12.75, 19.25, 25.75, 30.5 })
            session.AddOpening(north, OpeningKind.Window, Length - x, width: x is 1.5 or 30.5 ? 1.2 : 2.0, height: 1.4, sill: 0.9);
        session.AddRoom(level, new(3.0, 9.2), new(9.5, 15.85), "Sala y cocina 5B");
        session.AddRoom(level, new(9.5, 11.6), new(16.0, 15.85), "Dormitorio 5B");
        session.AddRoom(level, new(9.5, 9.2), new(14.9, 11.6), "Baño 5B");
        session.AddRoom(level, new(0.15, 11.0), new(3.0, 15.85), "Dormitorio 5B-2");
        session.AddRoom(level, new(22.5, 9.2), new(29.0, 15.85), "Sala y cocina 6B");
        session.AddRoom(level, new(16.0, 11.6), new(22.5, 15.85), "Dormitorio 6B");
        session.AddRoom(level, new(18.3, 9.2), new(22.5, 11.6), "Baño 6B");
        session.AddRoom(level, new(29.0, 11.0), new(31.85, 15.85), "Dormitorio 6B-2");
    }

    /// <summary>A flat roof with a parapet all round, the elevator's machine housing and a roof hatch.</summary>
    private static void AddRoof(DocumentSession session, Guid roof)
    {
        var slab = ArchitecturalLibrary.SlabPresets.FirstOrDefault(preset => preset.Kind == FloorKind.Roof && preset.Shape == RoofShape.Flat)
            ?? ArchitecturalLibrary.SlabPresets.First(preset => preset.Kind == FloorKind.Floor);
        session.AddSlabFromPreset(roof, Footprint, slab);
        var parapet = ArchitecturalLibrary.ElementPresets.Single(preset => preset.Name == "Solid Parapet 1000 mm");
        for (var index = 0; index < 4; index++)
            session.AddElementFromPreset(roof, parapet, Footprint[index], Footprint[(index + 1) % 4]);
        // The overrun of the elevator rises above the roof: a block-walled housing around its hoistway.
        foreach (var (from, to) in new (Point2, Point2)[]
                 {
                     (new(14.8, 9.1), new(17.2, 9.1)), (new(17.2, 9.1), new(17.2, 11.7)),
                     (new(17.2, 11.7), new(14.8, 11.7)), (new(14.8, 11.7), new(14.8, 9.1))
                 })
            session.AddWall(roof, from, to, 0.20, 3.2);
    }

    /// <summary>Creates a stairwell opening and rejects an invalid floor outline.</summary>
    private static void Hole(DocumentSession session, Guid level, Point2 from, Point2 to)
    {
        if (session.AddOpening(level, StructuralElementKind.Opening, "Hueco de escalera", from, to) is null)
            throw new InvalidDataException("The stair opening does not fit the floor.");
    }

    /// <summary>
    /// The plans of the ground and typical floors and of the roof, a section and an elevation, each on its own sheet.
    /// </summary>
    private static void AddSheets(DocumentSession session, IReadOnlyList<Guid> levels)
    {
        AddSheet(session, levels[0], "Planta baja");
        AddSheet(session, levels[1], "Planta tipo (pisos 2 a 10)");
        AddSheet(session, levels[Storeys], "Planta de azotea");
        AddSheet(session, null, "Corte A", BuiltInProjectViews.SectionAId);
        AddSheet(session, null, "Elevación norte", BuiltInProjectViews.NorthElevationId);
    }

    /// <summary>Places the selected plan or coordinated view on its own drawing sheet.</summary>
    private static void AddSheet(DocumentSession session, Guid? level, string name, Guid? viewId = null)
    {
        var sheetId = session.AddSheet(SheetPaperSize.A1, name);
        var sheet = session.Document.Sheets.Single(item => item.Id == sheetId);
        var view = viewId ?? session.Document.Views.First(item => item.Kind == ProjectViewKind.Plan && item.LevelId == level).Id;
        session.UpdateSheet(sheet with { Viewports = [sheet.Viewports[0] with { ViewId = view }] });
    }

    /// <summary>Captures component and device identities to isolate a newly added fit-out for copying.</summary>
    private static HashSet<Guid> Ids(BuildingDocument document) =>
    [
        .. document.StructuralElements.Select(element => element.Id), .. document.ElectricalDevices.Select(device => device.Id)
    ];

    /// <summary>The cistern and its pump, the meter, sewer and storm outlets, and the main panel of the common areas.</summary>
    private static Guid AddGroundServices(DocumentSession session, Guid level)
    {
        session.AddPlumbingPoint(level, PlumbingPointKind.WaterMeter, "Medidor AyA", new(16, -1.0));
        session.AddPlumbingPoint(level, PlumbingPointKind.Cistern, "Cisterna 30 m³", new(5.0, 13.0), "Bomba hidroneumática");
        session.AddPlumbingPoint(level, PlumbingPointKind.SewerOutlet, "Salida a colector", new(20, -1.0));
        session.AddPlumbingPoint(level, PlumbingPointKind.StormOutlet, "Salida pluvial", new(26, -1.0));
        return ApartmentFitOut.AddCommonAreas(session, level);
    }

    /// <summary>Provides the study's labels for generated plumbing systems.</summary>
    private static string PlumbingName(PlumbingSystemKind kind) => kind switch
    {
        PlumbingSystemKind.ColdWater => "Agua fría",
        PlumbingSystemKind.HotWater => "Agua caliente",
        PlumbingSystemKind.Sanitary => "Sanitaria",
        _ => "Pluvial"
    };

    /// <summary>Provides the study's labels for generated fire-protection devices.</summary>
    public static string FireName(ElectricalDeviceKind kind) => kind switch
    {
        ElectricalDeviceKind.Sprinkler => "Rociador",
        ElectricalDeviceKind.SmokeDetector => "Detector de humo",
        ElectricalDeviceKind.ExitSign => "Rótulo de salida",
        ElectricalDeviceKind.FireAlarmPullStation => "Estación manual",
        ElectricalDeviceKind.EmergencyLight => "Luz de emergencia",
        ElectricalDeviceKind.FireExtinguisher => "Extintor",
        ElectricalDeviceKind.FireHoseCabinet => "Gabinete contra incendio",
        ElectricalDeviceKind.HornStrobe => "Sirena con luz estroboscópica",
        _ => kind.ToString()
    };
}
