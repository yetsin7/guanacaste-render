using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Desktop;

/// <summary>A frozen interactive scene that can be constructed away from the WPF dispatcher.</summary>
internal sealed record ModelSceneSnapshot(IReadOnlyList<GeometryModel3D> Models, GeometryModel3D? Edges,
    (Vector3 Min, Vector3 Max) Bounds, bool HasGeometry)
{
    public GeometryModel3D? GroundShadow { get; init; }
    public IReadOnlyList<GeometryModel3D> Revealed { get; init; } = [];

    /// <summary>Meshes a document snapshot, prepares frozen visuals, and derives bounds without the infinite ground.</summary>
    public static ModelSceneSnapshot Build(BuildingDocument document, bool clay, bool xray, bool showEdges,
        IReadOnlyDictionary<SurfaceKind, (ImageSource Image, double Size)>? textures,
        Vector3? shadowSun = null, BuildingDocument? hiddenDocument = null, IReadOnlySet<Guid>? hiddenIds = null,
        ModelDetailLevel detail = ModelDetailLevel.Medium, bool thickLines = true, bool includeGround = false,
        IReadOnlyDictionary<(Guid, SurfaceKind), (ImageSource Image, double Size)>? scopedTextures = null)
    {
        var scene = BuildingMesher.Build(document, includeGround: includeGround, plants: detail == ModelDetailLevel.Fine ? PlantDetail.Render : PlantDetail.Model);
        if (detail == ModelDetailLevel.Coarse) scene = SceneDetailFilter.Coarse(scene);
        var indices = new List<int>();
        var minimum = new Vector3(float.PositiveInfinity);
        var maximum = new Vector3(float.NegativeInfinity);
        for (var triangle = 0; triangle < scene.TriangleCount; triangle++)
        {
            if (scene.TriangleKinds[triangle] == SurfaceKind.Ground) continue;
            for (var corner = 0; corner < 3; corner++)
            {
                var index = scene.Indices[triangle * 3 + corner];
                indices.Add(index);
                minimum = Vector3.Min(minimum, scene.Positions[index]);
                maximum = Vector3.Max(maximum, scene.Positions[index]);
            }
        }
        var hasGeometry = indices.Count > 0;
        if (!hasGeometry) { minimum = new(-5, -5, 0); maximum = new(5, 5, 3); }
        var thickness = ScenePresentation.EdgeThicknessForBounds(minimum, maximum, thickLines);
        var models = (xray ? ScenePresentation.ToWpfXRay(scene) : clay
            ? ScenePresentation.ToWpfClay(scene) : scopedTextures is { Count: > 0 }
                ? ScopedScenePresentation.Build(scene, textures, scopedTextures)
                : ScenePresentation.ToWpfModels(scene, textures)).ToArray();
        // Each edge becomes an eight-sided tube; bound the additional geometry on large models.
        var edgeLimit = scene.TriangleCount > 100_000 ? 10_000 : 80_000;
        var edges = showEdges && !xray
            ? ScenePresentation.ToWpfEdges(FeatureEdges.Extract(scene.Positions, indices, maximumEdges: edgeLimit), Matrix4x4.Identity,
                thickness, ScenePresentation.EdgeColor)
            : null;
        return new(models, edges, (minimum, maximum), hasGeometry)
        {
            GroundShadow = shadowSun is { } sun ? ModelSceneEffects.GroundShadow(scene, sun, Math.Min(0, minimum.Z)) : null,
            Revealed = hiddenDocument is null || hiddenIds is null ? [] : ModelSceneEffects.RevealEdges(hiddenDocument, hiddenIds, thickness)
        };
    }
}
