using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Contains the Revit-style move tool: the first plan click sets a base point, the second sets the
/// destination, and the selected wall, opening, room, or floor is translated by that difference.
/// A door or window follows the pointer live and re-hosts onto the nearest wall; it is never left in the air.
/// </summary>
public partial class MainWindow
{
    /// <summary>Screen distance from a wall face within which a moving opening snaps onto that wall.</summary>
    private const double OpeningCapturePixels = 24;

    private static readonly SolidColorBrush MoveTrailBrush = Frozen(Color.FromRgb(0, 96, 124));
    private static readonly SolidColorBrush MoveFillBrush = Frozen(Color.FromArgb(60, 0, 127, 163));
    private static readonly SolidColorBrush OpeningGhostBrush = Frozen(Color.FromRgb(0, 127, 163));
    private static readonly SolidColorBrush OpeningGhostFill = Frozen(Color.FromArgb(90, 0, 127, 163));
    private static readonly SolidColorBrush OpeningBlockedBrush = Frozen(Color.FromRgb(200, 40, 40));
    private static readonly SolidColorBrush OpeningBlockedFill = Frozen(Color.FromArgb(90, 200, 40, 40));
    private static readonly SolidColorBrush OpeningAirBrush = Frozen(Color.FromRgb(96, 104, 112));
    private static readonly SolidColorBrush OpeningAirFill = Frozen(Color.FromArgb(40, 96, 104, 112));

    private bool _movingElement;
    private Point2? _moveBasePoint;
    private Point2? _movePreviewPoint;

    /// <summary>Where a moving opening's center would land, and the host that position resolves to; a null host means it is in the air.</summary>
    private readonly record struct OpeningMove(Point2 Center, OpeningHost? Host);

    /// <summary>Starts the move tool for the current selection, or explains that nothing movable is selected.</summary>
    private void BeginMoveTool()
    {
        if (SelectedMovableId() is null)
        {
            StatusText.Text = GetText("Status.SelectElementToMove");
            return;
        }
        SetWallTool(false);
        CancelExtendedDrawing();
        _movingElement = true;
        _moveBasePoint = null;
        _movePreviewPoint = null;
        StatusText.Text = GetText("Status.MoveBasePoint");
        RenderPlan();
    }

    /// <summary>Leaves the move tool without changing the document.</summary>
    private void CancelMoveTool()
    {
        if (!_movingElement) return;
        ClearMoveState();
        StatusText.Text = GetText("Status.SelectionMode");
        RenderPlan();
    }

    /// <summary>Tracks the destination while the move tool is active and redraws its preview.</summary>
    private void UpdateMovePreview(Point canvasPoint)
    {
        var pointer = CanvasToWorld(canvasPoint);
        _movePreviewPoint = MovePoint(canvasPoint);
        UpdateAlignmentGuides(pointer, SelectedMovableId());
        RenderPlan();
    }

    /// <summary>
    /// Returns the model point a move uses for a pointer position. A selected opening uses the raw
    /// pointer so it tracks the mouse exactly; other elements keep the wall and grid snapping.
    /// </summary>
    private Point2 MovePoint(Point canvasPoint)
    {
        var world = CanvasToWorld(canvasPoint);
        return _selectedOpeningId is null ? SnapWallPoint(world) : world;
    }

    /// <summary>Uses the first click as the base point and the second as the destination, then applies the move once.</summary>
    private void HandleMoveClick(Point canvasPoint)
    {
        if (SelectedMovableId() is null)
        {
            CancelMoveTool();
            return;
        }
        var point = MovePoint(canvasPoint);
        if (_moveBasePoint is not Point2 basePoint)
        {
            _moveBasePoint = point;
            _movePreviewPoint = point;
            StatusText.Text = GetText("Status.MoveDestination");
            RenderPlan();
            return;
        }

        CommitMove(point.X - basePoint.X, point.Y - basePoint.Y);
    }

    /// <summary>
    /// Applies a finished translation as one validated transaction. A translation that leaves a
    /// door or window in the air is cancelled with a status message; an invalid one is rejected atomically.
    /// </summary>
    private void CommitMove(double deltaX, double deltaY)
    {
        ClearMoveState();
        var moved = Math.Abs(deltaX) > 1e-9 || Math.Abs(deltaY) > 1e-9;
        try
        {
            var applied = !moved || ApplyMove(deltaX, deltaY);
            StatusText.Text = GetText(applied ? "Status.MoveApplied" : "Status.OpeningNeedsWall");
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = GetText("Status.MoveRejected");
            MessageBox.Show(this, LocalizedErrors.Translate(exception.Message),
                GetText(SelectedOpening() is null ? "Dialog.MoveWall" : "Dialog.MoveOpening"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        RefreshAll();
    }

    /// <summary>Returns the one element the move tool translates; the most specific selection wins.</summary>
    private Guid? SelectedMovableId() =>
        _selectedOpeningId ?? _selectedWallId ?? _selectedRoomId ?? _selectedFloorId ?? _selectedColumnId ?? _selectedStructuralId ?? _selectedGridId;

    /// <summary>Returns the selected opening, or null when the selection is not an opening that still exists.</summary>
    private WallOpening? SelectedOpening() =>
        _selectedOpeningId is Guid id ? _session.Document.Openings.FirstOrDefault(candidate => candidate.Id == id) : null;

    /// <summary>True while the opening's original symbol is hidden because its ghost is being placed elsewhere.</summary>
    private bool IsOpeningInMotion(WallOpening opening) =>
        _movingElement && _moveBasePoint is not null && opening.Id == _selectedOpeningId;

    /// <summary>
    /// Translates the selected element. A hosted opening is re-hosted onto the wall its resolved
    /// position lies on. Returns false, changing nothing, when that position is in the air.
    /// </summary>
    private bool ApplyMove(double deltaX, double deltaY)
    {
        if (SelectedOpening() is WallOpening opening)
        {
            if (ResolveOpeningMove(opening, deltaX, deltaY).Host is not OpeningHost host) return false;
            _session.MoveOpening(opening.Id, host.WallId, host.Offset);
            _selectedWallId = host.WallId;
            _selectedWallOffset = host.Offset;
        }
        else if (_selectedWallId is Guid wallId) _session.MoveWall(wallId, deltaX, deltaY);
        else if (_selectedRoomId is Guid roomId) _session.MoveRoom(roomId, deltaX, deltaY);
        else if (_selectedFloorId is Guid floorId) _session.MoveFloor(floorId, deltaX, deltaY);
        else if (_selectedColumnId is Guid columnId) _session.MoveColumn(columnId, deltaX, deltaY);
        else if (_selectedStructuralId is Guid structuralId) _session.MoveStructuralElement(structuralId, deltaX, deltaY);
        else if (_selectedGridId is Guid gridId) _session.MoveGridAxis(gridId, deltaX, deltaY);
        return true;
    }

    /// <summary>Resolves where an opening lands after a translation, without changing the document.</summary>
    private OpeningMove ResolveOpeningMove(WallOpening opening, double deltaX, double deltaY)
    {
        var document = _session.Document;
        var wall = document.Walls.Single(candidate => candidate.Id == opening.WallId);
        var center = OpeningHosting.Center(wall, opening);
        var candidate = new Point2(center.X + deltaX, center.Y + deltaY);
        var host = OpeningHosting.Resolve(opening, candidate, document.Walls, document.Openings,
            OpeningCapturePixels / _scale);
        return new OpeningMove(candidate, host);
    }

    /// <summary>Draws the moving element at its provisional destination, with a dashed path from the base point.</summary>
    private void RenderMovePreview()
    {
        if (!_movingElement || _moveBasePoint is not Point2 basePoint || _movePreviewPoint is not Point2 target) return;
        var deltaX = target.X - basePoint.X;
        var deltaY = target.Y - basePoint.Y;
        if (SelectedOpening() is WallOpening opening)
            RenderOpeningMovePreview(opening, deltaX, deltaY);
        else if (_selectedGridId is Guid axisId &&
                 _session.Document.Grids.FirstOrDefault(axis => axis.Id == axisId) is { } axis)
        {
            AddDashedLine(new Point2(axis.Start.X + deltaX, axis.Start.Y + deltaY),
                new Point2(axis.End.X + deltaX, axis.End.Y + deltaY), MoveTrailBrush);
        }
        else
        {
            var outline = TranslatedOutline(deltaX, deltaY);
            if (outline is not null)
            {
                PlanCanvas.Children.Add(new Polygon
                {
                    Points = new PointCollection(outline.Select(WorldToCanvas)),
                    Fill = MoveFillBrush,
                    Stroke = MoveTrailBrush,
                    StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection { 4, 3 },
                    IsHitTestVisible = false
                });
            }
        }
        AddDashedLine(basePoint, target, MoveTrailBrush);
    }

    /// <summary>
    /// Draws a moving opening where it would land: a solid symbol inside its host wall when valid, a red
    /// symbol when it would overlap or overrun that wall, or a dashed footprint while it is in the air.
    /// </summary>
    private void RenderOpeningMovePreview(WallOpening opening, double deltaX, double deltaY)
    {
        var move = ResolveOpeningMove(opening, deltaX, deltaY);
        if (move.Host is OpeningHost host)
        {
            var hostWall = _session.Document.Walls.Single(candidate => candidate.Id == host.WallId);
            var ghost = opening with { WallId = host.WallId, Offset = host.Offset };
            DrawOpeningSymbol(hostWall, ghost,
                host.Fits ? OpeningGhostBrush : OpeningBlockedBrush,
                host.Fits ? OpeningGhostFill : OpeningBlockedFill, tag: null);
            if (!host.Fits) StatusText.Text = GetText("Status.OpeningBlocked");
            return;
        }

        var wall = _session.Document.Walls.Single(candidate => candidate.Id == opening.WallId);
        var axisX = (wall.End.X - wall.Start.X) / wall.Length;
        var axisY = (wall.End.Y - wall.Start.Y) / wall.Length;
        Point2 At(double u, double v) =>
            new(move.Center.X + axisX * u - axisY * v, move.Center.Y + axisY * u + axisX * v);
        var halfWidth = opening.Width / 2;
        var halfThickness = wall.Thickness / 2;
        PlanCanvas.Children.Add(new Polygon
        {
            Points = new PointCollection
            {
                WorldToCanvas(At(-halfWidth, -halfThickness)), WorldToCanvas(At(halfWidth, -halfThickness)),
                WorldToCanvas(At(halfWidth, halfThickness)), WorldToCanvas(At(-halfWidth, halfThickness))
            },
            Fill = OpeningAirFill,
            Stroke = OpeningAirBrush,
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            IsHitTestVisible = false
        });
        StatusText.Text = GetText("Status.OpeningNeedsWall");
    }

    /// <summary>Returns the plan outline of the selected element moved by a delta, or null when it has none to draw.</summary>
    private IReadOnlyList<Point2>? TranslatedOutline(double deltaX, double deltaY)
    {
        Point2 Shift(Point2 point) => new(point.X + deltaX, point.Y + deltaY);

        if (_selectedOpeningId is not null) return null;
        if (_selectedWallId is Guid wallId &&
            _session.Document.Walls.FirstOrDefault(candidate => candidate.Id == wallId) is Wall wall)
        {
            return WallPlacementGeometry.Footprint(Shift(wall.Start), Shift(wall.End), wall.Thickness, 0,
                WallLocationReference.Centerline).Points;
        }
        if (_selectedRoomId is Guid roomId &&
            _session.Document.Rooms.FirstOrDefault(candidate => candidate.Id == roomId) is Room room)
        {
            var first = room.FirstCorner;
            var second = room.SecondCorner;
            return [Shift(first), Shift(new Point2(second.X, first.Y)), Shift(second), Shift(new Point2(first.X, second.Y))];
        }
        if (_selectedFloorId is Guid floorId &&
            _session.Document.Floors.FirstOrDefault(candidate => candidate.Id == floorId) is FloorSlab floor)
        {
            return floor.Boundary.Select(Shift).ToArray();
        }
        if (_selectedColumnId is Guid columnId &&
            _session.Document.Columns.FirstOrDefault(candidate => candidate.Id == columnId) is { } column)
            return column.Outline.Select(Shift).ToArray();
        if (_selectedStructuralId is Guid structuralId &&
            _session.Document.StructuralElements.FirstOrDefault(candidate => candidate.Id == structuralId) is { } structural)
            return structural.Footprint.Select(Shift).ToArray();
        return null;
    }

    /// <summary>Clears every piece of move-tool state, including the guides it was showing.</summary>
    private void ClearMoveState()
    {
        ClearModelMovePreview();
        _modelMovePlane = null;
        _movingElement = false;
        _moveBasePoint = null;
        _movePreviewPoint = null;
        _alignmentGuides = [];
    }
}
