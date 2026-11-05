using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Guanacaste3D.Desktop;

/// <summary>Draws the shared architectural marker language used by elevation and section references.</summary>
public partial class MainWindow
{
    /// <summary>
    /// Draws a conventional circular reference head with the look triangle contained inside it, a divider, and a compact
    /// identifier. Keeping the arrow inside the head prevents the bird-beak silhouette of the earlier symbol.
    /// </summary>
    private void DrawCircularViewMarker(Point center, Vector direction, Guid id, string name, string code,
        Brush brush, ContextMenu? menu, bool hitTest)
    {
        const double radius = 14;
        direction.Normalize();
        var across = new Vector(-direction.Y, direction.X);
        var circle = new Ellipse
        {
            Width = radius * 2, Height = radius * 2, Fill = Brushes.White, Stroke = brush, StrokeThickness = 1.7,
            Tag = id, ContextMenu = menu, ToolTip = name, IsHitTestVisible = hitTest
        };
        Canvas.SetLeft(circle, center.X - radius);
        Canvas.SetTop(circle, center.Y - radius);
        PlanCanvas.Children.Add(circle);

        PlanCanvas.Children.Add(new Line
        {
            X1 = center.X - across.X * 11, Y1 = center.Y - across.Y * 11,
            X2 = center.X + across.X * 11, Y2 = center.Y + across.Y * 11,
            Stroke = brush, StrokeThickness = 1, Tag = id, ContextMenu = menu,
            ToolTip = name, IsHitTestVisible = hitTest
        });
        PlanCanvas.Children.Add(new Polygon
        {
            Points =
            [
                center + direction * 11,
                center + direction * 1 + across * 6,
                center + direction * 1 - across * 6
            ],
            Fill = brush, Stroke = brush, StrokeThickness = 0.8, Tag = id, ContextMenu = menu,
            ToolTip = name, IsHitTestVisible = hitTest
        });
        var identifier = new TextBlock
        {
            Text = code, FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = brush,
            TextAlignment = TextAlignment.Center, Tag = id, ContextMenu = menu, ToolTip = name,
            IsHitTestVisible = hitTest
        };
        identifier.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var textCenter = center - direction * 6;
        Canvas.SetLeft(identifier, textCenter.X - identifier.DesiredSize.Width / 2);
        Canvas.SetTop(identifier, textCenter.Y - identifier.DesiredSize.Height / 2);
        PlanCanvas.Children.Add(identifier);
    }

    /// <summary>Extracts a short drafting identifier from a view name without hardcoding a visible fallback label.</summary>
    private static string ViewMarkerCode(string name)
    {
        var token = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? name;
        return token.Length <= 3 ? token.ToUpperInvariant() : token[..2].ToUpperInvariant();
    }
}
