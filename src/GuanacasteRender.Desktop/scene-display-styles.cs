using Guanacaste3D.Core;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Guanacaste3D.Desktop;

/// <summary>Applies view-only materials while retaining depth testing and frozen scene geometry.</summary>
internal static class SceneDisplayStyles
{
    /// <summary>Converts solid visuals to hidden-line masks or unlit category colors as required.</summary>
    public static IEnumerable<GeometryModel3D> Apply(IEnumerable<GeometryModel3D> models, ModelDisplayStyle style)
    {
        if (style == ModelDisplayStyle.Wireframe) yield break;
        foreach (var source in models)
        {
            if (style is not (ModelDisplayStyle.HiddenLine or ModelDisplayStyle.ConsistentColors)) { yield return source; continue; }
            Material material = style == ModelDisplayStyle.HiddenLine
                ? SceneUnlitMaterial.Create(Brushes.White) : Unlit(source.Material);
            var model = new GeometryModel3D(source.Geometry, material) { BackMaterial = material, Transform = source.Transform };
            model.Freeze();
            yield return model;
        }
    }

    /// <summary>Preserves a category's color and transparency while removing directional shading.</summary>
    private static Material Unlit(Material source)
    {
        if (source is MaterialGroup group)
            return new MaterialGroup { Children = new MaterialCollection(group.Children.OfType<DiffuseMaterial>().Select(material => SceneUnlitMaterial.Create(material.Brush))) };
        return source is DiffuseMaterial diffuse ? SceneUnlitMaterial.Create(diffuse.Brush) : source;
    }
}
