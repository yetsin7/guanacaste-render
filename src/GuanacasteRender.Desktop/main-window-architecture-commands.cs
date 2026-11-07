using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core;
using Guanacaste3D.Desktop.Controls;

namespace Guanacaste3D.Desktop;

/// <summary>The Architecture tab commands that build elements from sketches, hosts, faces and picks.</summary>
public partial class MainWindow
{
    /// <summary>
    /// A slab sketch in progress: its points so far, the recipe, and whether two corners complete it. Other tools reuse
    /// it with <see cref="Complete"/> to receive the points instead of a slab, and <see cref="Open"/> for a polyline.
    /// </summary>
    private sealed record SlabSketch(SlabPreset Preset, bool Rectangle, List<Point2> Points)
    {
        public Action<IReadOnlyList<Point2>>? Complete { get; init; }
        public bool Open { get; init; }
        public int MinimumPoints => Open ? 2 : 3;
    }

    private SlabSketch? _slabSketch;
    private Point2? _sketchPointer;
    private Func<Guid, bool>? _pickWallAction;
    private Guid? _groupToPlace;
    private Point2? _groupAnchor;
    private bool _awaitingGroupPick;
    private bool _pickingWorkPlane;
    private Guid? _activeWorkPlaneId;

    /// <summary>Runs an Architecture tab command that needs more than arming a placement tool; false when it is not one.</summary>
    private bool ExecuteArchitectureCommand(ArchitectureCommand command)
    {
        switch (command)
        {
            case ArchitectureCommand.Deselect:
                SelectExtendedElement();
                _selectedWallId = null;
                _selectedOpeningId = null;
                RenderPlan();
                StatusText.Text = GetText("Status.SelectionCleared");
                return true;
            case ArchitectureCommand.WallArchitectural: Wall_Click(this, new RoutedEventArgs()); return true;
            case ArchitectureCommand.WallStructural: StartWallOfType("Reinforced Concrete 200 mm"); return true;
            case ArchitectureCommand.WallByFace: WallsByFace(); return true;
            case ArchitectureCommand.WallSweep: BeginWallPick(wall => _session.AddWallSweep(wall, false) != Guid.Empty, "Status.PickWallForSweep"); return true;
            case ArchitectureCommand.WallReveal: BeginWallPick(wall => _session.AddWallSweep(wall, true) != Guid.Empty, "Status.PickWallForReveal"); return true;
            case ArchitectureCommand.ComponentPlace: OpenArchitecturalLibrary(); return true;
            case ArchitectureCommand.ComponentInPlace: BeginKindTool(StructuralElementKind.Component); return true;
            case ArchitectureCommand.ColumnArchitectural:
                PlaceLibraryColumn(ArchitecturalLibrary.ColumnPresets.Single(preset => preset.Name == "Round Column Ø300 mm")); return true;
            case ArchitectureCommand.ColumnStructural:
                PlaceLibraryColumn(ArchitecturalLibrary.ColumnPresets.Single(preset => preset.Name == "Column 400 × 400 mm")); return true;
            case ArchitectureCommand.RoofByFootprint: ShowSlabPresetMenu(FloorKind.Roof, preset => BeginSlabSketch(preset, false)); return true;
            case ArchitectureCommand.RoofByExtrusion: ShowSlabPresetMenu(FloorKind.Roof, preset => BeginSlabSketch(preset, true)); return true;
            case ArchitectureCommand.RoofByFace: ShowSlabPresetMenu(FloorKind.Roof, preset => SlabByFace(preset, 0.6)); return true;
            case ArchitectureCommand.RoofSoffit: BeginKindTool(StructuralElementKind.Soffit); return true;
            case ArchitectureCommand.RoofFascia: BeginKindTool(StructuralElementKind.Fascia); return true;
            case ArchitectureCommand.RoofGutter: BeginKindTool(StructuralElementKind.Gutter); return true;
            case ArchitectureCommand.CeilingAutomatic: ShowSlabPresetMenu(FloorKind.Ceiling); return true;
            case ArchitectureCommand.CeilingSketch: ShowSlabPresetMenu(FloorKind.Ceiling, preset => BeginSlabSketch(preset, false)); return true;
            case ArchitectureCommand.FloorArchitectural: ShowSlabPresetMenu(FloorKind.Floor); return true;
            case ArchitectureCommand.FloorStructural:
                CreateEnclosureSlab(FloorKind.Floor, ArchitecturalLibrary.SlabPresets.Single(preset => preset.Name == "Concrete Slab 200 mm")); return true;
            case ArchitectureCommand.FloorByFace: ShowSlabPresetMenu(FloorKind.Floor, preset => SlabByFace(preset, 0)); return true;
            case ArchitectureCommand.SlabEdge: SlabEdgesOfSelection(); return true;
            case ArchitectureCommand.RailingSketch: BeginKindTool(StructuralElementKind.Railing); return true;
            case ArchitectureCommand.RailingOnHost: RailingsOnSelectedHost(); return true;
            case ArchitectureCommand.GroupCreate:
                BeginStructuralTool(StructuralElementKind.ModelLine, StructuralPlacementMode.Group); return true;
            case ArchitectureCommand.GroupPlace: BeginGroupPlace(); return true;
            case ArchitectureCommand.RoomAutomatic: AutomaticRooms(); return true;
            case ArchitectureCommand.TagAllRooms: ShowPlanCategoryTags(TagCategory.Room); return true;
            case ArchitectureCommand.TagAllAreas: ShowPlanCategoryTags(TagCategory.Area); return true;
            case ArchitectureCommand.AreaByBoundary: AreaFromBoundaries(); return true;
            case ArchitectureCommand.WallOpening: PlaceWallOpening(); return true;
            case ArchitectureCommand.WorkPlaneByLevel: Levels_Click(this, new RoutedEventArgs()); return true;
            case ArchitectureCommand.WorkPlanePickReference: BeginWorkPlanePick(); return true;
            default: return false;
        }
    }

    private void StartWallOfType(string typeName)
    {
        _session.LoadArchitecturalLibrary();
        var type = _session.Document.WallTypes.FirstOrDefault(candidate => candidate.Name == typeName);
        if (type is not null) PlaceLibraryWall(type);
        else Wall_Click(this, new RoutedEventArgs());
    }

    private void PlaceWallOpening()
    {
        _session.LoadArchitecturalLibrary();
        var type = _session.Document.OpeningTypes.FirstOrDefault(candidate => candidate.Operation == OpeningOperation.Void);
        if (type is null) { Window_Click(this, new RoutedEventArgs()); return; }
        PlaceLibraryOpening(type);
    }

    /// <summary>The boundary of the selected room or slab, with the level it stands on; null when neither is selected.</summary>
    private (Guid LevelId, IReadOnlyList<Point2> Boundary)? SelectedFace()
    {
        var room = _session.Document.Rooms.FirstOrDefault(candidate => candidate.Id == _selectedRoomId);
        if (room is not null)
            return (room.LevelId, RoomRegionGeometry.Outline(room));
        var floor = _session.Document.Floors.FirstOrDefault(candidate => candidate.Id == _selectedFloorId);
        return floor is null ? null : (floor.LevelId, floor.Boundary);
    }

    private void WallsByFace()
    {
        if (SelectedFace() is not { } face) { StatusText.Text = GetText("Status.SelectFaceFirst"); return; }
        var typeId = (WallTypePicker.SelectedItem as ComboBoxItem)?.Tag as Guid? ?? BuiltInWallTypes.Basic200Id;
        try
        {
            var count = _session.AddWallsAroundBoundary(face.LevelId, face.Boundary, typeId);
            StatusText.Text = FormatText("Status.WallsByFace", count);
        }
        catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
        RefreshAll();
    }

    private void SlabByFace(SlabPreset preset, double margin)
    {
        try
        {
            if (_session.Document.Rooms.Any(room => room.Id == _selectedRoomId))
                _selectedFloorId = _session.AddSlabOverRoom(_selectedRoomId!.Value, preset, margin);
            else if (_session.Document.Floors.Any(floor => floor.Id == _selectedFloorId))
                _selectedFloorId = _session.AddSlabOverSlab(_selectedFloorId!.Value, preset, margin);
            else { StatusText.Text = GetText("Status.SelectFaceFirst"); return; }
            StatusText.Text = GetText(preset.Kind == FloorKind.Roof ? "Status.RoofCreated" : "Status.FloorCreated");
        }
        catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
        RefreshAll();
    }

    private void SlabEdgesOfSelection()
    {
        if (_selectedFloorId is not Guid floor || _session.Document.Floors.All(candidate => candidate.Id != floor))
        {
            StatusText.Text = GetText("Status.SelectSlabFirst");
            return;
        }
        StatusText.Text = FormatText("Status.SlabEdgesCreated", _session.AddSlabEdges(floor));
        RefreshAll();
    }

    private void RailingsOnSelectedHost()
    {
        var host = _session.Document.StructuralElements.FirstOrDefault(element => element.Id == _selectedStructuralId &&
            element.Kind is StructuralElementKind.Stair or StructuralElementKind.Ramp);
        if (host is null) { StatusText.Text = GetText("Status.SelectStairFirst"); return; }
        StatusText.Text = FormatText("Status.RailingsCreated", _session.AddRailingsOnHost(host.Id));
        RefreshAll();
    }

    private void AutomaticRooms()
    {
        var count = _session.AddAutomaticRooms(_activeLevelId);
        StatusText.Text = count > 0
            ? FormatText("Status.RoomsCreated", count)
            : _session.Document.Rooms.Any(room => room.LevelId == _activeLevelId && room.IsAutomatic)
                ? GetText("Status.RoomsSynchronized")
                : GetText("Status.NoRoomsFound");
        RefreshAll();
    }

    private void AreaFromBoundaries()
    {
        try
        {
            var id = _session.AddAreaFromBoundaries(_activeLevelId, GetStructuralName(StructuralElementKind.Area));
            if (id is null) { StatusText.Text = GetText("Status.NoAreaBoundaries"); return; }
            _selectedStructuralId = id;
            StatusText.Text = FormatText("Status.StructuralCreated", GetStructuralName(StructuralElementKind.Area));
        }
        catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
        RefreshAll();
    }

    // ------------------------------------------------------------------ picking a wall (sweep, reveal)

    private void BeginWallPick(Func<Guid, bool> action, string statusKey)
    {
        BeginStructuralTool(StructuralElementKind.ModelLine, StructuralPlacementMode.Line);
        CancelStructuralTool();
        _pickWallAction = action;
        StatusText.Text = GetText(statusKey);
    }

    private bool TryHandleWallPick(MouseButtonEventArgs e)
    {
        if (_pickWallAction is not { } action) return false;
        if ((e.OriginalSource as FrameworkElement)?.Tag is not Guid id || _session.Document.Walls.All(wall => wall.Id != id))
        {
            StatusText.Text = GetText("Status.PickWallMissed");
            return true;
        }
        // An action that returns false is part-way through a multi-click command and has set its own prompt.
        try { if (action(id)) StatusText.Text = GetText("Status.ElementUpdated"); }
        catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
        RefreshAll();
        return true;
    }

    // ------------------------------------------------------------------ model groups

    private void BeginGroupPlace()
    {
        BeginStructuralTool(StructuralElementKind.ModelLine, StructuralPlacementMode.Line);
        CancelStructuralTool();
        _awaitingGroupPick = true;
        StatusText.Text = GetText("Status.GroupPickSource");
    }

    private bool TryHandleGroupPlace(MouseButtonEventArgs e)
    {
        if (!_awaitingGroupPick && _groupToPlace is null) return false;
        var point = SnapWallPoint(CanvasToWorld(PlanPosition(e)));
        if (_groupToPlace is null)
        {
            var source = (e.OriginalSource as FrameworkElement)?.Tag is Guid id
                ? _session.Document.StructuralElements.FirstOrDefault(element => element.Id == id) : null;
            if (source?.GroupId is not Guid group) { StatusText.Text = GetText("Status.GroupPickMissed"); return true; }
            _groupToPlace = group;
            _awaitingGroupPick = false;
            _groupAnchor = point;
            StatusText.Text = GetText("Status.GroupPickTarget");
            return true;
        }
        try
        {
            var anchor = _groupAnchor ?? point;
            var copied = _session.DuplicateStructuralGroup(_groupToPlace.Value, point.X - anchor.X, point.Y - anchor.Y);
            StatusText.Text = FormatText("Status.GroupPlaced", copied);
        }
        catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
        _groupToPlace = null;
        _groupAnchor = null;
        RefreshAll();
        return true;
    }

    // ------------------------------------------------------------------ work plane

    private void BeginWorkPlanePick()
    {
        BeginStructuralTool(StructuralElementKind.ModelLine, StructuralPlacementMode.Line);
        CancelStructuralTool();
        _pickingWorkPlane = true;
        StatusText.Text = GetText("Status.WorkPlanePick");
    }

    private bool TryHandleWorkPlanePick(MouseButtonEventArgs e)
    {
        if (!_pickingWorkPlane) return false;
        var plane = (e.OriginalSource as FrameworkElement)?.Tag is Guid id
            ? _session.Document.StructuralElements.FirstOrDefault(element => element.Id == id &&
                element.Kind == StructuralElementKind.ReferencePlane) : null;
        if (plane is null) { StatusText.Text = GetText("Status.WorkPlaneMissed"); return true; }
        _activeWorkPlaneId = plane.Id;
        _pickingWorkPlane = false;
        StatusText.Text = FormatText("Status.WorkPlaneSet", plane.Name);
        RenderPlan();
        return true;
    }

    /// <summary>Projects a point onto the active work plane when it is within snapping distance of it.</summary>
    private Point2? SnapToWorkPlane(Point2 point)
    {
        var plane = _session.Document.StructuralElements.FirstOrDefault(element => element.Id == _activeWorkPlaneId);
        if (plane is null || plane.Length < 1e-6) return null;
        var ux = (plane.End.X - plane.Start.X) / plane.Length;
        var uy = (plane.End.Y - plane.Start.Y) / plane.Length;
        var along = (point.X - plane.Start.X) * ux + (point.Y - plane.Start.Y) * uy;
        var projected = new Point2(plane.Start.X + ux * along, plane.Start.Y + uy * along);
        return projected.DistanceTo(point) <= Math.Max(0.05, 12 / _scale) ? projected : null;
    }

    // ------------------------------------------------------------------ slab sketch (perimeter and extrusion roofs, ceilings)

    private void BeginSlabSketch(SlabPreset preset, bool rectangle)
    {
        BeginStructuralTool(StructuralElementKind.ModelLine, StructuralPlacementMode.Line);
        CancelStructuralTool();
        _slabSketch = new SlabSketch(preset, rectangle, []);
        StatusText.Text = GetText(rectangle ? "Status.SketchRectangle" : "Status.SketchPolygon");
        RenderPlan();
    }

    private bool TryHandleSlabSketchClick(MouseButtonEventArgs e)
    {
        if (_slabSketch is not { } sketch) return false;
        var point = SnapWallPoint(CanvasToWorld(PlanPosition(e)));
        if (e.ClickCount >= 2 && sketch.Points.Count >= sketch.MinimumPoints && !sketch.Rectangle)
        {
            FinishSlabSketch();
            return true;
        }
        if (sketch.Points.Count == 0 || point.DistanceTo(sketch.Points[^1]) > 1e-6) sketch.Points.Add(point);
        if (sketch.Rectangle && sketch.Points.Count == 2) { FinishSlabSketch(); return true; }
        StatusText.Text = GetText("Status.SketchNext");
        RenderPlan();
        return true;
    }

    /// <summary>Completes the sketch with right click or double click; fewer than three points cancels it.</summary>
    private bool TryFinishSlabSketch()
    {
        if (_slabSketch is null) return false;
        FinishSlabSketch();
        return true;
    }

    private void FinishSlabSketch()
    {
        var sketch = _slabSketch;
        _slabSketch = null;
        _sketchPointer = null;
        if (sketch is null) return;
        IReadOnlyList<Point2> boundary = sketch.Points;
        if (sketch.Rectangle && sketch.Points.Count == 2)
        {
            var (a, b) = (sketch.Points[0], sketch.Points[1]);
            boundary = [a, new(b.X, a.Y), b, new(a.X, b.Y)];
        }
        if (boundary.Count < sketch.MinimumPoints) { StatusText.Text = GetText("Status.SketchCancelled"); RenderPlan(); return; }
        if (sketch.Complete is { } complete)
        {
            complete(boundary);
            return;
        }
        try
        {
            _selectedFloorId = _session.AddSlabFromPreset(_activeLevelId, boundary.ToArray(), sketch.Preset,
                _session.SlabElevation(_activeLevelId, sketch.Preset));
            StatusText.Text = GetText(sketch.Preset.Kind switch
            {
                FloorKind.Roof => "Status.RoofCreated", FloorKind.Ceiling => "Status.CeilingCreated",
                FloorKind.Toposolid => "Status.ToposolidCreated", _ => "Status.FloorCreated"
            });
        }
        catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
        RefreshAll();
    }

    /// <summary>Tracks the pointer for the sketch preview; true while a sketch is active.</summary>
    private bool TryUpdateSketchPreview(MouseEventArgs e)
    {
        if (_slabSketch is null) return false;
        _sketchPointer = SnapWallPoint(CanvasToWorld(PlanPosition(e)));
        RenderPlan();
        return true;
    }

    /// <summary>Draws the sketch so far as a dashed outline closing back to the first point.</summary>
    private void RenderSlabSketchPreview()
    {
        if (_slabSketch is not { } sketch) return;
        var points = sketch.Points.ToList();
        if (_sketchPointer is Point2 pointer)
        {
            if (sketch.Rectangle && points.Count == 1)
                points = [points[0], new(pointer.X, points[0].Y), pointer, new(points[0].X, pointer.Y)];
            else points.Add(pointer);
        }
        if (points.Count < 2) return;
        if (sketch.Open)
        {
            PlanCanvas.Children.Add(new Polyline
            {
                Points = new PointCollection(points.Select(WorldToCanvas)),
                Stroke = Brushes.DarkOrange, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false
            });
            return;
        }
        PlanCanvas.Children.Add(new Polygon
        {
            Points = new PointCollection(points.Select(WorldToCanvas)),
            Stroke = Brushes.DarkOrange, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = new SolidColorBrush(Color.FromArgb(40, 255, 140, 0)), IsHitTestVisible = false
        });
    }

    /// <summary>Clears every pick or sketch state of the Architecture tab commands.</summary>
    private void CancelArchitectureTools()
    {
        _slabSketch = null;
        _sketchPointer = null;
        _pickWallAction = null;
        _awaitingGroupPick = false;
        _groupToPlace = null;
        _groupAnchor = null;
        _pickingWorkPlane = false;
    }
}
