namespace Guanacaste3D.Core;

/// <summary>
/// A tropical house that uses the architectural catalog end to end: library wall assemblies, sliding, garage,
/// casement, hung, louvered, awning and clerestory openings, a ceramic floor, a gable roof with a skylight and
/// gutters, round and L-shaped columns, a steel pergola, an open stair with a glass guard, a ramp, a curtain wall
/// pavilion, and area and datum annotations. Every element goes through <see cref="DocumentSession"/>.
/// </summary>
public static class ArchitecturalShowcase
{
    private static Guid WallType(string name) =>
        ArchitecturalLibrary.WallTypes().Single(type => type.Name == name).Id;

    private static Guid OpeningTypeId(string family, double width, double height) =>
        ArchitecturalLibrary.OpeningTypes().Single(type => type.FamilyName == family &&
            Math.Abs(type.Width - width) < 1e-9 && Math.Abs(type.Height - height) < 1e-9).Id;

    public static BuildingDocument Create()
    {
        var session = new DocumentSession(ArchitecturalLibrary.CreateProject("Vivienda tropical: catálogo arquitectónico"));
        var level = session.Document.Levels[0].Id;
        var front = WallType("Brick Cavity Wall 300 mm");
        var block = WallType("Concrete Block 250 mm");
        var partition = WallType("Gypsum Partition 150 mm");

        var south = session.AddWall(level, new Point2(0, 0), new Point2(12, 0), height: 3, wallTypeId: front);
        var east = session.AddWall(level, new Point2(12, 0), new Point2(12, 8), height: 3, wallTypeId: block);
        var north = session.AddWall(level, new Point2(12, 8), new Point2(0, 8), height: 3, wallTypeId: block);
        var west = session.AddWall(level, new Point2(0, 8), new Point2(0, 0), height: 3, wallTypeId: block);
        var divider = session.AddWall(level, new Point2(6.5, 0.2), new Point2(6.5, 7.8), height: 3, wallTypeId: partition);

        session.AddOpening(south, OpeningKind.Door, 2.5, openingTypeId: OpeningTypeId("Sliding Door", 3.0, 2.4));
        session.AddOpening(south, OpeningKind.Window, 8.0, openingTypeId: OpeningTypeId("Casement Window", 1.2, 1.2));
        session.AddOpening(south, OpeningKind.Window, 10.5, openingTypeId: OpeningTypeId("Awning Window", 1.2, 0.6));
        session.AddOpening(east, OpeningKind.Window, 2.5, openingTypeId: OpeningTypeId("Hung Window", 0.9, 1.5));
        session.AddOpening(east, OpeningKind.Window, 5.5, openingTypeId: OpeningTypeId("Louvered Window", 1.2, 1.2));
        session.AddOpening(north, OpeningKind.Window, 3.0, openingTypeId: OpeningTypeId("Sliding Window", 2.0, 1.4));
        session.AddOpening(north, OpeningKind.Window, 8.0, openingTypeId: OpeningTypeId("Clerestory Window", 1.8, 0.5));
        session.AddOpening(west, OpeningKind.Door, 4.0, openingTypeId: OpeningTypeId("Garage Door", 2.7, 2.1));
        session.AddOpening(divider, OpeningKind.Door, 3.0, openingTypeId: OpeningTypeId("Pocket Door", 0.9, 2.1));
        session.AddOpening(divider, OpeningKind.Door, 6.0, openingTypeId: OpeningTypeId("Bi-Fold Door", 1.2, 2.1));

        var floorRecipe = ArchitecturalLibrary.SlabPresets.Single(preset => preset.Name == "Ceramic Tile Floor 200 mm");
        session.AddSlabFromPreset(level, [new(-0.15, -0.15), new(12.15, -0.15), new(12.15, 8.15), new(-0.15, 8.15)], floorRecipe);
        var roofRecipe = ArchitecturalLibrary.SlabPresets.Single(preset => preset.Name == "Gable Roof 30°");
        var roof = session.AddSlabFromPreset(level,
            [new(-0.8, -0.8), new(12.8, -0.8), new(12.8, 8.8), new(-0.8, 8.8)], roofRecipe, 3.1);
        session.AddSkylight(level, "Tragaluz", new Point2(2, 1.2), new Point2(3.6, 2.6));

        session.AddRoom(level, new Point2(0.15, 0.15), new Point2(6.4, 7.85), "Sala y cocina");
        session.AddRoom(level, new Point2(6.6, 0.15), new Point2(11.85, 7.85), "Dormitorios");

        // Rainwater: a gutter along each eave and a downspout at each corner.
        foreach (var preset in ArchitecturalLibrary.PresetsFor(StructuralElementKind.Gutter).Take(1))
        {
            session.AddElementFromPreset(level, preset with { Offset = 2.9 }, new Point2(-0.85, -0.85), new Point2(12.85, -0.85));
            session.AddElementFromPreset(level, preset with { Offset = 2.9 }, new Point2(-0.85, 8.85), new Point2(12.85, 8.85));
        }
        var spout = ArchitecturalLibrary.ElementPresets.Single(preset => preset.Name == "Round Downspout Ø100 mm");
        foreach (var corner in new[] { new Point2(-0.85, -0.85), new Point2(12.85, -0.85), new Point2(-0.85, 8.85), new Point2(12.85, 8.85) })
            session.AddElementFromPreset(level, spout with { Height = 2.9 }, corner, corner);

        // A pergola on round columns shades the entrance.
        var round = ArchitecturalLibrary.ColumnPresets.Single(preset => preset.Name == "Round Column Ø300 mm");
        foreach (var x in new[] { 1.0, 5.0, 9.0 })
            session.AddColumnFromPreset(level, new Point2(x, -2.6), round, 3.0);
        var ibeam = ArchitecturalLibrary.ElementPresets.Single(preset => preset.Name == "Steel I-Beam 200 × 400 mm");
        session.AddElementFromPreset(level, ibeam with { Offset = 2.6 }, new Point2(1.0, -2.6), new Point2(9.0, -2.6));
        var lColumn = ArchitecturalLibrary.ColumnPresets.Single(preset => preset.Shape == ColumnShape.LShaped);
        session.AddColumnFromPreset(level, new Point2(12.6, -2.6), lColumn, 3.0);

        // Access: a ramp to the entrance, an open stair with a glass guard, a solid parapet.
        session.AddElementFromPreset(level,
            ArchitecturalLibrary.ElementPresets.Single(preset => preset.Name == "Ramp 1:12") with { Height = 0.3, Width = 1.5 },
            new Point2(3.5, -2.0), new Point2(3.5, -0.2));
        session.AddElementFromPreset(level, ArchitecturalLibrary.ElementPresets.Single(preset => preset.Name == "Open Stair 1000 mm"),
            new Point2(14, 0), new Point2(14, 5));
        session.AddElementFromPreset(level, ArchitecturalLibrary.ElementPresets.Single(preset => preset.Name == "Glass Guard 1100 mm"),
            new Point2(14.6, 0), new Point2(14.6, 5));
        session.AddElementFromPreset(level, ArchitecturalLibrary.ElementPresets.Single(preset => preset.Name == "Solid Parapet 1000 mm"),
            new Point2(0, 10.5), new Point2(6, 10.5));

        // A curtain wall pavilion.
        session.AddElementFromPreset(level, ArchitecturalLibrary.ElementPresets.Single(preset => preset.Name == "Curtain Wall 100 mm"),
            new Point2(8, 10.5), new Point2(13, 10.5));

        session.AddArea(level, "Área construida", new Point2(0, 0), new Point2(12, 8));
        session.AddStructuralElement(level, StructuralElementKind.ReferencePlane, "Eje A", new Point2(-3, 4), new Point2(16, 4));
        session.SetRoofShape(roof, RoofShape.Gable, 30);
        session.AddSheet(SheetPaperSize.A1, "Planta arquitectónica");
        return session.Document.Clone();
    }
}
