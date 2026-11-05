using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>Updates the properties palette for rooms, floors, sheets, and wall types.</summary>
    private bool TryRefreshExtendedProperties()
    {
        if (TryRefreshElectricalProperties()) return true;
        if (TryRefreshPlumbingProperties()) return true;
        if (TryRefreshStructuralProperties()) return true;
        if (_session.Document.Grids.FirstOrDefault(axis => axis.Id == _selectedGridId) is { } axis)
        {
            PropertiesText.Text = FormatText("Documentation.AxisSummary", axis.Name, axis.Start.DistanceTo(axis.End));
            return true;
        }
        var selectedView = _session.Document.Views.FirstOrDefault(view => view.Id == _selectedViewId);
        if (selectedView is not null)
        {
            PropertiesText.Text = FormatText(
                "Properties.ViewSummary", selectedView.Name, GetText("Documentation.ViewKind." + selectedView.Kind),
                GetText("Documentation.Orientation." + selectedView.Orientation));
            return true;
        }
        var levelNames = _session.Document.Levels.ToDictionary(level => level.Id, level => level.Name);
        var room = _session.Document.Rooms.FirstOrDefault(candidate => candidate.Id == _selectedRoomId);
        if (room is not null)
        {
            PropertiesText.Text = FormatText(
                "Properties.RoomSummary", room.Number, room.Name, room.Area, levelNames[room.LevelId]);
            return true;
        }
        var floor = _session.Document.Floors.FirstOrDefault(candidate => candidate.Id == _selectedFloorId);
        if (floor is not null)
        {
            PropertiesText.Text = FormatText(
                "Properties.FloorSummary", floor.Name, floor.CalculateArea(), floor.Thickness, levelNames[floor.LevelId]);
            return true;
        }
        var column = _session.Document.Columns.FirstOrDefault(candidate => candidate.Id == _selectedColumnId);
        if (column is not null)
        {
            PropertiesText.Text = FormatText(
                "Properties.ColumnSummary", column.Width, column.Depth, column.Height, levelNames[column.LevelId]);
            return true;
        }
        var sheet = _session.Document.Sheets.FirstOrDefault(candidate => candidate.Id == _selectedSheetId);
        if (sheet is null) return false;
        PropertiesText.Text = FormatText("Properties.SheetSummary", sheet.Number, sheet.Name, sheet.PaperSize);
        return true;
    }

}
