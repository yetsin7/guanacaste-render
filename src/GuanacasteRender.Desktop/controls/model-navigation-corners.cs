using System.Numerics;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Guanacaste3D.Desktop.Controls;

public partial class ModelNavigation
{
    /// <summary>Exposes the camera-facing cube corners as small, independently clickable orientation targets.</summary>
    private void DrawCornerTargets()
    {
        foreach (var x in new[] { -1, 1 })
        foreach (var y in new[] { -1, 1 })
        foreach (var z in new[] { -1, 1 })
        {
            var direction = new Vector3(x, y, z);
            if (Vector3.Dot(direction, Camera!.Forward) >= 0) continue;
            var point = Project(direction);
            var target = new Rectangle
            {
                Width = 8, Height = 8, Tag = direction, Cursor = Cursors.Hand,
                Fill = Brushes.White, Stroke = Brushes.Gray, StrokeThickness = 0.8
            };
            target.MouseEnter += CornerEntered;
            target.MouseLeave += CornerLeft;
            Canvas.SetLeft(target, point.X - 4);
            Canvas.SetTop(target, point.Y - 4);
            CubeCanvas.Children.Add(target);
        }
    }

    /// <summary>Highlights the precise corner target without changing the surrounding face.</summary>
    private static void CornerEntered(object sender, MouseEventArgs args)
    {
        if (sender is Rectangle target) target.Fill = Brushes.LightSkyBlue;
    }

    /// <summary>Restores the neutral target when the pointer leaves it.</summary>
    private static void CornerLeft(object sender, MouseEventArgs args)
    {
        if (sender is Rectangle target) target.Fill = Brushes.White;
    }
}
