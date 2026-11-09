using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private Guid? _selectedViewId;

    /// <summary>Displays the generated north elevation.</summary>
    private void ShowElevation_Click(object sender, RoutedEventArgs e) => SelectProjectView(_elevationViewId);

    /// <summary>Displays the generated section view.</summary>
    private void ShowSection_Click(object sender, RoutedEventArgs e) =>
        SelectProjectView(_sectionViewId);

    /// <summary>Redraws generated elevation and section views when their viewport changes.</summary>
    private void OrthographicCanvas_SizeChanged(object sender, SizeChangedEventArgs e) =>
        RenderOrthographicViews();

    /// <summary>Selects a persistent view and coordinates the active level and workspace tab.</summary>
    private void SelectProjectView(Guid viewId)
    {
        _selectedElectricalId = _selectedPlumbingId = _selectedGridId = null;
        var view = _session.Document.Views.FirstOrDefault(candidate => candidate.Id == viewId);
        if (view is null) return;
        _selectedViewId = view.Id;
        if (view.Kind == ProjectViewKind.Elevation) _elevationViewId = view.Id;
        if (view.Kind == ProjectViewKind.Section) _sectionViewId = view.Id;
        _selectedWallId = null;
        _selectedColumnId = _selectedStructuralId = null;
        _selectedOpeningId = null;
        _selectedRoomId = null;
        _selectedFloorId = null;
        _selectedSheetId = null;
        if (view.LevelId is Guid levelId) _activeLevelId = levelId;
        ViewTabs.SelectedIndex = GetViewTabIndex(view.Kind);
        RefreshLevelPicker();
        RefreshViewShortcuts();
        RefreshProperties();
        RenderOrthographicViews();
    }

    /// <summary>Maps a persistent view kind to the corresponding workspace tab.</summary>
    private static int GetViewTabIndex(ProjectViewKind kind) => kind switch
    {
        ProjectViewKind.Plan => 0,
        ProjectViewKind.Model3D => 1,
        ProjectViewKind.Schedule => 2,
        ProjectViewKind.Elevation => 4,
        ProjectViewKind.Section => 5,
        _ => 0
    };

    /// <summary>Renders all generated orthographic workspaces from the shared document.</summary>
    private void RenderOrthographicViews()
    {
        if (ElevationCanvas is null || SectionCanvas is null || _session is null) return;
        var (elevation, name) = CurrentElevation();
        // The Elevation tab carries the name of the elevation it shows, so a renamed view is renamed everywhere.
        if (ViewTabs.Items.Count > 4 && ViewTabs.Items[4] is TabItem elevationTab) elevationTab.Header = name;
        // Drawing these views meshes the whole building twice and projects every vertex, which took about three
        // seconds on the breeze-block house after each edit. Nobody sees them from another tab, so they wait there and
        // are drawn when their tab opens, like the 3D view.
        if (ViewTabs.SelectedIndex is not (ElevationTabIndex or SectionTabIndex))
        {
            _orthographicViewsStale = true;
            UpdateDocumentationCaption();
            return;
        }
        _orthographicViewsStale = false;
        // Each generated view draws what its own visibility shows: hidden or isolated elements are left out per view.
        var elevationDocument = ViewVisibility.Apply(_session.Document, ProjectViewById(_elevationViewId));
        RenderProjectedView(ElevationCanvas, elevation, BuildingMesher.Build(elevationDocument), elevationDocument);
        var sectionView = ProjectViewById(_sectionViewId);
        var sectionDocument = ViewVisibility.Apply(_session.Document, sectionView);
        if (sectionView?.SectionMode == SectionDisplayMode.Structural)
            sectionDocument = StructuralSectionDocument.Create(sectionDocument);
        RenderSectionView(BuildingMesher.Build(sectionDocument));
        UpdateDocumentationCaption();
    }

    private const int ElevationTabIndex = 4;
    private const int SectionTabIndex = 5;

    /// <summary>True when the elevation and section drawings are older than the model because their tabs were not open.</summary>
    private bool _orthographicViewsStale;

    /// <summary>The project view with this id, or null when it is not in the document.</summary>
    private ProjectView? ProjectViewById(Guid viewId) => _session.Document.Views.FirstOrDefault(view => view.Id == viewId);

    /// <summary>Projects model walls, openings, floors, and levels into one orthographic canvas.</summary>
    private void RenderProjectedView(Canvas canvas, ViewOrientation orientation, SceneMesh mesh, BuildingDocument document)
    {
        canvas.Children.Clear();
        if (canvas.ActualWidth < 100 || canvas.ActualHeight < 100) return;
        var walls = document.Walls.ToArray();
        var projectedPoints = mesh.Positions.Select(point => ProjectCoordinate(new(point.X, point.Y), orientation))
            .Concat(_session.Document.Grids.Where(axis => Math.Abs(ProjectCoordinate(axis.Start, orientation) -
                ProjectCoordinate(axis.End, orientation)) < 0.00001).Select(axis => ProjectCoordinate(axis.Start, orientation))).ToArray();
        var minimumCoordinate = projectedPoints.Length == 0 ? -5 : projectedPoints.Min();
        var maximumCoordinate = projectedPoints.Length == 0 ? 5 : projectedPoints.Max();
        var (lower, upper) = mesh.Bounds();
        var minimumElevation = Math.Min(lower.Z - 0.5, _session.Document.Levels.Min(level => level.Elevation) - 0.5);
        var maximumElevation = Math.Max(upper.Z + 0.3, _session.Document.Levels.Max(level => level.Elevation) + 0.5);
        var horizontalRange = Math.Max(1, maximumCoordinate - minimumCoordinate);
        var verticalRange = Math.Max(1, maximumElevation - minimumElevation);
        var scale = Math.Min(
            (canvas.ActualWidth - 140) / horizontalRange,
            (canvas.ActualHeight - 100) / verticalRange);
        var horizontalOffset = (canvas.ActualWidth - horizontalRange * scale) / 2;

        DrawProjectedAxes(canvas, orientation, minimumCoordinate, minimumElevation, maximumElevation, horizontalOffset, scale);
        DrawProjectedLevels(canvas, minimumCoordinate, maximumCoordinate, minimumElevation,
            horizontalOffset, scale);
        DrawProjectedFloors(canvas, document, orientation, minimumCoordinate, minimumElevation,
            horizontalOffset, scale);
        foreach (var wall in walls)
        {
            DrawProjectedWall(canvas, document, wall, orientation, minimumCoordinate, minimumElevation,
                horizontalOffset, scale, ReferenceEquals(canvas, SectionCanvas));
        }
        DrawProjectedElements(canvas, document, orientation, minimumCoordinate, minimumElevation, horizontalOffset, scale);
    }

    /// <summary>Draws persistent level datums across an orthographic view.</summary>
    private void DrawProjectedLevels(
        Canvas canvas,
        double minimumCoordinate,
        double maximumCoordinate,
        double minimumElevation,
        double horizontalOffset,
        double scale)
    {
        foreach (var level in _session.Document.Levels)
        {
            var elevationY = ProjectElevation(canvas, level.Elevation, minimumElevation, scale);
            var line = new Line
            {
                X1 = horizontalOffset - 28,
                X2 = horizontalOffset + (maximumCoordinate - minimumCoordinate) * scale + 28,
                Y1 = elevationY,
                Y2 = elevationY,
                Stroke = Brushes.SteelBlue,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 5, 4 }
            };
            canvas.Children.Add(line);
            var label = new TextBlock
            {
                Text = $"{level.Name}  {Units.FormatLength(level.Elevation)}",
                Foreground = Brushes.SteelBlue,
                FontSize = 11,
                Background = Brushes.White
            };
            Canvas.SetLeft(label, Math.Max(4, horizontalOffset - 105));
            Canvas.SetTop(label, elevationY - 18);
            canvas.Children.Add(label);
        }
    }

    /// <summary>Draws floor slabs as coordinated horizontal profiles.</summary>
    private void DrawProjectedFloors(
        Canvas canvas,
        BuildingDocument document,
        ViewOrientation orientation,
        double minimumCoordinate,
        double minimumElevation,
        double horizontalOffset,
        double scale)
    {
        foreach (var floor in document.Floors)
        {
            var level = _session.Document.Levels.Single(candidate => candidate.Id == floor.LevelId);
            if (floor.IsToposolid)
            {
                DrawToposolidProfile(canvas, floor, level.Elevation, orientation, minimumCoordinate, minimumElevation,
                    horizontalOffset, scale);
                continue;
            }
            if (floor.IsPitched)
            {
                DrawRoofProfile(canvas, floor, level.Elevation, orientation, minimumCoordinate, minimumElevation,
                    horizontalOffset, scale);
                continue;
            }
            var projected = floor.Boundary.Select(point => ProjectCoordinate(point, orientation)).ToArray();
            var left = horizontalOffset + (projected.Min() - minimumCoordinate) * scale;
            var top = ProjectElevation(canvas, level.Elevation, minimumElevation, scale);
            var slab = new Rectangle
            {
                Width = Math.Max(3, (projected.Max() - projected.Min()) * scale),
                Height = Math.Max(3, floor.Thickness * scale),
                Fill = new SolidColorBrush(Color.FromRgb(127, 143, 134)),
                Stroke = Brushes.DarkSlateGray,
                StrokeThickness = 1
            };
            Canvas.SetLeft(slab, left);
            Canvas.SetTop(slab, top);
            canvas.Children.Add(slab);
        }
    }

    /// <summary>Draws one wall profile and its hosted voids in an orthographic view.</summary>
    private void DrawProjectedWall(
        Canvas canvas,
        BuildingDocument document,
        Wall wall,
        ViewOrientation orientation,
        double minimumCoordinate,
        double minimumElevation,
        double horizontalOffset,
        double scale,
        bool useCutAppearance)
    {
        var level = _session.Document.Levels.Single(candidate => candidate.Id == wall.LevelId);
        var start = ProjectCoordinate(wall.Start, orientation);
        var end = ProjectCoordinate(wall.End, orientation);
        var projectedLength = Math.Abs(end - start);
        var left = horizontalOffset + (Math.Min(start, end) - minimumCoordinate) * scale;
        var top = ProjectElevation(canvas, level.Elevation + wall.Height, minimumElevation, scale);
        var wallWidth = Math.Max(4, projectedLength * scale);
        var wallType = _session.Document.WallTypes.Single(type => type.Id == wall.WallTypeId);
        Shape wallShape;
        if (wall.TopProfile is { Count: > 1 } && projectedLength * scale >= 4)
        {
            // A wall with a profile is drawn as the polygon under it, in the same coordinates as the rectangle it replaces.
            wallShape = ProfiledWallShape(wall,
                along => horizontalOffset + (start + (end - start) * along / wall.Length - minimumCoordinate) * scale,
                height => ProjectElevation(canvas, level.Elevation + height, minimumElevation, scale));
            Canvas.SetLeft(wallShape, 0);
            Canvas.SetTop(wallShape, 0);
        }
        else
        {
            wallShape = new Rectangle { Width = wallWidth, Height = wall.NetHeight * scale };
            Canvas.SetLeft(wallShape, left);
            Canvas.SetTop(wallShape, top);
        }
        wallShape.Fill = useCutAppearance
            ? WallAppearanceRendering.CreateBrush(wallType.SectionFillColor, wallType.SectionFillPattern)
            : WallAppearanceRendering.CreateBrush(wallType.PlanFillColor, WallFillPattern.Solid, 0.58);
        wallShape.Stroke = Brushes.DarkSlateGray;
        wallShape.StrokeThickness = 1;
        canvas.Children.Add(wallShape);

        if (projectedLength < 0.01) return;
        foreach (var opening in document.Openings.Where(candidate => candidate.WallId == wall.Id))
        {
            var direction = (end - start) / wall.Length;
            var openingCenter = start + direction * opening.Offset;
            var openingWidth = Math.Max(2, Math.Abs(direction) * opening.Width * scale);
            var openingLeft = horizontalOffset + (openingCenter - minimumCoordinate) * scale - openingWidth / 2;
            var openingTop = ProjectElevation(
                canvas, level.Elevation + opening.Sill + opening.Height, minimumElevation, scale);
            var openingShape = new Rectangle
            {
                Width = openingWidth,
                Height = opening.Height * scale,
                Fill = Brushes.White,
                Stroke = opening.Kind == OpeningKind.Window ? Brushes.SteelBlue : Brushes.DarkSlateGray,
                StrokeThickness = 1
            };
            Canvas.SetLeft(openingShape, openingLeft);
            Canvas.SetTop(openingShape, openingTop);
            canvas.Children.Add(openingShape);
        }
    }

    /// <summary>Projects a plan coordinate onto the horizontal axis of a generated view.</summary>
    private static double ProjectCoordinate(Point2 point, ViewOrientation orientation) => orientation switch
    {
        // Seen from the opposite side the left-to-right axis runs backwards, so south and west mirror north and east.
        ViewOrientation.East => point.Y,
        ViewOrientation.South => -point.X,
        ViewOrientation.West => -point.Y,
        _ => point.X
    };

    /// <summary>Converts a model elevation into a canvas Y coordinate.</summary>
    private static double ProjectElevation(
        Canvas canvas, double elevation, double minimumElevation, double scale) =>
        canvas.ActualHeight - 45 - (elevation - minimumElevation) * scale;
}
