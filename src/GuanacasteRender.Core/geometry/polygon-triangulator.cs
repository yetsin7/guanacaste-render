namespace Guanacaste3D.Core.Geometry;

/// <summary>Triangulates simple (non self-intersecting) plan polygons, convex or concave.</summary>
public static class PolygonTriangulator
{
    /// <summary>Returns the signed doubled area; positive for counter-clockwise order.</summary>
    public static double SignedDoubleArea(IReadOnlyList<Point2> polygon)
    {
        // Translate the shoelace products to a local origin. Building outlines can be
        // far from (0, 0); multiplying absolute coordinates loses small floor areas.
        if (polygon.Count == 0) return 0;
        var origin = polygon[0];
        var sum = 0d;
        for (var index = 1; index < polygon.Count - 1; index++)
        {
            var current = polygon[index];
            var next = polygon[index + 1];
            sum += (current.X - origin.X) * (next.Y - origin.Y) -
                   (next.X - origin.X) * (current.Y - origin.Y);
        }
        return sum;
    }

    /// <summary>
    /// Ear-clips a simple polygon into counter-clockwise triangles that index the input points.
    /// Throws when the polygon is degenerate or self-intersecting enough to leave no ear.
    /// </summary>
    public static IReadOnlyList<(int A, int B, int C)> Triangulate(IReadOnlyList<Point2> polygon)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        if (polygon.Count < 3) throw new ArgumentException("A polygon needs at least three points.");
        if (polygon.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            throw new ArgumentException("Polygon coordinates must be finite.", nameof(polygon));
        var area = SignedDoubleArea(polygon);
        if (!double.IsFinite(area) || Math.Abs(area) < 1e-12)
            throw new ArgumentException("The polygon must have a finite nonzero area.", nameof(polygon));

        var remaining = Enumerable.Range(0, polygon.Count).ToList();
        if (area < 0) remaining.Reverse();
        var triangles = new List<(int, int, int)>(polygon.Count - 2);
        var guard = 0;
        while (remaining.Count > 3)
        {
            var clipped = false;
            for (var i = 0; i < remaining.Count; i++)
            {
                var previous = remaining[(i + remaining.Count - 1) % remaining.Count];
                var current = remaining[i];
                var next = remaining[(i + 1) % remaining.Count];
                var cross = Cross(polygon[previous], polygon[current], polygon[next]);
                if (Math.Abs(cross) < 1e-14)
                {
                    // Collinear vertex: removing it does not change the covered area.
                    remaining.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (cross < 0 || ContainsOtherVertex(polygon, remaining, previous, current, next)) continue;
                triangles.Add((previous, current, next));
                remaining.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped || ++guard > polygon.Count * polygon.Count)
                throw new ArgumentException("The polygon is self-intersecting or degenerate.");
        }
        if (Math.Abs(Cross(polygon[remaining[0]], polygon[remaining[1]], polygon[remaining[2]])) > 1e-14)
            triangles.Add((remaining[0], remaining[1], remaining[2]));
        return triangles;
    }

    /// <summary>Returns the z component of (b - a) × (c - b).</summary>
    private static double Cross(Point2 a, Point2 b, Point2 c) =>
        (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);

    /// <summary>Tests whether any other remaining vertex lies inside or on the candidate ear.</summary>
    private static bool ContainsOtherVertex(IReadOnlyList<Point2> polygon, List<int> remaining,
        int a, int b, int c)
    {
        foreach (var index in remaining)
        {
            if (index == a || index == b || index == c) continue;
            var p = polygon[index];
            if (p == polygon[a] || p == polygon[b] || p == polygon[c]) continue;
            var d1 = Cross(polygon[a], polygon[b], p);
            var d2 = Cross(polygon[b], polygon[c], p);
            var d3 = Cross(polygon[c], polygon[a], p);
            if (d1 >= 0 && d2 >= 0 && d3 >= 0) return true;
        }
        return false;
    }
}
