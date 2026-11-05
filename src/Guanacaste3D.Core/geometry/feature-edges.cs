using System.Numerics;

namespace Guanacaste3D.Core.Geometry;

/// <summary>A straight edge of the model that should be drawn as a line.</summary>
public readonly record struct EdgeSegment(Vector3 A, Vector3 B);

/// <summary>
/// Finds the edges that give a model its readable outline: boundary edges (used by one triangle) and creases where two
/// faces meet at a visible angle. Edges between nearly coplanar triangles are not reported, and neither are the seams
/// left by splitting a wall around its openings: the pieces are separate closed solids, so their edges rarely match one
/// to one (T-junctions) and their internal end faces would otherwise show up as lines across a flat wall.
/// </summary>
public static class FeatureEdges
{
    /// <summary>Vertices closer than this (in meters) are treated as one, since meshes often repeat each corner per triangle.</summary>
    private const float WeldTolerance = 1e-4f;

    /// <summary>Returns feature edges of an indexed triangle mesh, at most <paramref name="maximumEdges"/> of them.</summary>
    public static List<EdgeSegment> Extract(IReadOnlyList<Vector3> positions, IReadOnlyList<int> indices, float creaseDegrees = 28,
        int maximumEdges = 80_000)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(indices);
        var result = new List<EdgeSegment>();
        var triangleCount = indices.Count / 3;
        if (triangleCount == 0) return result;

        var weld = new Dictionary<(int, int, int), int>(positions.Count / 2);
        var welded = new int[positions.Count];
        var representatives = new List<Vector3>();
        for (var i = 0; i < positions.Count; i++)
        {
            var p = positions[i];
            var key = ((int)MathF.Round(p.X / WeldTolerance), (int)MathF.Round(p.Y / WeldTolerance), (int)MathF.Round(p.Z / WeldTolerance));
            if (!weld.TryGetValue(key, out var index))
            {
                index = representatives.Count;
                weld[key] = index;
                representatives.Add(p);
            }
            welded[i] = index;
        }

        var cosineLimit = MathF.Cos(creaseDegrees * MathF.PI / 180);
        // Per edge: the normal of the first triangle that used it, and how many triangles used it.
        var edges = new Dictionary<(int, int), (Vector3 Normal, int Count, bool Sharp)>(triangleCount * 2);
        var triangles = new List<(int A, int B, int C, Vector3 Normal)>(triangleCount);
        for (var t = 0; t < triangleCount; t++)
        {
            var a = welded[indices[t * 3]];
            var b = welded[indices[t * 3 + 1]];
            var c = welded[indices[t * 3 + 2]];
            if (a == b || b == c || a == c) continue;
            var cross = Vector3.Cross(representatives[b] - representatives[a], representatives[c] - representatives[a]);
            if (cross.LengthSquared() < 1e-14f) continue;
            var normal = Vector3.Normalize(cross);
            triangles.Add((a, b, c, normal));
            Add(edges, a, b, normal, cosineLimit);
            Add(edges, b, c, normal, cosineLimit);
            Add(edges, c, a, normal, cosineLimit);
        }

        // A "rim" is one triangle's side of an edge that is not an invisible flat join: it bounds that triangle's surface.
        var rims = new List<Rim>();
        foreach (var (a, b, c, normal) in triangles)
        {
            var corners = new[] { a, b, c };
            for (var side = 0; side < 3; side++)
            {
                var from = corners[side];
                var to = corners[(side + 1) % 3];
                var key = from < to ? (from, to) : (to, from);
                var (_, count, sharp) = edges[key];
                if (count == 2 && !sharp) continue;
                var start = representatives[from];
                var end = representatives[to];
                var inward = Vector3.Cross(normal, end - start);
                if (inward.LengthSquared() < 1e-14f) continue;
                inward = Vector3.Normalize(inward);
                if (Vector3.Dot(inward, representatives[corners[(side + 2) % 3]] - start) < 0) inward = -inward;
                rims.Add(new Rim(key, start, end, normal, inward));
            }
        }

        foreach (var segment in VisibleEdges(rims))
        {
            result.Add(segment);
            if (result.Count >= maximumEdges) break;
        }
        return result;
    }

    /// <summary>One triangle's side of an edge, with the triangle's normal and the in-plane direction pointing into it.</summary>
    private readonly record struct Rim((int, int) Edge, Vector3 Start, Vector3 End, Vector3 Normal, Vector3 Inward);

    /// <summary>
    /// Emits each edge once, minus the stretches where the flat surface of one of its faces continues across it. Rims are
    /// grouped by the infinite line they lie on. On that line, a rim is continued by another rim that lies in the same
    /// plane, on the opposite side of the line, over the same stretch: the two are neighbouring pieces of one flat
    /// surface, and whatever end face the pieces have there is inside the solid.
    /// </summary>
    private static IEnumerable<EdgeSegment> VisibleEdges(List<Rim> rims)
    {
        const float PlaneTolerance = 1e-3f;
        const float MinimumLength = 1e-3f;
        var groups = new Dictionary<(int, int, int, int, int, int), List<int>>();
        for (var i = 0; i < rims.Count; i++)
        {
            var direction = CanonicalDirection(rims[i]);
            var onLine = rims[i].Start - direction * Vector3.Dot(rims[i].Start, direction);
            var key = (Quantize(direction.X), Quantize(direction.Y), Quantize(direction.Z),
                Quantize(onLine.X), Quantize(onLine.Y), Quantize(onLine.Z));
            if (!groups.TryGetValue(key, out var members)) groups[key] = members = [];
            members.Add(i);
        }

        foreach (var members in groups.Values)
        {
            foreach (var edgeRims in members.GroupBy(index => rims[index].Edge))
            {
                var first = rims[edgeRims.First()];
                var direction = CanonicalDirection(first);
                var origin = Vector3.Dot(first.Start, direction);
                var (low, high) = Range(first, direction);
                var covers = new List<(float Low, float High)>();
                foreach (var index in edgeRims)
                {
                    var rim = rims[index];
                    foreach (var other in members)
                    {
                        // Two pieces may share the very same edge (their top faces meet at a seam), so a partner on the
                        // same edge counts too; a rim never covers itself.
                        if (other == index) continue;
                        var partner = rims[other];
                        var coplanar = MathF.Abs(Vector3.Dot(rim.Normal, partner.Normal)) > 0.9995f &&
                                       MathF.Abs(Vector3.Dot(rim.Normal, partner.Start - rim.Start)) < PlaneTolerance;
                        if (!coplanar || Vector3.Dot(rim.Inward, partner.Inward) > -0.5f) continue;
                        var (partnerLow, partnerHigh) = Range(partner, direction);
                        if (partnerHigh > low && partnerLow < high) covers.Add((Math.Max(low, partnerLow), Math.Min(high, partnerHigh)));
                    }
                }

                var cursor = low;
                foreach (var (coverLow, coverHigh) in covers.OrderBy(cover => cover.Low))
                {
                    if (coverLow - cursor > MinimumLength) yield return Piece(first.Start, direction, origin, cursor, coverLow);
                    cursor = Math.Max(cursor, coverHigh);
                }
                if (high - cursor > MinimumLength) yield return Piece(first.Start, direction, origin, cursor, high);
            }
        }
    }

    /// <summary>The rim's unit direction with a fixed sign, so collinear edges drawn either way round share one key.</summary>
    private static Vector3 CanonicalDirection(Rim rim)
    {
        var direction = Vector3.Normalize(rim.End - rim.Start);
        const float Epsilon = 1e-4f;
        var flip = direction.X < -Epsilon || (MathF.Abs(direction.X) <= Epsilon && (direction.Y < -Epsilon ||
            (MathF.Abs(direction.Y) <= Epsilon && direction.Z < 0)));
        return flip ? -direction : direction;
    }

    private static (float Low, float High) Range(Rim rim, Vector3 direction)
    {
        var a = Vector3.Dot(rim.Start, direction);
        var b = Vector3.Dot(rim.End, direction);
        return (Math.Min(a, b), Math.Max(a, b));
    }

    private static EdgeSegment Piece(Vector3 anchor, Vector3 direction, float anchorParameter, float from, float to) =>
        new(anchor + direction * (from - anchorParameter), anchor + direction * (to - anchorParameter));

    private static int Quantize(float value) => (int)MathF.Round(value * 1000f);

    private static void Add(Dictionary<(int, int), (Vector3 Normal, int Count, bool Sharp)> edges, int a, int b, Vector3 normal,
        float cosineLimit)
    {
        var key = a < b ? (a, b) : (b, a);
        if (!edges.TryGetValue(key, out var existing))
        {
            edges[key] = (normal, 1, false);
            return;
        }
        // Compare against the first face by absolute angle: meshes with mixed winding must not turn every seam into a line.
        var sharp = existing.Sharp || MathF.Abs(Vector3.Dot(existing.Normal, normal)) < cosineLimit;
        edges[key] = (existing.Normal, existing.Count + 1, sharp);
    }
}
