using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Elevation symbols in the plan, as in Revit: a circle with an arrow at each side of the building, one per elevation
/// view. Click one to select it, double-click to open the view, and rename it from its menu, the project browser or F2.
/// The Elevation tab shows whichever elevation was chosen last.
/// </summary>
public partial class MainWindow
{
    private Guid _elevationViewId = BuiltInProjectViews.NorthElevationId;

    private static readonly Brush ElevationSymbolBrush = Frozen(Color.FromRgb(70, 70, 76));
    private static readonly Brush ElevationSelectedBrush = Frozen(Color.FromRgb(240, 130, 20));
    private Point? _elevationPressCanvasPoint;
    private Guid? _pressedElevationViewId;
    private Point2? _elevationDragPreview;
    private bool _draggingElevation;

    /// <summary>The elevation views, with the plan position and look direction of their symbols.</summary>
    private IEnumerable<(ProjectView View, Point2 Position, Point2 Direction, ElevationMarker Marker)> ElevationSymbols()
    {
        var document = _session.Document;
        foreach (var view in document.Views.Where(view => view.Kind == ProjectViewKind.Elevation))
        {
            var marker = ElevationViewGeometry.Resolve(document, view);
            if (_pressedElevationViewId == view.Id && _elevationDragPreview is Point2 preview)
                marker = marker with { Position = preview };
            yield return (view, marker.Position, ElevationViewGeometry.Direction(view.Orientation), marker);
        }
    }

    /// <summary>Draws every elevation symbol on the plan, the selected one in orange.</summary>
    private void RenderElevationSymbols()
    {
        foreach (var (view, position, direction, marker) in ElevationSymbols())
        {
            var selected = _selectedViewId == view.Id;
            var brush = selected ? ElevationSelectedBrush : ElevationSymbolBrush;
            if (selected) DrawElevationCoverage(view, marker);
            var center = WorldToCanvas(position);
            const double radius = 14;
            var along = new Vector(direction.X, -direction.Y);
            var menu = CreateViewMenu(view.Id);
            var code = GetText("Elevation." + view.Orientation);
            DrawCircularViewMarker(center, along, view.Id, view.Name, code[..1].ToUpperInvariant(), brush, menu, hitTest: true);

            var label = new TextBlock
            {
                Text = view.Name, FontSize = 11, Foreground = brush, Background = Brushes.White, Padding = new Thickness(3, 0, 3, 0),
                Tag = view.Id, ContextMenu = menu, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            // The label goes on the side away from the arrow, so it never covers the building.
            var halfExtent = Math.Abs(along.X) * label.DesiredSize.Width / 2 + Math.Abs(along.Y) * label.DesiredSize.Height / 2;
            var anchor = center - along * (radius + 4 + halfExtent);
            Canvas.SetLeft(label, anchor.X - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, anchor.Y - label.DesiredSize.Height / 2);
            PlanCanvas.Children.Add(label);
        }
    }

    /// <summary>Draws the selected elevation's transverse limit and translucent forward field.</summary>
    private void DrawElevationCoverage(ProjectView view, ElevationMarker marker)
    {
        var corners = ElevationViewGeometry.Coverage(marker, view.Orientation).Select(WorldToCanvas).ToArray();
        PlanCanvas.Children.Add(new Polygon
        {
            Points = new PointCollection(corners),
            Fill = new SolidColorBrush(Color.FromArgb(34, 240, 130, 20)),
            Stroke = new SolidColorBrush(Color.FromArgb(150, 240, 130, 20)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 7, 4 },
            IsHitTestVisible = false
        });
        PlanCanvas.Children.Add(new Line
        {
            X1 = corners[0].X, Y1 = corners[0].Y, X2 = corners[1].X, Y2 = corners[1].Y,
            Stroke = ElevationSelectedBrush, StrokeThickness = 2, IsHitTestVisible = false
        });
    }

    private ContextMenu CreateViewMenu(Guid viewId)
    {
        var open = new MenuItem { Header = GetText("Elevation.Open") };
        open.Click += (_, _) => SelectProjectView(viewId);
        var rename = new MenuItem { Header = GetText("Elevation.Rename"), InputGestureText = "F2" };
        rename.Click += (_, _) => RenameView(viewId);
        var remove = new MenuItem { Header = GetText("Elevation.Delete") };
        remove.Click += (_, _) => RemoveElevationView(viewId);
        var apply = new MenuItem { Header = GetText("ViewTemplate.Apply") };
        AddTemplateItems(apply, [viewId]);
        var menu = new ContextMenu { Items = { open, rename, apply } };
        if (_session.Document.Views.Any(view => view.Id == viewId && view.Kind == ProjectViewKind.Elevation))
            menu.Items.Add(remove);
        if (_session.Document.Views.FirstOrDefault(view => view.Id == viewId) is { Kind: ProjectViewKind.Section } section)
        {
            var structural = new MenuItem
            {
                Header = GetText("Section.StructuralMode"), IsCheckable = true,
                IsChecked = section.SectionMode == SectionDisplayMode.Structural
            };
            structural.Click += (_, _) => SetStructuralSectionMode(viewId, structural.IsChecked);
            menu.Items.Add(structural);
        }
        return menu;
    }

    /// <summary>Switches one section between coordinated and structural content from its marker or browser menu.</summary>
    private void SetStructuralSectionMode(Guid viewId, bool structural)
    {
        try
        {
            _session.SetSectionDisplayMode(viewId,
                structural ? SectionDisplayMode.Structural : SectionDisplayMode.Coordinated);
            StatusText.Text = GetText(structural ? "Status.StructuralSection" : "Status.CoordinatedSection");
            RefreshAll();
            RenderOrthographicViews();
        }
        catch (InvalidDataException exception)
        {
            ShowViewError(exception, GetText("View.Section"));
        }
    }

    /// <summary>Selects an elevation symbol clicked in the plan; a double-click opens its view.</summary>
    private bool TrySelectElevationSymbol(MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.Tag is not Guid id ||
            _session.Document.Views.FirstOrDefault(view => view.Id == id) is not { Kind: ProjectViewKind.Elevation }) return false;
        if (e.ClickCount >= 2)
        {
            SelectProjectView(id);
            return true;
        }
        _elevationViewId = id;
        _pressedElevationViewId = id;
        _elevationPressCanvasPoint = e.GetPosition(PlanCanvas);
        _selectedViewId = id;
        _selectedWallId = null;
        _selectedOpeningId = null;
        _selectedFloorId = _selectedRoomId = _selectedColumnId = _selectedStructuralId = null;
        RefreshProperties();
        RenderPlan();
        return true;
    }

    /// <summary>Starts and previews direct marker movement after the pointer crosses the drag threshold.</summary>
    private bool TryUpdateElevationDrag(MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pressedElevationViewId is null ||
            _elevationPressCanvasPoint is not Point press) return false;
        var current = e.GetPosition(PlanCanvas);
        if (!_draggingElevation && (current - press).Length < DragThreshold) return false;
        if (!_draggingElevation)
        {
            _draggingElevation = true;
            PlanCanvas.CaptureMouse();
        }
        var preview = CanvasToWorld(current);
        if (_elevationDragPreview == preview) return true;
        _elevationDragPreview = preview;
        RenderPlan();
        return true;
    }

    /// <summary>Commits a dragged marker once, or clears press state when the gesture was only a click.</summary>
    private bool FinishElevationDrag()
    {
        var wasDragging = _draggingElevation;
        var id = _pressedElevationViewId;
        var preview = _elevationDragPreview;
        _draggingElevation = false;
        _pressedElevationViewId = null;
        _elevationPressCanvasPoint = null;
        _elevationDragPreview = null;
        if (!wasDragging || id is not Guid viewId || preview is not Point2 position) return false;
        PlanCanvas.ReleaseMouseCapture();
        var view = _session.Document.Views.First(candidate => candidate.Id == viewId);
        var marker = ElevationViewGeometry.AtPosition(_session.Document, view.Orientation, position);
        _session.UpdateElevationMarker(viewId, marker);
        StatusText.Text = GetText("Status.ElevationMoved");
        RefreshAll();
        return true;
    }

    /// <summary>Asks for a new name for a view and applies it as one undoable step.</summary>
    private void RenameView(Guid id)
    {
        if (_session.Document.Views.FirstOrDefault(view => view.Id == id) is not { } current) return;
        var name = PromptWindow.Ask(this, GetText("Elevation.RenameTitle"), current.Name);
        if (name is null) return;
        try
        {
            _session.RenameView(id, name);
            StatusText.Text = GetText("Status.ViewRenamed");
            RefreshAll();
            RenderOrthographicViews();
        }
        catch (InvalidDataException exception)
        {
            MessageBox.Show(this, LocalizedErrors.Translate(exception.Message), GetText("Elevation.RenameTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>The orientation and name of the elevation the Elevation tab is showing.</summary>
    private (ViewOrientation Orientation, string Name) CurrentElevation()
    {
        var view = _session.Document.Views.FirstOrDefault(item => item.Id == _elevationViewId && item.Kind == ProjectViewKind.Elevation)
            ?? _session.Document.Views.FirstOrDefault(item => item.Kind == ProjectViewKind.Elevation);
        return view is null ? (ViewOrientation.North, GetText("View.Elevation")) : (view.Orientation, view.Name);
    }
}
