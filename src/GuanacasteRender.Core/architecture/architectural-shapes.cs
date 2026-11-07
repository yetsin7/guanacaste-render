namespace Guanacaste3D.Core;

/// <summary>Plan outlines and strip helpers shared by the architectural element geometry.</summary>
public static class ArchitecturalShapes
{
    private const int RoundSides = 24;

    /// <summary>Returns the counter-clockwise plan outline of a column around its position.</summary>
    public static IReadOnlyList<Point2> ColumnOutline(Column column) =>
        Outline(column.Shape, column.Position, column.Width, column.Depth);

    /// <summary>Builds the counter-clockwise outline of a column-like section of the given overall extent.</summary>
    public static IReadOnlyList<Point2> Outline(ColumnShape shape, Point2 center, double width, double depth)
    {
        var hw = width / 2;
        var hd = depth / 2;
        var arm = 0.35 * Math.Min(width, depth);
        (double X, double Y)[] local = shape switch
        {
            ColumnShape.Round => Enumerable.Range(0, RoundSides).Select(index =>
            {
                var angle = 2 * Math.PI * index / RoundSides;
                return (hw * Math.Cos(angle), hd * Math.Sin(angle));
            }).ToArray(),
            ColumnShape.Octagonal => OctagonOutline(hw, hd),
            ColumnShape.LShaped =>
            [
                (-hw, -hd), (hw, -hd), (hw, -hd + arm), (-hw + arm, -hd + arm), (-hw + arm, hd), (-hw, hd)
            ],
            ColumnShape.TShaped =>
            [
                (-arm / 2, -hd), (arm / 2, -hd), (arm / 2, hd - arm), (hw, hd - arm), (hw, hd), (-hw, hd),
                (-hw, hd - arm), (-arm / 2, hd - arm)
            ],
            _ => [(-hw, -hd), (hw, -hd), (hw, hd), (-hw, hd)]
        };
        return local.Select(point => new Point2(center.X + point.X, center.Y + point.Y)).ToArray();
    }

    /// <summary>A regular-looking octagon: the rectangle with each corner cut at 29% of its short side.</summary>
    private static (double X, double Y)[] OctagonOutline(double hw, double hd)
    {
        var cut = 0.29 * 2 * Math.Min(hw, hd);
        return
        [
            (-hw + cut, -hd), (hw - cut, -hd), (hw, -hd + cut), (hw, hd - cut),
            (hw - cut, hd), (-hw + cut, hd), (-hw, hd - cut), (-hw, -hd + cut)
        ];
    }

    /// <summary>A round or square footprint of a point element.</summary>
    public static IReadOnlyList<Point2> PointFootprint(Point2 center, double size, bool round) =>
        round ? Outline(ColumnShape.Round, center, size, size) : Outline(ColumnShape.Rectangular, center, size, size);

    /// <summary>
    /// A rectangle along the segment from <paramref name="from"/> to <paramref name="to"/>, centered
    /// <paramref name="lateral"/> meters to the left of it and <paramref name="width"/> meters wide.
    /// </summary>
    public static IReadOnlyList<Point2> Strip(Point2 from, Point2 to, double lateral, double width)
    {
        var length = from.DistanceTo(to);
        var normalX = -(to.Y - from.Y) / length;
        var normalY = (to.X - from.X) / length;
        var near = lateral - width / 2;
        var far = lateral + width / 2;
        return
        [
            new(from.X + normalX * near, from.Y + normalY * near),
            new(to.X + normalX * near, to.Y + normalY * near),
            new(to.X + normalX * far, to.Y + normalY * far),
            new(from.X + normalX * far, from.Y + normalY * far)
        ];
    }

    /// <summary>The point at a fraction of the way from one point to another.</summary>
    public static Point2 Lerp(Point2 from, Point2 to, double fraction) =>
        new(from.X + (to.X - from.X) * fraction, from.Y + (to.Y - from.Y) * fraction);

    /// <summary>The geometry variants that make sense for a kind of element; the first is always the default.</summary>
    public static IReadOnlyList<ElementStyle> StylesFor(StructuralElementKind kind) => kind switch
    {
        StructuralElementKind.Beam or StructuralElementKind.Brace or StructuralElementKind.Truss =>
            [ElementStyle.Default, ElementStyle.IShape, ElementStyle.Tee],
        StructuralElementKind.Component or StructuralElementKind.IsolatedFoundation or StructuralElementKind.Downspout or StructuralElementKind.Bolt =>
            [ElementStyle.Default, ElementStyle.Round],
        StructuralElementKind.Railing =>
            [ElementStyle.Default, ElementStyle.Pickets, ElementStyle.GlassPanel, ElementStyle.Cable, ElementStyle.SolidParapet],
        StructuralElementKind.Stair =>
            [ElementStyle.Default, ElementStyle.Open, ElementStyle.LShaped, ElementStyle.UShaped, ElementStyle.Spiral],
        StructuralElementKind.CurtainWall => [ElementStyle.Default, ElementStyle.BreezeBlock],
        StructuralElementKind.Planting =>
            [ElementStyle.Default, ElementStyle.Guanacaste, ElementStyle.Palm, ElementStyle.Conifer, ElementStyle.Shrub],
        _ => [ElementStyle.Default]
    };

    /// <summary>Rejects out-of-range pitch, shape, and style values and degenerate new element geometry.</summary>
    public static void Validate(BuildingDocument document)
    {
        foreach (var floor in document.Floors)
            if (!Enum.IsDefined(floor.Shape) || !double.IsFinite(floor.Pitch) ||
                floor.Pitch is < 0 or > RoofGeometry.MaximumPitch ||
                (floor.Shape != RoofShape.Flat && floor.Kind != FloorKind.Roof))
                throw new InvalidDataException("Invalid roof shape or pitch.");
        if (document.Columns.Any(column => !Enum.IsDefined(column.Shape)))
            throw new InvalidDataException("Invalid column shape.");
        foreach (var element in document.StructuralElements)
        {
            if (!Enum.IsDefined(element.Style) || !StylesFor(element.Kind).Contains(element.Style))
                throw new InvalidDataException("Invalid element style.");
            if (!Enum.IsDefined(element.ComponentShape) || !Enum.IsDefined(element.ComponentCategory) ||
                !double.IsFinite(element.Depth) || element.Depth is < 0 or > 50 ||
                (element.Kind != StructuralElementKind.Component &&
                    (element.Depth != 0 || element.ComponentShape != ResidentialComponentShape.Box ||
                     element.ComponentCategory != ResidentialComponentCategory.None)))
                throw new InvalidDataException("Invalid component family geometry.");
            if (element.HasSolid && element.Kind is not (StructuralElementKind.Dormer or StructuralElementKind.Skylight or StructuralElementKind.Mass or StructuralElementKind.Plate) &&
                (!double.IsFinite(element.Width) || !double.IsFinite(element.Height) || !double.IsFinite(element.Offset) ||
                 element.Width is < 0.005 or > 50 || element.Height is <= 0 or > 100 || element.Offset is < -100 or > 1000))
                throw new InvalidDataException("Invalid ramp, gutter, downspout, curtain wall or skylight geometry.");
            if (element.Wires is { IsValid: false })
                throw new InvalidDataException("The fabric sheet is invalid.");
            if (element.Cuts is { } cuts && (cuts.Count > MemberCuts.MaximumCuts || cuts.Any(cut => cut is null || !cut.IsValid)))
                throw new InvalidDataException("The cut is invalid.");
            if (!double.IsFinite(element.GridWidth) || !double.IsFinite(element.GridHeight) ||
                element.GridWidth != 0 && element.GridWidth is < 0.2 or > 20 ||
                element.GridHeight != 0 && element.GridHeight is < 0.2 or > 20)
                throw new InvalidDataException("Invalid ramp, gutter, downspout, curtain wall or skylight geometry.");
            if (!double.IsFinite(element.Rise) || element.Rise is < -100 or > 100)
                throw new InvalidDataException("Invalid ramp, gutter, downspout, curtain wall or skylight geometry.");
            var lengthKind = element.Kind is StructuralElementKind.Ramp or StructuralElementKind.Gutter
                or StructuralElementKind.CurtainWall;
            var pointKind = element.Kind is StructuralElementKind.Downspout or StructuralElementKind.Planting;
            if (!lengthKind && !pointKind && element.Kind != StructuralElementKind.Skylight) continue;
            if (element.Id == Guid.Empty || !document.Levels.Any(level => level.Id == element.LevelId) ||
                !double.IsFinite(element.Width) || !double.IsFinite(element.Height) ||
                !double.IsFinite(element.Offset) || !double.IsFinite(element.Length) ||
                element.Height is <= 0 or > 100 || (lengthKind && element.Length is < 0.1 or > 1000) ||
                (element.Kind != StructuralElementKind.Skylight && element.Width is < 0.02 or > 10 &&
                 element.Kind != StructuralElementKind.Planting) ||
                (element.Kind == StructuralElementKind.Planting && element.Width is < 0.2 or > 50))
                throw new InvalidDataException("Invalid ramp, gutter, downspout, curtain wall or skylight geometry.");
        }
    }
}
