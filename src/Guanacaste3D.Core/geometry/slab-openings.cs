namespace Guanacaste3D.Core.Geometry;

/// <summary>An axis-aligned plan rectangle in meters, used for openings and their bounding boxes.</summary>
public readonly record struct PlanRectangle(double MinX, double MinY, double MaxX, double MaxY)
{
    /// <summary>Returns the smallest rectangle that contains every point.</summary>
    public static PlanRectangle Of(IEnumerable<Point2> points)
    {
        var list = points.ToArray();
        return new PlanRectangle(list.Min(point => point.X), list.Min(point => point.Y),
            list.Max(point => point.X), list.Max(point => point.Y));
    }

    /// <summary>Tests whether a point lies inside or on the boundary of the rectangle.</summary>
    public bool Contains(Point2 point) =>
        point.X >= MinX && point.X <= MaxX && point.Y >= MinY && point.Y <= MaxY;
}

/// <summary>Cuts rectangular openings out of planar floor slabs by splitting their plan into convex pieces.</summary>
public static class SlabOpenings
{
    /// <summary>
    /// Returns convex counter-clockwise pieces that cover the outline minus every opening. The pieces
    /// come from ear-clipping the outline and then subtracting each rectangle with half-plane clips.
    /// </summary>
    public static IReadOnlyList<Point2[]> Pieces(IReadOnlyList<Point2> outline, IReadOnlyList<PlanRectangle> openings)
    {
        ArgumentNullException.ThrowIfNull(outline);
        ArgumentNullException.ThrowIfNull(openings);
        var pieces = PolygonTriangulator.Triangulate(outline)
            .Select(triangle => new[] { outline[triangle.A], outline[triangle.B], outline[triangle.C] })
            .ToList();
        foreach (var opening in openings)
            pieces = pieces.SelectMany(piece => Subtract(piece, opening)).ToList();
        return pieces;
    }

    /// <summary>
    /// Tests whether the entire rectangle is covered by a simple polygon, including its boundary.
    /// Comparing intersection areas also detects concave notches that lie between four interior corners.
    /// </summary>
    public static bool ContainsRectangle(IReadOnlyList<Point2> outline, PlanRectangle rectangle)
    {
        var width = rectangle.MaxX - rectangle.MinX;
        var height = rectangle.MaxY - rectangle.MinY;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            return false;

        var intersectionArea = 0.0;
        foreach (var (a, b, c) in PolygonTriangulator.Triangulate(outline))
        {
            var piece = new[] { outline[a], outline[b], outline[c] };
            piece = ClipX(piece, rectangle.MinX, keepLess: false);
            piece = ClipX(piece, rectangle.MaxX, keepLess: true);
            piece = ClipY(piece, rectangle.MinY, keepLess: false);
            piece = ClipY(piece, rectangle.MaxY, keepLess: true);
            if (piece.Length >= 3)
                intersectionArea += Math.Abs(PolygonTriangulator.SignedDoubleArea(piece)) / 2;
        }

        var area = width * height;
        return Math.Abs(area - intersectionArea) <= Math.Max(1e-10, area * 1e-9);
    }

    /// <summary>Ray-casting test that treats the polygon as simple (non self-intersecting).</summary>
    public static bool ContainsPoint(IReadOnlyList<Point2> polygon, Point2 point)
    {
        var inside = false;
        for (var index = 0; index < polygon.Count; index++)
        {
            var a = polygon[index];
            var b = polygon[(index + 1) % polygon.Count];
            if ((a.Y > point.Y) != (b.Y > point.Y) &&
                point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>Returns the four corners of a rectangle in counter-clockwise order.</summary>
    public static Point2[] Corners(PlanRectangle rectangle) =>
    [
        new(rectangle.MinX, rectangle.MinY), new(rectangle.MaxX, rectangle.MinY),
        new(rectangle.MaxX, rectangle.MaxY), new(rectangle.MinX, rectangle.MaxY)
    ];

    /// <summary>Removes one rectangle from a convex piece. Each side of the rectangle keeps a disjoint convex remainder.</summary>
    private static IEnumerable<Point2[]> Subtract(Point2[] piece, PlanRectangle opening)
    {
        var remainders = new[]
        {
            ClipX(piece, opening.MinX, keepLess: true),
            ClipX(piece, opening.MaxX, keepLess: false)
        };
        var middle = ClipX(ClipX(piece, opening.MinX, keepLess: false), opening.MaxX, keepLess: true);
        remainders = [.. remainders,
            ClipY(middle, opening.MinY, keepLess: true),
            ClipY(middle, opening.MaxY, keepLess: false)];
        return remainders.Where(IsArea);
    }

    private static bool IsArea(Point2[] polygon) =>
        polygon.Length >= 3 && Math.Abs(PolygonTriangulator.SignedDoubleArea(polygon)) > 1e-12;

    private static Point2[] ClipX(Point2[] polygon, double x, bool keepLess) =>
        Clip(polygon, point => keepLess ? point.X - x : x - point.X);

    private static Point2[] ClipY(Point2[] polygon, double y, bool keepLess) =>
        Clip(polygon, point => keepLess ? point.Y - y : y - point.Y);

    /// <summary>Sutherland-Hodgman clip keeping the points whose signed distance is not positive.</summary>
    private static Point2[] Clip(Point2[] polygon, Func<Point2, double> signedDistance)
    {
        var output = new List<Point2>();
        for (var index = 0; index < polygon.Length; index++)
        {
            var current = polygon[index];
            var next = polygon[(index + 1) % polygon.Length];
            var currentDistance = signedDistance(current);
            var nextDistance = signedDistance(next);
            if (currentDistance <= 0) output.Add(current);
            if ((currentDistance < 0 && nextDistance > 0) || (currentDistance > 0 && nextDistance < 0))
            {
                var t = currentDistance / (currentDistance - nextDistance);
                output.Add(new Point2(current.X + t * (next.X - current.X), current.Y + t * (next.Y - current.Y)));
            }
        }
        return output.ToArray();
    }
}
