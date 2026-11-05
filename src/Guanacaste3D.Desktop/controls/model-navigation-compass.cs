using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Guanacaste3D.Desktop.Controls;

public partial class ModelNavigation
{
    /// <summary>Draws a shallow compass ring below the cube with cardinal orientation targets.</summary>
    private void DrawCompass()
    {
        var ring = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 8,95 A 70,25 0 1 0 148,95 A 70,25 0 1 0 8,95 M 23,94 A 55,18 0 1 0 133,94 A 55,18 0 1 0 23,94"),
            Fill = new SolidColorBrush(Color.FromRgb(246, 246, 246)),
            Stroke = new SolidColorBrush(Color.FromRgb(190, 190, 190)), StrokeThickness = 0.6,
            Cursor = Cursors.Hand
        };
        CubeCanvas.Children.Add(ring);
        var platform = new Ellipse { Width = 110, Height = 36, Fill = Brushes.White,
            Stroke = new SolidColorBrush(Color.FromRgb(212, 212, 212)), StrokeThickness = 0.5, IsHitTestVisible = false };
        Canvas.SetLeft(platform, 23);
        Canvas.SetTop(platform, 76);
        CubeCanvas.Children.Add(platform);
        foreach (var (key, normal) in new[] { ("North", Vector3.UnitY), ("East", Vector3.UnitX),
                     ("South", -Vector3.UnitY), ("West", -Vector3.UnitX) })
        {
            var x = 78 + Vector3.Dot(normal, Camera!.Right) * 65;
            var y = 95 - Vector3.Dot(normal, Camera.Up) * 23;
            var label = new TextBlock { Text = UiText.Get("Navigation.Compass." + key), FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(161, 164, 167)), Cursor = Cursors.Hand,
                Background = Brushes.Transparent, Tag = normal, ToolTip = UiText.Get("Navigation.Direction." + key) };
            label.MouseLeftButtonDown += CompassPressed;
            Canvas.SetLeft(label, x - 4);
            Canvas.SetTop(label, y - 6);
            CubeCanvas.Children.Add(label);
        }
    }

    /// <summary>Draws subtle inset bevels so the neutral cube retains crisp edges on a white canvas.</summary>
    private void DrawBevel(Vector3 normal, Vector3 across, Vector3 up)
    {
        var inset = new Polyline { Stroke = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
            StrokeThickness = 0.7, IsHitTestVisible = false };
        foreach (var (horizontal, vertical) in new[] { (-1, 1), (1, 1), (1, -1), (-1, -1), (-1, 1) })
            inset.Points.Add(Project(normal + across * horizontal * 0.95f + up * vertical * 0.95f));
        CubeCanvas.Children.Add(inset);
    }

    /// <summary>Orients to the selected cardinal direction without starting an orbit gesture.</summary>
    private void CompassPressed(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBlock { Tag: Vector3 normal }) DirectionRequested?.Invoke(normal);
        e.Handled = true;
    }
}
