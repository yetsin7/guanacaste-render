using System.Numerics;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// Optional, budget-limited render-only geometry for natural-stone faces. The source BIM wall and its
/// dimensions are unchanged; shallow raised stone and recessed mortar now physically occlude rays.
/// Subdivision is limited globally so older CPU-only computers can choose not to enable it.
/// </summary>
public static class RenderStoneDisplacement
{
    /// <summary>
    /// Appends an offset tessellation over WallStone triangles. Original triangles stay in place as a
    /// watertight structural backing. Returns added triangle count. Texture and element attribution survive.
    /// </summary>
    public static int Append(SceneMesh mesh, int triangleBudget = 80_000, float targetEdgeMeters = 0.075f)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        triangleBudget = Math.Clamp(triangleBudget, 0, 150_000);
        if (triangleBudget < 4) return 0;
        if (!float.IsFinite(targetEdgeMeters)) targetEdgeMeters = 0.075f;
        targetEdgeMeters = Math.Clamp(targetEdgeMeters, 0.04f, 0.3f);

        var initial = mesh.TriangleCount;
        var ranges = mesh.ElementRanges.ToArray();
        var ownerCursor = 0;
        var added = 0;
        for (var triangle = 0; triangle < initial; triangle++)
        {
            while (ownerCursor < ranges.Length && ranges[ownerCursor].End <= triangle) ownerCursor++;
            if (mesh.TriangleKinds[triangle] != SurfaceKind.WallStone) continue;
            if (triangleBudget - added < 4) break;
            var a = mesh.Positions[mesh.Indices[triangle * 3]];
            var b = mesh.Positions[mesh.Indices[triangle * 3 + 1]];
            var c = mesh.Positions[mesh.Indices[triangle * 3 + 2]];
            var cross = Vector3.Cross(b - a, c - a);
            if (cross.LengthSquared() < 1e-12f) continue;
            var normal = Vector3.Normalize(cross);
            var maxEdge = MathF.Max(Vector3.Distance(a, b), MathF.Max(Vector3.Distance(b, c), Vector3.Distance(c, a)));
            if (maxEdge < 0.16f) continue;
            // N^2 tessellation triangles. Never exceed the fixed budget even for a giant wall.
            var steps = Math.Clamp((int)MathF.Ceiling(maxEdge / targetEdgeMeters), 2, 64);
            steps = Math.Min(steps, (int)MathF.Sqrt(triangleBudget - added));
            if (steps < 2) break;
            var first = mesh.TriangleCount;
            var ab = (b - a) / steps;
            var ac = (c - a) / steps;

            Vector3 Raised(int i, int j)
            {
                var onWall = a + ab * i + ac * j;
                var (u, v) = SurfacePatterns.PlaneUv(onWall, normal);
                // Keep the mortar above the original surface. The stone stands proud by up to ~2.6 cm.
                // All values are in meters: do not grow the wall's BIM or structural dimensions.
                var bump = Math.Clamp(0.013f + SurfacePatterns.StoneDisplacement(u, v), 0.002f, 0.028f);
                return onWall + normal * bump;
            }

            for (var i = 0; i < steps; i++)
            for (var j = 0; j < steps - i; j++)
            {
                var p0 = Raised(i, j);
                var p1 = Raised(i + 1, j);
                var p2 = Raised(i, j + 1);
                mesh.AddTriangle(p0, p1, p2, SurfaceKind.WallStone);
                if (i + j < steps - 1)
                    mesh.AddTriangle(p1, Raised(i + 1, j + 1), p2, SurfaceKind.WallStone);
            }

            if (ownerCursor < ranges.Length && ranges[ownerCursor].First <= triangle)
                mesh.MarkElement(ranges[ownerCursor].ElementId, first);
            added += steps * steps;
        }
        return added;
    }
}
