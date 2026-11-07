using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core;

/// <summary>
/// One closed solid of an element: a vertical prism over <see cref="Footprint"/> from <see cref="Bottom"/> to
/// <see cref="Top"/>, both measured from the element's level. When <see cref="EndTop"/> is set, the footprint is a
/// quad whose second and third vertices rise to that height, which gives a ramp its slope.
/// </summary>
public sealed record ElementPart(IReadOnlyList<Point2> Footprint, double Bottom, double Top, SurfaceKind Kind)
{
    public double? EndTop { get; init; }

    /// <summary>Underside height at the end vertices; when null the underside stays level at <see cref="Bottom"/>.</summary>
    public double? EndBottom { get; init; }
}

/// <summary>Breaks every solid structural and architectural element into the parts that the 3D model and plan draw.</summary>
public static class ElementGeometry
{
    private const double RailPost = 0.06;

    /// <summary>Returns the solids of an element; drafting marks, openings and shafts have none.</summary>
    public static IEnumerable<ElementPart> Parts(StructuralElement element)
    {
        if (!element.HasSolid) return [];
        var parts = BaseParts(element);
        return element.Cuts is { Count: > 0 } cuts && element.Length >= 0.01 ? MemberCuts.Apply(element, parts, cuts) : parts;
    }

    private static IEnumerable<ElementPart> BaseParts(StructuralElement element)
    {
        return element.Kind switch
        {
            StructuralElementKind.Stair => StairParts(element),
            StructuralElementKind.Railing => RailingParts(element),
            StructuralElementKind.Ramp => RampParts(element),
            StructuralElementKind.WallSweep or StructuralElementKind.WallReveal => WallSweepParts(element),
            StructuralElementKind.Gutter => GutterParts(element),
            StructuralElementKind.CurtainWall => CurtainWallParts(element),
            StructuralElementKind.Skylight => SkylightParts(element),
            StructuralElementKind.Elevator => ElevatorGeometry.Parts(element),
            var kind when (MepElements.IsRound(kind) || kind == StructuralElementKind.Rebar) && element.Length >= 0.05 => RoundRunParts(element),
            StructuralElementKind.Planting => PlantGeometry.ProxyParts(element),
            StructuralElementKind.Component when element.ComponentShape != ResidentialComponentShape.Box =>
                ResidentialComponentGeometry.Parts(element),
            StructuralElementKind.Downspout or StructuralElementKind.Component or StructuralElementKind.Bolt or
                StructuralElementKind.IsolatedFoundation when element.Style == ElementStyle.Round => RoundPointParts(element),
            StructuralElementKind.Beam or StructuralElementKind.Brace or StructuralElementKind.Truss
                when element.Style is ElementStyle.IShape or ElementStyle.Tee && element.Length >= 0.05 =>
                SectionedMemberParts(element),
            _ => [Block(element, element.Footprint, element.Offset, element.Offset + element.Height)]
        };
    }

    /// <summary>
    /// A round pipe, conduit or flexible duct as two boxes crossed over each other: one full width and 70 % high, the other
    /// 70 % wide and full height, which together read as an octagon.
    /// </summary>
    private static IEnumerable<ElementPart> RoundRunParts(StructuralElement element)
    {
        yield return Block(element, ArchitecturalShapes.Strip(element.Start, element.End, 0, element.Width),
            element.Offset + element.Height * 0.15, element.Offset + element.Height * 0.85);
        yield return Block(element, ArchitecturalShapes.Strip(element.Start, element.End, 0, element.Width * 0.7),
            element.Offset, element.Offset + element.Height);
    }

    private static ElementPart Block(StructuralElement element, IReadOnlyList<Point2> footprint, double bottom,
        double top, SurfaceKind kind = SurfaceKind.Structure) => new(footprint, bottom, top, kind);

    /// <summary>Straight stairs: solid steps by default, or treads on stepped stringers when open.</summary>
    private static IEnumerable<ElementPart> StairParts(StructuralElement element)
    {
        if (StairGeometry.IsComposite(element))
        {
            foreach (var part in StairGeometry.Parts(element)) yield return part;
            yield break;
        }
        if (element.Length < 0.05) yield break;
        if (element.Style != ElementStyle.Open)
        {
            foreach (var step in CirculationGeometry.Parts(element))
                yield return Block(element, step.Footprint, step.Offset, step.Offset + step.Height);
            yield break;
        }
        var count = StairGeometry.RiserCount(element);
        var treadThickness = Math.Min(0.04, element.Height / count / 2);
        const double stringerWidth = 0.06;
        const double stringerDepth = 0.20;
        for (var index = 0; index < count; index++)
        {
            var from = ArchitecturalShapes.Lerp(element.Start, element.End, (double)index / count);
            var to = ArchitecturalShapes.Lerp(element.Start, element.End, (double)(index + 1) / count);
            var top = element.Offset + element.Height * (index + 1) / count;
            yield return Block(element, ArchitecturalShapes.Strip(from, to, 0, element.Width), top - treadThickness, top);
            foreach (var side in new[] { -1, 1 })
                yield return Block(element, ArchitecturalShapes.Strip(from, to,
                    side * (element.Width / 2 - stringerWidth / 2), stringerWidth),
                    Math.Max(element.Offset, top - treadThickness - stringerDepth), top - treadThickness);
        }
    }

    /// <summary>Guards: posts and two rails by default, or balusters, glass, cables, or a solid parapet.</summary>
    private static IEnumerable<ElementPart> RailingParts(StructuralElement element)
    {
        if (element.Length < 0.05) yield break;
        if (element.Rise != 0)
        {
            foreach (var part in SlopedRailingParts(element)) yield return part;
            yield break;
        }
        var bottom = element.Offset;
        var top = element.Offset + element.Height;
        switch (element.Style)
        {
            case ElementStyle.Default:
                foreach (var part in CirculationGeometry.Parts(element))
                    yield return Block(element, part.Footprint, part.Offset, part.Offset + part.Height);
                yield break;
            case ElementStyle.SolidParapet:
                const double thickness = 0.15;
                yield return Block(element, ArchitecturalShapes.Strip(element.Start, element.End, 0, thickness),
                    bottom, top - 0.05);
                yield return Block(element, ArchitecturalShapes.Strip(element.Start, element.End, 0, thickness + 0.06),
                    top - 0.05, top);
                yield break;
        }

        // Posts every 1.8 m (1.2 m for cables), always at both ends, then the style's infill and a top rail.
        var postSpacing = element.Style == ElementStyle.Cable ? 1.2 : 1.8;
        var intervals = (int)Math.Ceiling(element.Length / postSpacing);
        for (var index = 0; index <= intervals; index++)
        {
            var position = ArchitecturalShapes.Lerp(element.Start, element.End, (double)index / intervals);
            yield return Block(element, ArchitecturalShapes.PointFootprint(position, Math.Max(element.Width, RailPost), false),
                bottom, top);
        }
        yield return Block(element, ArchitecturalShapes.Strip(element.Start, element.End, 0, element.Width + 0.02),
            top - 0.05, top);
        switch (element.Style)
        {
            case ElementStyle.Pickets:
                yield return Block(element, ArchitecturalShapes.Strip(element.Start, element.End, 0, element.Width),
                    bottom + 0.08, bottom + 0.13);
                const double picket = 0.02;
                var spacing = Math.Max(0.11, element.Length / 2000);
                var count = (int)Math.Floor(element.Length / spacing);
                for (var index = 1; index < count; index++)
                    yield return Block(element, ArchitecturalShapes.PointFootprint(
                        ArchitecturalShapes.Lerp(element.Start, element.End, index * spacing / element.Length), picket, false),
                        bottom + 0.13, top - 0.05);
                break;
            case ElementStyle.GlassPanel:
                yield return Block(element, ArchitecturalShapes.Strip(element.Start, element.End, 0, 0.012),
                    bottom + 0.10, top - 0.05, SurfaceKind.Glass);
                break;
            case ElementStyle.Cable:
                for (var index = 1; index <= 5; index++)
                {
                    var height = bottom + 0.12 + (element.Height - 0.22) * (index - 1) / 4.0;
                    yield return Block(element, ArchitecturalShapes.Strip(element.Start, element.End, 0, 0.012),
                        height, height + 0.012);
                }
                break;
        }
    }

    /// <summary>A guard that follows a rising stair or ramp: level posts at the stair's height, sloped rails and infill.</summary>
    private static IEnumerable<ElementPart> SlopedRailingParts(StructuralElement element)
    {
        var length = element.Length;
        double Z(double fraction) => element.Offset + element.Rise * fraction;
        ElementPart Sloped(double lateral, double width, double lower, double upper, SurfaceKind kind = SurfaceKind.Structure) =>
            new(ArchitecturalShapes.Strip(element.Start, element.End, lateral, width), Z(0) + lower, Z(0) + upper, kind)
            { EndBottom = Z(1) + lower, EndTop = Z(1) + upper };
        var height = element.Height;
        if (element.Style == ElementStyle.SolidParapet)
        {
            yield return Sloped(0, 0.15, 0, height - 0.05);
            yield return Sloped(0, 0.21, height - 0.05, height);
            yield break;
        }
        var intervals = Math.Max(1, (int)Math.Ceiling(length / (element.Style == ElementStyle.Cable ? 1.2 : 1.8)));
        for (var index = 0; index <= intervals; index++)
        {
            var fraction = (double)index / intervals;
            yield return Block(element, ArchitecturalShapes.PointFootprint(
                ArchitecturalShapes.Lerp(element.Start, element.End, fraction), Math.Max(element.Width, RailPost), false),
                Z(fraction), Z(fraction) + height);
        }
        yield return Sloped(0, element.Width + 0.02, height - 0.05, height);
        switch (element.Style)
        {
            case ElementStyle.Default:
                yield return Sloped(0, element.Width, height / 2 - 0.05, height / 2);
                break;
            case ElementStyle.GlassPanel:
                yield return Sloped(0, 0.012, 0.10, height - 0.05, SurfaceKind.Glass);
                break;
            case ElementStyle.Cable:
                for (var index = 0; index < 5; index++)
                    yield return Sloped(0, 0.012, 0.12 + (height - 0.22) * index / 4.0, 0.132 + (height - 0.22) * index / 4.0);
                break;
            case ElementStyle.Pickets:
                yield return Sloped(0, element.Width, 0.08, 0.13);
                const double spacing = 0.11;
                var count = (int)Math.Min(2000, Math.Floor(length / spacing));
                for (var index = 1; index < count; index++)
                {
                    var fraction = index * spacing / length;
                    yield return Block(element, ArchitecturalShapes.PointFootprint(
                        ArchitecturalShapes.Lerp(element.Start, element.End, fraction), 0.02, false),
                        Z(fraction) + 0.13, Z(fraction) + height - 0.05);
                }
                break;
        }
    }

    /// <summary>A profile on both faces of the host wall: proud by 2.5 cm for a sweep, 4 mm for a reveal line.</summary>
    private static IEnumerable<ElementPart> WallSweepParts(StructuralElement element)
    {
        if (element.Length < 0.05) yield break;
        var reveal = element.Kind == StructuralElementKind.WallReveal;
        var proud = reveal ? 0.004 : 0.025;
        foreach (var side in new[] { -1, 1 })
            yield return Block(element, ArchitecturalShapes.Strip(element.Start, element.End,
                side * (element.Width / 2 + proud / 2), proud), element.Offset, element.Offset + element.Height,
                reveal ? SurfaceKind.OpeningFrame : SurfaceKind.Wall);
    }

    /// <summary>A wedge rising from the start (thin) to the end (full height).</summary>
    private static IEnumerable<ElementPart> RampParts(StructuralElement element)
    {
        if (element.Length < 0.05) yield break;
        var toe = Math.Min(0.08, element.Height / 2);
        yield return new ElementPart(ArchitecturalShapes.Strip(element.Start, element.End, 0, element.Width),
            element.Offset, element.Offset + toe, SurfaceKind.Structure) { EndTop = element.Offset + element.Height };
    }

    /// <summary>A U-shaped channel: a thin bottom and two thin sides, 8 mm thick.</summary>
    private static IEnumerable<ElementPart> GutterParts(StructuralElement element)
    {
        if (element.Length < 0.05) yield break;
        const double sheet = 0.008;
        yield return Block(element, ArchitecturalShapes.Strip(element.Start, element.End, 0, element.Width),
            element.Offset, element.Offset + sheet);
        foreach (var side in new[] { -1, 1 })
            yield return Block(element, ArchitecturalShapes.Strip(element.Start, element.End,
                side * (element.Width / 2 - sheet / 2), sheet), element.Offset, element.Offset + element.Height);
    }

    private static IEnumerable<ElementPart> RoundPointParts(StructuralElement element)
    {
        var footprint = ArchitecturalShapes.PointFootprint(element.Start, element.Width, true);
        yield return Block(element, footprint, element.Offset, element.Offset + element.Height);
    }

    /// <summary>Wide-flange and tee members as stacked flange and web boxes along the axis.</summary>
    private static IEnumerable<ElementPart> SectionedMemberParts(StructuralElement element)
    {
        var flange = Math.Clamp(element.Height * 0.12, 0.01, element.Height / 3);
        var web = Math.Max(0.01, element.Width * 0.12);
        var bottom = element.Offset;
        var top = element.Offset + element.Height;
        if (element.Style == ElementStyle.IShape)
            yield return Block(element, ArchitecturalShapes.Strip(element.Start, element.End, 0, element.Width),
                bottom, bottom + flange);
        yield return Block(element, ArchitecturalShapes.Strip(element.Start, element.End, 0, web),
            element.Style == ElementStyle.IShape ? bottom + flange : bottom, top - flange);
        yield return Block(element, ArchitecturalShapes.Strip(element.Start, element.End, 0, element.Width),
            top - flange, top);
    }

    /// <summary>A pane of glass in a grid of mullions (every 1.5 m) and transoms (every 1.2 m).</summary>
    private static IEnumerable<ElementPart> CurtainWallParts(StructuralElement element)
    {
        if (element.Length < 0.05) yield break;
        if (element.Style == ElementStyle.BreezeBlock)
        {
            foreach (var part in BreezeBlockParts(element)) yield return part;
            yield break;
        }
        const double member = 0.06;
        var bottom = element.Offset;
        var top = element.Offset + element.Height;
        yield return Block(element, ArchitecturalShapes.Strip(element.Start, element.End, 0, 0.012),
            bottom, top, SurfaceKind.Glass);
        var columns = Math.Max(1, (int)Math.Ceiling(element.Length / (element.GridWidth > 0 ? element.GridWidth : 1.5)));
        for (var index = 0; index <= columns; index++)
        {
            var fraction = (double)index / columns;
            var from = ArchitecturalShapes.Lerp(element.Start, element.End,
                Math.Max(0, fraction - member / 2 / element.Length));
            var to = ArchitecturalShapes.Lerp(element.Start, element.End,
                Math.Min(1, fraction + member / 2 / element.Length));
            yield return Block(element, ArchitecturalShapes.Strip(from, to, 0, element.Width), bottom, top, SurfaceKind.OpeningFrame);
        }
        var rows = Math.Max(1, (int)Math.Ceiling(element.Height / (element.GridHeight > 0 ? element.GridHeight : 1.2)));
        for (var index = 0; index <= rows; index++)
        {
            var center = bottom + element.Height * index / rows;
            yield return Block(element, ArchitecturalShapes.Strip(element.Start, element.End, 0, element.Width),
                Math.Max(bottom, center - member / 2), Math.Min(top, center + member / 2), SurfaceKind.OpeningFrame);
        }
    }

    /// <summary>
    /// A breeze-block screen: thin masonry webs on a square module (the grid spacing, 20 cm by default) through the full
    /// depth of the screen, leaving real holes so sunlight and views pass through the cells.
    /// </summary>
    private static IEnumerable<ElementPart> BreezeBlockParts(StructuralElement element)
    {
        const double web = 0.04;
        var bottom = element.Offset;
        var top = element.Offset + element.Height;
        var columns = Math.Max(1, (int)Math.Round(element.Length / (element.GridWidth > 0 ? element.GridWidth : 0.20)));
        for (var index = 0; index <= columns; index++)
        {
            var fraction = (double)index / columns;
            var from = ArchitecturalShapes.Lerp(element.Start, element.End, Math.Max(0, fraction - web / 2 / element.Length));
            var to = ArchitecturalShapes.Lerp(element.Start, element.End, Math.Min(1, fraction + web / 2 / element.Length));
            yield return Block(element, ArchitecturalShapes.Strip(from, to, 0, element.Width), bottom, top, SurfaceKind.Wall);
        }
        var rows = Math.Max(1, (int)Math.Round(element.Height / (element.GridHeight > 0 ? element.GridHeight : 0.20)));
        for (var index = 0; index <= rows; index++)
        {
            var center = bottom + element.Height * index / rows;
            yield return Block(element, ArchitecturalShapes.Strip(element.Start, element.End, 0, element.Width),
                Math.Max(bottom, center - web / 2), Math.Min(top, center + web / 2), SurfaceKind.Wall);
        }
    }

    /// <summary>A curb of four bars around a glass pane.</summary>
    private static IEnumerable<ElementPart> SkylightParts(StructuralElement element)
    {
        var extent = element.PlanExtent;
        const double bar = 0.08;
        var width = extent.MaxX - extent.MinX;
        var depth = extent.MaxY - extent.MinY;
        if (width <= 2 * bar + 0.05 || depth <= 2 * bar + 0.05) yield break;
        var bottom = element.Offset;
        var top = element.Offset + element.Height;
        IReadOnlyList<Point2> Rectangle(double x0, double y0, double x1, double y1) =>
            [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)];
        yield return Block(element, Rectangle(extent.MinX, extent.MinY, extent.MaxX, extent.MinY + bar), bottom, top);
        yield return Block(element, Rectangle(extent.MinX, extent.MaxY - bar, extent.MaxX, extent.MaxY), bottom, top);
        yield return Block(element, Rectangle(extent.MinX, extent.MinY + bar, extent.MinX + bar, extent.MaxY - bar), bottom, top);
        yield return Block(element, Rectangle(extent.MaxX - bar, extent.MinY + bar, extent.MaxX, extent.MaxY - bar), bottom, top);
        yield return Block(element, Rectangle(extent.MinX + bar, extent.MinY + bar, extent.MaxX - bar,
            extent.MaxY - bar), top - 0.03, top - 0.015, SurfaceKind.Glass);
    }
}
