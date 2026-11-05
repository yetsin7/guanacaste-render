namespace Guanacaste3D.Core.Geometry;

/// <summary>Architectural display detail independently of authoring precision.</summary>
public enum ModelDetailLevel { Coarse, Medium, Fine }

/// <summary>Removes decorative trim from coarse model previews while retaining opening cuts and service ranges.</summary>
public static class SceneDetailFilter
{
    /// <summary>Builds a display-only triangle stream; the source document and source mesh remain unchanged.</summary>
    public static SceneMesh Coarse(SceneMesh source)
    {
        var result = new SceneMesh();
        for (var triangle = 0; triangle < source.TriangleCount; triangle++)
        {
            var kind = source.TriangleKinds[triangle];
            if (kind == SurfaceKind.OpeningFrame) continue;
            var first = source.Indices[triangle * 3];
            var second = source.Indices[triangle * 3 + 1];
            var third = source.Indices[triangle * 3 + 2];
            if (source.Normals is { } normals)
                result.AddSmoothTriangle(source.Positions[first], source.Positions[second], source.Positions[third],
                    normals[first], normals[second], normals[third], kind);
            else result.AddTriangle(source.Positions[first], source.Positions[second], source.Positions[third], kind);
            if (source.IsService(triangle)) result.Services.Add((result.TriangleCount - 1, result.TriangleCount));
            result.Dim(result.TriangleCount - 1, source.EmissionOf(triangle));
        }
        return result;
    }
}
