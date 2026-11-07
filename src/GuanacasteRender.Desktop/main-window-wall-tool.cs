using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>Contains the contextual, preview-driven wall placement workflow.</summary>
public partial class MainWindow
{
    private Point2? _wallPreviewPoint;
    private WallSnapResult? _activeWallSnap;

    /// <summary>The last wall of a chained sketch and the reference point where its sketch segment ended.</summary>
    private (Guid WallId, Point2 ReferenceEnd)? _chainTail;

    /// <summary>
    /// The first wall of the active chain and the reference point the chain started from. Coming back to that point
    /// closes the loop: the pointer is pulled onto it and the closing wall is joined to the first one.
    /// </summary>
    private (Guid FirstWallId, Point2 ReferenceStart)? _chainOrigin;

    /// <summary>Screen reach of the magnet that closes a chain on its own first point; generous, as closing is the common case.</summary>
    private const double ChainClosureSnapPixels = 30;

    /// <summary>Screen reach of endpoint, midpoint and nearest-on-wall snaps.</summary>
    private const double WallObjectSnapPixels = 14;

    /// <summary>True while Create Similar waits for the next wall picked in the plan.</summary>
    private bool _awaitingSimilarWallPick;

    /// <summary>Tracks the pointer in model space so the active wall sketch has immediate visual feedback.</summary>
    private void PlanCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (TryPanPlan(e)) return;
        if (TryUpdateElevationDrag(e)) return;
        // Redrawing the plan replaces the shapes under a resting pointer, so WPF raises a MouseMove at the same position;
        // answering it with another redraw would loop forever and starve the interface. Only real movement counts.
        var position = PlanPosition(e);
        if (_lastPlanPointer == position) return;
        _lastPlanPointer = position;
        _placementPointer = CanvasToWorld(PlanPosition(e));
        if (_placingOpening is not null || _drawingColumn || _drawingRoom || _electricalTool is not null || _plumbingPointTool is not null)
        {
            RenderPlan();
            return;
        }
        TryStartElementDrag(e);
        if (_movingElement)
        {
            UpdateMovePreview(PlanPosition(e));
            return;
        }
        if (TryUpdateStructuralPreview(e)) return;
        if (!_drawingWall) return;
        var pointer = CanvasToWorld(PlanPosition(e));
        _wallPreviewPoint = SnapWallPoint(pointer);
        UpdateAlignmentGuides(pointer, null);
        RenderPlan();
    }

    /// <summary>Last pointer position a plan MouseMove was handled at, so repeats at the same place are ignored.</summary>
    private Point? _lastPlanPointer;

    /// <summary>Removes cursor-attached ghosts when the pointer leaves the plan, retaining any first anchor.</summary>
    private void PlanCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        _lastPlanPointer = null;
        _placementPointer = null;
        _structuralPreviewPoint = null;
        _wallPreviewPoint = null;
        RenderPlan();
    }

    /// <summary>Handles either endpoint placement or a two-corner rectangular wall operation.</summary>
    private void HandleWallPlacementClick(Point canvasPoint)
    {
        var point = SnapWallPoint(CanvasToWorld(canvasPoint));
        if (_pendingWallStart is null) _pendingArcEnd = null;
        if (_pendingWallStart is null)
        {
            _pendingWallStart = point;
            _chainTail = null;
            _chainOrigin = null;
            _wallPreviewPoint = point;
            ClearDirectWallLength();
            StatusText.Text = GetText("Status.WallSecondPoint");
            RenderPlan();
            return;
        }

        if (!TryReadWallPlacementOptions(out var height, out var offset, out var typeId))
        {
            StatusText.Text = GetText("Status.WallInvalidOptions");
            return;
        }

        var start = _pendingWallStart.Value;
        if (WallArcMode.IsChecked == true)
        {
            // An arc takes three clicks: where it starts, where it ends, and a point it passes through.
            if (_pendingArcEnd is null)
            {
                if (point.DistanceTo(start) < 0.05) return;
                _pendingArcEnd = point;
                StatusText.Text = GetText("Status.WallArcThrough");
                RenderPlan();
                return;
            }
            CreateArcWall(start, _pendingArcEnd.Value, point, height, typeId);
            return;
        }
        try
        {
            if (WallRectangleMode.IsChecked == true)
                CreateWallRectangle(start, point, height, offset, typeId);
            else
                CreateWallSegment(start, ConstrainWallEndpoint(start, point), height, offset, typeId);
        }
        catch (InvalidDataException exception)
        {
            MessageBox.Show(this, LocalizedErrors.Translate(exception.Message), GetText("Dialog.InvalidGeometry"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>The end of the arc being drawn, once the second click has fixed it; the third click picks its bulge.</summary>
    private Point2? _pendingArcEnd;

    /// <summary>Creates a curved wall through three clicked points, as one undoable step, and ends the sketch.</summary>
    private void CreateArcWall(Point2 start, Point2 end, Point2 through, double height, Guid typeId)
    {
        try
        {
            var ids = _session.AddArcWall(_activeLevelId, start, end, through, height, typeId);
            _selectedWallId = ids[ids.Count / 2];
            _selectedViewId = null;
            _pendingWallStart = null;
            _pendingArcEnd = null;
            _wallPreviewPoint = null;
            _chainTail = null;
            _chainOrigin = null;
            ClearDirectWallLength();
            StatusText.Text = FormatText("Status.WallArcCreated", ids.Count);
            RefreshAll();
        }
        catch (InvalidDataException exception)
        {
            MessageBox.Show(this, LocalizedErrors.Translate(exception.Message), GetText("Dialog.InvalidGeometry"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Creates a line wall and optionally carries its reference endpoint into a chained segment.</summary>
    private void CreateWallSegment(Point2 referenceStart, Point2 referenceEnd, double height, double offset, Guid typeId)
    {
        var (start, end) = OffsetWallCenterline(referenceStart, referenceEnd, offset, typeId);
        var thickness = _session.Document.WallTypes.Single(type => type.Id == typeId).Thickness;
        using var group = _session.BeginGroup("Transaction.DrawWall");
        if (FindContinuedWallEnd(referenceStart, typeId) is { } continued)
        {
            // Face and interior references offset each wall on its own, so a continued end only
            // meets where the two offset lines cross. Moving the existing end there closes the corner.
            var existing = continued.Wall;
            var meeting = WallPlacementGeometry.CenterlineIntersection(
                new WallCenterline(existing.Start, existing.End), new WallCenterline(start, end));
            if (meeting is Point2 corner && corner.DistanceTo(referenceStart) <= 2 * thickness)
            {
                var anchor = continued.End == WallEnd.Start ? existing.Start : existing.End;
                if (corner.DistanceTo(anchor) > WallJoinIndex.Tolerance)
                    _session.MoveWallEnd(existing.Id, continued.End, corner);
                start = corner;
            }
        }
        var closesLoop = _chainOrigin is { } origin && origin.ReferenceStart.DistanceTo(referenceEnd) < 1e-6;
        if (closesLoop && _session.Document.Walls.FirstOrDefault(wall => wall.Id == _chainOrigin!.Value.FirstWallId) is
                { } first && first.WallTypeId == typeId)
        {
            // The closing wall and the first wall meet where their centerlines cross; with a face reference that
            // corner is off the clicked point, so both ends move there and the loop is joined like any other corner.
            var meeting = WallPlacementGeometry.CenterlineIntersection(
                new WallCenterline(first.Start, first.End), new WallCenterline(start, end));
            if (meeting is Point2 corner && corner.DistanceTo(referenceEnd) <= 2 * thickness)
            {
                if (corner.DistanceTo(first.Start) > WallJoinIndex.Tolerance)
                    _session.MoveWallEnd(first.Id, WallEnd.Start, corner);
                end = corner;
            }
        }
        var others = _session.Document.Walls.Where(wall => wall.LevelId == _activeLevelId)
            .Select(wall => new WallCenterline(wall.Start, wall.End)).ToList();
        var overlap = WallOverlap.Find(_session.Document.Walls, _activeLevelId, start, end, thickness);
        var id = _session.AddWall(_activeLevelId, start, end, height: height, wallTypeId: typeId,
            locationReference: SelectedWallLocationReference());
        group.Assimilate();
        _selectedWallId = id;
        if (_chainTail is null) _chainOrigin = (id, referenceStart);
        // A chain ends by itself when it closes on its first point, or lands on or crosses another wall: the tool
        // stays armed for the next wall.
        var continuesChain = WallChainCheck.IsChecked == true && !closesLoop &&
            !WallPlacementGeometry.EndsOnWall(new WallCenterline(start, end), others, thickness / 2 + 1e-6);
        _chainTail = continuesChain ? (id, referenceEnd) : null;
        if (!continuesChain) _chainOrigin = null;
        _selectedViewId = null;
        _pendingWallStart = continuesChain ? referenceEnd : null;
        _wallPreviewPoint = continuesChain ? referenceEnd : null;
        ClearDirectWallLength();
        StatusText.Text = GetText(continuesChain ? "Status.WallCreated"
            : closesLoop ? "Status.WallLoopClosed" : "Status.WallChainClosed");
        if (overlap is { } overlapped) StatusText.Text = FormatText("Status.WallOverlaps", overlapped.Length);
        RefreshAll();
    }

    /// <summary>Creates four joined walls in one undoable transaction from opposite reference corners.</summary>
    private void CreateWallRectangle(Point2 first, Point2 opposite, double height, double offset, Guid typeId)
    {
        var type = _session.Document.WallTypes.Single(candidate => candidate.Id == typeId);
        var centerlines = WallPlacementGeometry.Rectangle(
            first, opposite, type.Thickness, offset, SelectedWallLocationReference());

        var overlap = centerlines.Select(centerline => WallOverlap.Find(_session.Document.Walls, _activeLevelId,
            centerline.Start, centerline.End, type.Thickness)).FirstOrDefault(found => found is not null);
        using var group = _session.BeginGroup("Transaction.DrawRectangle");
        foreach (var centerline in centerlines)
            _selectedWallId = _session.AddWall(
                _activeLevelId, centerline.Start, centerline.End,
                height: height, wallTypeId: typeId, locationReference: SelectedWallLocationReference());
        group.Assimilate();
        _selectedViewId = null;
        _pendingWallStart = null;
        _wallPreviewPoint = null;
        _chainTail = null;
        _chainOrigin = null;
        StatusText.Text = overlap is { } overlapped ? FormatText("Status.WallOverlaps", overlapped.Length)
            : GetText("Status.WallRectangleCreated");
        RefreshAll();
    }

    /// <summary>
    /// Resolves the pointer to the point a click would place, in Revit's order of preference: the first point of the
    /// chain (closing the loop), wall endpoints, midpoints and nearest points, the work plane, then the drawing
    /// direction (axes, 45° and 15° steps), alignment with corners on screen, and the 10 cm grid.
    /// </summary>
    private Point2 SnapWallPoint(Point2 point)
    {
        _activeWallSnap = null;
        var line = WallRectangleMode.IsChecked != true;
        if (line && _pendingWallStart is Point2 from &&
            WallPlacementGeometry.FindChainClosure(point, _chainOrigin?.ReferenceStart, from,
                ChainClosureSnapPixels / _scale) is WallSnapResult closure)
        {
            _activeWallSnap = closure;
            return closure.Point;
        }
        if (WallObjectSnapCheck.IsChecked == true)
        {
            var wallCenterlines = _session.Document.Walls
                .Where(wall => wall.LevelId == _activeLevelId)
                .Select(wall => new WallCenterline(wall.Start, wall.End));
            _activeWallSnap = WallPlacementGeometry.FindNearestSnap(
                point, wallCenterlines, Math.Max(0.02, WallObjectSnapPixels / _scale));
            if (_activeWallSnap is WallSnapResult snap) return snap.Point;
        }
        if (SnapToWorkPlane(point) is Point2 onPlane) return onPlane;

        var grid = SnapGridCheck.IsChecked == true;
        var guides = ChooseAlignmentGuides(point, PlanAlignment.Find(point, AlignmentCorners(null),
            VisibleWorldBounds(), AlignmentTolerancePixels / _scale));
        if (line && _pendingWallStart is Point2 start && !OrthogonalDrawingActive() &&
            SnapWallDirection(start, point) is Point2 along)
        {
            // Along a snapped direction only the length is free: stop it on a guide that crosses the line, or round it.
            var vertical = Math.Abs(along.X - start.X) < 1e-9;
            var horizontal = Math.Abs(along.Y - start.Y) < 1e-9;
            foreach (var guide in guides)
            {
                if (vertical && guide.Axis == PlanGuideAxis.Horizontal) return new Point2(start.X, guide.Corner.Y);
                if (horizontal && guide.Axis == PlanGuideAxis.Vertical) return new Point2(guide.Corner.X, start.Y);
            }
            return grid ? WallPlacementGeometry.RoundLength(start, along, 0.1) : along;
        }

        double? alignedX = null, alignedY = null;
        foreach (var guide in guides)
        {
            if (guide.Axis == PlanGuideAxis.Vertical) alignedX ??= guide.Corner.X;
            else alignedY ??= guide.Corner.Y;
        }
        return new Point2(
            alignedX ?? (grid ? Math.Round(point.X * 10, MidpointRounding.AwayFromZero) / 10 : point.X),
            alignedY ?? (grid ? Math.Round(point.Y * 10, MidpointRounding.AwayFromZero) / 10 : point.Y));
    }

    /// <summary>Snaps the drawing direction: firmly to the axes, a little less to 45° and only just to 15° steps.</summary>
    private static Point2? SnapWallDirection(Point2 start, Point2 pointer) =>
        WallPlacementGeometry.SnapAngle(start, pointer, 90, 5) ??
        WallPlacementGeometry.SnapAngle(start, pointer, 45, 3) ??
        WallPlacementGeometry.SnapAngle(start, pointer, 15, 1.5);

    /// <summary>Orthogonal drawing is on when the option is checked or, as in Revit, while Shift is held.</summary>
    private bool OrthogonalDrawingActive() =>
        WallOrthogonalCheck.IsChecked == true || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

    /// <summary>Locks a line endpoint to the dominant axis when orthogonal placement is enabled; a closing point is kept exact.</summary>
    private Point2 ConstrainWallEndpoint(Point2 start, Point2 candidate) =>
        _activeWallSnap?.Kind == WallSnapKind.ChainStart
            ? candidate
            : WallPlacementGeometry.ConstrainEndpoint(start, candidate, OrthogonalDrawingActive());

    /// <summary>Moves the stored centerline according to the chosen reference face and explicit offset.</summary>
    private (Point2 Start, Point2 End) OffsetWallCenterline(
        Point2 start, Point2 end, double offset, Guid typeId)
    {
        var thickness = _session.Document.WallTypes.Single(type => type.Id == typeId).Thickness;
        var centerline = WallPlacementGeometry.OffsetLine(
            start, end, thickness, offset, SelectedWallLocationReference());
        return (centerline.Start, centerline.End);
    }

    /// <summary>Maps the localized placement selector to a stable domain reference.</summary>
    private WallLocationReference SelectedWallLocationReference() =>
        ((WallLocationLinePicker.SelectedItem as ComboBoxItem)?.Tag as string) switch
        {
            "Exterior" => WallLocationReference.ExteriorFace,
            "Interior" => WallLocationReference.InteriorFace,
            _ => WallLocationReference.Centerline
        };

    /// <summary>Validates localized numeric placement inputs without mutating the document.</summary>
    private bool TryReadWallPlacementOptions(out double height, out double offset, out Guid typeId)
    {
        var validHeight = double.TryParse(WallHeightInput.Text, NumberStyles.Float,
            CultureInfo.CurrentCulture, out height) && height is >= 0.1 and <= 1000;
        var validOffset = double.TryParse(WallOffsetInput.Text, NumberStyles.Float,
            CultureInfo.CurrentCulture, out offset) && double.IsFinite(offset) && Math.Abs(offset) <= 100000;
        typeId = (WallTypePicker.SelectedItem as ComboBoxItem)?.Tag as Guid? ?? Guid.Empty;
        return validHeight && validOffset && typeId != Guid.Empty;
    }

    /// <summary>Creates a line wall at the exact entered length along the current preview direction.</summary>
    private void PlaceWallByLength_Click(object sender, RoutedEventArgs e)
    {
        if (WallRectangleMode.IsChecked == true)
        {
            StatusText.Text = GetText("Status.WallLengthRequiresLine");
            return;
        }
        if (_pendingWallStart is not Point2 start || _wallPreviewPoint is not Point2 directionCandidate)
        {
            StatusText.Text = GetText("Status.WallLengthNeedsDirection");
            return;
        }
        if (!TryReadWallPlacementOptions(out var height, out var offset, out var typeId) ||
            !double.TryParse(CurrentWallLengthText(), NumberStyles.Float, CultureInfo.CurrentCulture, out var length) ||
            !double.IsFinite(length) || length is < 0.05 or > 100000)
        {
            StatusText.Text = GetText("Status.WallInvalidOptions");
            return;
        }

        try
        {
            var endpoint = WallPlacementGeometry.EndpointAtLength(
                start, directionCandidate, length, OrthogonalDrawingActive());
            CreateWallSegment(start, endpoint, height, offset, typeId);
            ClearDirectWallLength();
        }
        catch (InvalidDataException exception)
        {
            MessageBox.Show(this, LocalizedErrors.Translate(exception.Message), GetText("Dialog.InvalidGeometry"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Places the exact-length wall when Enter is pressed in the contextual length field.</summary>
    private void WallLengthInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        PlaceWallByLength_Click(sender, new RoutedEventArgs());
        e.Handled = true;
    }

    /// <summary>Refreshes the provisional sketch when a contextual placement option changes.</summary>
    private void WallOptions_Changed(object sender, RoutedEventArgs e)
    {
        if (WallObjectSnapCheck?.IsChecked != true) _activeWallSnap = null;
        // Switching between line, rectangle and arc abandons an arc that has its end but not yet its bulge.
        if (sender is RadioButton) _pendingArcEnd = null;
        if (PlanCanvas is not null && _session is not null) RenderPlan();
    }

    /// <summary>Leaves wall placement while retaining all completed wall transactions.</summary>
    private void FinishWallTool_Click(object sender, RoutedEventArgs e) => SetWallTool(false);

    /// <summary>Cancels the provisional wall and leaves the wall command.</summary>
    private void CancelWallTool_Click(object sender, RoutedEventArgs e)
    {
        _pendingWallStart = null;
        _wallPreviewPoint = null;
        _activeWallSnap = null;
        _chainTail = null;
        _chainOrigin = null;
        ClearDirectWallLength();
        SetWallTool(false);
    }

    /// <summary>Cancels the active segment first and exits the wall command on the next cancellation.</summary>
    private void CancelWallPlacementStep()
    {
        if (_pendingWallStart is not null)
        {
            _pendingWallStart = null;
            _wallPreviewPoint = null;
            _activeWallSnap = null;
            _chainTail = null;
            _chainOrigin = null;
            ClearDirectWallLength();
            StatusText.Text = GetText("Status.WallTool");
            RenderPlan();
            return;
        }
        SetWallTool(false);
    }

    /// <summary>
    /// Revit's spacebar during wall placement: the wall moves to the other side of the line being drawn. With a face
    /// reference that swaps exterior and interior face; on the centerline both sides are the same, which is reported.
    /// </summary>
    private bool TryFlipWallSide()
    {
        var target = ((WallLocationLinePicker.SelectedItem as ComboBoxItem)?.Tag as string) switch
        {
            "Exterior" => "Interior",
            "Interior" => "Exterior",
            _ => null
        };
        if (target is null)
        {
            StatusText.Text = GetText("Status.WallFlipCenterline");
            return true;
        }
        WallLocationLinePicker.SelectedItem = WallLocationLinePicker.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag as string == target);
        StatusText.Text = GetText("Status.WallFlipped");
        RenderPlan();
        return true;
    }

    /// <summary>Applies Revit-like two-stage escape behavior to the active placement command.</summary>
    private void HandleWallEscape() => CancelWallPlacementStep();

    /// <summary>
    /// Returns the same-type wall end that a new wall starts from: the chained tail when it is
    /// continued, otherwise any wall on the level whose endpoint sits on the reference point.
    /// </summary>
    private (Wall Wall, WallEnd End)? FindContinuedWallEnd(Point2 referenceStart, Guid typeId)
    {
        if (_chainTail is { } tail && tail.ReferenceEnd.DistanceTo(referenceStart) < 1e-9 &&
            _session.Document.Walls.FirstOrDefault(candidate => candidate.Id == tail.WallId) is Wall previous &&
            previous.WallTypeId == typeId)
            return (previous, WallEnd.End);

        foreach (var wall in _session.Document.Walls)
        {
            if (wall.LevelId != _activeLevelId || wall.WallTypeId != typeId) continue;
            if (wall.End.DistanceTo(referenceStart) <= WallJoinIndex.Tolerance) return (wall, WallEnd.End);
            if (wall.Start.DistanceTo(referenceStart) <= WallJoinIndex.Tolerance) return (wall, WallEnd.Start);
        }
        return null;
    }

    /// <summary>Arms Create Similar; the next wall picked in the plan supplies the type and height of the new wall.</summary>
    private void CreateSimilar_Click(object sender, RoutedEventArgs e)
    {
        SetWallTool(false);
        _awaitingSimilarWallPick = true;
        StatusText.Text = GetText("Status.CreateSimilarPick");
    }

    /// <summary>Starts the wall tool with the type and height of the wall picked for Create Similar.</summary>
    private void TryStartSimilarWall(object source)
    {
        if ((source as FrameworkElement)?.Tag is not Guid id ||
            _session.Document.Walls.FirstOrDefault(candidate => candidate.Id == id) is not Wall picked)
        {
            StatusText.Text = GetText("Status.CreateSimilarPick");
            return;
        }

        _awaitingSimilarWallPick = false;
        SetWallTool(true);
        WallTypePicker.SelectedItem = WallTypePicker.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is Guid typeId && typeId == picked.WallTypeId);
        WallHeightInput.Text = picked.Height.ToString("0.00", CultureInfo.CurrentCulture);
    }

    /// <summary>Shows or hides contextual placement controls so command state is always visible.</summary>
    private void UpdateWallToolVisualState()
    {
        if (WallOptionsBar is null) return;
        WallOptionsBar.Visibility = _drawingWall ? Visibility.Visible : Visibility.Collapsed;
        UpdateContextualTabs();
        WallCommandButton.FontWeight = _drawingWall ? FontWeights.Bold : FontWeights.Normal;
        // Drawing uses the plain crosshair, as in Revit; only the snap marker and the sketch follow the pointer.
        PlanCanvas.Cursor = _drawingWall ? Cursors.Cross : Cursors.Arrow;
        if (!_drawingWall) _activeWallSnap = null;
    }
}
