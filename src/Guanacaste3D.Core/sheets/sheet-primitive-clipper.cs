using Guanacaste3D.Core.Interop;

namespace Guanacaste3D.Core;

/// <summary>
/// Trims drawing primitives to a plan view's model crop, the way a cropped view prints on a sheet: lines are cut at
/// the boundary (Liang–Barsky), filled shapes are clipped to it (Sutherland–Hodgman) and text outside it is dropped.
/// </summary>
public static class SheetPrimitiveClipper
{
    public static IReadOnlyList<SheetPrimitive> Clip(IEnumerable<SheetPrimitive> primitives, PlanBounds crop)
    {
        var clipped = new List<SheetPrimitive>();
        foreach (var primitive in primitives)
        {
            switch (primitive)
            {
                case SheetPolygon polygon:
                    // An annotation head (a tag or grid bubble) keeps its paper size, so it is kept or dropped whole.
                    if (polygon.PaperRadius is not null)
                    {
                        if (polygon.Points.Any(crop.Contains)) clipped.Add(polygon);
                        break;
                    }
                    var inside = ClipPolygon(polygon.Points, crop);
                    if (inside.Count >= 3) clipped.Add(polygon with { Points = inside });
                    break;
                case SheetPolyline line:
                    for (var index = 0; index + 1 < line.Points.Count; index++)
                        if (ClipSegment(line.Points[index], line.Points[index + 1], crop) is { } segment)
                            clipped.Add(line with { Points = [segment.From, segment.To] });
                    break;
                case SheetText text:
                    if (crop.Contains(text.Position)) clipped.Add(text);
                    break;
                default:
                    clipped.Add(primitive);
                    break;
            }
        }
        return clipped;
    }

    /// <summary>The part of a segment inside the rectangle, or null when it lies wholly outside.</summary>
    public static (Point2 From, Point2 To)? ClipSegment(Point2 from, Point2 to, PlanBounds crop)
    {
        var (dx, dy) = (to.X - from.X, to.Y - from.Y);
        double low = 0, high = 1;
        foreach (var (p, q) in new[]
                 {
                     (-dx, from.X - crop.MinX), (dx, crop.MaxX - from.X), (-dy, from.Y - crop.MinY), (dy, crop.MaxY - from.Y)
                 })
        {
            if (Math.Abs(p) < 1e-12)
            {
                if (q < 0) return null;
                continue;
            }
            var t = q / p;
            if (p < 0) low = Math.Max(low, t);
            else high = Math.Min(high, t);
            if (low > high) return null;
        }
        return (new Point2(from.X + dx * low, from.Y + dy * low), new Point2(from.X + dx * high, from.Y + dy * high));
    }

    /// <summary>The polygon cut to the rectangle, edge by edge.</summary>
    public static IReadOnlyList<Point2> ClipPolygon(IReadOnlyList<Point2> points, PlanBounds crop)
    {
        IReadOnlyList<Point2> result = points;
        foreach (var (inside, cross) in new (Func<Point2, bool>, Func<Point2, Point2, Point2>)[]
                 {
                     (point => point.X >= crop.MinX, (a, b) => AtX(a, b, crop.MinX)),
                     (point => point.X <= crop.MaxX, (a, b) => AtX(a, b, crop.MaxX)),
                     (point => point.Y >= crop.MinY, (a, b) => AtY(a, b, crop.MinY)),
                     (point => point.Y <= crop.MaxY, (a, b) => AtY(a, b, crop.MaxY))
                 })
        {
            if (result.Count == 0) break;
            var output = new List<Point2>();
            for (var index = 0; index < result.Count; index++)
            {
                var current = result[index];
                var previous = result[(index + result.Count - 1) % result.Count];
                if (inside(current))
                {
                    if (!inside(previous)) output.Add(cross(previous, current));
                    output.Add(current);
                }
                else if (inside(previous)) output.Add(cross(previous, current));
            }
            result = output;
        }
        return result;
    }

    private static Point2 AtX(Point2 a, Point2 b, double x) => new(x, a.Y + (b.Y - a.Y) * (x - a.X) / (b.X - a.X));

    private static Point2 AtY(Point2 a, Point2 b, double y) => new(a.X + (b.X - a.X) * (y - a.Y) / (b.Y - a.Y), y);
}
