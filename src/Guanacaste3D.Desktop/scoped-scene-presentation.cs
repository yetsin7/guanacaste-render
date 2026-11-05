using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop;

/// <summary>Builds lightweight WPF geometry with element-specific material overrides.</summary>
internal static class ScopedScenePresentation
{
    /// <summary>Groups triangles by surface and scoped texture while retaining world-space face projection.</summary>
    internal static IEnumerable<GeometryModel3D> Build(SceneMesh scene,
        IReadOnlyDictionary<SurfaceKind, (ImageSource Image, double Size)>? global,
        IReadOnlyDictionary<(Guid, SurfaceKind), (ImageSource Image, double Size)> scoped)
    {
        var groups = new Dictionary<(SurfaceKind Kind, Guid? Element), MeshGeometry3D>();
        var rangeIndex = 0;
        for (var triangle = 0; triangle < scene.TriangleCount; triangle++)
        {
            while (rangeIndex < scene.ElementRanges.Count && scene.ElementRanges[rangeIndex].End <= triangle) rangeIndex++;
            var kind = scene.TriangleKinds[triangle];
            Guid? element = rangeIndex < scene.ElementRanges.Count && scene.ElementRanges[rangeIndex].First <= triangle
                ? scene.ElementRanges[rangeIndex].ElementId : null;
            if (element is not Guid id || !scoped.ContainsKey((id, kind))) element = null;
            var key = (kind, element);
            if (!groups.TryGetValue(key, out var mesh)) groups[key] = mesh = new MeshGeometry3D();
            var texture = element is Guid scopedId ? scoped[(scopedId, kind)] :
                global is not null && global.TryGetValue(kind, out var found) ? found : default;
            var first = scene.Positions[scene.Indices[triangle * 3]];
            var normal = Vector3.Cross(scene.Positions[scene.Indices[triangle * 3 + 1]] - first,
                scene.Positions[scene.Indices[triangle * 3 + 2]] - first);
            for (var corner = 0; corner < 3; corner++)
            {
                var point = scene.Positions[scene.Indices[triangle * 3 + corner]];
                mesh.TriangleIndices.Add(mesh.Positions.Count);
                mesh.Positions.Add(ScenePresentation.ToWpf(point));
                if (texture.Image is null || texture.Size <= 0) continue;
                var (u, v) = TextureProjection.PlaneUv(point, normal);
                mesh.TextureCoordinates.Add(new Point(u / texture.Size, -v / texture.Size));
            }
        }
        foreach (var (key, mesh) in groups.OrderBy(item => item.Key.Kind == SurfaceKind.Glass))
        {
            mesh.Freeze();
            var texture = key.Element is Guid id ? scoped[(id, key.Kind)] :
                global is not null && global.TryGetValue(key.Kind, out var found) ? found : default;
            var material = texture.Image is null ? ScenePresentation.MaterialFor(key.Kind) : ScenePresentation.Textured(texture.Image);
            var model = new GeometryModel3D(mesh, material) { BackMaterial = material };
            model.Freeze();
            yield return model;
        }
    }
}
