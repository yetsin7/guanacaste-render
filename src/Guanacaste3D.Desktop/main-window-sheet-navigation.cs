using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private Point? _sheetPanStart;

    /// <summary>Adds cursor-centered zoom and middle-button panning inside the drawing-sheet workspace.</summary>
    private void InitializeSheetNavigation()
    {
        if (SheetCanvas.Parent is not Viewbox viewbox) return;
        viewbox.RenderTransform = new MatrixTransform();
        if (viewbox.Parent is UIElement viewport) viewport.ClipToBounds = true;
        SheetCanvas.MouseWheel += SheetCanvas_MouseWheel;
        SheetCanvas.MouseDown += SheetCanvas_MouseDown;
        SheetCanvas.MouseMove += SheetCanvas_MouseMove;
        SheetCanvas.MouseUp += SheetCanvas_MouseUp;
    }

    /// <summary>Magnifies the fitted paper around the pointer without changing any printed dimensions.</summary>
    private void SheetCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (SheetCanvas.Parent is not Viewbox viewbox) return;
        var matrix = ((MatrixTransform)viewbox.RenderTransform).Matrix;
        var factor = Math.Clamp(matrix.M11 * (e.Delta > 0 ? 1.2 : 1 / 1.2), 0.5, 12) / matrix.M11;
        var point = e.GetPosition(viewbox);
        var zoom = Matrix.Identity; zoom.Translate(-point.X, -point.Y); zoom.Scale(factor, factor); zoom.Translate(point.X, point.Y);
        viewbox.RenderTransform = new MatrixTransform(zoom * matrix);
        e.Handled = true;
    }

    /// <summary>Starts paper navigation; a double middle-click restores the fitted page.</summary>
    private void SheetCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || SheetCanvas.Parent is not Viewbox viewbox) return;
        if (e.ClickCount > 1) viewbox.RenderTransform = new MatrixTransform();
        else { _sheetPanStart = e.GetPosition((IInputElement)viewbox.Parent); Mouse.Capture(SheetCanvas); }
        e.Handled = true;
    }

    /// <summary>Pans the magnified paper in viewport coordinates while the middle button remains held.</summary>
    private void SheetCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_sheetPanStart is not Point start || SheetCanvas.Parent is not Viewbox viewbox) return;
        var point = e.GetPosition((IInputElement)viewbox.Parent);
        var matrix = ((MatrixTransform)viewbox.RenderTransform).Matrix; matrix.Translate(point.X - start.X, point.Y - start.Y);
        viewbox.RenderTransform = new MatrixTransform(matrix); _sheetPanStart = point;
    }

    /// <summary>Releases only the capture owned by sheet navigation.</summary>
    private void SheetCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_sheetPanStart is null) return;
        _sheetPanStart = null;
        if (Mouse.Captured == SheetCanvas) Mouse.Capture(null);
    }
}
