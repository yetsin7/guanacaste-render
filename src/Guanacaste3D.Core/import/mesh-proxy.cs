using System.Numerics;

namespace Guanacaste3D.Core.Import;

/// <summary>A reduced copy of a model's surface, light enough for an interactive 3D view.</summary>
public sealed class ProxyMesh
{
    public Vector3[] Positions { get; init; } = [];
    public int[] Indices { get; init; } = [];
    /// <summary>Index into the source model's materials for each triangle.</summary>
    public int[] TriangleMaterial { get; init; } = [];
    /// <summary>
    /// For a reduced mesh, the source triangle each triangle was collapsed from, so per-corner data such as texture
    /// coordinates can be carried over; empty when the mesh was not reduced.
    /// </summary>
    public int[] SourceTriangle { get; init; } = [];
    public int TriangleCount => Indices.Length / 3;
    /// <summary>Triangles in the full-detail source.</summary>
    public int SourceTriangles { get; init; }
    public bool IsReduced => TriangleCount < SourceTriangles;
}

/// <summary>
/// Reduces a heavy model (millions of triangles from a Revit export) to a proxy for the interactive view by vertex
/// clustering: vertices that fall in the same cell of a regular grid merge, triangles that collapse disappear. The
/// grid is sized from the model's surface area so the result lands near the requested triangle budget. The
/// full-detail geometry is still what the renderer uses; the proxy only keeps navigation smooth.
/// </summary>
public static class MeshProxy
{
    public static ProxyMesh Build(ImportedModel model, int maximumTriangles = 120_000)
    {
        ArgumentNullException.ThrowIfNull(model);
        maximumTriangles = Math.Max(1000, maximumTriangles);
        if (model.TriangleCount <= maximumTriangles)
        {
            return new ProxyMesh
            {
                Positions = model.Positions, Indices = model.Indices, TriangleMaterial = model.TriangleMaterial,
                SourceTriangles = model.TriangleCount
            };
        }

        var (min, max) = model.Bounds();
        var area = SurfaceArea(model);
        // Clustering keeps roughly one triangle pair per occupied cell on a surface, so cell² ≈ area / (budget / 2).
        var cell = MathF.Max(1e-3f, MathF.Sqrt(area / (maximumTriangles / 2f)));
        ProxyMesh? best = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            best = Cluster(model, min, cell);
            if (best.TriangleCount <= maximumTriangles * 1.25) break;
            cell *= 1.4f;
        }
        _ = max;
        return best!;
    }

    private static float SurfaceArea(ImportedModel model)
    {
        double total = 0;
        // A strided sample keeps this cheap on huge models; the estimate only sizes the grid.
        var step = Math.Max(1, model.TriangleCount / 200_000);
        for (var t = 0; t < model.TriangleCount; t += step)
        {
            var a = model.Positions[model.Indices[t * 3]];
            total += Vector3.Cross(model.Positions[model.Indices[t * 3 + 1]] - a, model.Positions[model.Indices[t * 3 + 2]] - a).Length() / 2;
        }
        return (float)(total * step);
    }

    private static ProxyMesh Cluster(ImportedModel model, Vector3 origin, float cell)
    {
        var inverse = 1 / cell;
        var cellOfVertex = new int[model.Positions.Length];
        var cells = new Dictionary<long, int>(1 << 16);
        var sums = new List<Vector3>();
        var counts = new List<int>();
        for (var v = 0; v < model.Positions.Length; v++)
        {
            var p = model.Positions[v] - origin;
            var key = ((long)(int)MathF.Floor(p.X * inverse) & 0x1FFFFF) << 42 |
                      ((long)(int)MathF.Floor(p.Y * inverse) & 0x1FFFFF) << 21 |
                      ((long)(int)MathF.Floor(p.Z * inverse) & 0x1FFFFF);
            if (!cells.TryGetValue(key, out var index))
            {
                index = sums.Count;
                cells[key] = index;
                sums.Add(Vector3.Zero);
                counts.Add(0);
            }
            sums[index] += model.Positions[v];
            counts[index]++;
            cellOfVertex[v] = index;
        }
        var positions = new Vector3[sums.Count];
        for (var i = 0; i < positions.Length; i++) positions[i] = sums[i] / counts[i];

        var indices = new List<int>(Math.Min(model.Indices.Length, 600_000));
        var materials = new List<int>(indices.Capacity / 3);
        var sources = new List<int>(indices.Capacity / 3);
        var seen = new HashSet<(int, int, int)>();
        for (var t = 0; t < model.TriangleCount; t++)
        {
            var a = cellOfVertex[model.Indices[t * 3]];
            var b = cellOfVertex[model.Indices[t * 3 + 1]];
            var c = cellOfVertex[model.Indices[t * 3 + 2]];
            if (a == b || b == c || a == c) continue;
            // The same cell triple often arises from many source triangles; keep one.
            var sorted = a < b ? (b < c ? (a, b, c) : (a < c ? (a, c, b) : (c, a, b))) : (a < c ? (b, a, c) : (b < c ? (b, c, a) : (c, b, a)));
            if (!seen.Add(sorted)) continue;
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
            materials.Add(t < model.TriangleMaterial.Length ? model.TriangleMaterial[t] : 0);
            sources.Add(t);
        }
        return new ProxyMesh
        {
            Positions = positions, Indices = [.. indices], TriangleMaterial = [.. materials], SourceTriangles = model.TriangleCount,
            SourceTriangle = [.. sources]
        };
    }
}
