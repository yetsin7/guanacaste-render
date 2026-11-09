using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>Non-destructive presentation overrides for rendered images and walkthroughs.</summary>
public static class RenderSceneStyles
{
    /// <summary>Creates a neutral study model without changing BIM materials or linked-model assignments.</summary>
    public static RenderScene Clay(RenderScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var materials = scene.Materials.Select(material => material with
        {
            BaseColor = new Vector3(0.65f), Metallic = 0, Roughness = 0.85f,
            Pattern = SurfacePattern.None, Texture = null, TextureRelief = 0,
            TransmissionTint = new Vector3(0.94f)
        }).ToArray();
        return new RenderScene(scene.Positions, scene.Indices, scene.TriangleMaterial, scene.Normals,
            materials, scene.Lights, scene.DroppedTriangles, null, scene.DynamicTriangleStart, scene.DynamicTriangleCount,
            scene.DynamicVertexStart, scene.DynamicVertexCount);
    }
}
