using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Revit-style plan navigation and direct manipulation. The middle button pans the
/// view while held, and pressing then dragging a selected wall, door, or window moves
/// it. The drag reuses the move tool's preview and its single, validated transaction.
/// </summary>
public partial class MainWindow
{
    /// <summary>Pixels the pointer must travel before a press becomes a drag, so clicks never move elements.</summary>
    private const double DragThreshold = 4;

    private Point? _panAnchor;
    private Point? _pressCanvasPoint;
    private Guid? _pressedElementId;
    private bool _dragMove;

    /// <summary>Refreshes the plan when a building-services overlay is shown or hidden.</summary>
    private void PlanLayerVisibility_Changed(object sender, RoutedEventArgs e)
    {
        if (PlanCanvas is not null && _session is not null) RenderPlan();
    }

    /// <summary>Starts panning when the middle button goes down over the plan.</summary>
    private void PlanCanvas_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        if (e.ClickCount >= 2)
        {
            _panAnchor = null;
            if (PlanCanvas.IsMouseCaptured) PlanCanvas.ReleaseMouseCapture();
            FitPlan_Click(sender, e);
            e.Handled = true;
            return;
        }
        _panAnchor = e.GetPosition(PlanCanvas);
        PlanCanvas.CaptureMouse();
        e.Handled = true;
    }

    /// <summary>Ends panning when the middle button is released.</summary>
    private void PlanCanvas_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || _panAnchor is null) return;
        _panAnchor = null;
        PlanCanvas.ReleaseMouseCapture();
        e.Handled = true;
    }

    /// <summary>Moves the view by the pointer travel while the middle button is held. Returns true when it consumed the move.</summary>
    private bool TryPanPlan(MouseEventArgs e)
    {
        if (_panAnchor is not Point anchor) return false;
        var current = e.GetPosition(PlanCanvas);
        // A move to the same place (WPF raises one after every redraw under the pointer) changes nothing to pan.
        if (current == anchor) return true;
        _panOffset = new Point(_panOffset.X + current.X - anchor.X, _panOffset.Y + current.Y - anchor.Y);
        _panAnchor = current;
        RenderPlan();
        return true;
    }

    /// <summary>Remembers where a left press landed and which element it hit, before the selection changes.</summary>
    private void RecordPlanPress(MouseButtonEventArgs e)
    {
        _pressCanvasPoint = e.GetPosition(PlanCanvas);
        _pressedElementId = (e.OriginalSource as FrameworkElement)?.Tag as Guid?;
    }

    /// <summary>
    /// Starts moving the pressed element once the pointer has travelled past the threshold.
    /// Only the element that was pressed and is now selected can be dragged.
    /// </summary>
    private void TryStartElementDrag(MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pressCanvasPoint is not Point press ||
            _movingElement || _drawingWall) return;
        if ((e.GetPosition(PlanCanvas) - press).Length < DragThreshold) return;
        var pressed = _pressedElementId;
        _pressCanvasPoint = null;
        if (pressed is not Guid id || SelectedMovableId() != id) return;

        _dragMove = true;
        _movingElement = true;
        _moveBasePoint = MovePoint(press);
        _movePreviewPoint = _moveBasePoint;
        PlanCanvas.CaptureMouse();
        StatusText.Text = GetText("Status.MoveDestination");
    }

    /// <summary>Finishes a drag at the release point, or simply forgets a press that was only a click.</summary>
    private void PlanCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _pressCanvasPoint = null;
        if (FinishElevationDrag()) return;
        _pressedElevationViewId = null;
        _elevationPressCanvasPoint = null;
        if (!_dragMove) return;
        _dragMove = false;
        PlanCanvas.ReleaseMouseCapture();
        CommitElementDrag();
    }

    /// <summary>Applies the dragged translation once, rejecting it atomically when the document would be invalid.</summary>
    private void CommitElementDrag()
    {
        var (deltaX, deltaY) = CurrentMoveDelta();
        CommitMove(deltaX, deltaY);
    }

    /// <summary>Returns the translation the move tool currently previews, or zero when no move is in progress.</summary>
    private (double X, double Y) CurrentMoveDelta()
    {
        if (!_movingElement || _moveBasePoint is not Point2 from || _movePreviewPoint is not Point2 to)
            return (0, 0);
        return (to.X - from.X, to.Y - from.Y);
    }
}
