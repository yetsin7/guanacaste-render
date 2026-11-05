using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Navigation;

namespace Guanacaste3D.Desktop;

/// <summary>Frozen ground-shadow and reveal geometry for interactive model presentation.</summary>
internal static class ModelSceneEffects
{
    /// <summary>Projects opaque native geometry onto the ground plane for inexpensive architectural study shadows.</summary>
    public static GeometryModel3D? GroundShadow(SceneMesh scene, Vector3 sun, float elevation)
    {
        if (sun.Z <= 0.05f) return null;
        var mesh = new MeshGeometry3D();
        for (var triangle = 0; triangle < scene.TriangleCount; triangle++)
        {
            if (scene.TriangleKinds[triangle] is SurfaceKind.Ground or SurfaceKind.Glass) continue;
            for (var corner = 0; corner < 3; corner++)
            {
                var position = scene.Positions[scene.Indices[triangle * 3 + corner]];
                var projection = position - sun * Math.Max(0, (position.Z - elevation) / sun.Z);
                projection.Z = elevation + 0.002f;
                mesh.TriangleIndices.Add(mesh.Positions.Count);
                mesh.Positions.Add(ScenePresentation.ToWpf(projection));
            }
        }
        if (mesh.TriangleIndices.Count == 0) return null;
        var material = SceneUnlitMaterial.Create(new SolidColorBrush(Color.FromRgb(113, 119, 110)));
        var model = new GeometryModel3D(mesh, material) { BackMaterial = material };
        model.Freeze();
        return model;
    }

    /// <summary>Outlines only hidden element identities, leaving visible hosts and their materials unchanged.</summary>
    public static IReadOnlyList<GeometryModel3D> RevealEdges(BuildingDocument source, IReadOnlySet<Guid> hiddenIds, float thickness)
    {
        if (hiddenIds.Count == 0) return [];
        var picker = new ElementPicker(source);
        var edges = new List<EdgeSegment>();
        foreach (var (kind, ids) in new[]
        {
            (PickedKind.Wall, source.Walls.Select(wall => wall.Id)), (PickedKind.Opening, source.Openings.Select(opening => opening.Id)),
            (PickedKind.Floor, source.Floors.Select(floor => floor.Id)), (PickedKind.Column, source.Columns.Select(column => column.Id)),
            (PickedKind.Structural, source.StructuralElements.Select(element => element.Id))
        })
        foreach (var id in ids.Where(hiddenIds.Contains)) edges.AddRange(picker.Outline(kind, id));
        var model = ScenePresentation.ToWpfEdges(edges, Matrix4x4.Identity, thickness * 2, Color.FromRgb(223, 45, 180));
        return model is null ? [] : [model];
    }
}
