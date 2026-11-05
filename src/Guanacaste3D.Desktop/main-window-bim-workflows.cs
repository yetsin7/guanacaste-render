using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>Provides localized values for the coordinated schedule grid.</summary>
internal sealed record ScheduleDisplayRow(
    string Category,
    string Number,
    string Name,
    string Level,
    double Quantity,
    string Unit);

public partial class MainWindow
{
    private bool _drawingRoom;
    private Point2? _pendingRoomStart;
    private Guid? _selectedRoomId;
    private Guid? _selectedFloorId;
    private Guid? _selectedSheetId;
    private bool _drawingColumn;
    private Guid? _selectedColumnId;

    /// <summary>Resolves a user-facing string from the active WPF resource dictionary.</summary>
    private static string GetText(string key) =>
        Application.Current.TryFindResource(key) as string ?? key;

    /// <summary>Formats a localized string with culture-aware values.</summary>
    private static string FormatText(string key, params object[] values) =>
        string.Format(CultureInfo.CurrentCulture, GetText(key), values);

    /// <summary>Starts rectangular room placement in the active plan view.</summary>
    private void Room_Click(object sender, RoutedEventArgs e)
    {
        SetWallTool(false);
        _drawingRoom = true;
        _selectedViewId = null;
        _pendingWallStart = null;
        _pendingRoomStart = null;
        ViewTabs.SelectedIndex = 0;
        StatusText.Text = GetText("Status.RoomFirstCorner");
        RenderPlan();
    }

    /// <summary>Creates a floor from the bounding rectangle of active-level walls.</summary>
    private void Floor_Click(object sender, RoutedEventArgs e) => ShowSlabPresetMenu(FloorKind.Floor);

    /// <summary>Creates a ceiling hung at the wall top of the active level.</summary>
    private void Ceiling_Click(object sender, RoutedEventArgs e) => ShowSlabPresetMenu(FloorKind.Ceiling);

    /// <summary>Creates a roof slab that bears on the wall top of the active level.</summary>
    private void Roof_Click(object sender, RoutedEventArgs e) => ShowSlabPresetMenu(FloorKind.Roof);

    /// <summary>Creates a slab of the requested role from the bounding rectangle of active-level walls.</summary>
    private void CreateEnclosureSlab(FloorKind kind, SlabPreset? preset = null)
    {
        var walls = _session.Document.Walls.Where(wall => wall.LevelId == _activeLevelId).ToArray();
        if (walls.Length < 2)
        {
            StatusText.Text = GetText("Status.FloorNeedsWalls");
            return;
        }
        var points = walls.SelectMany(wall => new[] { wall.Start, wall.End }).ToArray();
        var minimumX = points.Min(point => point.X);
        var maximumX = points.Max(point => point.X);
        var minimumY = points.Min(point => point.Y);
        var maximumY = points.Max(point => point.Y);
        if (maximumX - minimumX < 0.1 || maximumY - minimumY < 0.1)
        {
            StatusText.Text = GetText("Status.FloorNeedsEnclosure");
            return;
        }
        var slabThickness = preset?.Thickness ?? 0.20;
        var wallTop = walls.Max(wall => wall.Height);
        var elevationOffset = kind switch
        {
            FloorKind.Ceiling => wallTop,
            FloorKind.Roof => wallTop + slabThickness,
            _ => 0
        };
        var (nameKey, statusKey) = kind switch
        {
            FloorKind.Ceiling => ("Tool.Ceiling", "Status.CeilingCreated"),
            FloorKind.Roof => ("Tool.Roof", "Status.RoofCreated"),
            _ => ("Entity.Floor", "Status.FloorCreated")
        };
        var index = _session.Document.Floors.Count + 1;
        Point2[] boundary =
        [
            new Point2(minimumX, minimumY),
            new Point2(maximumX, minimumY),
            new Point2(maximumX, maximumY),
            new Point2(minimumX, maximumY)
        ];
        _selectedFloorId = preset is null
            ? _session.AddFloor(_activeLevelId, boundary, slabThickness, $"{GetText(nameKey)} {index}", kind, elevationOffset)
            : _session.AddSlabFromPreset(_activeLevelId, boundary, preset, elevationOffset);
        StatusText.Text = GetText(statusKey);
        RefreshAll();
    }

    /// <summary>Adds an A3 architectural drawing sheet and opens the editable paper workspace.</summary>
    private void AddSheet_Click(object sender, RoutedEventArgs e)
    {
        var index = _session.Document.Sheets.Count + 1;
        _selectedSheetId = _session.AddSheet(
            SheetPaperSize.A3, $"{GetText("Entity.Sheet")} {index}");
        _selectedViewId = null;
        ViewTabs.SelectedIndex = 3;
        StatusText.Text = GetText("Status.SheetCreated");
        RefreshAll();
    }

    /// <summary>Displays the coordinated project schedule.</summary>
    private void ShowSchedule_Click(object sender, RoutedEventArgs e) =>
        SelectProjectView(BuiltInProjectViews.ProjectScheduleId);

    /// <summary>Displays the drawing-sheet workspace.</summary>
    private void ShowSheet_Click(object sender, RoutedEventArgs e)
    {
        _selectedViewId = null;
        ViewTabs.SelectedIndex = 3;
    }

    /// <summary>Assigns the selected catalog type to the selected wall.</summary>
    private void ApplyWallType_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWallId is not Guid wallId)
        {
            StatusText.Text = GetText("Status.SelectWallForType");
            return;
        }
        if (WallTypePicker.SelectedItem is not ComboBoxItem { Tag: Guid wallTypeId }) return;
        _session.SetWallType(wallId, wallTypeId);
        StatusText.Text = GetText("Status.WallTypeApplied");
    }

    /// <summary>Refreshes project-browser matches while the search query changes.</summary>
    private void ProjectSearchInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ClearProjectSearchButton is not null)
            ClearProjectSearchButton.IsEnabled = !string.IsNullOrWhiteSpace(ProjectSearchInput.Text);
        if (ProjectTree is not null && _session is not null) RefreshTree();
    }

    /// <summary>
    /// Replaces the standard properties and project-browser palettes with the render controls while the Render view is
    /// active. The render surface spans the complete workspace so its sidebar occupies the same column as the palettes.
    /// </summary>
    private void UpdateRenderWorkspace(bool renderIsActive)
    {
        var standardVisibility = renderIsActive ? Visibility.Collapsed : Visibility.Visible;
        StandardSidebar.Visibility = standardVisibility;
        StandardSidebarSplitter.Visibility = standardVisibility;
        // Render retains the window title bar while hiding the BIM ribbon and palettes.
        // The project model, view state and commands remain loaded and are restored on return to 3D.
        MainTitleBar.Visibility = Visibility.Visible;
        RibbonTabControl.Visibility = standardVisibility;
        MainStatusStrip.Visibility = standardVisibility;
        RenderReturn3DButton.Visibility = renderIsActive && !App.RenderOnly ? Visibility.Visible : Visibility.Collapsed;
        RenderPanelControl.Visibility = renderIsActive ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Completes room placement when the plan canvas receives two corners.</summary>
    private bool TryHandleRoomPlacement(MouseButtonEventArgs e)
    {
        if (!_drawingRoom) return false;
        var point = CanvasToWorld(PlanPosition(e));
        if (SnapGridCheck.IsChecked == true)
            point = new Point2(
                Math.Round(point.X * 10, MidpointRounding.AwayFromZero) / 10,
                Math.Round(point.Y * 10, MidpointRounding.AwayFromZero) / 10);
        if (_pendingRoomStart is null)
        {
            _pendingRoomStart = point;
            StatusText.Text = GetText("Status.RoomSecondCorner");
            return true;
        }
        var index = _session.Document.Rooms.Count + 1;
        _selectedRoomId = _session.AddRoom(
            _activeLevelId, _pendingRoomStart.Value, point, $"{GetText("Entity.Room")} {index}");
        _pendingRoomStart = null;
        _drawingRoom = false;
        StatusText.Text = GetText("Status.RoomCreated");
        RefreshAll();
        return true;
    }

    /// <summary>Starts structural column placement in the active plan view.</summary>
    private void Column_Click(object sender, RoutedEventArgs e)
    {
        SetWallTool(false);
        _drawingColumn = true;
        _selectedViewId = null;
        ViewTabs.SelectedIndex = 0;
        StatusText.Text = GetText("Status.ColumnPlace");
        RenderPlan();
    }

    /// <summary>Places a column at the clicked plan point while column placement is active.</summary>
    private bool TryHandleColumnPlacement(MouseButtonEventArgs e)
    {
        if (!_drawingColumn) return false;
        var point = CanvasToWorld(PlanPosition(e));
        if (SnapGridCheck.IsChecked == true)
            point = new Point2(
                Math.Round(point.X * 10, MidpointRounding.AwayFromZero) / 10,
                Math.Round(point.Y * 10, MidpointRounding.AwayFromZero) / 10);
        SelectExtendedElement(columnId: _columnPreset is { } preset
            ? _session.AddColumnFromPreset(_activeLevelId, point, preset)
            : _session.AddColumn(_activeLevelId, point));
        _drawingColumn = false;
        _columnPreset = null;
        StatusText.Text = GetText("Status.ColumnPlaced");
        RefreshAll();
        return true;
    }

    /// <summary>Cancels room and column placement and clears their provisional state.</summary>
    private void CancelExtendedDrawing()
    {
        _placingOpening = null;
        _placingOpeningTypeId = null;
        _columnPreset = null;
        _placementPointer = null;
        _drawingRoom = false;
        _drawingColumn = false;
        _pendingRoomStart = null;
        CancelStructuralTool();
    }

    /// <summary>Selects a room or floor from its rendered plan element.</summary>
    private bool TrySelectExtendedPlanElement(MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.Tag is not Guid id) return false;
        if (_session.Document.Rooms.Any(room => room.Id == id))
        {
            SelectExtendedElement(roomId: id);
            return true;
        }
        if (_session.Document.Floors.Any(floor => floor.Id == id))
        {
            SelectExtendedElement(floorId: id);
            return true;
        }
        if (_session.Document.Columns.Any(column => column.Id == id))
        {
            SelectExtendedElement(columnId: id);
            return true;
        }
        if (_session.Document.StructuralElements.Any(element => element.Id == id))
        {
            SelectExtendedElement(structuralId: id);
            return true;
        }
        return false;
    }

    /// <summary>Centralizes selection state for rooms, floors, sheets, columns, and structural elements.</summary>
    private void SelectExtendedElement(Guid? roomId = null, Guid? floorId = null, Guid? sheetId = null,
        Guid? columnId = null, Guid? structuralId = null)
    {
        _selectedRoomId = roomId;
        _selectedFloorId = floorId;
        _selectedSheetId = sheetId;
        _selectedColumnId = columnId;
        _selectedStructuralId = structuralId;
        _selectedGridId = null;
        _selectedElectricalId = null;
        _selectedPlumbingId = null;
        _selectedViewId = null;
        _selectedWallId = null;
        _selectedOpeningId = null;
        RefreshProperties();
        RenderPlan();
        RenderSheet();
    }

    /// <summary>Handles project-browser selection for new coordinated BIM entities.</summary>
    private bool TryHandleProjectTreeSelection(Guid id)
    {
        if (_session.Document.Callouts.Any(callout => callout.Id == id))
        {
            ZoomToCallout(id);
            return true;
        }
        if (_session.Document.Views.Any(view => view.Id == id))
        {
            SelectProjectView(id);
            return true;
        }
        var room = _session.Document.Rooms.FirstOrDefault(candidate => candidate.Id == id);
        if (room is not null)
        {
            _activeLevelId = room.LevelId;
            ViewTabs.SelectedIndex = 0;
            SelectExtendedElement(roomId: id);
            return true;
        }
        var floor = _session.Document.Floors.FirstOrDefault(candidate => candidate.Id == id);
        if (floor is not null)
        {
            _activeLevelId = floor.LevelId;
            ViewTabs.SelectedIndex = 0;
            SelectExtendedElement(floorId: id);
            return true;
        }
        if (_session.Document.Sheets.Any(sheet => sheet.Id == id))
        {
            ViewTabs.SelectedIndex = 3;
            SelectExtendedElement(sheetId: id);
            return true;
        }
        var type = _session.Document.WallTypes.FirstOrDefault(candidate => candidate.Id == id);
        if (type is null) return false;
        WallTypePicker.SelectedItem = WallTypePicker.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => Equals(item.Tag, id));
        PropertiesText.Text = BuildWallTypeSummary(type);
        return true;
    }

    /// <summary>Deletes the selected room, floor, or sheet when one owns selection.</summary>
    private bool DeleteExtendedSelection()
    {
        if (_selectedRoomId is Guid roomId) _session.RemoveRoom(roomId);
        else if (_selectedFloorId is Guid floorId) _session.RemoveFloor(floorId);
        else if (_selectedSheetId is Guid sheetId) _session.RemoveSheet(sheetId);
        else if (_selectedColumnId is Guid columnId) _session.RemoveColumn(columnId);
        else if (_selectedStructuralId is Guid structuralId) _session.RemoveStructuralElement(structuralId);
        else return false;
        _selectedRoomId = null;
        _selectedFloorId = null;
        _selectedSheetId = null;
        _selectedColumnId = null;
        _selectedStructuralId = null;
        RefreshAll();
        return true;
    }

    /// <summary>Refreshes wall types, schedules, sheets, and extended selection state.</summary>
    private void RefreshExtendedWorkflows()
    {
        RefreshWallTypePicker();
        RefreshSchedule();
        RenderSheet();
        RenderOrthographicViews();
    }

    /// <summary>Synchronizes the wall-type picker with the project catalog and selection.</summary>
    private void RefreshWallTypePicker()
    {
        var selectedWall = _session.Document.Walls.FirstOrDefault(wall => wall.Id == _selectedWallId);
        var selectedTypeId = selectedWall?.WallTypeId ??
            (WallTypePicker.SelectedItem as ComboBoxItem)?.Tag as Guid? ?? BuiltInWallTypes.Basic200Id;
        WallTypePicker.Items.Clear();
        foreach (var type in _session.Document.WallTypes)
        {
            var item = new ComboBoxItem { Content = $"{type.Name} · {type.Thickness * 1000:0} mm", Tag = type.Id };
            WallTypePicker.Items.Add(item);
            if (type.Id == selectedTypeId) WallTypePicker.SelectedItem = item;
        }
    }

    private const int ScheduleTabIndex = 2;
    private bool _scheduleStale = true;

    /// <summary>Builds localized rows for the coordinated project schedule.</summary>
    private void RefreshSchedule()
    {
        // The schedule of a big project takes a tenth of a second or more to work out, so it waits until its tab is the one shown.
        if (ViewTabs.SelectedIndex != ScheduleTabIndex)
        {
            _scheduleStale = true;
            return;
        }
        _scheduleStale = false;
        ScheduleGrid.ItemsSource = ProjectScheduleService.Build(_session.Document)
            .Select(row => new ScheduleDisplayRow(
                GetText($"Schedule.{row.Category}"), row.Number, row.Name,
                row.Level, row.Quantity, row.Unit))
            .ToArray();
    }

}
