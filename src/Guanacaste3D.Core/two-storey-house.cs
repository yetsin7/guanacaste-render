namespace Guanacaste3D.Core;

/// <summary>Creates an editable residential study using the normal validated authoring commands.</summary>
public static class TwoStoreyHouse
{
    /// <summary>Builds a 12 by 8 meter concept with two levels, a stair opening, roof and coordinated sheets.</summary>
    public static BuildingDocument Create()
    {
        var session = new DocumentSession(ArchitecturalLibrary.CreateProject("Two-storey house study"));
        var ground = session.Document.Levels[0].Id;
        var upper = session.Document.Levels.OrderBy(level => level.Elevation).Skip(1).First().Id;
        var footprint = new Point2[] { new(0, 0), new(12, 0), new(12, 8), new(0, 8) };
        var floor = ArchitecturalLibrary.SlabPresets.Single(preset => preset.Name == "Ceramic Tile Floor 200 mm");
        session.AddSlabFromPreset(ground, footprint, floor);
        session.AddSlabFromPreset(upper, footprint, floor);
        AddWalls(session, ground, false);
        AddWalls(session, upper, true);
        AddBathroom(session, ground);

        var stairPreset = ArchitecturalLibrary.ElementPresets.Single(preset => preset.Name == "Straight Stair 1200 mm");
        var stair = session.AddElementFromPreset(ground, stairPreset, new(2, 1), new(2, 6.04));
        session.AddRailingsOnHost(stair);
        if (session.AddOpening(upper, StructuralElementKind.Opening, "Stair opening", new(1.25, 0.7), new(2.75, 6.4)) is null)
            throw new InvalidOperationException("The stair opening does not fit the upper floor.");

        var roof = ArchitecturalLibrary.SlabPresets.Single(preset => preset.Name == "Gable Roof 30°");
        session.AddSlabFromPreset(upper, footprint, roof, 3.1);
        AddSheet(session, ground, "Ground floor plan");
        AddSheet(session, upper, "Upper floor plan");
        AddSheet(session, null, "Section", BuiltInProjectViews.SectionAId);
        AddSheet(session, null, "North elevation", BuiltInProjectViews.NorthElevationId);
        return session.Document.Clone();
    }

    /// <summary>Places the enclosure, partitions, openings and room annotations for one storey.</summary>
    private static void AddWalls(DocumentSession session, Guid level, bool upper)
    {
        var exterior = BuiltInWallTypes.Exterior300Id;
        var interior = BuiltInWallTypes.Interior100Id;
        var south = session.AddWall(level, new(0, 0), new(12, 0), wallTypeId: exterior);
        var east = session.AddWall(level, new(12, 0), new(12, 8), wallTypeId: exterior);
        var north = session.AddWall(level, new(12, 8), new(0, 8), wallTypeId: exterior);
        var west = session.AddWall(level, new(0, 8), new(0, 0), wallTypeId: exterior);
        var partition = session.AddWall(level, new(7, 0.15), new(7, 7.85), wallTypeId: interior);
        var divider = session.AddWall(level, new(7.05, 4), new(11.85, 4), wallTypeId: interior);
        session.AddOpening(south, upper ? OpeningKind.Window : OpeningKind.Door, 4, width: 1, height: upper ? 1.2 : 2.1, sill: upper ? 1 : 0);
        session.AddOpening(south, OpeningKind.Window, 9.5, width: 1.4, height: 1.2, sill: 1);
        session.AddOpening(north, OpeningKind.Window, 2.5, width: 1.4, height: 1.2, sill: 1);
        session.AddOpening(north, OpeningKind.Window, 8, width: 1.4, height: 1.2, sill: 1);
        session.AddOpening(east, OpeningKind.Window, 2, width: 1.2, height: 1.2, sill: 1);
        session.AddOpening(west, OpeningKind.Window, 3, width: 1.2, height: 1.2, sill: 1);
        session.AddOpening(partition, OpeningKind.Door, 2, width: 0.9, height: 2.1);
        session.AddOpening(partition, OpeningKind.Door, 5.8, width: 0.9, height: 2.1);
        session.AddOpening(divider, OpeningKind.Door, 1.2, width: 0.8, height: 2.1);
        session.AddRoom(level, new(3, 0.2), new(6.9, 7.8), upper ? "Upper hall" : "Living and dining");
        session.AddRoom(level, new(7.1, 0.2), new(11.8, 3.9), upper ? "Bedroom 1" : "Kitchen");
        session.AddRoom(level, new(7.1, 4.1), new(upper ? 11.8 : 9.9, 7.8), upper ? "Bedroom 2" : "Study");
    }

    /// <summary>Adds a ground-floor bathroom and connects the catalog toilet to water and sanitary systems.</summary>
    private static void AddBathroom(DocumentSession session, Guid level)
    {
        var wall = session.AddWall(level, new(10, 4.05), new(10, 7.85), wallTypeId: BuiltInWallTypes.Interior100Id);
        session.AddOpening(wall, OpeningKind.Door, 1.3, width: 0.8, height: 2.1);
        session.AddRoom(level, new(10.1, 4.1), new(11.8, 7.8), "Bathroom");
        var toilet = ResidentialComponentCatalog.Presets.Single(preset => preset.ComponentShape == ResidentialComponentShape.Toilet);
        session.AddElementFromPreset(level, toilet, new(11, 7.3), new(11, 7.3));
        session.AddPlumbingPoint(level, PlumbingPointKind.WaterMeter, "Water meter", new(-2, 0));
        session.AddPlumbingPoint(level, PlumbingPointKind.SewerOutlet, "Sewer outlet", new(14, 8));
        session.ConnectPlumbing((kind, number) => $"{kind} {number}");
    }

    /// <summary>Assigns a coordinated view to a separate sheet using the existing layout.</summary>
    private static void AddSheet(DocumentSession session, Guid? level, string name, Guid? viewId = null)
    {
        var sheetId = session.AddSheet(SheetPaperSize.A3, name);
        var sheet = session.Document.Sheets.Single(item => item.Id == sheetId);
        var view = viewId ?? session.Document.Views.Single(item => item.Kind == ProjectViewKind.Plan && item.LevelId == level).Id;
        session.UpdateSheet(sheet with { Viewports = [sheet.Viewports[0] with { ViewId = view }] });
    }
}
