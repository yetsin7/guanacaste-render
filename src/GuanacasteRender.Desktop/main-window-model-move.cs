using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Navigation;

namespace Guanacaste3D.Desktop;

/// <summary>Previews direct drags and two-click moves on a stable construction plane without editing document history.</summary>
public partial class MainWindow
{
    private ModelDragPlane? _modelMovePlane;
    private Vector3 _modelMoveAnchor;
    private Vector3 _modelMoveDelta;
    private bool _modelMoveValid;
    private WallOpening? _modelOpeningCandidate;
    private readonly List<ModelVisual3D> _modelMoveVisuals = [];
    private Border? _modelMoveChip;
    private readonly List<UIElement> _modelMoveOverlayShapes = [];

    /// <summary>Lets the navigator reserve selected-element drags while retaining normal camera gestures.</summary>
    private void InitializeModelMovement()
    {
        if (_modelNavigator is null) return;
        _modelNavigator.BeginElementDrag = BeginModelDrag;
        _modelNavigator.ElementDragMoved += UpdateModelMove;
        _modelNavigator.ElementDragCompleted += CompleteModelMove;
        _modelNavigator.ElementDragCancelled += CancelMoveTool;
        _modelNavigator.PointerMoved += UpdateModelMove;
        _modelNavigator.EditingActive = () => _movingElement;
        Deactivated += (_, _) => { if (_modelMovePlane is not null) CancelMoveTool(); };
        ViewTabs.SelectionChanged += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, ViewTabs) && _modelMovePlane is not null) CancelMoveTool();
        };
    }

    /// <summary>Claims a drag only when its press hit the currently selected movable element.</summary>
    private bool BeginModelDrag(Point position)
    {
        if (PlanToolArmed() || CurrentSelection() is not { } selection || SelectedMovableId() is null) return false;
        var ray = ModelPointerRay(position);
        if (Picker.Pick(_navigation.Position, ray) is not { } hit || hit.Id != selection.Id || hit.Kind != selection.Kind) return false;
        BeginMoveTool();
        if (BeginModelMove(position)) return true;
        CancelMoveTool();
        return false;
    }

    /// <summary>Consumes the move tool's base and destination clicks before general 3D placement can flatten them.</summary>
    private bool HandleModelMoveClick(Point position)
    {
        if (!_movingElement) return false;
        if (_modelMovePlane is null) BeginModelMove(position);
        else CompleteModelMove(position);
        return true;
    }

    /// <summary>Locks openings to their wall plane and other elements to a horizontal plane through the picked point.</summary>
    private bool BeginModelMove(Point position)
    {
        if (CurrentSelection() is not { } selection) return false;
        var outline = Picker.Outline(selection.Kind, selection.Id);
        if (outline.Count == 0) return false;
        var ray = ModelPointerRay(position);
        var hit = Picker.Pick(_navigation.Position, ray);
        var origin = hit is { } picked && picked.Id == selection.Id
            ? _navigation.Position + ray * picked.Distance : outline[0].A;
        var normal = Vector3.UnitZ;
        if (SelectedOpening() is { } opening)
        {
            var wall = _session.Document.Walls.Single(item => item.Id == opening.WallId);
            normal = Vector3.Normalize(new Vector3((float)(wall.Start.Y - wall.End.Y), (float)(wall.End.X - wall.Start.X), 0));
        }
        else if (Math.Abs(ray.Z) < 0.08f)
        {
            // A horizontal ray cannot meet a horizontal plane; elevations retain screen-horizontal movement.
            normal = Vector3.Normalize(new Vector3(_navigation.Forward.X, _navigation.Forward.Y, 0));
        }
        var plane = new ModelDragPlane(origin, normal);
        if (plane.Intersect(_navigation.Position, ray) is not Vector3 anchor)
        {
            StatusText.Text = GetText("Status.ModelMovePlane");
            return false;
        }
        _modelMovePlane = plane;
        _modelMoveAnchor = anchor;
        _moveBasePoint = new Point2(anchor.X, anchor.Y);
        StatusText.Text = GetText("Status.ModelMoveDestination");
        UpdateModelMove(position);
        return true;
    }

    /// <summary>Builds the pointer ray in the modeling surface's current layout, including narrow windows.</summary>
    private Vector3 ModelPointerRay(Point position) => _navigation.PixelRay((float)position.X, (float)position.Y,
        (float)ModelSurface.ActualWidth, (float)ModelSurface.ActualHeight);

    /// <summary>Repositions a translucent ghost and metric feedback; invalid placements remain visible in red.</summary>
    private void UpdateModelMove(Point position)
    {
        if (!_movingElement || _modelMovePlane is not ModelDragPlane plane) return;
        ClearModelMovePreview();
        _modelMoveValid = false;
        if (plane.Intersect(_navigation.Position, ModelPointerRay(position)) is not Vector3 point)
        {
            StatusText.Text = GetText("Status.ModelMovePlane");
            return;
        }
        _modelMoveDelta = point - _modelMoveAnchor;
        _modelOpeningCandidate = null;
        if (SelectedOpening() is { } opening)
        {
            var wall = _session.Document.Walls.Single(item => item.Id == opening.WallId);
            var axis = new Vector3((float)((wall.End.X - wall.Start.X) / wall.Length),
                (float)((wall.End.Y - wall.Start.Y) / wall.Length), 0);
            var along = Vector3.Dot(_modelMoveDelta, axis);
            var vertical = opening.Kind == OpeningKind.Door ? 0 : _modelMoveDelta.Z;
            _modelMoveDelta = axis * along + Vector3.UnitZ * vertical;
            var candidate = opening with { Offset = opening.Offset + along, Sill = opening.Sill + vertical };
            if (Picker.Pick(_navigation.Position, ModelPointerRay(position)) is { } hit &&
                _session.Document.Walls.FirstOrDefault(item => item.Id == (hit.Kind == PickedKind.Opening ? hit.HostId : hit.Id)) is { } targetWall &&
                targetWall.Id != wall.Id)
            {
                var targetAxis = new Vector3((float)((targetWall.End.X - targetWall.Start.X) / targetWall.Length),
                    (float)((targetWall.End.Y - targetWall.Start.Y) / targetWall.Length), 0);
                var hitPoint = _navigation.Position + ModelPointerRay(position) * hit.Distance;
                var originalCenter = OpeningHosting.Center(wall, opening);
                var grabAlong = Vector3.Dot(_modelMoveAnchor - new Vector3((float)originalCenter.X, (float)originalCenter.Y, 0), axis);
                var offset = Vector3.Dot(hitPoint - new Vector3((float)targetWall.Start.X, (float)targetWall.Start.Y, 0), targetAxis) - grabAlong;
                var sill = opening.Kind == OpeningKind.Door ? 0 : opening.Sill + hitPoint.Z - _modelMoveAnchor.Z;
                candidate = opening with { WallId = targetWall.Id, Offset = offset, Sill = sill };
                var center = OpeningHosting.Center(targetWall, candidate);
                _modelMoveDelta = new Vector3((float)(center.X - originalCenter.X), (float)(center.Y - originalCenter.Y), (float)(sill - opening.Sill));
                wall = targetWall;
            }
            _modelOpeningCandidate = candidate;
            var resolved = OpeningHosting.Resolve(candidate, OpeningHosting.Center(wall, candidate),
                [wall], _session.Document.Openings, 0);
            var sourceWall = _session.Document.Walls.Single(item => item.Id == opening.WallId);
            _modelMoveValid = wall.LevelId == sourceWall.LevelId && candidate.Sill >= 0 && resolved is { Fits: true } host &&
                Math.Abs(host.Offset - candidate.Offset) < 1e-5;
        }
        else
        {
            _modelMoveDelta = new Vector3(_modelMoveDelta.X, _modelMoveDelta.Y, 0);
            if (SnapGridCheck.IsChecked == true)
                _modelMoveDelta = new Vector3(MathF.Round(_modelMoveDelta.X * 10) / 10, MathF.Round(_modelMoveDelta.Y * 10) / 10, 0);
            _modelMoveValid = true;
            if (_selectedStructuralId is Guid id && _session.Document.StructuralElements.FirstOrDefault(item => item.Id == id) is { } structural &&
                structural.Kind is StructuralElementKind.Opening or StructuralElementKind.Shaft)
            {
                var translated = structural with
                {
                    Start = new Point2(structural.Start.X + _modelMoveDelta.X, structural.Start.Y + _modelMoveDelta.Y),
                    End = new Point2(structural.End.X + _modelMoveDelta.X, structural.End.Y + _modelMoveDelta.Y)
                };
                _modelMoveValid = SlabOpeningHosts.Resolve(_session.Document, translated).Count > 0;
            }
        }
        DrawModelMovePreview(position);
        StatusText.Text = GetText(_modelMoveValid ? "Status.ModelMoveDestination" : "Status.ModelMoveBlocked");
    }

    /// <summary>Commits exactly one validated operation on release or destination click; failed previews create no history.</summary>
    private void CompleteModelMove(Point position)
    {
        if (!_movingElement || _modelMovePlane is null) return;
        UpdateModelMove(position);
        var valid = _modelMoveValid;
        var delta = _modelMoveDelta;
        var opening = _modelOpeningCandidate;
        ClearMoveState();
        if (!valid)
        {
            StatusText.Text = GetText("Status.MoveRejected");
            RenderPlan();
            return;
        }
        try
        {
            if (delta.LengthSquared() > 1e-10f)
            {
                if (opening is not null)
                {
                    _session.MoveOpening(opening.Id, opening.WallId, opening.Offset, opening.Sill);
                    _selectedWallId = opening.WallId;
                    _selectedWallOffset = opening.Offset;
                }
                else ApplyMove(delta.X, delta.Y);
            }
            StatusText.Text = GetText("Status.MoveApplied");
        }
        catch (InvalidDataException)
        {
            StatusText.Text = GetText("Status.MoveRejected");
        }
        RefreshAll();
    }

    /// <summary>Draws depth-tested ghost faces, edges, and a cursor-adjacent distance label.</summary>
    private void DrawModelMovePreview(Point position)
    {
        if (CurrentSelection() is not { } selection) return;
        var outline = Picker.Outline(selection.Kind, selection.Id);
        var color = _modelMoveValid ? Color.FromRgb(20, 126, 217) : Color.FromRgb(210, 49, 56);
        var translated = _modelOpeningCandidate is { } candidate
            ? ElementPicker.OpeningOutline(_session.Document.Walls.Single(item => item.Id == candidate.WallId), candidate,
                (float)_session.Document.Levels.Single(item => item.Id == _session.Document.Walls.Single(wall => wall.Id == candidate.WallId).LevelId).Elevation).ToArray()
            : outline.Select(edge => new EdgeSegment(edge.A + _modelMoveDelta, edge.B + _modelMoveDelta)).ToArray();
        var edges = ScenePresentation.ToWpfEdges(translated, Matrix4x4.Identity, EdgeThickness() * 2, color);
        if (edges is not null) AddModelMoveVisual(edges);
        var mesh = new MeshGeometry3D();
        for (var index = 0; index + 2 < Math.Min(translated.Length, 9000); index += 3)
        {
            var bottom = translated[index];
            var top = translated[index + 1];
            var start = mesh.Positions.Count;
            foreach (var vertex in new[] { bottom.A, bottom.B, top.B, top.A }) mesh.Positions.Add(ScenePresentation.ToWpf(vertex));
            foreach (var corner in new[] { 0, 1, 2, 0, 2, 3 }) mesh.TriangleIndices.Add(start + corner);
            var projected = new[] { bottom.A, bottom.B, top.B, top.A }.Select(vertex => _navigation.Project(vertex,
                (float)ModelSurface.ActualWidth, (float)ModelSurface.ActualHeight)).ToArray();
            if (projected.All(vertex => vertex.HasValue))
            {
                // The candidate may lie inside the original wall; overlay faces keep its destination visible through that wall.
                var face = new System.Windows.Shapes.Polygon
                {
                    Points = new PointCollection(projected.Select(vertex => new Point(vertex!.Value.X, vertex.Value.Y))),
                    Fill = new SolidColorBrush(Color.FromArgb(28, color.R, color.G, color.B)),
                    Stroke = new SolidColorBrush(color), StrokeThickness = 1, IsHitTestVisible = false
                };
                _modelMoveOverlayShapes.Add(face);
                ModelOverlay.Children.Add(face);
            }
        }
        var material = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(65, color.R, color.G, color.B)));
        AddModelMoveVisual(new GeometryModel3D(mesh, material) { BackMaterial = material });
        _modelMoveChip = new Border
        {
            Background = Brushes.White, BorderBrush = new SolidColorBrush(color), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 5, 9, 5), IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = FormatText("ModelMove.Distance", _modelMoveDelta.Length().ToString("0.000", CultureInfo.CurrentCulture)),
                Foreground = new SolidColorBrush(color), FontWeight = FontWeights.SemiBold
            }
        };
        _modelMoveChip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_modelMoveChip, Math.Clamp(position.X + 18, 0, Math.Max(0, ModelSurface.ActualWidth - _modelMoveChip.DesiredSize.Width)));
        Canvas.SetTop(_modelMoveChip, Math.Clamp(position.Y + 18, 0, Math.Max(0, ModelSurface.ActualHeight - _modelMoveChip.DesiredSize.Height)));
        ModelOverlay.Children.Add(_modelMoveChip);
    }

    /// <summary>Tracks transient visuals separately from document geometry and selection measurements.</summary>
    private void AddModelMoveVisual(GeometryModel3D model)
    {
        var visual = new ModelVisual3D { Content = model };
        _modelMoveVisuals.Add(visual);
        ModelViewport.Children.Add(visual);
    }

    /// <summary>Removes the previous frame's ghost without touching model elements or undo state.</summary>
    private void ClearModelMovePreview()
    {
        foreach (var visual in _modelMoveVisuals) ModelViewport.Children.Remove(visual);
        _modelMoveVisuals.Clear();
        foreach (var shape in _modelMoveOverlayShapes) ModelOverlay.Children.Remove(shape);
        _modelMoveOverlayShapes.Clear();
        if (_modelMoveChip is not null) ModelOverlay.Children.Remove(_modelMoveChip);
        _modelMoveChip = null;
    }
}
