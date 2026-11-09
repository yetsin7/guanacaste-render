namespace Guanacaste3D.Core;

/// <summary>
/// A two-storey modern villa: a glazed living pavilion on the ground floor, a white concrete upper volume that
/// cantilevers past it on two sides, broad entrance steps down to a reflecting pool, and a planted garden.
/// It is the reference scene for photorealistic rendering (glass, white concrete, water and vegetation) and, like
/// every other sample, it is built only through <see cref="DocumentSession"/>.
/// </summary>
public static class ModernHouseShowcase
{
    private static SlabPreset Slab(string name) => ArchitecturalLibrary.SlabPresets.Single(preset => preset.Name == name);

    private static ElementPreset Element(string name) => ArchitecturalLibrary.ElementPresets.Single(preset => preset.Name == name);

    private static Guid WallType(string name) => ArchitecturalLibrary.WallTypes().Single(type => type.Name == name).Id;

    private static Point2[] Rectangle(double x1, double y1, double x2, double y2) =>
        [new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2)];

    public static BuildingDocument Create()
    {
        var session = new DocumentSession(ArchitecturalLibrary.CreateProject("Villa moderna: vidrio, concreto blanco y agua"));
        var ground = session.Document.Levels.Single(level => level.Role == LevelRole.BaseFloor).Id;
        var upper = session.AddLevel();
        session.UpdateLevel(upper, "Planta alta", 3.2);

        // Rendered (plastered) block reads as white paint in the render; fair-faced concrete would read grey.
        var concrete = WallType("Rendered Block 150 mm");
        var timber = WallType("Timber Clad Wall 200 mm");
        var curtain = Element("Curtain Wall 100 mm");

        // Ground floor: a solid service block on the west, a glazed living pavilion on the south and east.
        var southBlock = session.AddWall(ground, new Point2(0, 0), new Point2(5, 0), height: 3, wallTypeId: concrete);
        session.AddWall(ground, new Point2(0, 8), new Point2(0, 0), height: 3, wallTypeId: concrete);
        session.AddWall(ground, new Point2(14, 8), new Point2(0, 8), height: 3, wallTypeId: concrete);
        session.AddWall(ground, new Point2(14, 6), new Point2(14, 8), height: 3, wallTypeId: concrete);
        session.AddWall(ground, new Point2(5, 0), new Point2(5, 8), height: 3, wallTypeId: concrete);
        session.AddOpening(southBlock, OpeningKind.Door, offset: 2.5, width: 1.1, height: 2.4);
        session.AddElementFromPreset(ground, curtain, new Point2(5, 0), new Point2(14, 0));
        session.AddElementFromPreset(ground, curtain, new Point2(14, 0), new Point2(14, 6));

        session.AddSlabFromPreset(ground, Rectangle(-0.2, -0.2, 14.2, 8.2), Slab("Concrete Slab 200 mm"));
        session.AddSlabFromPreset(ground, Rectangle(5, -6, 20, -0.2), Slab("Porcelain Tile Floor 200 mm"));
        session.AddRoom(ground, new Point2(5.2, 0.2), new Point2(13.8, 7.8), "Sala y comedor");
        session.AddRoom(ground, new Point2(0.2, 0.2), new Point2(4.8, 7.8), "Cocina y servicio");

        // Upper floor: a white volume that overhangs the pavilion by 1.5 m to the south and 3 m to the east.
        session.AddSlabFromPreset(upper, Rectangle(2.8, -1.7, 17.2, 9.2), Slab("Concrete Slab 200 mm"));
        session.AddWall(upper, new Point2(17, 9), new Point2(3, 9), height: 3, wallTypeId: concrete);
        session.AddWall(upper, new Point2(3, 9), new Point2(3, -1.5), height: 3, wallTypeId: concrete);
        session.AddWall(upper, new Point2(17, -1.5), new Point2(17, 9), height: 3, wallTypeId: concrete);
        session.AddWall(upper, new Point2(13, -1.5), new Point2(17, -1.5), height: 3, wallTypeId: concrete);
        // Vertical timber slats between the glazing and the solid end, as in the reference photograph.
        session.AddWall(upper, new Point2(10.5, -1.5), new Point2(13, -1.5), height: 3, wallTypeId: timber);
        session.AddElementFromPreset(upper, curtain, new Point2(3, -1.5), new Point2(10.5, -1.5));
        session.AddSlabFromPreset(upper, Rectangle(2.5, -2.0, 17.5, 9.5), Slab("Flat Concrete Roof 150 mm"), 3.15);
        session.AddElementFromPreset(upper, Element("Glass Guard 1100 mm"), new Point2(3, -1.6), new Point2(10.5, -1.6));
        session.AddRoom(upper, new Point2(3.2, -1.3), new Point2(16.8, 8.8), "Dormitorio principal y estudio");

        // Stairs: one inside the service block, and wide entrance steps toward the pool.
        session.AddElementFromPreset(ground, Element("Straight Stair 1200 mm") with { Height = 3.2 },
            new Point2(1.0, 1.0), new Point2(1.0, 6.0));
        session.AddElementFromPreset(ground, Element("Straight Stair 1200 mm") with { Width = 5.0, Height = 0.6 },
            new Point2(10, -6.0), new Point2(10, -9.5));

        // The reflecting pool, with its surface 15 cm under the surrounding paving so the water reads as recessed.
        session.AddSlabFromPreset(ground, Rectangle(-3, -16, 12, -9.6), Slab("Reflecting Pool 300 mm"), -0.15);
        var paving = Slab("Concrete Slab 150 mm");
        session.AddSlabFromPreset(ground, Rectangle(-7, -20, -3, -6), paving);
        session.AddSlabFromPreset(ground, Rectangle(12, -20, 20, -6), paving);
        session.AddSlabFromPreset(ground, Rectangle(-3, -20, 12, -16), paving);

        // Garden: broadleaf trees around the house, a cypress, and shrubs along the pool.
        var tree = Element("Broadleaf Tree 8 m");
        foreach (var point in new Point2[] { new(-9, 6), new(-12, -6), new(26, -6), new(25, 12) })
            session.AddElementFromPreset(ground, tree, point, point);
        var cypress = Element("Cypress 12 m");
        foreach (var point in new Point2[] { new(-14, 12), new(30, 2) })
            session.AddElementFromPreset(ground, cypress, point, point);
        var shrub = Element("Shrub 1.5 m") with { Width = 1.3, Height = 0.9 };
        for (var step = 0; step < 5; step++)
        {
            var point = new Point2(-2.2, 1.0 + step * 1.6);
            session.AddElementFromPreset(ground, shrub, point, point);
        }

        session.AddArea(ground, "Área construida planta baja", new Point2(0, 0), new Point2(14, 8));
        session.AddSheet(SheetPaperSize.A1, "Planta baja");
        return session.Document.Clone();
    }
}
