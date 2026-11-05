namespace Guanacaste3D.Core;

/// <summary>A wall-bounded room face with zero or more interior courtyards.</summary>
public sealed record RoomRegion(IReadOnlyList<Point2> Boundary)
{
    /// <summary>Clockwise interior rings excluded from the room's usable area.</summary>
    public IReadOnlyList<IReadOnlyList<Point2>> Holes { get; init; } = [];

    /// <summary>A completely nested face; by default it denotes a courtyard, not an indoor room.</summary>
    public bool IsCourtyard { get; init; }

    public Point2 Min => new(Boundary.Min(point => point.X), Boundary.Min(point => point.Y));
    public Point2 Max => new(Boundary.Max(point => point.X), Boundary.Max(point => point.Y));
    public double Area => RoomRegionGeometry.Area(Boundary) - Holes.Sum(RoomRegionGeometry.Area);
}

/// <summary>
/// Shared polygon measurements for persisted room footprints. Independent from any view or WPF
/// library, so drawings, schedules and file round trips measure the same room geometry.
/// </summary>
public static class RoomRegionGeometry
{
    public static double SignedArea(IReadOnlyList<Point2> boundary)
    {
        if (boundary.Count < 3) return 0;
        // Local coordinates avoid cancellation for projects far from the world origin.
        var origin = boundary[0];
        double twice = 0;
        for (var i = 1; i < boundary.Count - 1; i++)
        {
            var a = boundary[i];
            var b = boundary[i + 1];
            twice += (a.X - origin.X) * (b.Y - origin.Y) -
                     (b.X - origin.X) * (a.Y - origin.Y);
        }
        return twice / 2;
    }

    public static double Area(IReadOnlyList<Point2> boundary) => Math.Abs(SignedArea(boundary));

    public static IReadOnlyList<Point2> Outline(Room room)
    {
        if (room.Boundary is { Count: >= 3 } boundary) return boundary;
        var minX = Math.Min(room.FirstCorner.X, room.SecondCorner.X);
        var maxX = Math.Max(room.FirstCorner.X, room.SecondCorner.X);
        var minY = Math.Min(room.FirstCorner.Y, room.SecondCorner.Y);
        var maxY = Math.Max(room.FirstCorner.Y, room.SecondCorner.Y);
        return [new(minX, minY), new(maxX, minY), new(maxX, maxY), new(minX, maxY)];
    }

    /// <summary>Checks self-intersection and degenerate edges, including nonadjacent edge touches.</summary>
    public static bool IsValid(IReadOnlyList<Point2> polygon)
    {
        if (polygon.Count is < 3 or > 2048 ||
            polygon.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)) ||
            Area(polygon) < 0.1) return false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            if (a.DistanceTo(b) < 1e-7) return false;
            for (var j = i + 1; j < polygon.Count; j++)
            {
                if (j == i + 1 || i == 0 && j == polygon.Count - 1) continue;
                var c = polygon[j];
                var d = polygon[(j + 1) % polygon.Count];
                if (Intersect(a, b, c, d)) return false;
            }
        }
        return true;
    }

    private static double Cross(Point2 a, Point2 b, Point2 c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static bool OnSegment(Point2 a, Point2 b, Point2 point) =>
        Math.Abs(Cross(a, b, point)) < 1e-9 &&
        point.X >= Math.Min(a.X, b.X) - 1e-9 && point.X <= Math.Max(a.X, b.X) + 1e-9 &&
        point.Y >= Math.Min(a.Y, b.Y) - 1e-9 && point.Y <= Math.Max(a.Y, b.Y) + 1e-9;

    private static bool Intersect(Point2 a, Point2 b, Point2 c, Point2 d)
    {
        var ac = Cross(a, b, c);
        var ad = Cross(a, b, d);
        var ca = Cross(c, d, a);
        var cb = Cross(c, d, b);
        if ((ac > 0 && ad < 0 || ac < 0 && ad > 0) &&
            (ca > 0 && cb < 0 || ca < 0 && cb > 0)) return true;
        return Math.Abs(ac) < 1e-9 && OnSegment(a, b, c) ||
               Math.Abs(ad) < 1e-9 && OnSegment(a, b, d) ||
               Math.Abs(ca) < 1e-9 && OnSegment(c, d, a) ||
               Math.Abs(cb) < 1e-9 && OnSegment(c, d, b);
    }

    public static bool Contains(IReadOnlyList<Point2> polygon, Point2 point)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];
            if ((a.Y > point.Y) != (b.Y > point.Y) &&
                point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>Centroid, with a guaranteed interior fallback for concave orthogonal footprints.</summary>
    public static Point2 LabelPoint(IReadOnlyList<Point2> polygon)
    {
        var first = polygon[0];
        double twice = 0, x = 0, y = 0;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = new Point2(polygon[i].X - first.X, polygon[i].Y - first.Y);
            var b = new Point2(polygon[(i + 1) % polygon.Count].X - first.X,
                polygon[(i + 1) % polygon.Count].Y - first.Y);
            var cross = a.X * b.Y - b.X * a.Y;
            twice += cross;
            x += (a.X + b.X) * cross;
            y += (a.Y + b.Y) * cross;
        }
        if (Math.Abs(twice) > 1e-8)
        {
            var centroid = new Point2(first.X + x / (3 * twice), first.Y + y / (3 * twice));
            if (Contains(polygon, centroid)) return centroid;
        }

        var xs = polygon.Select(point => point.X).Distinct().OrderBy(value => value).ToArray();
        var ys = polygon.Select(point => point.Y).Distinct().OrderBy(value => value).ToArray();
        var largest = -1d;
        var best = polygon[0];
        for (var i = 0; i + 1 < xs.Length; i++)
        for (var j = 0; j + 1 < ys.Length; j++)
        {
            var midpoint = new Point2((xs[i] + xs[i + 1]) / 2, (ys[j] + ys[j + 1]) / 2);
            var area = (xs[i + 1] - xs[i]) * (ys[j + 1] - ys[j]);
            if (area > largest && Contains(polygon, midpoint))
            {
                largest = area;
                best = midpoint;
            }
        }
        return best;
    }


    /// <summary>Tests the occupied room face, excluding all courtyard holes.</summary>
    public static bool Contains(Room room, Point2 point) =>
        Contains(Outline(room), point) &&
        (room.Holes is null || room.Holes.All(hole => !Contains(hole, point)));

    /// <summary>Tests a discovered bounded face including its excluded inner rings.</summary>
    public static bool Contains(RoomRegion region, Point2 point) =>
        Contains(region.Boundary, point) && region.Holes.All(hole => !Contains(hole, point));

    /// <summary>Returns a label position in the usable area, not in a courtyard.</summary>
    public static Point2 LabelPoint(IReadOnlyList<Point2> outline, IReadOnlyList<IReadOnlyList<Point2>> holes)
    {
        var centroid = LabelPoint(outline);
        if (holes.All(hole => !Contains(hole, centroid))) return centroid;
        var xs = outline.Concat(holes.SelectMany(h => h)).Select(point => point.X)
            .Distinct().OrderBy(value => value).ToArray();
        var ys = outline.Concat(holes.SelectMany(h => h)).Select(point => point.Y)
            .Distinct().OrderBy(value => value).ToArray();
        double largest = -1;
        var best = centroid;
        for (var i = 1; i < xs.Length; i++)
        for (var j = 1; j < ys.Length; j++)
        {
            var point = new Point2((xs[i - 1] + xs[i]) / 2, (ys[j - 1] + ys[j]) / 2);
            var size = (xs[i] - xs[i - 1]) * (ys[j] - ys[j - 1]);
            if (size > largest && Contains(outline, point) && holes.All(hole => !Contains(hole, point)))
            {
                largest = size;
                best = point;
            }
        }
        return best;
    }

    /// <summary>Validates simple internal rings, rejecting rings outside the room or overlapping each other.</summary>
    public static bool ValidRegion(IReadOnlyList<Point2> outline, IReadOnlyList<IReadOnlyList<Point2>> holes)
    {
        if (!IsValid(outline) || holes.Count > 64) return false;
        double usable = Area(outline);
        foreach (var hole in holes)
        {
            if (!IsValid(hole) || hole.Any(vertex => !Contains(outline, vertex))) return false;
            if (TouchOrIntersect(outline, hole)) return false;
            usable -= Area(hole);
        }
        for (var i = 0; i < holes.Count; i++)
        for (var j = i + 1; j < holes.Count; j++)
            if (TouchOrIntersect(holes[i], holes[j]) ||
                Contains(holes[i], holes[j][0]) || Contains(holes[j], holes[i][0]))
                return false;
        return usable >= 0.1;
    }

    private static bool TouchOrIntersect(IReadOnlyList<Point2> first, IReadOnlyList<Point2> second)
    {
        for (var i = 0; i < first.Count; i++)
        for (var j = 0; j < second.Count; j++)
            if (Intersect(first[i], first[(i + 1) % first.Count],
                second[j], second[(j + 1) % second.Count])) return true;
        return false;
    }

    /// <summary>
    /// Precise intersection area for simple polygons, including diagonal and tessellated arc edges.
    /// Both polygons are triangulated and their convex triangle pairs are clipped against each other.
    /// </summary>
    public static double IntersectionAreaGeneral(IReadOnlyList<Point2> a, IReadOnlyList<Point2> b)
    {
        var trianglesA = Triangulate(a);
        var trianglesB = Triangulate(b);
        var result = 0d;
        foreach (var first in trianglesA)
        foreach (var second in trianglesB)
        {
            IReadOnlyList<Point2> polygon = first;
            for (var edge = 0; edge < 3 && polygon.Count > 0; edge++)
                polygon = Clip(polygon, second[edge], second[(edge + 1) % 3]);
            result += Area(polygon);
        }
        return result;
    }

    /// <summary>Overlap of two room faces after subtracting every interior courtyard.</summary>
    public static double IntersectionArea(Room room, RoomRegion region)
    {
        var a = Outline(room);
        var b = region.Boundary;
        var overlap = IntersectionAreaGeneral(a, b);
        foreach (var hole in room.Holes ?? [])
            overlap -= IntersectionAreaGeneral(hole, b);
        foreach (var hole in region.Holes)
            overlap -= IntersectionAreaGeneral(a, hole);
        foreach (var holeA in room.Holes ?? [])
        foreach (var holeB in region.Holes)
            overlap += IntersectionAreaGeneral(holeA, holeB);
        return Math.Max(0, overlap);
    }

    private static IReadOnlyList<Point2> Clip(IReadOnlyList<Point2> polygon, Point2 a, Point2 b)
    {
        if (polygon.Count == 0) return [];
        var output = new List<Point2>();
        var prev = polygon[^1];
        var previousDistance = Cross(a, b, prev);
        foreach (var current in polygon)
        {
            var distance = Cross(a, b, current);
            if ((distance >= -1e-9) != (previousDistance >= -1e-9))
            {
                var t = previousDistance / (previousDistance - distance);
                output.Add(new Point2(prev.X + t * (current.X - prev.X),
                    prev.Y + t * (current.Y - prev.Y)));
            }
            if (distance >= -1e-9) output.Add(current);
            prev = current;
            previousDistance = distance;
        }
        return output;
    }

    private static List<Point2[]> Triangulate(IReadOnlyList<Point2> polygon)
    {
        var points = polygon.ToList();
        if (SignedArea(points) < 0) points.Reverse();
        var triangles = new List<Point2[]>();
        // Ear clipping needs no external geometry engine; reject non-simple input before calling.
        for (var limit = 0; points.Count > 3 && limit < 2048; limit++)
        {
            var ear = false;
            for (var i = 0; i < points.Count; i++)
            {
                var a = points[(i + points.Count - 1) % points.Count];
                var b = points[i];
                var c = points[(i + 1) % points.Count];
                var turn = Cross(a, b, c);
                if (turn < -1e-9) continue;
                if (Math.Abs(turn) <= 1e-9)
                {
                    points.RemoveAt(i);
                    ear = true;
                    break;
                }
                bool inside = false;
                for (var j = 0; j < points.Count; j++)
                {
                    if (j == i || j == (i + points.Count - 1) % points.Count ||
                        j == (i + 1) % points.Count) continue;
                    var p = points[j];
                    if (Cross(a, b, p) >= -1e-9 && Cross(b, c, p) >= -1e-9 &&
                        Cross(c, a, p) >= -1e-9)
                    {
                        inside = true;
                        break;
                    }
                }
                if (inside) continue;
                triangles.Add([a, b, c]);
                points.RemoveAt(i);
                ear = true;
                break;
            }
            if (!ear) throw new InvalidDataException("The room polygon cannot be triangulated.");
        }
        if (points.Count == 3) triangles.Add([points[0], points[1], points[2]]);
        if (triangles.Count == 0 && Area(polygon) > 0.1)
            throw new InvalidDataException("The room polygon cannot be triangulated.");
        return triangles;
    }

    /// <summary>
    /// Parallel offset for a simple, orthogonal footprint. A positive value grows the footprint
    /// outward, including its inward notches; a zero value returns the unchanged outline.
    /// Invalid or self-crossing offsets are rejected instead of changing a roof into a rectangle.
    /// </summary>
    public static IReadOnlyList<Point2> OffsetOrthogonal(IReadOnlyList<Point2> boundary, double margin)
    {
        if (!double.IsFinite(margin) || margin < 0 || margin > 5 || !IsValid(boundary))
            throw new InvalidDataException("The room outline cannot be offset.");
        if (margin < 1e-10) return boundary;
        var normalized = SignedArea(boundary) >= 0
            ? boundary.ToList() : boundary.Reverse().ToList();
        // Remove redundant straight-run vertices before intersecting parallel offset lines.
        for (var i = normalized.Count - 1; i >= 0 && normalized.Count > 3; i--)
        {
            var a = normalized[(i + normalized.Count - 1) % normalized.Count];
            var b = normalized[i];
            var c = normalized[(i + 1) % normalized.Count];
            if (Math.Abs(Cross(a, b, c)) < 1e-8)
                normalized.RemoveAt(i);
        }

        var result = new List<Point2>();
        for (var i = 0; i < normalized.Count; i++)
        {
            var previous = normalized[(i + normalized.Count - 1) % normalized.Count];
            var current = normalized[i];
            var next = normalized[(i + 1) % normalized.Count];
            var prevHorizontal = Math.Abs(previous.Y - current.Y) < 1e-8;
            var nextHorizontal = Math.Abs(next.Y - current.Y) < 1e-8;
            if (prevHorizontal == nextHorizontal)
                throw new InvalidDataException("The room outline cannot be offset.");
            double x, y;
            if (prevHorizontal)
            {
                y = current.Y - Math.Sign(current.X - previous.X) * margin;
                x = current.X + Math.Sign(next.Y - current.Y) * margin;
            }
            else
            {
                x = current.X + Math.Sign(current.Y - previous.Y) * margin;
                y = current.Y - Math.Sign(next.X - current.X) * margin;
            }
            result.Add(new Point2(x, y));
        }
        if (!IsValid(result)) throw new InvalidDataException("The room outline cannot be offset.");
        return result;
    }

    /// <summary>Exact planar intersection area for orthogonal polygons, including concave L-shaped rooms.</summary>
    public static double IntersectionArea(IReadOnlyList<Point2> a, IReadOnlyList<Point2> b)
    {
        var xs = a.Concat(b).Select(point => point.X).Distinct().OrderBy(value => value).ToArray();
        var ys = a.Concat(b).Select(point => point.Y).Distinct().OrderBy(value => value).ToArray();
        var total = 0d;
        for (var i = 0; i + 1 < xs.Length; i++)
        for (var j = 0; j + 1 < ys.Length; j++)
        {
            var center = new Point2((xs[i] + xs[i + 1]) / 2, (ys[j] + ys[j + 1]) / 2);
            if (Contains(a, center) && Contains(b, center))
                total += (xs[i + 1] - xs[i]) * (ys[j + 1] - ys[j]);
        }
        return total;
    }
}
