using System.Windows;
using System.Windows.Media;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>Creates reusable WPF brushes from persisted wall drafting settings.</summary>
internal static class WallAppearanceRendering
{
    /// <summary>Creates a solid or hatched brush suitable for plan and section wall bodies.</summary>
    public static Brush CreateBrush(string colorText, WallFillPattern pattern, double opacity = 1)
    {
        var color = (Color)ColorConverter.ConvertFromString(colorText);
        color.A = (byte)Math.Clamp(Math.Round(opacity * 255), 0, 255);
        var background = new SolidColorBrush(color);
        background.Freeze();
        if (pattern == WallFillPattern.Solid) return background;

        var lineColor = Color.FromArgb(color.A, Darken(color.R), Darken(color.G), Darken(color.B));
        var tile = new DrawingGroup();
        tile.Children.Add(new GeometryDrawing(
            background, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        tile.Children.Add(new GeometryDrawing(
            null,
            new Pen(new SolidColorBrush(lineColor), 1),
            pattern == WallFillPattern.Crosshatch
                ? Geometry.Parse("M 0,0 L 8,8 M 8,0 L 0,8")
                : Geometry.Parse("M 0,8 L 8,0")));
        var brush = new DrawingBrush(tile)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 8, 8),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>Reduces a color channel for readable hatch strokes over its matching fill.</summary>
    private static byte Darken(byte channel) => (byte)Math.Round(channel * 0.62);
}
