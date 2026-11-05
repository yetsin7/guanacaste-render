namespace Guanacaste3D.Core.Import;

/// <summary>A planar point used by the polygon mesher.</summary>
internal readonly record struct P2(double X, double Y);

/// <summary>
/// Triangulates a simple polygon that may have holes. Holes are joined to the outline with zero-width bridges
/// (Eberly's method) and the resulting weakly simple polygon is ear-clipped. Triangles index the combined vertex
/// list: the outline first, then each hole in the order given. The routine tolerates collinear and duplicated
/// points and always terminates, dropping a sliver rather than failing on slightly invalid input.
/// </summary>
internal static class PolygonMesher
{
    private const double Epsilon = 1e-12;

    public static List<(int A, int B, int C)> Triangulate(IReadOnlyList<P2> outline, IReadOnlyList<IReadOnlyList<P2>>? holes = null)
    {
        var points = new List<P2>(outline);
        var ring = Enumerable.Range(0, outline.Count).ToList();
        if (SignedArea(points, ring) < 0) ring.Reverse();

        if (holes is { Count: > 0 })
        {
            var holeRings = new List<List<int>>();
            foreach (var hole in holes)
            {
                if (hole.Count < 3) continue;
                var start = points.Count;
                points.AddRange(hole);
                var indices = Enumerable.Range(start, hole.Count).ToList();
                if (SignedArea(points, indices) > 0) indices.Reverse(); // holes run clockwise
                holeRings.Add(indices);
            }
            // Bridging the rightmost hole first keeps every later bridge from crossing an earlier one.
            foreach (var hole in holeRings.OrderByDescending(h => h.Max(i => points[i].X)))
                ring = Bridge(points, ring, hole);
        }
        return EarClip(points, ring);
    }

    public static double SignedArea(IReadOnlyList<P2> points, IReadOnlyList<int> ring)
    {
        var sum = 0.0;
        for (var i = 0; i < ring.Count; i++)
        {
            var a = points[ring[i]];
            var b = points[ring[(i + 1) % ring.Count]];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return sum / 2;
    }

    /// <summary>Joins <paramref name="hole"/> to <paramref name="ring"/> through a pair of coincident edges.</summary>
    private static List<int> Bridge(List<P2> points, List<int> ring, List<int> hole)
    {
        var rightmost = 0;
        for (var i = 1; i < hole.Count; i++)
            if (points[hole[i]].X > points[hole[rightmost]].X) rightmost = i;
        var m = points[hole[rightmost]];

        // Find the closest outline edge hit by a ray from the hole's rightmost vertex toward +x.
        var bestX = double.MaxValue;
        var edge = -1;
        for (var i = 0; i < ring.Count; i++)
        {
            var a = points[ring[i]];
            var b = points[ring[(i + 1) % ring.Count]];
            if ((a.Y > m.Y) == (b.Y > m.Y) && !(a.Y == m.Y && b.Y == m.Y)) continue;
            if (a.Y == b.Y) continue;
            var x = a.X + (m.Y - a.Y) / (b.Y - a.Y) * (b.X - a.X);
            if (x >= m.X - Epsilon && x < bestX)
            {
                bestX = x;
                edge = i;
            }
        }
        if (edge < 0) return ring; // the hole lies outside the outline; leave it out rather than corrupt the ring

        var endA = edge;
        var endB = (edge + 1) % ring.Count;
        var visible = points[ring[endA]].X >= points[ring[endB]].X ? endA : endB;
        var intersection = new P2(bestX, m.Y);

        // A reflex outline vertex inside the triangle (M, intersection, candidate) would block the view; prefer the one
        // that makes the smallest angle with the ray.
        var candidate = points[ring[visible]];
        var best = visible;
        var bestAngle = double.MaxValue;
        for (var i = 0; i < ring.Count; i++)
        {
            var p = points[ring[i]];
            if (i == visible || !InsideTriangle(m, intersection, candidate, p)) continue;
            if (!IsReflex(points, ring, i)) continue;
            var angle = Math.Abs(Math.Atan2(p.Y - m.Y, p.X - m.X));
            if (angle < bestAngle || (angle == bestAngle && p.X < points[ring[best]].X))
            {
                bestAngle = angle;
                best = i;
            }
        }

        var merged = new List<int>(ring.Count + hole.Count + 2);
        for (var i = 0; i <= best; i++) merged.Add(ring[i]);
        for (var k = 0; k <= hole.Count; k++) merged.Add(hole[(rightmost + k) % hole.Count]);
        merged.Add(ring[best]);
        for (var i = best + 1; i < ring.Count; i++) merged.Add(ring[i]);
        return merged;
    }

    private static bool IsReflex(List<P2> points, List<int> ring, int i)
    {
        var previous = points[ring[(i + ring.Count - 1) % ring.Count]];
        var current = points[ring[i]];
        var next = points[ring[(i + 1) % ring.Count]];
        return Cross(previous, current, next) < 0;
    }

    private static bool InsideTriangle(P2 a, P2 b, P2 c, P2 p)
    {
        var d1 = Cross(a, b, p);
        var d2 = Cross(b, c, p);
        var d3 = Cross(c, a, p);
        var negative = d1 < 0 || d2 < 0 || d3 < 0;
        var positive = d1 > 0 || d2 > 0 || d3 > 0;
        return !(negative && positive) && p != a && p != b && p != c;
    }

    /// <summary>Twice the signed area of triangle (a, b, c); positive when counter-clockwise.</summary>
    private static double Cross(P2 a, P2 b, P2 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static List<(int, int, int)> EarClip(List<P2> points, List<int> ring)
    {
        var triangles = new List<(int, int, int)>(Math.Max(0, ring.Count - 2));
        var remaining = new List<int>(ring);
        var guard = 0;
        var limit = remaining.Count * remaining.Count + 16;
        while (remaining.Count > 3 && guard++ < limit)
        {
            var clipped = false;
            for (var i = 0; i < remaining.Count; i++)
            {
                var iPrevious = remaining[(i + remaining.Count - 1) % remaining.Count];
                var iCurrent = remaining[i];
                var iNext = remaining[(i + 1) % remaining.Count];
                var a = points[iPrevious];
                var b = points[iCurrent];
                var c = points[iNext];
                var area = Cross(a, b, c);
                if (Math.Abs(area) < Epsilon * (1 + Math.Abs(a.X) + Math.Abs(b.X) + Math.Abs(c.X)))
                {
                    // A collinear or repeated vertex contributes no area; removing it keeps the outline unchanged.
                    remaining.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (area < 0) continue;
                var blocked = false;
                foreach (var other in remaining)
                {
                    if (other == iPrevious || other == iCurrent || other == iNext) continue;
                    var p = points[other];
                    if (p == a || p == b || p == c) continue;
                    if (InsideTriangle(a, b, c, p))
                    {
                        blocked = true;
                        break;
                    }
                }
                if (blocked) continue;
                triangles.Add((iPrevious, iCurrent, iNext));
                remaining.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped)
            {
                // No ear exists (self-touching input). Drop the flattest corner so the loop still finishes.
                var flattest = 0;
                var smallest = double.MaxValue;
                for (var i = 0; i < remaining.Count; i++)
                {
                    var area = Math.Abs(Cross(points[remaining[(i + remaining.Count - 1) % remaining.Count]],
                        points[remaining[i]], points[remaining[(i + 1) % remaining.Count]]));
                    if (area < smallest)
                    {
                        smallest = area;
                        flattest = i;
                    }
                }
                remaining.RemoveAt(flattest);
            }
        }
        if (remaining.Count == 3 && Math.Abs(Cross(points[remaining[0]], points[remaining[1]], points[remaining[2]])) > Epsilon)
            triangles.Add((remaining[0], remaining[1], remaining[2]));
        return triangles;
    }
}
