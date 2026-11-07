using System.Numerics;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Core.Import;

/// <summary>A triangle list (three points per triangle) used while building solids, before vertices are shared.</summary>
internal sealed class TriSoup
{
    public List<Vector3> Points { get; } = [];
    public int TriangleCount => Points.Count / 3;

    public void Add(Vector3 a, Vector3 b, Vector3 c)
    {
        Points.Add(a);
        Points.Add(b);
        Points.Add(c);
    }

    public void Append(TriSoup other) => Points.AddRange(other.Points);

    public TriSoup Transformed(Matrix4x4 matrix)
    {
        var result = new TriSoup();
        foreach (var point in Points) result.Points.Add(Vector3.Transform(point, matrix));
        // A mirroring transform reverses winding; swap two corners of every triangle to restore outward normals.
        if (matrix.GetDeterminant() < 0)
            for (var i = 0; i + 2 < result.Points.Count; i += 3)
                (result.Points[i + 1], result.Points[i + 2]) = (result.Points[i + 2], result.Points[i + 1]);
        return result;
    }

    public (Vector3 Min, Vector3 Max) Bounds()
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var point in Points)
        {
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }
        return (min, max);
    }
}

/// <summary>
/// Cuts closed, outward-facing triangle meshes with planes and closes the cut with a cap. Subtracting a convex
/// solid (a door or window opening) from a wall is done as a sequence of such cuts, which gives exact results for
/// the box-like openings building models contain without needing a general boolean engine.
/// </summary>
internal static class MeshClipper
{
    private const float PlaneTolerance = 2e-5f;

    /// <summary>
    /// Keeps the part of <paramref name="mesh"/> on one side of the plane n·x = d and caps the cut.
    /// </summary>
    public static TriSoup Clip(TriSoup mesh, Vector3 normal, float d, bool keepPositive)
    {
        var sign = keepPositive ? 1f : -1f;
        var result = new TriSoup();
        var cut = new List<(Vector3 From, Vector3 To)>();
        var points = mesh.Points;
        Span<Vector3> polygon = stackalloc Vector3[4];
        Span<float> distance = stackalloc float[3];
        Span<Vector3> corner = stackalloc Vector3[3];

        for (var t = 0; t + 2 < points.Count; t += 3)
        {
            corner[0] = points[t];
            corner[1] = points[t + 1];
            corner[2] = points[t + 2];
            var inside = 0;
            var outside = 0;
            for (var i = 0; i < 3; i++)
            {
                var value = sign * (Vector3.Dot(normal, corner[i]) - d);
                if (MathF.Abs(value) <= PlaneTolerance) value = 0;
                distance[i] = value;
                if (value > 0) inside++;
                else if (value < 0) outside++;
            }
            if (outside == 0 && inside == 0)
            {
                // Coplanar with the cut: it bounds the kept solid only when its outward normal points at the removed side.
                var faceNormal = Vector3.Cross(corner[1] - corner[0], corner[2] - corner[0]);
                if (Vector3.Dot(faceNormal, normal) * sign < 0) result.Add(corner[0], corner[1], corner[2]);
                continue;
            }
            if (outside == 0)
            {
                result.Add(corner[0], corner[1], corner[2]);
                continue;
            }
            if (inside == 0) continue;

            // Sutherland–Hodgman against the plane; a triangle yields a triangle or a quadrilateral.
            var count = 0;
            for (var i = 0; i < 3; i++)
            {
                var j = (i + 1) % 3;
                var a = corner[i];
                var b = corner[j];
                var da = distance[i];
                var db = distance[j];
                if (da >= 0) polygon[count++] = da == 0 ? Snap(a, normal, d) : a;
                if ((da > 0 && db < 0) || (da < 0 && db > 0)) polygon[count++] = Intersect(a, b, normal, d);
            }
            if (count < 3) continue;
            for (var k = 1; k + 1 < count; k++) result.Add(polygon[0], polygon[k], polygon[k + 1]);
            // The clipped polygon's edge that lies on the plane is the cut; the cap uses it in the opposite direction.
            for (var k = 0; k < count; k++)
            {
                var from = polygon[k];
                var to = polygon[(k + 1) % count];
                if (OnPlane(from, normal, d) && OnPlane(to, normal, d) && from != to) cut.Add((to, from));
            }
        }

        if (cut.Count >= 3) Cap(result, cut, -sign * normal);
        return result;
    }

    private static bool OnPlane(Vector3 point, Vector3 normal, float d) =>
        MathF.Abs(Vector3.Dot(normal, point) - d) <= PlaneTolerance * 2;

    private static Vector3 Snap(Vector3 point, Vector3 normal, float d) =>
        point - normal * (Vector3.Dot(normal, point) - d);

    /// <summary>
    /// Intersection of segment ab with the plane. The endpoints are ordered canonically first, so two triangles that
    /// share an edge compute bit-identical points and their cut segments join exactly.
    /// </summary>
    private static Vector3 Intersect(Vector3 a, Vector3 b, Vector3 normal, float d)
    {
        if (Less(b, a)) (a, b) = (b, a);
        var da = Vector3.Dot(normal, a) - d;
        var db = Vector3.Dot(normal, b) - d;
        var t = da / (da - db);
        return Snap(a + (b - a) * t, normal, d);
    }

    private static bool Less(Vector3 a, Vector3 b) =>
        a.X != b.X ? a.X < b.X : a.Y != b.Y ? a.Y < b.Y : a.Z < b.Z;

    /// <summary>Joins cut segments into loops and fills them, with holes, as triangles facing <paramref name="capNormal"/>.</summary>
    private static void Cap(TriSoup result, List<(Vector3 From, Vector3 To)> cut, Vector3 capNormal)
    {
        var next = new Dictionary<Vector3, List<int>>();
        for (var i = 0; i < cut.Count; i++)
        {
            if (!next.TryGetValue(cut[i].From, out var list)) next[cut[i].From] = list = [];
            list.Add(i);
        }
        var used = new bool[cut.Count];
        var loops = new List<List<Vector3>>();
        for (var start = 0; start < cut.Count; start++)
        {
            if (used[start]) continue;
            var loop = new List<Vector3>();
            var current = start;
            var closed = false;
            while (true)
            {
                used[current] = true;
                loop.Add(cut[current].From);
                var end = cut[current].To;
                if (end == cut[start].From)
                {
                    closed = true;
                    break;
                }
                if (!next.TryGetValue(end, out var candidates)) break;
                var following = candidates.FirstOrDefault(index => !used[index], -1);
                if (following < 0) break;
                current = following;
            }
            if (closed && loop.Count >= 3) loops.Add(loop);
        }
        if (loops.Count == 0) return;

        var (tangent, bitangent) = Sampling.Basis(Vector3.Normalize(capNormal));
        List<P2> Project(List<Vector3> loop) => loop
            .Select(p => new P2(Vector3.Dot(p, tangent), Vector3.Dot(p, bitangent))).ToList();
        var projected = loops.Select(Project).ToList();
        var areas = projected.Select(loop => PolygonMesher.SignedArea(loop, Enumerable.Range(0, loop.Count).ToList())).ToList();
        var outers = Enumerable.Range(0, loops.Count).Where(i => areas[i] > 1e-10).ToList();
        var holes = Enumerable.Range(0, loops.Count).Where(i => areas[i] < -1e-10).ToList();

        foreach (var outer in outers)
        {
            var mine = holes.Where(hole => Contains(projected[outer], projected[hole][0]) &&
                                           !outers.Any(other => other != outer && areas[other] < areas[outer] &&
                                               Contains(projected[other], projected[hole][0]) &&
                                               Contains(projected[outer], projected[other][0]))).ToList();
            var triangles = PolygonMesher.Triangulate(projected[outer], mine.Select(hole => (IReadOnlyList<P2>)projected[hole]).ToList());
            var vertices = new List<Vector3>(loops[outer]);
            foreach (var hole in mine) vertices.AddRange(loops[hole]);
            foreach (var (a, b, c) in triangles) result.Add(vertices[a], vertices[b], vertices[c]);
        }
    }

    private static bool Contains(List<P2> polygon, P2 point)
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

    /// <summary>
    /// Returns the outward planes (n·x ≤ d inside) of a convex solid, or null when the mesh is not convex. Planes that
    /// several triangles share are listed once.
    /// </summary>
    public static List<(Vector3 Normal, float D)>? ConvexPlanes(TriSoup solid)
    {
        var planes = new List<(Vector3 Normal, float D)>();
        for (var t = 0; t + 2 < solid.Points.Count; t += 3)
        {
            var cross = Vector3.Cross(solid.Points[t + 1] - solid.Points[t], solid.Points[t + 2] - solid.Points[t]);
            if (cross.LengthSquared() < 1e-14f) continue;
            var normal = Vector3.Normalize(cross);
            var d = Vector3.Dot(normal, solid.Points[t]);
            if (!planes.Any(plane => Vector3.Dot(plane.Normal, normal) > 0.9999f && MathF.Abs(plane.D - d) < 1e-4f))
                planes.Add((normal, d));
        }
        if (planes.Count < 4) return null;
        foreach (var point in solid.Points)
            foreach (var (normal, d) in planes)
                if (Vector3.Dot(normal, point) - d > 1e-3f) return null;
        return planes;
    }

    /// <summary>
    /// Removes a convex volume from a solid: the result is the union of the parts of the solid that lie outside the
    /// volume's first plane, outside its second plane (but inside the first), and so on.
    /// </summary>
    public static List<TriSoup> Subtract(TriSoup solid, List<(Vector3 Normal, float D)> convexPlanes)
    {
        var pieces = new List<TriSoup>();
        var remaining = solid;
        foreach (var (normal, d) in convexPlanes)
        {
            var outside = Clip(remaining, normal, d, keepPositive: true);
            if (outside.TriangleCount > 0) pieces.Add(outside);
            remaining = Clip(remaining, normal, d, keepPositive: false);
            if (remaining.TriangleCount == 0) break;
        }
        return pieces;
    }
}
