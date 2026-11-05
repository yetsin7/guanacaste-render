using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>Adds a level and makes it the active plan.</summary>
    private void AddLevel_Click(object sender, RoutedEventArgs e)
    {
        _activeLevelId = _session.AddLevel();
        RefreshAll();
    }

    /// <summary>Switches the active plan when the level selector changes.</summary>
    private void LevelPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingLevels || LevelPicker.SelectedItem is not ComboBoxItem item || item.Tag is not Guid id)
            return;
        _activeLevelId = id;
        _selectedViewId = _session.Document.Views.First(view =>
            view.Kind == ProjectViewKind.Plan && view.LevelId == id).Id;
        _selectedWallId = null;
        _pendingWallStart = null;
        RefreshAll();
    }

    /// <summary>Coordinates project-browser selection with the active model selection.</summary>
    private void ProjectTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _selectedGridId = null;
        if (e.NewValue is not TreeViewItem item || item.Tag is not Guid id) return;
        if (TryHandleProjectTreeSelection(id)) return;
        _selectedViewId = null;
        if (_session.Document.Openings.FirstOrDefault(opening => opening.Id == id) is WallOpening opening)
        {
            _selectedOpeningId = id;
            _selectedWallId = opening.WallId;
            RefreshProperties();
            RenderPlan();
        }
        else if (_session.Document.Walls.Any(wall => wall.Id == id))
        {
            _selectedOpeningId = null;
            _selectedWallId = id;
            RefreshProperties();
            RenderPlan();
        }
        else if (_session.Document.Levels.Any(level => level.Id == id))
        {
            _activeLevelId = id;
            _selectedViewId = _session.Document.Views.First(view =>
                view.Kind == ProjectViewKind.Plan && view.LevelId == id).Id;
            _selectedWallId = null;
            _selectedOpeningId = null;
            RefreshLevelPicker();
            RenderPlan();
            RefreshProperties();
        }
    }

    /// <summary>Handles wall placement and element selection in the plan canvas.</summary>
    private void PlanCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // A double-click on an element opens its editor; its first click has just selected it.
        if (e.ClickCount == 2 && _planPointerOverride is null && !PlanToolArmed() && CurrentSelection() is not null)
        {
            OpenSelectionEditor();
            e.Handled = true;
            return;
        }
        if (!_movingElement && !PlanToolArmed() &&
            (TrySelectGridAxis(e) || TrySelectElevationSymbol(e) || TrySelectSectionSymbol(e))) return;
        if (TryHandleGridPlacement(e)) return;
        if (TryHandleSectionPlacement(e)) return;
        if (TryHandleCurvedRoomSeparatorClick(e)) return;
        if (TryPlaceOpening(e)) return;
        RecordPlanPress(e);
        if (_movingElement)
        {
            Keyboard.Focus(PlanCanvas);
            HandleMoveClick(PlanPosition(e));
            return;
        }
        if (TryHandleElectricalClick(e)) return;
        if (TryHandlePlumbingClick(e)) return;
        if (TryHandleTopoPointClick(e)) return;
        if (TryHandleRoomPlacement(e)) return;
        if (TryHandleColumnPlacement(e)) return;
        if (TryHandleStructuralPlacement(e)) return;
        if (_awaitingSimilarWallPick)
        {
            Keyboard.Focus(PlanCanvas);
            TryStartSimilarWall(e.OriginalSource);
            return;
        }
        if (_drawingWall)
        {
            Keyboard.Focus(PlanCanvas);
            HandleWallPlacementClick(PlanPosition(e));
            return;
        }
        if (!_drawingWall)
        {
            if (TrySelectOpeningSymbol(e)) return;
            if (ElectricalPlanVisibility.IsChecked == true && TrySelectElectrical(e)) return;
            if (PlumbingPlanVisibility.IsChecked == true && TrySelectPlumbing(e)) return;
            if (TrySelectExtendedPlanElement(e)) return;
            _selectedViewId = null;
            _selectedWallId = (e.OriginalSource as FrameworkElement)?.Tag is Guid id
                && _session.Document.Walls.Any(wall => wall.Id == id) ? id : null;
            _selectedWallOffset = null;
            _selectedOpeningId = null;
            _selectedRoomId = null;
            _selectedFloorId = null;
            _selectedStructuralId = null;
            _selectedColumnId = null;
            _selectedElectricalId = null;
            _selectedPlumbingId = null;
            if (_selectedWallId is Guid wallId)
            {
                var wall = _session.Document.Walls.First(candidate => candidate.Id == wallId);
                var clicked = CanvasToWorld(PlanPosition(e));
                var directionX = (wall.End.X - wall.Start.X) / wall.Length;
                var directionY = (wall.End.Y - wall.Start.Y) / wall.Length;
                _selectedWallOffset = Math.Clamp(
                    (clicked.X - wall.Start.X) * directionX + (clicked.Y - wall.Start.Y) * directionY,
                    0, wall.Length);
            }
            RefreshProperties();
            RenderPlan();
            return;
        }

    }

    /// <summary>Cancels any in-progress sketch when the secondary mouse button is pressed.</summary>
    private void PlanCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (CancelElectricalTools() | CancelPlumbingTools())
        {
            StatusText.Text = GetText("Status.SelectionMode");
            e.Handled = true;
            return;
        }
        if (CancelCurvedRoomSeparatorTool())
        {
            e.Handled = true;
            return;
        }
        if (CancelTopoPointTool())
        {
            e.Handled = true;
            return;
        }
        if (TryFinishSlabSketch())
        {
            e.Handled = true;
            return;
        }
        if (_movingElement)
        {
            CancelMoveTool();
            e.Handled = true;
            return;
        }
        if (_drawingWall)
        {
            CancelWallPlacementStep();
            e.Handled = true;
            return;
        }
        if (_awaitingSimilarWallPick)
        {
            _awaitingSimilarWallPick = false;
            StatusText.Text = GetText("Status.SelectionMode");
            RenderPlan();
            e.Handled = true;
            return;
        }
        _pendingWallStart = null;
        CancelExtendedDrawing();
        RenderPlan();
        StatusText.Text = GetText("Status.DrawingCancelled");
    }

    /// <summary>Pixels per meter limits of the plan: the closest zoom is four times what it used to be.</summary>
    private const double MinimumPlanScale = 8, MaximumPlanScale = 800;

    /// <summary>Zooms the plan around the pointer position.</summary>
    private void PlanCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var cursor = PlanPosition(e);
        var anchor = CanvasToWorld(cursor);
        _scale = Math.Clamp(_scale * (e.Delta > 0 ? 1.15 : 1.0 / 1.15), MinimumPlanScale, MaximumPlanScale);
        _panOffset = new Point(
            cursor.X - PlanCanvas.ActualWidth / 2 - anchor.X * _scale,
            cursor.Y - PlanCanvas.ActualHeight / 2 + anchor.Y * _scale);
        // The pointer keeps its world position under zoom, so an active sketch keeps tracking it without a mouse move.
        if (_drawingWall && _pendingWallStart is not null) _wallPreviewPoint = SnapWallPoint(CanvasToWorld(cursor));
        RenderPlan();
        e.Handled = true;
    }

    /// <summary>Redraws the plan after the viewport size changes.</summary>
    private void PlanCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderPlan();

    /// <summary>Transforms a model coordinate into the current plan-canvas coordinate system.</summary>
    private Point WorldToCanvas(Point2 point) =>
        new(PlanCanvas.ActualWidth / 2 + _panOffset.X + point.X * _scale,
            PlanCanvas.ActualHeight / 2 + _panOffset.Y - point.Y * _scale);

    /// <summary>Transforms a plan-canvas coordinate into model space.</summary>
    private Point2 CanvasToWorld(Point point) =>
        new((point.X - PlanCanvas.ActualWidth / 2 - _panOffset.X) / _scale,
            (PlanCanvas.ActualHeight / 2 + _panOffset.Y - point.Y) / _scale);

    /// <summary>Fits the active level geometry into the available plan viewport.</summary>
    private void FitPlan_Click(object sender, RoutedEventArgs e)
    {
        var walls = _session.Document.Walls.Where(wall => wall.LevelId == _activeLevelId).ToArray();
        var points = walls.SelectMany(wall => new[] { wall.Start, wall.End })
            .Concat(_session.Document.Floors.Where(floor => floor.LevelId == _activeLevelId).SelectMany(floor => floor.Boundary))
            .Concat(_session.Document.Columns.Where(column => column.LevelId == _activeLevelId).SelectMany(column => column.Outline))
            .Concat(_session.Document.StructuralElements.Where(element => element.LevelId == _activeLevelId && element.HasSolid)
                .SelectMany(element => element.Footprint)).ToList();
        if (points.Count == 0)
        {
            _scale = 36;
            _panOffset = new Point();
            RenderPlan();
            StatusText.Text = GetText("Status.PlanCentered");
            return;
        }
        // The elevation symbols sit just outside the building, so framing the walls alone would cut them off.
        points.AddRange(ElevationSymbols().SelectMany(symbol => new[]
            {
                new Point2(symbol.Position.X - 1.4, symbol.Position.Y - 1.4), new Point2(symbol.Position.X + 1.4, symbol.Position.Y + 1.4)
            }));
        var minimumX = points.Min(point => point.X);
        var maximumX = points.Max(point => point.X);
        var minimumY = points.Min(point => point.Y);
        var maximumY = points.Max(point => point.Y);
        var width = Math.Max(1, PlanCanvas.ActualWidth - 100);
        var height = Math.Max(1, PlanCanvas.ActualHeight - 100);
        _scale = Math.Clamp(Math.Min(
            width / Math.Max(1, maximumX - minimumX),
            height / Math.Max(1, maximumY - minimumY)), MinimumPlanScale, MaximumPlanScale);
        _panOffset = new Point(
            -(minimumX + maximumX) / 2 * _scale,
            (minimumY + maximumY) / 2 * _scale);
        RenderPlan();
        StatusText.Text = GetText("Status.PlanFit");
    }

    /// <summary>Toggles the plan grid from either ribbon or menu commands.</summary>
    private void ToggleGrid_Click(object sender, RoutedEventArgs e) =>
        ShowGridCheck.IsChecked = ShowGridCheck.IsChecked != true;

    /// <summary>Redraws the plan when the grid visibility changes.</summary>
    private void GridVisibility_Changed(object sender, RoutedEventArgs e)
    {
        if (PlanCanvas is not null) RenderPlan();
    }

    /// <summary>Renders the active coordinated plan, including grid, BIM elements, and sketch feedback.</summary>
    private void RenderPlan()
    {
        if (PlanCanvas is null || _session is null) return;
        PlanCanvas.Children.Clear();
        if (PlanCanvas.ActualWidth < 1 || PlanCanvas.ActualHeight < 1) return;
        var left = -PlanCanvas.ActualWidth / 2 - _panOffset.X;
        var right = PlanCanvas.ActualWidth / 2 - _panOffset.X;
        var top = -PlanCanvas.ActualHeight / 2 - _panOffset.Y;
        var bottom = PlanCanvas.ActualHeight / 2 - _panOffset.Y;
        if (ShowGridCheck.IsChecked == true)
        {
            for (var gridX = Math.Ceiling(left / _scale); gridX <= right / _scale; gridX++)
                AddLine(new Point2(gridX, -bottom / _scale), new Point2(gridX, -top / _scale), 0.5, Brushes.Gainsboro);
            for (var gridY = Math.Ceiling(top / _scale); gridY <= bottom / _scale; gridY++)
                AddLine(new Point2(left / _scale, -gridY), new Point2(right / _scale, -gridY), 0.5, Brushes.Gainsboro);
        }
        AddLine(new Point2(left / _scale, 0), new Point2(right / _scale, 0), 1, Brushes.LightSteelBlue);
        AddLine(new Point2(0, -bottom / _scale), new Point2(0, -top / _scale), 1, Brushes.LightSteelBlue);
        RenderSiteReferences();
        // The plan draws what its own view shows; hidden or isolated elements are left out of this plan only.
        var planView = _session.Document.Views.FirstOrDefault(view =>
            view.Kind == ProjectViewKind.Plan && view.LevelId == _activeLevelId);
        var range = planView?.Range ?? PlanViewRange.Architectural;
        var planDocument = ViewVisibility.Apply(_session.Document, planView);
        RenderExtendedPlanElements(planDocument, range);
        RenderCurvedRoomSeparators();
        RenderInPlaceFamilies();
        var joins = WallJoinIndex.Build(planDocument.Walls);
        var levelWalls = planDocument.Walls.Where(candidate => candidate.LevelId == _activeLevelId &&
            range.Intersects(0, candidate.Height)).ToArray();
        // Every outline sits beneath every fill, so a neighbour covers the shared faces and miter seams.
        var wallLayer = PlanCanvas.Children.Count;
        foreach (var wall in levelWalls) AddWallBody(wall, joins);
        RenderJoinedWallOutline(levelWalls, joins, wallLayer);
        // Draw structural columns above masonry fills. This makes the plan cut read correctly
        // at wall/column overlaps instead of exposing block cells through a solid column.
        foreach (var column in planDocument.Columns.Where(candidate => candidate.LevelId == _activeLevelId))
        {
            PlanCanvas.Children.Add(new Polygon
            {
                Points = new PointCollection(column.Outline.Select(WorldToCanvas)),
                Fill = Brushes.LightGray,
                Stroke = column.Id == _selectedColumnId ? Brushes.DarkOrange : Brushes.DimGray,
                StrokeThickness = column.Id == _selectedColumnId ? 2 : 1,
                Tag = column.Id
            });
        }
        foreach (var wall in levelWalls)
        {
            foreach (var opening in planDocument.Openings.Where(candidate => candidate.WallId == wall.Id))
            {
                if (IsOpeningInMotion(opening)) continue;
                if (range.CutOffset < opening.Sill || range.CutOffset > opening.Sill + opening.Height) continue;
                RenderOpeningSymbol(wall, opening);
            }
        }
        if (_pendingWallStart is Point2 start)
        {
            var canvasPoint = WorldToCanvas(start);
            var marker = new Ellipse { Width = 12, Height = 12, Fill = Brushes.ForestGreen };
            Canvas.SetLeft(marker, canvasPoint.X - 6);
            Canvas.SetTop(marker, canvasPoint.Y - 6);
            PlanCanvas.Children.Add(marker);
        }
        if (ElectricalPlanVisibility?.IsChecked == true) RenderElectricalPlan();
        if (PlumbingPlanVisibility?.IsChecked == true) RenderPlumbingPlan();
        RenderMovePreview();
        RenderAnalyticalOverlay();
        AddResultsFillLegend();
        AddNorthArrow();
        RenderTemporaryDimensions();
        RenderWallSketchOverlay();
        RenderStructuralPreview();
        RenderSlabSketchPreview();
        AddAlignmentGuides();
        RenderPlacementPreview();
        RenderPermanentDimensions();
        RenderElementTags();
        RenderCallouts();
        RenderElevationSymbols();
        RenderSectionSymbols();
        RenderGridAxes();
    }

    /// <summary>Fills a wall's mitered plan footprint, drawn above every outline so joined faces disappear.</summary>
    private void AddWallBody(Wall wall, WallJoinIndex joins)
    {
        var wallType = _session.Document.WallTypes.Single(type => type.Id == wall.WallTypeId);
        var footprint = joins.Footprint(wall);
        PlanCanvas.Children.Add(new Polygon
        {
            Points = new PointCollection(footprint.Points.Select(WorldToCanvas)),
            Fill = WallAppearanceRendering.CreateBrush(
                wallType.PlanFillColor == "#59636B" && wallType.Construction == WallConstruction.Brick
                    ? "#B9795B"
                    : wallType.PlanFillColor == "#59636B" && wallType.Construction == WallConstruction.Block
                        ? "#BFC1BD"
                        : wallType.PlanFillColor,
                wallType.PlanFillPattern),
            SnapsToDevicePixels = true,
            Tag = wall.Id
        });
        RenderWallAssemblyPlan(wall, wallType, footprint.Points);
    }

    /// <summary>Strokes the union boundary, hiding interior seams at corners, tees and crossings.</summary>
    private void RenderJoinedWallOutline(IReadOnlyList<Wall> walls, WallJoinIndex joins, int wallLayer)
    {
        Geometry outline = Geometry.Empty;
        foreach (var wall in walls)
        {
            var points = joins.Footprint(wall).Points.Select(WorldToCanvas).ToArray();
            var figure = new PathFigure { StartPoint = points[0], IsClosed = true, IsFilled = true };
            figure.Segments.Add(new PolyLineSegment(points.Skip(1), true));
            outline = Geometry.Combine(outline, new PathGeometry(new[] { figure }), GeometryCombineMode.Union, null);
        }
        PlanCanvas.Children.Insert(wallLayer, new System.Windows.Shapes.Path
        {
            Data = outline, Fill = Brushes.DarkSlateGray, Stroke = Brushes.DarkSlateGray, StrokeThickness = 1,
            IsHitTestVisible = false
        });
        if (walls.FirstOrDefault(wall => wall.Id == _selectedWallId) is { } selected)
            AddLine(selected.Start, selected.End, 1.5, Brushes.DarkOrange, selected.Id);
    }

    /// <summary>Adds a selectable model-space line to the plan canvas.</summary>
    private void AddLine(Point2 start, Point2 end, double thickness, Brush color, Guid? tag = null)
    {
        var startPoint = WorldToCanvas(start);
        var endPoint = WorldToCanvas(end);
        var line = new Line
        {
            X1 = startPoint.X,
            Y1 = startPoint.Y,
            X2 = endPoint.X,
            Y2 = endPoint.Y,
            Stroke = color,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Square,
            StrokeEndLineCap = PenLineCap.Square,
            SnapsToDevicePixels = true,
            Tag = tag
        };
        PlanCanvas.Children.Add(line);
    }
}
