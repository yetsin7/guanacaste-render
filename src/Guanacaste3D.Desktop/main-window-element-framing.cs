using System.Numerics;
using Guanacaste3D.Core.Navigation;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>
    /// Frames a native selected element in the default 3D workspace. This is a camera operation,
    /// not a clipping/section box: section-box geometry is deliberately a separate feature.
    /// </summary>
    private void FrameSelectedElementIn3D()
    {
        if (CurrentSelection() is not { } selected || _modelNavigator is null) return;
        var bounds = new ElementPicker(_session.Document).Bounds(selected.Kind, selected.Id);
        if (bounds is not { } box) return;
        var extent = box.Max - box.Min;
        var margin = new Vector3(
            MathF.Max(0.25f, extent.X * 0.12f),
            MathF.Max(0.25f, extent.Y * 0.12f),
            MathF.Max(0.25f, extent.Z * 0.12f));
        var previousBounds = _modelNavigator.Bounds;
        ViewTabs.SelectedIndex = Model3DTabIndex;
        try
        {
            _modelNavigator.Bounds = () => (box.Min - margin, box.Max + margin);
            _modelNavigator.FrameAll();
        }
        finally
        {
            _modelNavigator.Bounds = previousBounds;
        }
    }
}
