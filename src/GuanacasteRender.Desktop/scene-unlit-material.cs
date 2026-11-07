using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Guanacaste3D.Desktop;

/// <summary>Creates stable unlit WPF materials for outlines, color studies, and reveal geometry.</summary>
internal static class SceneUnlitMaterial
{
    /// <summary>Establishes a black diffuse base before emission so WPF does not render emissive-only meshes as white.</summary>
    public static MaterialGroup Create(Brush brush)
    {
        var alpha = brush is SolidColorBrush solid ? solid.Color.A : (byte)255;
        var baseBrush = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0)) { Opacity = brush.Opacity };
        return new MaterialGroup { Children = { new DiffuseMaterial(baseBrush), new EmissiveMaterial(brush) } };
    }
}
