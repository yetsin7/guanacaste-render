namespace Guanacaste3D.Core;

/// <summary>Builds representative projects for onboarding, demonstrations, and regression tests.</summary>
public static class SampleProjects
{
    /// <summary>
    /// Creates a one-storey 12 × 8 m house with exterior and interior walls, doors, windows,
    /// an L-shaped slab with a terrace, four rooms, and a numbered sheet, all through
    /// <see cref="DocumentSession"/> so every element passes the same validation as user edits.
    /// </summary>
    public static BuildingDocument CreateDemonstrationHouse()
    {
        var session = new DocumentSession(BuildingDocument.Create("Casa demostrativa Guanacaste"));
        var level = session.Document.Levels[0].Id;
        var exterior = BuiltInWallTypes.Exterior300Id;
        var interior = BuiltInWallTypes.Interior100Id;

        var south = session.AddWall(level, new Point2(0, 0), new Point2(12, 0), height: 3, wallTypeId: exterior);
        var east = session.AddWall(level, new Point2(12, 0), new Point2(12, 8), height: 3, wallTypeId: exterior);
        var north = session.AddWall(level, new Point2(12, 8), new Point2(0, 8), height: 3, wallTypeId: exterior);
        var west = session.AddWall(level, new Point2(0, 8), new Point2(0, 0), height: 3, wallTypeId: exterior);
        var partition = session.AddWall(level, new Point2(7, 0.15), new Point2(7, 7.85), height: 3, wallTypeId: interior);
        var bedroomSplit = session.AddWall(level, new Point2(7.05, 4), new Point2(11.85, 4), height: 3, wallTypeId: interior);

        session.AddOpening(south, OpeningKind.Door, offset: 3.5, width: 1.0, height: 2.2);
        session.AddOpening(south, OpeningKind.Window, offset: 1.4, width: 1.6, sill: 0.9, height: 1.4);
        session.AddOpening(south, OpeningKind.Window, offset: 5.6, width: 1.6, sill: 0.9, height: 1.4);
        session.AddOpening(south, OpeningKind.Window, offset: 9.5, width: 1.4, sill: 1.0, height: 1.2);
        session.AddOpening(east, OpeningKind.Window, offset: 6.0, width: 1.2, sill: 1.0, height: 1.2);
        session.AddOpening(north, OpeningKind.Window, offset: 3.0, width: 1.4, sill: 1.0, height: 1.2);
        session.AddOpening(north, OpeningKind.Window, offset: 8.5, width: 2.4, sill: 0.9, height: 1.4);
        session.AddOpening(west, OpeningKind.Door, offset: 2.0, width: 0.9, height: 2.1);
        session.AddOpening(partition, OpeningKind.Door, offset: 2.0, width: 0.9, height: 2.1);
        session.AddOpening(partition, OpeningKind.Door, offset: 5.8, width: 0.9, height: 2.1);
        session.AddOpening(bedroomSplit, OpeningKind.Door, offset: 1.2, width: 0.8, height: 2.1);

        session.AddFloor(level,
        [
            new(-0.15, -2.5), new(6, -2.5), new(6, -0.15), new(12.15, -0.15),
            new(12.15, 8.15), new(-0.15, 8.15)
        ], 0.2, "Losa de planta baja y terraza");

        session.AddRoom(level, new Point2(0.15, 0.15), new Point2(6.95, 7.85), "Sala-comedor-cocina");
        session.AddRoom(level, new Point2(7.05, 0.15), new Point2(11.85, 3.95), "Dormitorio 1");
        session.AddRoom(level, new Point2(7.05, 4.05), new Point2(11.85, 7.85), "Dormitorio 2");
        session.AddRoom(level, new Point2(0, -2.5), new Point2(6, -0.15), "Terraza");
        session.AddSheet(SheetPaperSize.A1, "Planta arquitectónica");
        return session.Document.Clone();
    }
}
