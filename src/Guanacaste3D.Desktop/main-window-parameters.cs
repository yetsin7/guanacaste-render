using System.Windows;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>Opens the project parameter and door/window type managers from the main workspace.</summary>
public partial class MainWindow
{
    /// <summary>Shows the parameter manager focused on the category of the current selection.</summary>
    private void ProjectParameters_Click(object sender, RoutedEventArgs e)
    {
        var window = new ProjectParametersWindow(_session, SelectedCategory()) { Owner = this };
        window.ShowDialog();
        RefreshAll();
    }

    /// <summary>Shows the door and window type manager, preselecting the type of the selected opening.</summary>
    private void OpeningTypes_Click(object sender, RoutedEventArgs e)
    {
        var typeId = _session.Document.Openings.FirstOrDefault(opening => opening.Id == _selectedOpeningId)?.OpeningTypeId;
        var window = new OpeningTypesWindow(_session, typeId) { Owner = this };
        window.ShowDialog();
        RefreshAll();
    }

    /// <summary>Shows the level manager focused on the active level.</summary>
    private void Levels_Click(object sender, RoutedEventArgs e)
    {
        var window = new LevelsWindow(_session, _activeLevelId) { Owner = this };
        window.ShowDialog();
        if (_session.Document.Levels.All(level => level.Id != _activeLevelId))
            _activeLevelId = _session.Document.Levels.OrderBy(level => level.Elevation).First().Id;
        RefreshAll();
    }

    /// <summary>Shows the saved schedule designer.</summary>
    private void Schedules_Click(object sender, RoutedEventArgs e)
    {
        var window = new SchedulesWindow(_session) { Owner = this };
        window.ShowDialog();
        RefreshAll();
    }

    /// <summary>
    /// Maps the explicit model selection to its parameter category. The sheet
    /// shown in the sheet workspace is display state, not a user selection, so
    /// it is deliberately ignored (it previously forced every open to Sheets).
    /// </summary>
    private ElementCategory? SelectedCategory()
    {
        if (_selectedOpeningId is Guid openingId)
        {
            var opening = _session.Document.Openings.FirstOrDefault(candidate => candidate.Id == openingId);
            if (opening is not null)
                return opening.Kind == OpeningKind.Door ? ElementCategory.Doors : ElementCategory.Windows;
        }
        if (_selectedWallId is not null) return ElementCategory.Walls;
        if (_selectedRoomId is not null) return ElementCategory.Rooms;
        if (_selectedFloorId is not null) return ElementCategory.Floors;
        return null;
    }
}
