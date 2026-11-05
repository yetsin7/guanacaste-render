using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Desktop.Controls;

public partial class RenderPanel
{
    /// <summary>Notifies the host when the selection projection must follow a resized render surface.</summary>
    internal event Action? SelectionViewportChanged;

    /// <summary>Updates the selection guide after the render surface changes size.</summary>
    private void NotifySelectionViewportChanged() => SelectionViewportChanged?.Invoke();

    /// <summary>Draws transient selection guides over the preview without changing the rendered or exported pixels.</summary>
    internal void ShowSelection(IReadOnlyList<EdgeSegment> edges)
    {
        SelectionOverlay.Children.Clear();
        if (_source is null || !IsVisible || ImageHost.ActualWidth <= 0 || ImageHost.ActualHeight <= 0) return;
        var width = (float)ImageHost.ActualWidth;
        var height = (float)ImageHost.ActualHeight;
        var brush = new SolidColorBrush(Color.FromArgb(165, 56, 157, 242));
        brush.Freeze();
        foreach (var edge in edges)
        {
            var start = _source.Camera.Project(edge.A, width, height);
            var end = _source.Camera.Project(edge.B, width, height);
            if (start is null || end is null) continue;
            SelectionOverlay.Children.Add(new Line
            {
                X1 = start.Value.X, Y1 = start.Value.Y, X2 = end.Value.X, Y2 = end.Value.Y,
                Stroke = brush, StrokeThickness = 1.2, IsHitTestVisible = false
            });
        }
    }
}
