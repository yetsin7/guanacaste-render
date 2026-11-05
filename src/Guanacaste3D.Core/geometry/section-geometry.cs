using System.Numerics;

namespace Guanacaste3D.Core.Geometry;

/// <summary>A continuous model-plane contour; coordinates are distance along the cut and absolute elevation.</summary>
public sealed record SectionContour(IReadOnlyList<Point2> Points, bool Closed, SurfaceKind Kind);

/// <summary>Intersects the same triangulated geometry used by 3D and rendering, including foundations and families.</summary>
public static class SectionGeometry
{
    private const double Tolerance = 0.00001;

    /// <summary>Returns welded cut profiles, omitting geometry outside the finite cutting line.</summary>
    public static IReadOnlyList<SectionContour> Build(SceneMesh mesh, SectionCut cut)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (cut is null || !cut.IsValid) throw new ArgumentException("A valid cutting line is required.", nameof(cut));
        var length = cut.Start.DistanceTo(cut.End);
        var along = new Point2((cut.End.X - cut.Start.X) / length, (cut.End.Y - cut.Start.Y) / length);
        var segments = new Dictionary<SurfaceKind, List<(Point2 Start, Point2 End)>>();
        for (var triangle = 0; triangle < mesh.TriangleCount; triangle++)
        {
            var vertices = new[] { mesh.Positions[mesh.Indices[triangle * 3]],
                mesh.Positions[mesh.Indices[triangle * 3 + 1]], mesh.Positions[mesh.Indices[triangle * 3 + 2]] };
            var distances = vertices.Select(vertex => SignedDistance(vertex, cut.Start, along)).ToArray();
            if (distances.All(distance => Math.Abs(distance) <= Tolerance) ||
                distances.All(distance => distance > Tolerance) || distances.All(distance => distance < -Tolerance)) continue;
            var intersections = new List<Point2>(3);
            for (var edge = 0; edge < 3; edge++)
            {
                var next = (edge + 1) % 3;
                if (Math.Abs(distances[edge]) <= Tolerance) intersections.Add(Project(vertices[edge], cut.Start, along));
                if (distances[edge] * distances[next] < -Tolerance * Tolerance)
                {
                    var fraction = distances[edge] / (distances[edge] - distances[next]);
                    var point = Vector3.Lerp(vertices[edge], vertices[next], (float)fraction);
                    intersections.Add(Project(point, cut.Start, along));
                }
            }
            var distinct = intersections.DistinctBy(Key).ToArray();
            if (distinct.Length < 2) continue;
            var start = distinct[0];
            var end = distinct[1];
            if (!ClipToExtent(ref start, ref end, length) || start.DistanceTo(end) < Tolerance) continue;
            // Consistent face winding distinguishes solid exteriors from voids and prevents overlapping solids cancelling their fill.
            var faceNormal = Vector3.Cross(vertices[1] - vertices[0], vertices[2] - vertices[0]);
            var tangent = Vector3.Cross(new((float)along.Y, (float)-along.X, 0), faceNormal);
            var direction = (end.X - start.X) * (tangent.X * along.X + tangent.Y * along.Y) +
                (end.Y - start.Y) * tangent.Z;
            if (direction < 0) (start, end) = (end, start);
            var kind = mesh.TriangleKinds[triangle];
            if (!segments.TryGetValue(kind, out var bucket)) segments[kind] = bucket = [];
            bucket.Add((start, end));
        }
        return segments.SelectMany(pair => Join(pair.Value, pair.Key)).ToArray();
    }

    /// <summary>Computes the signed distance to the vertical cutting plane in plan coordinates.</summary>
    private static double SignedDistance(Vector3 point, Point2 origin, Point2 along) =>
        -(point.X - origin.X) * along.Y + (point.Y - origin.Y) * along.X;

    /// <summary>Maps a model point to the section's metric drawing axes.</summary>
    private static Point2 Project(Vector3 point, Point2 origin, Point2 along) =>
        new((point.X - origin.X) * along.X + (point.Y - origin.Y) * along.Y, point.Z);

    /// <summary>Clips a segment to the horizontal extent of the user-defined cutting line.</summary>
    private static bool ClipToExtent(ref Point2 start, ref Point2 end, double length)
    {
        var minimum = Math.Min(start.X, end.X);
        var maximum = Math.Max(start.X, end.X);
        if (maximum < -Tolerance || minimum > length + Tolerance) return false;
        var delta = end.X - start.X;
        if (Math.Abs(delta) < Tolerance) return true;
        var first = Math.Clamp(start.X, 0, length);
        var last = Math.Clamp(end.X, 0, length);
        var original = start;
        var rise = end.Y - start.Y;
        start = new(first, original.Y + (first - original.X) / delta * rise);
        end = new(last, original.Y + (last - original.X) / delta * rise);
        return true;
    }

    /// <summary>Welds repeated mesh vertices within a small drafting tolerance.</summary>
    private static (long X, long Y) Key(Point2 point) =>
        ((long)Math.Round(point.X / Tolerance), (long)Math.Round(point.Y / Tolerance));

    /// <summary>Stitches triangle intersections into closed profiles without drawing triangle seams.</summary>
    private static IEnumerable<SectionContour> Join(List<(Point2 Start, Point2 End)> segments, SurfaceKind kind)
    {
        var vertices = new Dictionary<(long X, long Y), int>();
        var points = new List<Point2>();
        var edges = new List<(int Start, int End)>();
        foreach (var segment in segments)
        {
            var start = Vertex(segment.Start, vertices, points);
            var end = Vertex(segment.End, vertices, points);
            // Retain directed coincident edges: opposite faces cancel by winding, while overlapping solids remain filled.
            if (start != end) edges.Add((start, end));
        }
        var adjacency = Enumerable.Range(0, points.Count).Select(_ => new List<int>()).ToArray();
        var incoming = new int[points.Count];
        for (var index = 0; index < edges.Count; index++)
        {
            adjacency[edges[index].Start].Add(index);
            incoming[edges[index].End]++;
        }
        var visited = new bool[edges.Count];
        foreach (var index in Enumerable.Range(0, edges.Count).OrderBy(index => incoming[edges[index].Start] == 0 ? 0 : 1))
        {
            if (visited[index]) continue;
            var first = edges[index].Start;
            var current = first;
            var contour = new List<Point2> { points[first] };
            while (true)
            {
                var next = adjacency[current].FirstOrDefault(edge => !visited[edge], -1);
                if (next < 0) break;
                visited[next] = true;
                current = edges[next].End;
                contour.Add(points[current]);
                if (current == first) break;
            }
            yield return new(contour, current == first, kind);
        }
    }

    /// <summary>Assigns stable vertex indices to the quantized endpoints of cut segments.</summary>
    private static int Vertex(Point2 point, Dictionary<(long X, long Y), int> vertices, List<Point2> points)
    {
        var key = Key(point);
        if (vertices.TryGetValue(key, out var index)) return index;
        index = points.Count;
        vertices.Add(key, index);
        points.Add(point);
        return index;
    }
}
