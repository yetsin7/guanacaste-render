using System.Text.Json.Serialization;

namespace Guanacaste3D.Core;

/// <summary>Classifies a wall type by its primary building role.</summary>
public enum WallFunction
{
    Interior,
    Exterior,
    Foundation,
    Retaining
}

/// <summary>What a wall is built of; it sets the material of its faces in the 3D views and the render.</summary>
public enum WallConstruction
{
    /// <summary>Rendered masonry: plaster on both faces.</summary>
    Plaster,
    /// <summary>Gypsum board on steel or wood studs: smooth paint on both faces.</summary>
    Gypsum,
    /// <summary>A glass partition: tempered panes in aluminium channels with mullions.</summary>
    Glass,
    /// <summary>Exposed clay brick outside, plaster inside.</summary>
    Brick,
    /// <summary>Exposed concrete block outside, plaster inside.</summary>
    Block,
    /// <summary>Exposed (fair-faced) concrete outside, plaster inside.</summary>
    Concrete,
    /// <summary>Natural stone outside, plaster inside.</summary>
    Stone,
    /// <summary>Timber cladding outside.</summary>
    Wood,
    /// <summary>Metal panel outside.</summary>
    Metal
}

/// <summary>Defines the drafting pattern used to represent a wall in a coordinated view.</summary>
public enum WallFillPattern
{
    Solid,
    Diagonal,
    Crosshatch
}

/// <summary>Defines reusable parameters shared by wall instances.</summary>
public sealed record WallType(
    Guid Id,
    string Name,
    WallFunction Function,
    double Thickness,
    string MaterialName)
{
    /// <summary>Hexadecimal RGB color used for the wall body in plan views.</summary>
    public string PlanFillColor { get; init; } = "#59636B";

    /// <summary>Drafting pattern used for the wall body in plan views.</summary>
    public WallFillPattern PlanFillPattern { get; init; } = WallFillPattern.Solid;

    /// <summary>Hexadecimal RGB color used for cut wall material in section views.</summary>
    public string SectionFillColor { get; init; } = "#87928B";

    /// <summary>Drafting pattern used for cut wall material in section views.</summary>
    public WallFillPattern SectionFillPattern { get; init; } = WallFillPattern.Diagonal;

    /// <summary>What the wall is built of: it sets the material of its faces in 3D and in the render.</summary>
    public WallConstruction Construction { get; init; }

    /// <summary>Finish thickness on each wall face in metres; zero means exposed substrate.</summary>
    public double LeftRepelloThickness { get; init; }
    public double LeftFineThickness { get; init; }
    public double RightRepelloThickness { get; init; }
    public double RightFineThickness { get; init; }

    /// <summary>Gypsum stud spacing in metres (16 or 24 inches, or custom).</summary>
    public double GypsumStudSpacing { get; init; } = WallAssemblyPlanRules.Stud16Inches;
}

/// <summary>Provides stable identifiers and defaults for built-in wall types.</summary>
public static class BuiltInWallTypes
{
    public static readonly Guid Interior100Id = Guid.Parse("ea403711-f44c-4cab-bf9c-f96ddb578659");
    public static readonly Guid Basic200Id = Guid.Parse("4ae439fa-5c6b-41f1-ae56-9cf417e567ad");
    public static readonly Guid Exterior300Id = Guid.Parse("a4fba685-e21b-4694-b8bc-e67dd513b996");

    /// <summary>Creates an independent default wall-type catalog for a new document.</summary>
    public static List<WallType> CreateDefaults() =>
    [
        // Keep the historical default face materials for existing projects and render regressions.
        // Users can explicitly select Gypsum/Block/Brick in the wall type editor.
        new(Interior100Id, "Interior 100 mm", WallFunction.Interior, 0.10, "Gypsum assembly"),
        new(Basic200Id, "Basic 200 mm", WallFunction.Interior, 0.20, "Concrete masonry"),
        new(Exterior300Id, "Exterior 300 mm", WallFunction.Exterior, 0.30, "Insulated masonry")
    ];
}

/// <summary>Represents a rectangular room annotation and its calculated area.</summary>
public sealed record Room(
    Guid Id,
    Guid LevelId,
    string Number,
    string Name,
    Point2 FirstCorner,
    Point2 SecondCorner)
{
    /// <summary>
    /// True only for rooms discovered from enclosure geometry. Hand-placed rooms and existing
    /// legacy room records remain independent of automatic boundary synchronization.
    /// </summary>
    public bool IsAutomatic { get; init; }

    /// <summary>
    /// Optional actual footprint in project coordinates. Null preserves the legacy rectangle;
    /// a concave orthogonal room stores a polygon instead of its bounding-box area.
    /// </summary>
    public List<Point2>? Boundary { get; init; }

    /// <summary>Interior rings excluded from the footprint (e.g. an unroofed courtyard).</summary>
    public List<List<Point2>>? Holes { get; init; }

    [JsonIgnore]
    public double Area => Boundary is { Count: >= 3 } outline
        ? RoomRegionGeometry.Area(outline) - (Holes?.Sum(RoomRegionGeometry.Area) ?? 0)
        : Math.Abs((SecondCorner.X - FirstCorner.X) * (SecondCorner.Y - FirstCorner.Y));

    [JsonIgnore]
    public Point2 Center => Boundary is { Count: >= 3 } outline
        ? RoomRegionGeometry.LabelPoint(outline,
            Holes?.Select(hole => (IReadOnlyList<Point2>)hole).ToArray() ?? [])
        : new((FirstCorner.X + SecondCorner.X) / 2, (FirstCorner.Y + SecondCorner.Y) / 2);
}

/// <summary>Classifies a horizontal slab by the building role it plays.</summary>
public enum FloorKind
{
    Floor,
    Ceiling,
    Roof,
    /// <summary>A basin of still water: a thin slab whose top face renders as a reflective water surface.</summary>
    Water,
    /// <summary>
    /// A solid of site terrain: its top passes through <see cref="FloorSlab.TopoPoints"/>, its boundary corners rest at
    /// <see cref="FloorSlab.ElevationOffset"/>, and it reaches <see cref="FloorSlab.Thickness"/> below its lowest point.
    /// </summary>
    Toposolid
}

/// <summary>Plan shape of a roof. Pitched shapes rise from the eave toward a ridge.</summary>
public enum RoofShape
{
    /// <summary>A horizontal slab.</summary>
    Flat,

    /// <summary>One plane rising across the shorter side of the boundary.</summary>
    Shed,

    /// <summary>Two planes meeting at a ridge along the longer side (gable roof).</summary>
    Gable,

    /// <summary>Four planes rising from every edge (hip roof).</summary>
    Hip
}

/// <summary>Represents a horizontal slab with a closed polygonal boundary.</summary>
public sealed record FloorSlab(
    Guid Id,
    Guid LevelId,
    string Name,
    List<Point2> Boundary,
    double Thickness)
{
    /// <summary>Plan shape; only roofs may be pitched. The eave stays at <see cref="ElevationOffset"/>.</summary>
    public RoofShape Shape { get; init; } = RoofShape.Flat;

    /// <summary>Roof pitch in degrees from the horizontal. Ignored for flat slabs.</summary>
    public double Pitch { get; init; }

    /// <summary>Whether the slab rises above its eave (a pitched roof with a positive pitch).</summary>
    [JsonIgnore] public bool IsPitched => Kind == FloorKind.Roof && Shape != RoofShape.Flat && Pitch > 0;

    /// <summary>Finish applied to the walking surface, used for finish area takeoff.</summary>
    public FinishKind Finish { get; init; }

    /// <summary>Role of the slab; ceilings and roofs reuse the same boundary and thickness rules.</summary>
    public FloorKind Kind { get; init; } = FloorKind.Floor;

    /// <summary>Meters from the level elevation to the slab top; ceilings and roofs sit above the floor.</summary>
    public double ElevationOffset { get; init; }

    /// <summary>
    /// Roofs only: how far the roof reaches past its boundary (the eaves and verges), in meters. The roof plane keeps
    /// passing over the boundary at the same height, so a pitched eave drops below the wall top by the overhang's rise.
    /// </summary>
    public double Overhang { get; init; }

    /// <summary>Pitched roofs only: whether the triangles between the wall tops and the roof (gables) are closed with wall.</summary>
    public bool GableWalls { get; init; } = true;

    /// <summary>Toposolids only: surveyed points of the terrain, with elevations relative to the level.</summary>
    public List<Geometry.TopoPoint>? TopoPoints { get; init; }

    /// <summary>Whether the slab is site terrain rather than a building slab.</summary>
    [JsonIgnore] public bool IsToposolid => Kind == FloorKind.Toposolid;

    /// <summary>Calculates plan area relative to the first vertex to preserve precision far from the origin.</summary>
    public double CalculateArea()
    {
        if (Boundary.Count < 3) return 0;
        // Global-coordinate cross products lose small areas when the site has large survey coordinates.
        // A triangle fan about the first vertex preserves signed area for concave outlines as well.
        var origin = Boundary[0];
        var doubledArea = 0d;
        for (var index = 1; index < Boundary.Count - 1; index++)
        {
            var current = Boundary[index];
            var next = Boundary[index + 1];
            doubledArea += (current.X - origin.X) * (next.Y - origin.Y) -
                           (next.X - origin.X) * (current.Y - origin.Y);
        }
        return Math.Abs(doubledArea) / 2;
    }
}

/// <summary>Cross-section outline of a column. Width and depth give the overall extent in plan.</summary>
public enum ColumnShape
{
    Rectangular,
    Round,
    LShaped,
    TShaped,
    Octagonal
}

/// <summary>Represents a structural column standing on a building level.</summary>
public sealed record Column(
    Guid Id,
    Guid LevelId,
    Point2 Position,
    double Width,
    double Depth,
    double Height)
{
    /// <summary>Plan outline of the column; rectangular unless a catalog type says otherwise.</summary>
    public ColumnShape Shape { get; init; } = ColumnShape.Rectangular;

    /// <summary>Counter-clockwise plan outline of the column around its position.</summary>
    [JsonIgnore]
    public IReadOnlyList<Point2> Outline => ArchitecturalShapes.ColumnOutline(this);
}

/// <summary>Supported physical paper sizes for drawing sheets.</summary>
public enum SheetPaperSize
{
    A0,
    A1,
    A2,
    A3,
    A4
}

/// <summary>Represents a numbered drawing sheet in the project document set.</summary>
public sealed record DrawingSheet(
    Guid Id,
    string Number,
    string Name,
    SheetPaperSize PaperSize)
{
    /// <summary>Reusable editable border family shared with other sheets.</summary>
    public Guid? TitleBlockFamilyId { get; init; }
    public List<SheetViewport> Viewports { get; init; } = [];
    public string Description { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string DateText { get; init; } = string.Empty;

    /// <summary>Spacing, in sheet millimeters, of the guide grid shown while composing the sheet; 0 shows none. It is never printed.</summary>
    public double GuideGridMm { get; init; }
}
