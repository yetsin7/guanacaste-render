using System.Text.Json.Serialization;

namespace Guanacaste3D.Core;

/// <summary>Identifies the structural, foundation, connection, or drafting role of an element.</summary>
public enum StructuralElementKind
{
    Beam,
    Brace,
    Truss,
    WallFoundation,
    IsolatedFoundation,
    Component,
    Connection,
    ModelLine,

    /// <summary>A rectangular hole through the floor slab of its own level.</summary>
    Opening,

    /// <summary>A rectangular hole through every floor slab at or above its level.</summary>
    Shaft,

    /// <summary>An annotation placed in the plan; its text is stored in the element name.</summary>
    ModelText,

    /// <summary>A box rising from a roof slab. It cuts an opening through that roof and stands on its top face.</summary>
    Dormer,

    /// <summary>A straight run of solid steps rising from the start to the end.</summary>
    Stair,

    /// <summary>A straight guard with posts and two horizontal rails.</summary>
    Railing,

    /// <summary>A sloped walking surface rising from the start to the end by its height.</summary>
    Ramp,

    /// <summary>A rain gutter (canal) running along the eave between its two points.</summary>
    Gutter,

    /// <summary>A vertical rainwater pipe (bajante) at a point, running up from its offset by its height.</summary>
    Downspout,

    /// <summary>A glazed wall system with a mullion and transom grid between its two points.</summary>
    CurtainWall,

    /// <summary>A glazed rooflight in a rectangle of a roof; it cuts the roof and stands on its top face.</summary>
    Skylight,

    /// <summary>A datum line that hosts and dimensions can refer to. It has no solid.</summary>
    ReferencePlane,

    /// <summary>A line that divides a space into rooms without a wall. It has no solid.</summary>
    RoomSeparator,

    /// <summary>A line that bounds an area of an area scheme. It has no solid.</summary>
    AreaBoundary,

    /// <summary>A rectangular area of an area scheme, measured in plan. It has no solid.</summary>
    Area,

    /// <summary>A profile swept along both faces of a wall, such as a skirting or cornice.</summary>
    WallSweep,

    /// <summary>A shallow groove line drafted along both faces of a wall.</summary>
    WallReveal,

    /// <summary>A flat panel under the overhang of a roof.</summary>
    Soffit,

    /// <summary>A vertical board closing the edge of a roof.</summary>
    Fascia,

    /// <summary>A band running along the edge of a floor slab.</summary>
    SlabEdge,

    /// <summary>
    /// A tree or shrub of the site, at a point. <see cref="StructuralElement.Width"/> is the canopy spread and
    /// <see cref="StructuralElement.Height"/> the overall height; the style is the kind of growth.
    /// </summary>
    Planting,

    /// <summary>
    /// A traction elevator in its hoistway, drawn as the rectangle between two corners. <see cref="StructuralElement.Height"/>
    /// is the hoistway from the pit floor (<see cref="StructuralElement.Offset"/>, below the level) to the overhead;
    /// <see cref="StructuralElement.GridHeight"/> is the spacing of its landings and <see cref="StructuralElement.Width"/>
    /// the clear width of its doors. The hoistway cuts every floor above its own level that it serves.
    /// </summary>
    Elevator,

    /// <summary>A line of drafting drawn in the plan only: it has no solid and never appears in 3D.</summary>
    DetailLine,

    /// <summary>A rectangle of hatched fill drawn in the plan only, to mark a material or an area on the sheet.</summary>
    FilledRegion,

    /// <summary>A rectangle outlined with scallops, drawn in the plan only, to flag a change for review.</summary>
    RevisionCloud,

    /// <summary>
    /// Batting drawn along a line in the plan, as in a detail of an insulated wall. <see cref="StructuralElement.Width"/>
    /// is the thickness of the insulation. It has no solid.
    /// </summary>
    Insulation,

    /// <summary>
    /// A conceptual volume, drawn between two corners and rising by its height from its level: the massing of a building
    /// before it has walls. Walls and a roof can be built from its sides and top.
    /// </summary>
    Mass,

    /// <summary>A parking space drawn between two corners in the plan. It has no solid.</summary>
    ParkingSpace,

    /// <summary>A line that marks where one plan continues on another sheet. It has no solid.</summary>
    Matchline,

    /// <summary>A mark in a view that points to another view and the sheet it is on; its text is the element's name. It has no solid.</summary>
    ViewReference,

    /// <summary>The elevation of a point, read from its level plus a height. It has no solid.</summary>
    SpotElevation,

    /// <summary>The shared coordinates of a point, read from the project's site. It has no solid.</summary>
    SpotCoordinate,

    /// <summary>The slope between two points, from the rise stored in the element. It has no solid.</summary>
    SpotSlope,

    /// <summary>A keynote call-out whose text is the element's name. It has no solid.</summary>
    Keynote,

    /// <summary>A 2D symbol or detail component of the library; the name is its key and the width its size. It has no solid.</summary>
    Symbol,

    /// <summary>A legend that colours the rooms of its level by name and lists the names. It has no solid.</summary>
    ColorLegend,

    /// <summary>A rectangular air duct drawn between two points, at a height above its level.</summary>
    Duct,

    /// <summary>A duct that is only reserved in the model, to be given its size later.</summary>
    DuctPlaceholder,

    /// <summary>A flexible round duct.</summary>
    FlexDuct,

    /// <summary>A cable tray.</summary>
    CableTray,

    /// <summary>A round electrical conduit.</summary>
    Conduit,

    /// <summary>A flexible pipe.</summary>
    FlexPipe,

    /// <summary>A part made to order for a duct, pipe or tray run.</summary>
    FabricationPart,

    /// <summary>A diffuser or grille that air enters or leaves a room through, at a point on the ceiling.</summary>
    AirTerminal,

    /// <summary>A thermostat, sensor or other control of the mechanical system, at a point on the wall.</summary>
    MechanicalControlDevice,

    /// <summary>A band that stiffens a duct; made by the duct stiffener command.</summary>
    DuctStiffener,

    /// <summary>A steel plate drawn between two corners; its height is its thickness.</summary>
    Plate,

    /// <summary>A bolt at a point; its width is its diameter.</summary>
    Bolt,

    /// <summary>A weld bead along a line.</summary>
    Weld,

    /// <summary>A reinforcing bar along a line; its width is its diameter.</summary>
    Rebar,

    /// <summary>A sheet of welded wire fabric, drawn along its length; its width is the sheet's width.</summary>
    FabricSheet
}

/// <summary>Variant of an element's geometry. Which values apply depends on the element kind.</summary>
public enum ElementStyle
{
    /// <summary>The standard form of the kind.</summary>
    Default,

    /// <summary>Round section for point elements (piers, foundations, downspouts).</summary>
    Round,

    /// <summary>Wide-flange section for beams, braces and trusses.</summary>
    IShape,

    /// <summary>Tee section for beams, braces and trusses.</summary>
    Tee,

    /// <summary>A guard with closely spaced vertical balusters.</summary>
    Pickets,

    /// <summary>A guard with a glass panel between the rails.</summary>
    GlassPanel,

    /// <summary>A guard with slim horizontal cables.</summary>
    Cable,

    /// <summary>A solid parapet with a cap.</summary>
    SolidParapet,

    /// <summary>A stair of treads on stringers, without risers.</summary>
    Open,

    /// <summary>Planting: the guanacaste tree, with a short massive trunk under a very wide, flat umbrella crown.</summary>
    Guanacaste,

    /// <summary>Planting: a palm with a slender leaning trunk and a crown of arching fronds.</summary>
    Palm,

    /// <summary>Planting: a conifer of stacked, tapering tiers.</summary>
    Conifer,

    /// <summary>Planting: a low shrub of a few rounded clumps, without a visible trunk.</summary>
    Shrub,

    /// <summary>Stair: two flights turning a quarter left around a square landing.</summary>
    LShaped,

    /// <summary>Stair: two flights side by side, turning back around a half landing (switchback).</summary>
    UShaped,

    /// <summary>Stair: wedge treads winding counter-clockwise around a central column.</summary>
    Spiral,

    /// <summary>Curtain wall: a breeze-block (cobogó) screen of open masonry cells instead of glazed panels.</summary>
    BreezeBlock
}

/// <summary>
/// A framing, foundation, or connection element tied to a building level. Line kinds run from
/// <see cref="Start"/> to <see cref="End"/>; point kinds share one position for both ends.
/// <see cref="Width"/> is the plan size across the element, <see cref="Height"/> its vertical size, and
/// <see cref="Offset"/> the elevation of its underside relative to the level datum.
/// </summary>
public sealed record StructuralElement(
    Guid Id,
    Guid LevelId,
    StructuralElementKind Kind,
    string Name,
    Point2 Start,
    Point2 End,
    double Width,
    double Height,
    double Offset)
{
    /// <summary>
    /// Plan depth for point-hosted components. Zero preserves the legacy square footprint whose depth equals
    /// <see cref="Width"/>.
    /// </summary>
    public double Depth { get; init; }

    /// <summary>Parametric residential geometry used by component instances; other element kinds keep the box default.</summary>
    public ResidentialComponentShape ComponentShape { get; init; }

    /// <summary>Residential library group retained on component instances for schedules and future type browsing.</summary>
    public ResidentialComponentCategory ComponentCategory { get; init; }

    /// <summary>
    /// Point-hosted elements only: counter-clockwise turn in radians about <see cref="Start"/>. Zero faces the family's
    /// front toward -Y, as authored.
    /// </summary>
    public double Rotation { get; init; }

    /// <summary>
    /// Planting and components only: the loaded mesh family (<see cref="BuildingDocument.ComponentFamilies"/>) whose
    /// detailed model the 3D view and the renderer draw, stretched to this instance's width, depth and height. The
    /// procedural shape remains the plan symbol and the fallback.
    /// </summary>
    public Guid? FamilyId { get; init; }

    /// <summary>Members of one model group share this id; moves and deletes act on the whole group.</summary>
    public Guid? GroupId { get; init; }

    /// <summary>Stair or ramp that generated this railing, if it remains associated with a host.</summary>
    public Guid? RailingHostId { get; init; }

    /// <summary>Curtain wall only: center-to-center spacing of mullions in meters; zero means the 1.5 m default.</summary>
    public double GridWidth { get; init; }

    /// <summary>
    /// Curtain wall only: center-to-center spacing of transoms in meters; zero means the 1.2 m default.
    /// Elevator only: the floor-to-floor spacing of its landings.
    /// </summary>
    public double GridHeight { get; init; }

    /// <summary>Stairs only: requested riser (contrahuella) in meters; zero lets the stair pick the tallest comfortable riser.</summary>
    public double RiserHeight { get; init; }

    /// <summary>Stairs only: requested tread going (huella) in meters; zero keeps the going the placed run gives.</summary>
    public double TreadDepth { get; init; }

    /// <summary>Guards only: vertical rise from the start to the end, so a guard can follow a stair or ramp.</summary>
    public double Rise { get; init; }

    /// <summary>Geometry variant: beam section, guard type, stair type, or round point elements.</summary>
    public ElementStyle Style { get; init; }

    /// <summary>Rectangles cut out of the member, from its top down; null when it has none. The list is never changed after it is set.</summary>
    public List<MemberCut>? Cuts { get; init; }

    /// <summary>Fabric sheets only: the wires and their spacing; null takes 5.5 mm wires every 15 cm.</summary>
    public WireSpec? Wires { get; init; }

    /// <summary>Drafting marks and openings have no solid of their own; openings only cut floor slabs.</summary>
    [JsonIgnore] public bool HasSolid => Kind is not (StructuralElementKind.ModelLine or StructuralElementKind.Opening
        or StructuralElementKind.Shaft or StructuralElementKind.ModelText or StructuralElementKind.ReferencePlane
        or StructuralElementKind.RoomSeparator or StructuralElementKind.AreaBoundary or StructuralElementKind.Area
        or StructuralElementKind.DetailLine or StructuralElementKind.FilledRegion or StructuralElementKind.RevisionCloud
        or StructuralElementKind.Insulation or StructuralElementKind.ParkingSpace or StructuralElementKind.Matchline
        or StructuralElementKind.ViewReference or StructuralElementKind.SpotElevation or StructuralElementKind.SpotCoordinate
        or StructuralElementKind.SpotSlope or StructuralElementKind.Keynote or StructuralElementKind.Symbol
        or StructuralElementKind.ColorLegend);

    /// <summary>Whether the kind is drafted as a single line between its two points.</summary>
    [JsonIgnore] public bool IsDatumLine => Kind is StructuralElementKind.ReferencePlane
        or StructuralElementKind.RoomSeparator or StructuralElementKind.AreaBoundary
        or StructuralElementKind.ModelLine or StructuralElementKind.DetailLine or StructuralElementKind.Matchline;

    /// <summary>Plan area in square meters of an area rectangle; zero for every other kind.</summary>
    [JsonIgnore] public double PlanArea => Kind == StructuralElementKind.Area
        ? Math.Abs(End.X - Start.X) * Math.Abs(End.Y - Start.Y) : 0;

    /// <summary>Plan outline: a rectangle along the axis for lines, or a square around the point for points.</summary>
    [JsonIgnore]
    public IReadOnlyList<Point2> Footprint
    {
        get
        {
            var half = Width / 2;
            var halfDepth = (Depth > 0 ? Depth : Width) / 2;
            var length = Start.DistanceTo(End);
            if (Kind is StructuralElementKind.Opening or StructuralElementKind.Shaft or StructuralElementKind.Dormer
                    or StructuralElementKind.Skylight or StructuralElementKind.Area or StructuralElementKind.Elevator
                    or StructuralElementKind.FilledRegion or StructuralElementKind.RevisionCloud or StructuralElementKind.Mass
                    or StructuralElementKind.ParkingSpace or StructuralElementKind.Plate && length >= 1e-9)
                return Geometry.SlabOpenings.Corners(PlanExtent);
            if (length < 1e-9)
            {
                var (cos, sin) = (Math.Cos(Rotation), Math.Sin(Rotation));
                return new[] { (-half, -halfDepth), (half, -halfDepth), (half, halfDepth), (-half, halfDepth) }
                    .Select(corner => new Point2(Start.X + corner.Item1 * cos - corner.Item2 * sin,
                        Start.Y + corner.Item1 * sin + corner.Item2 * cos))
                    .ToArray();
            }

            var normalX = -(End.Y - Start.Y) / length * half;
            var normalY = (End.X - Start.X) / length * half;
            return
            [
                new(Start.X - normalX, Start.Y - normalY), new(End.X - normalX, End.Y - normalY),
                new(End.X + normalX, End.Y + normalY), new(Start.X + normalX, Start.Y + normalY)
            ];
        }
    }

    [JsonIgnore] public double Length => Start.DistanceTo(End);

    /// <summary>Bounding rectangle of the two corners of an opening or shaft drawn by its two clicks.</summary>
    [JsonIgnore]
    public Geometry.PlanRectangle PlanExtent => Geometry.PlanRectangle.Of([Start, End]);
}

/// <summary>Holds the default section, footprint, and elevation used when a tool places an element.</summary>
public static class StructuralElementStandards
{
    /// <summary>Returns the width, height, and underside offset for a new element of the given kind.</summary>
    public static (double Width, double Height, double Offset) For(StructuralElementKind kind) => kind switch
    {
        // Beams and trusses hang below the level so their top sits on it, like a framed floor.
        StructuralElementKind.Beam => (0.30, 0.50, -0.50),
        StructuralElementKind.Truss => (0.20, 0.80, -0.80),
        StructuralElementKind.Brace => (0.30, 0.30, -0.30),
        StructuralElementKind.WallFoundation => (0.40, 0.60, -0.60),
        StructuralElementKind.IsolatedFoundation => (1.20, 0.40, -0.40),
        StructuralElementKind.Component => (0.40, 0.40, 0),
        StructuralElementKind.Connection => (0.20, 0.02, 0),
        // Openings are placed as 1.20 m squares by a click, or as the rectangle between two clicks.
        StructuralElementKind.Opening => (1.20, 0, 0),
        StructuralElementKind.Shaft or StructuralElementKind.ModelText => (0, 0, 0),
        // A dormer's Height is its body height above the roof top; its Offset is set from the roof when placed.
        StructuralElementKind.Dormer => (0, 2.40, 0),
        StructuralElementKind.Stair => (1.20, 3.0, 0),
        StructuralElementKind.Railing => (0.06, 1.10, 0),
        // A ramp rises 0.60 m by default; its run is the distance between the clicks (1:12 is 7.2 m).
        StructuralElementKind.Ramp => (1.50, 0.60, 0),
        // Gutters hang at the eave of a 3 m wall; the channel is 12 cm wide and 10 cm deep.
        StructuralElementKind.Gutter => (0.12, 0.10, 2.85),
        StructuralElementKind.Downspout => (0.09, 2.85, 0),
        StructuralElementKind.CurtainWall => (0.10, 3.0, 0),
        StructuralElementKind.Skylight => (0, 0.15, 0),
        StructuralElementKind.ReferencePlane or StructuralElementKind.RoomSeparator
            or StructuralElementKind.AreaBoundary or StructuralElementKind.Area
            or StructuralElementKind.DetailLine or StructuralElementKind.FilledRegion
            or StructuralElementKind.RevisionCloud or StructuralElementKind.ParkingSpace or StructuralElementKind.Matchline
            or StructuralElementKind.ViewReference or StructuralElementKind.SpotElevation or StructuralElementKind.SpotCoordinate
            or StructuralElementKind.SpotSlope or StructuralElementKind.Keynote or StructuralElementKind.ColorLegend => (0, 0, 0),
        StructuralElementKind.Symbol => (1.0, 0, 0),
        // Services: a 400 x 300 duct, a 200 mm flexible duct, a 300 mm tray, a 25 mm conduit and a 32 mm flexible pipe, at their usual heights.
        StructuralElementKind.Duct => (0.40, 0.30, 2.60),
        StructuralElementKind.DuctPlaceholder or StructuralElementKind.FlexDuct => (0.20, 0.20, 2.60),
        StructuralElementKind.CableTray => (0.30, 0.08, 2.50),
        StructuralElementKind.Conduit => (0.025, 0.025, 2.40),
        StructuralElementKind.FlexPipe => (0.032, 0.032, 0.50),
        StructuralElementKind.FabricationPart => (0.30, 0.30, 2.50),
        StructuralElementKind.AirTerminal => (0.60, 0.12, 2.70),
        StructuralElementKind.MechanicalControlDevice => (0.12, 0.12, 1.50),
        StructuralElementKind.DuctStiffener => (0.48, 0.38, 2.56),
        // Steel: a 12 mm plate, a 20 mm bolt 80 mm long and an 8 mm weld bead.
        StructuralElementKind.Plate => (0, 0.012, 1.0),
        StructuralElementKind.Bolt => (0.02, 0.08, 1.0),
        StructuralElementKind.Weld => (0.008, 0.008, 1.0),
        StructuralElementKind.Rebar => (0.012, 0.012, 0.05),
        StructuralElementKind.FabricSheet => (2.4, 0.006, 0.03),
        // A mass of two storeys is the usual first sketch of a building.
        StructuralElementKind.Mass => (0, 6.0, 0),
        // Batting 10 cm thick, the usual insulation of a masonry wall.
        StructuralElementKind.Insulation => (0.10, 0, 0),
        // A sweep's width is the host wall's thickness; a reveal is a 2 cm groove at 1 m.
        StructuralElementKind.WallSweep => (0.20, 0.10, 0),
        StructuralElementKind.WallReveal => (0.20, 0.02, 1.0),
        StructuralElementKind.Soffit => (0.60, 0.02, 2.95),
        StructuralElementKind.Fascia => (0.03, 0.25, 2.80),
        StructuralElementKind.SlabEdge => (0.20, 0.30, -0.30),
        // A broadleaf tree 8 m tall with a 6 m canopy, standing on the level.
        StructuralElementKind.Planting => (6.0, 8.0, 0),
        // A 900 mm door, a 1.2 m pit, one 3 m storey and a 3.6 m overhead; placing it between levels sets the real travel.
        StructuralElementKind.Elevator => (0.90, 1.20 + 3.0 + 3.6, -1.20),
        _ => (0.02, 0, 0)
    };
}
