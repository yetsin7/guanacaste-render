using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Guanacaste3D.Core;

/// <summary>A named slab recipe: a floor, ceiling or roof with its thickness, shape, pitch and finish.</summary>
public sealed record SlabPreset(string Name, FloorKind Kind, double Thickness, RoofShape Shape, double Pitch,
    FinishKind Finish, double Overhang = 0);

/// <summary>A named column section.</summary>
public sealed record ColumnPreset(string Name, ColumnShape Shape, double Width, double Depth);

/// <summary>A named structural or architectural element section, with the style that shapes its geometry.</summary>
public sealed record ElementPreset(string Name, StructuralElementKind Kind, ElementStyle Style, double Width,
    double Height, double Offset, double Depth = 0,
    ResidentialComponentShape ComponentShape = ResidentialComponentShape.Box,
    ResidentialComponentCategory ComponentCategory = ResidentialComponentCategory.None,
    string? LocalizationKey = null);

/// <summary>
/// The standard catalog of architectural types that ships with Guanacaste 3D: wall assemblies, door and window
/// families, slab, roof and ceiling recipes, column sections, and framing, guard, stair and drainage sections.
/// Identifiers derive from the type name, so loading the library twice never duplicates a type.
/// </summary>
public static class ArchitecturalLibrary
{
    /// <summary>A stable identifier derived from a type's category and name.</summary>
    public static Guid StableId(string category, string name)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes($"guanacaste3d:{category}:{name}"));
        return new Guid(hash);
    }

    /// <summary>A new project that already holds the whole standard wall, door and window catalog.</summary>
    public static BuildingDocument CreateProject(string name = "Untitled Project")
    {
        var document = BuildingDocument.Create(name);
        document.WallTypes.AddRange(WallTypes().Where(type => document.WallTypes.All(existing => existing.Id != type.Id)));
        document.OpeningTypes.AddRange(OpeningTypes().Where(type => document.OpeningTypes.All(existing => existing.Id != type.Id)));
        return document;
    }

    /// <summary>Wall assemblies: partitions, masonry, concrete, regional earth walls, foundations and retaining walls.</summary>
    public static IReadOnlyList<WallType> WallTypes() =>
    [
        Wall("Gypsum Partition 75 mm", WallFunction.Interior, 0.075, "Gypsum on steel studs", "#8C959B", WallFillPattern.Solid, WallConstruction.Gypsum),
        Wall("Gypsum Partition 100 mm", WallFunction.Interior, 0.10, "Gypsum both faces on 64 mm steel studs", "#8C959B", WallFillPattern.Solid, WallConstruction.Gypsum),
        Wall("Gypsum Partition 150 mm", WallFunction.Interior, 0.15, "Insulated gypsum on steel studs", "#8C959B", WallFillPattern.Solid, WallConstruction.Gypsum),
        Wall("Moisture-Resistant Gypsum 100 mm", WallFunction.Interior, 0.10, "Green moisture-resistant gypsum for baths", "#7FA49B", WallFillPattern.Solid, WallConstruction.Gypsum),
        Wall("Double-Layer Acoustic Gypsum 160 mm", WallFunction.Interior, 0.16, "Two gypsum layers each face, mineral wool", "#8C959B", WallFillPattern.Diagonal, WallConstruction.Gypsum),
        Wall("Gypsum Shaft Wall 100 mm", WallFunction.Interior, 0.10, "Shaft-liner gypsum on C-H studs", "#A0654F", WallFillPattern.Crosshatch, WallConstruction.Gypsum),
        Wall("Exterior Gypsum Sheathing 150 mm", WallFunction.Exterior, 0.15, "Glass-mat exterior board on steel framing", "#9AA3A8", WallFillPattern.Solid, WallConstruction.Gypsum),
        Wall("Fiber Cement Wall 100 mm", WallFunction.Exterior, 0.10, "Fiber cement board on steel framing", "#A3A39B", WallFillPattern.Solid, WallConstruction.Plaster),
        Wall("Covintec Panel 120 mm", WallFunction.Exterior, 0.12, "Welded-wire panel with polystyrene and mortar", "#9E9A90", WallFillPattern.Diagonal, WallConstruction.Plaster),
        Wall("Fire-Rated Partition 200 mm", WallFunction.Interior, 0.20, "Fire-rated gypsum assembly", "#A0654F", WallFillPattern.Crosshatch, WallConstruction.Gypsum),
        Wall("Wood Stud Partition 140 mm", WallFunction.Interior, 0.14, "Wood studs and board", "#A9835A", WallFillPattern.Diagonal, WallConstruction.Gypsum),
        Wall("Glass Partition 50 mm", WallFunction.Interior, 0.05, "Tempered glass", "#9CC7D6", WallFillPattern.Solid, WallConstruction.Glass),
        Wall("Framed Glass Partition 100 mm", WallFunction.Interior, 0.10, "Tempered glass in aluminium frame (mampara)", "#9CC7D6", WallFillPattern.Solid, WallConstruction.Glass),
        Wall("Glass Storefront 150 mm", WallFunction.Exterior, 0.15, "Laminated glass storefront", "#9CC7D6", WallFillPattern.Solid, WallConstruction.Glass),
        Wall("Concrete Block 120 mm", WallFunction.Interior, 0.12, "Concrete block 12x20x40", "#7D8185", WallFillPattern.Crosshatch, WallConstruction.Block),
        Wall("Concrete Block 150 mm", WallFunction.Interior, 0.15, "Concrete masonry", "#7D8185", WallFillPattern.Crosshatch, WallConstruction.Block),
        Wall("Concrete Block 200 mm", WallFunction.Exterior, 0.20, "Concrete block 20x20x40", "#7D8185", WallFillPattern.Crosshatch, WallConstruction.Block),
        Wall("Concrete Block 250 mm", WallFunction.Exterior, 0.25, "Concrete masonry", "#7D8185", WallFillPattern.Crosshatch, WallConstruction.Block),
        Wall("Rendered Block 150 mm", WallFunction.Exterior, 0.15, "Concrete block, rendered and painted", "#7D8185", WallFillPattern.Crosshatch, WallConstruction.Plaster),
        Wall("Clay Brick 120 mm", WallFunction.Interior, 0.12, "Clay brick", "#A8553C", WallFillPattern.Diagonal, WallConstruction.Brick),
        Wall("Exposed Brick 150 mm", WallFunction.Exterior, 0.15, "Face brick", "#A8553C", WallFillPattern.Diagonal, WallConstruction.Brick),
        Wall("Brick Cavity Wall 300 mm", WallFunction.Exterior, 0.30, "Brick cavity wall", "#A8553C", WallFillPattern.Diagonal, WallConstruction.Brick),
        Wall("Reinforced Concrete 200 mm", WallFunction.Exterior, 0.20, "Cast-in-place concrete", "#6E7378", WallFillPattern.Solid, WallConstruction.Concrete),
        Wall("Reinforced Concrete 300 mm", WallFunction.Exterior, 0.30, "Cast-in-place concrete", "#6E7378", WallFillPattern.Solid, WallConstruction.Concrete),
        Wall("Stone Wall 400 mm", WallFunction.Exterior, 0.40, "Natural stone", "#8E8677", WallFillPattern.Crosshatch, WallConstruction.Stone),
        Wall("Stone Veneer Wall 250 mm", WallFunction.Exterior, 0.25, "Stone veneer on block", "#8E8677", WallFillPattern.Crosshatch, WallConstruction.Stone),
        Wall("Timber Clad Wall 200 mm", WallFunction.Exterior, 0.20, "Timber cladding on studs", "#A9835A", WallFillPattern.Diagonal, WallConstruction.Wood),
        Wall("Bahareque 150 mm", WallFunction.Exterior, 0.15, "Cane and earth (bahareque)", "#B08968", WallFillPattern.Diagonal, WallConstruction.Plaster),
        Wall("Adobe 400 mm", WallFunction.Exterior, 0.40, "Adobe", "#B8895A", WallFillPattern.Solid, WallConstruction.Plaster),
        Wall("Insulated Metal Panel 100 mm", WallFunction.Exterior, 0.10, "Insulated metal panel", "#B7BEC4", WallFillPattern.Solid, WallConstruction.Metal),
        Wall("Parapet 150 mm", WallFunction.Exterior, 0.15, "Masonry parapet", "#7D8185", WallFillPattern.Solid),
        Wall("Foundation Wall 300 mm", WallFunction.Foundation, 0.30, "Reinforced concrete", "#5D6166", WallFillPattern.Solid),
        Wall("Foundation Wall 400 mm", WallFunction.Foundation, 0.40, "Reinforced concrete", "#5D6166", WallFillPattern.Solid),
        Wall("Retaining Wall 300 mm", WallFunction.Retaining, 0.30, "Reinforced concrete", "#5D6166", WallFillPattern.Crosshatch),
        Wall("Retaining Wall 500 mm", WallFunction.Retaining, 0.50, "Reinforced concrete", "#5D6166", WallFillPattern.Crosshatch)
    ];

    private static WallType Wall(string name, WallFunction function, double thickness, string material, string color,
        WallFillPattern pattern, WallConstruction construction = WallConstruction.Plaster) =>
        new(StableId("wall", name), name, function, thickness, material)
        {
            PlanFillColor = color, PlanFillPattern = pattern, SectionFillColor = color, SectionFillPattern = pattern,
            Construction = construction
        };

    /// <summary>Door families (swing, double, sliding, pocket, folding, garage, revolving) and window families.</summary>
    public static IReadOnlyList<OpeningType> OpeningTypes()
    {
        var types = new List<OpeningType>();
        void Door(string family, double width, double height, OpeningOperation operation, double depth = 0.04,
            bool glazed = false, string material = "Timber", DoorLeafStyle style = DoorLeafStyle.Panelled) =>
            types.Add(new OpeningType(StableId("opening", $"{family}/{Size(width, height)}"), OpeningKind.Door,
                family, Size(width, height), width, height, 0)
            { Depth = depth, Operation = operation, GlazedLeaf = glazed, MaterialName = material, LeafStyle = style });
        void Window(string family, double width, double height, double sill, OpeningOperation operation, int lights = 0,
            string material = "Aluminium", bool bars = false) =>
            types.Add(new OpeningType(StableId("opening", $"{family}/{Size(width, height)}"), OpeningKind.Window,
                family, Size(width, height), width, height, sill)
            { Operation = operation, GlazingLightCount = lights, MaterialName = material, SecurityBars = bars });

        foreach (var width in new[] { 0.7, 1.0 }) Door("Single Door", width, 2.1, OpeningOperation.Standard);
        foreach (var width in new[] { 1.4, 1.8, 2.0 }) Door("Double Door", width, 2.1, OpeningOperation.Standard);
        foreach (var width in new[] { 1.5, 1.8 }) Door("French Door", width, 2.1, OpeningOperation.Standard, glazed: true);
        Door("Glazed Entry Door", 1.0, 2.4, OpeningOperation.Standard, glazed: true, material: "Aluminium");
        foreach (var width in new[] { 1.8, 2.4 }) Door("Sliding Door", width, 2.1, OpeningOperation.Sliding, glazed: true, material: "Aluminium");
        Door("Sliding Door", 3.0, 2.4, OpeningOperation.Sliding, glazed: true, material: "Aluminium");
        foreach (var width in new[] { 0.8, 0.9 }) Door("Pocket Door", width, 2.1, OpeningOperation.Pocket);
        foreach (var width in new[] { 1.2, 1.8, 2.4 }) Door("Bi-Fold Door", width, 2.1, OpeningOperation.Folding);
        foreach (var width in new[] { 2.4, 2.7, 4.8 }) Door("Garage Door", width, 2.1, OpeningOperation.Overhead, 0.05, material: "Steel");
        // A tall pivoting timber leaf, flush with the facade, as used for the main entrance of modern houses.
        foreach (var (width, height) in new[] { (1.2, 2.4), (1.6, 2.8) })
            Door("Pivot Door", width, height, OpeningOperation.Standard, 0.06, style: DoorLeafStyle.Flush);
        Door("Revolving Door", 2.0, 2.2, OpeningOperation.Revolving, 0.9, glazed: true, material: "Glass and steel");
        foreach (var width in new[] { 0.7, 0.8, 0.9 }) Door("Flush Door", width, 2.1, OpeningOperation.Standard, style: DoorLeafStyle.Flush);
        foreach (var width in new[] { 0.8, 0.9 }) Door("Six-Panel Door", width, 2.1, OpeningOperation.Standard, style: DoorLeafStyle.SixPanel);
        foreach (var width in new[] { 0.6, 0.8 }) Door("Louvered Door", width, 2.1, OpeningOperation.Standard, style: DoorLeafStyle.Louvered);
        foreach (var width in new[] { 0.9, 1.0 }) Door("Frameless Glass Door", width, 2.1, OpeningOperation.Standard, 0.012, glazed: true,
            material: "Tempered glass", style: DoorLeafStyle.FullGlass);
        foreach (var width in new[] { 0.9, 1.0 }) Door("Security Door", width, 2.1, OpeningOperation.Standard, 0.05, material: "Steel",
            style: DoorLeafStyle.Security);
        Door("Bathroom Door", 0.7, 2.0, OpeningOperation.Standard, style: DoorLeafStyle.Flush);

        foreach (var (width, height, sill) in new[] { (1.8, 1.2, 0.9), (2.4, 1.5, 0.9) })
            Window("Picture Window", width, height, sill, OpeningOperation.Standard, 1);
        foreach (var (width, height, sill) in new[] { (1.2, 1.2, 0.9), (2.0, 1.4, 0.9) })
            Window("Sliding Window", width, height, sill, OpeningOperation.Sliding, 2);
        foreach (var (width, height, lights) in new[] { (0.6, 1.2, 1), (0.9, 1.2, 1), (1.2, 1.2, 2), (1.8, 1.2, 3) })
            Window("Casement Window", width, height, 0.9, OpeningOperation.Casement, lights);
        foreach (var (width, height, sill) in new[] { (0.9, 0.6, 1.8), (1.2, 0.6, 1.5) })
            Window("Awning Window", width, height, sill, OpeningOperation.Awning, 1);
        foreach (var width in new[] { 0.9, 1.2 }) Window("Hung Window", width, 1.5, 0.9, OpeningOperation.Hung, 1);
        foreach (var width in new[] { 0.6, 1.2 }) Window("Louvered Window", width, 1.2, 0.9, OpeningOperation.Louvered, 1, "Glass blades");
        Window("Clerestory Window", 1.8, 0.5, 2.2, OpeningOperation.Standard, 3);
        foreach (var (width, height) in new[] { (1.2, 1.2), (1.5, 1.2), (2.0, 1.4) })
            Window("Sliding Window with Security Bars", width, height, 0.9, OpeningOperation.Sliding, 2, bars: true);
        foreach (var width in new[] { 0.9, 1.2 }) Window("Louvered Window with Security Bars", width, 1.2, 0.9, OpeningOperation.Louvered, 1, "Glass blades", true);
        foreach (var width in new[] { 1.2, 2.4 }) Window("Floor-to-Ceiling Window", width, 2.4, 0.1, OpeningOperation.Standard, width > 2 ? 2 : 1);
        Window("Bathroom Window", 0.6, 0.4, 1.6, OpeningOperation.Louvered, 1, "Frosted glass blades");
        Window("Corner Fixed Window", 0.9, 1.5, 0.6, OpeningOperation.Standard, 1);
        // Plain holes in a wall: a doorway, a pass-through and a large opening.
        Window("Wall Opening", 0.9, 2.1, 0, OpeningOperation.Void, 0, string.Empty);
        Window("Wall Opening", 1.5, 1.2, 0.9, OpeningOperation.Void, 0, string.Empty);
        Window("Wall Opening", 2.4, 2.4, 0, OpeningOperation.Void, 0, string.Empty);
        return types;
    }

    private static string Size(double width, double height) =>
        string.Create(CultureInfo.InvariantCulture, $"{width * 1000:0} × {height * 1000:0} mm");

    /// <summary>Floor, ceiling and roof recipes.</summary>
    public static IReadOnlyList<SlabPreset> SlabPresets { get; } =
    [
        new("Concrete Slab 150 mm", FloorKind.Floor, 0.15, RoofShape.Flat, 0, FinishKind.ExposedConcrete),
        new("Concrete Slab 200 mm", FloorKind.Floor, 0.20, RoofShape.Flat, 0, FinishKind.None),
        new("Ceramic Tile Floor 200 mm", FloorKind.Floor, 0.20, RoofShape.Flat, 0, FinishKind.Ceramic),
        new("Porcelain Tile Floor 200 mm", FloorKind.Floor, 0.20, RoofShape.Flat, 0, FinishKind.Tile),
        new("Timber Joist Floor 250 mm", FloorKind.Floor, 0.25, RoofShape.Flat, 0, FinishKind.None),
        new("Gypsum Ceiling 15 mm", FloorKind.Ceiling, 0.015, RoofShape.Flat, 0, FinishKind.Paint),
        new("Suspended Tile Ceiling 20 mm", FloorKind.Ceiling, 0.02, RoofShape.Flat, 0, FinishKind.None),
        new("Timber Plank Ceiling 20 mm", FloorKind.Ceiling, 0.02, RoofShape.Flat, 0, FinishKind.None),
        new("Flat Concrete Roof 150 mm", FloorKind.Roof, 0.15, RoofShape.Flat, 0, FinishKind.None),
        new("Shed Roof 10°", FloorKind.Roof, 0.10, RoofShape.Shed, 10, FinishKind.None, 0.6),
        new("Gable Roof 20°", FloorKind.Roof, 0.10, RoofShape.Gable, 20, FinishKind.None, 0.6),
        new("Gable Roof 30°", FloorKind.Roof, 0.10, RoofShape.Gable, 30, FinishKind.None, 0.6),
        new("Hip Roof 25°", FloorKind.Roof, 0.10, RoofShape.Hip, 25, FinishKind.None, 0.6),
        new("Hip Roof 35°", FloorKind.Roof, 0.10, RoofShape.Hip, 35, FinishKind.None, 0.6),
        new("Reflecting Pool 300 mm", FloorKind.Water, 0.30, RoofShape.Flat, 0, FinishKind.None),
        new("Toposolid 1 m", FloorKind.Toposolid, 1.0, RoofShape.Flat, 0, FinishKind.None),
        // Site and facade slabs: a white painted volume floor (its soffit reads as plaster), a fair-faced canopy, a road.
        new("Painted Concrete Slab 300 mm", FloorKind.Floor, 0.30, RoofShape.Flat, 0, FinishKind.Paint),
        new("Exposed Concrete Slab 250 mm", FloorKind.Floor, 0.25, RoofShape.Flat, 0, FinishKind.ExposedConcrete),
        new("Asphalt Road 100 mm", FloorKind.Floor, 0.10, RoofShape.Flat, 0, FinishKind.Asphalt),
        new("Lawn 150 mm", FloorKind.Floor, 0.15, RoofShape.Flat, 0, FinishKind.Grass)
    ];

    /// <summary>Column sections.</summary>
    public static IReadOnlyList<ColumnPreset> ColumnPresets { get; } =
    [
        new("Column 300 × 300 mm", ColumnShape.Rectangular, 0.30, 0.30),
        new("Column 400 × 400 mm", ColumnShape.Rectangular, 0.40, 0.40),
        new("Column 300 × 600 mm", ColumnShape.Rectangular, 0.30, 0.60),
        new("Round Column Ø300 mm", ColumnShape.Round, 0.30, 0.30),
        new("Round Column Ø500 mm", ColumnShape.Round, 0.50, 0.50),
        new("Octagonal Column 400 mm", ColumnShape.Octagonal, 0.40, 0.40),
        new("L-Shaped Column 600 mm", ColumnShape.LShaped, 0.60, 0.60),
        new("T-Shaped Column 600 mm", ColumnShape.TShaped, 0.60, 0.60)
    ];

    /// <summary>Beams, guards, stairs, ramps and rainwater drainage.</summary>
    public static IReadOnlyList<ElementPreset> ElementPresets { get; } =
    [
        new("Concrete Beam 250 × 400 mm", StructuralElementKind.Beam, ElementStyle.Default, 0.25, 0.40, -0.40),
        new("Concrete Beam 300 × 500 mm", StructuralElementKind.Beam, ElementStyle.Default, 0.30, 0.50, -0.50),
        new("Steel I-Beam 200 × 400 mm", StructuralElementKind.Beam, ElementStyle.IShape, 0.20, 0.40, -0.40),
        new("Steel Tee 200 × 300 mm", StructuralElementKind.Beam, ElementStyle.Tee, 0.20, 0.30, -0.30),
        new("Steel I-Brace 150 × 200 mm", StructuralElementKind.Brace, ElementStyle.IShape, 0.15, 0.20, -0.20),
        new("Steel Truss 200 × 800 mm", StructuralElementKind.Truss, ElementStyle.IShape, 0.20, 0.80, -0.80),
        new("Round Pier Ø500 mm", StructuralElementKind.IsolatedFoundation, ElementStyle.Round, 0.50, 0.80, -0.80),
        new("Guard with Posts and Rails 1100 mm", StructuralElementKind.Railing, ElementStyle.Default, 0.06, 1.10, 0),
        new("Guard with Balusters 1100 mm", StructuralElementKind.Railing, ElementStyle.Pickets, 0.06, 1.10, 0),
        new("Glass Guard 1100 mm", StructuralElementKind.Railing, ElementStyle.GlassPanel, 0.06, 1.10, 0),
        new("Cable Guard 1100 mm", StructuralElementKind.Railing, ElementStyle.Cable, 0.06, 1.10, 0),
        new("Solid Parapet 1000 mm", StructuralElementKind.Railing, ElementStyle.SolidParapet, 0.15, 1.00, 0),
        new("Straight Stair 1200 mm", StructuralElementKind.Stair, ElementStyle.Default, 1.20, 3.00, 0),
        new("Open Stair 1000 mm", StructuralElementKind.Stair, ElementStyle.Open, 1.00, 3.00, 0),
        new("L-Shaped Stair 1100 mm", StructuralElementKind.Stair, ElementStyle.LShaped, 1.10, 3.00, 0,
            LocalizationKey: "Stair.LShaped"),
        new("U-Shaped Stair 1100 mm", StructuralElementKind.Stair, ElementStyle.UShaped, 1.10, 3.00, 0,
            LocalizationKey: "Stair.UShaped"),
        new("Spiral Stair Ø2000 mm", StructuralElementKind.Stair, ElementStyle.Spiral, 2.00, 3.00, 0,
            LocalizationKey: "Stair.Spiral"),
        new("Ramp 1:12", StructuralElementKind.Ramp, ElementStyle.Default, 1.50, 0.60, 0),
        new("Box Gutter 120 × 100 mm", StructuralElementKind.Gutter, ElementStyle.Default, 0.12, 0.10, 2.85),
        new("Wide Gutter 200 × 120 mm", StructuralElementKind.Gutter, ElementStyle.Default, 0.20, 0.12, 2.85),
        new("Square Downspout 90 mm", StructuralElementKind.Downspout, ElementStyle.Default, 0.09, 2.85, 0),
        new("Round Downspout Ø100 mm", StructuralElementKind.Downspout, ElementStyle.Round, 0.10, 2.85, 0),
        new("Curtain Wall 100 mm", StructuralElementKind.CurtainWall, ElementStyle.Default, 0.10, 3.00, 0),
        // A cobogó: a grid of open masonry cells that screens sun and view but lets light and air through.
        new("Breeze Block Screen 150 mm", StructuralElementKind.CurtainWall, ElementStyle.BreezeBlock, 0.15, 3.00, 0),
        // Site planting: Width is the canopy spread, Height the overall height.
        new("Broadleaf Tree 8 m", StructuralElementKind.Planting, ElementStyle.Default, 6.0, 8.0, 0,
            LocalizationKey: "Planting.Broadleaf"),
        new("Guanacaste Tree 15 m", StructuralElementKind.Planting, ElementStyle.Guanacaste, 22.0, 15.0, 0,
            LocalizationKey: "Planting.Guanacaste"),
        new("Coconut Palm 9 m", StructuralElementKind.Planting, ElementStyle.Palm, 6.0, 9.0, 0,
            LocalizationKey: "Planting.Palm"),
        new("Cypress 12 m", StructuralElementKind.Planting, ElementStyle.Conifer, 4.5, 12.0, 0,
            LocalizationKey: "Planting.Conifer"),
        new("Shrub 1.5 m", StructuralElementKind.Planting, ElementStyle.Shrub, 2.0, 1.5, 0,
            LocalizationKey: "Planting.Shrub"),
        .. ResidentialComponentCatalog.Presets
    ];

    /// <summary>Presets that apply to one kind of element, in catalog order.</summary>
    public static IEnumerable<ElementPreset> PresetsFor(StructuralElementKind kind) =>
        ElementPresets.Where(preset => preset.Kind == kind);
}
