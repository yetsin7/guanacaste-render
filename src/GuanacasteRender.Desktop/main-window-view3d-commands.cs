using System.Windows;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>What the level section box hid, so showing everything again takes back exactly that.</summary>
    private readonly HashSet<Guid> _sectionBoxIds = [];

    /// <summary>
    /// The section box of the 3D view, by level: with it on, only the active level is shown, so a floor can be looked at
    /// from above without the ones over it in the way. Everything else is hidden for now only, and the project, the
    /// saved view and the other views are untouched.
    /// </summary>
    private void ToggleActiveLevelOnly()
    {
        EnsureTemporarySession();
        var on = _sectionBoxIds.Count > 0 && _temporaryHidden.IsSupersetOf(_sectionBoxIds);
        if (on)
        {
            _temporaryHidden.ExceptWith(_sectionBoxIds);
            _sectionBoxIds.Clear();
            _temporaryActive = _temporaryHidden.Count > 0 || _temporaryIsolated.Count > 0;
            StatusText.Text = GetText("Status.SectionBoxOff");
        }
        else
        {
            var document = _session.Document;
            _sectionBoxIds.Clear();
            foreach (var id in document.Walls.Where(wall => wall.LevelId != _activeLevelId).Select(wall => wall.Id)
                         .Concat(document.Floors.Where(floor => floor.LevelId != _activeLevelId).Select(floor => floor.Id))
                         .Concat(document.Columns.Where(column => column.LevelId != _activeLevelId).Select(column => column.Id))
                         .Concat(document.StructuralElements.Where(element => element.LevelId != _activeLevelId).Select(element => element.Id)))
                _sectionBoxIds.Add(id);
            _temporaryHidden.UnionWith(_sectionBoxIds);
            _temporaryActive = true;
            var level = document.Levels.First(candidate => candidate.Id == _activeLevelId).Name;
            StatusText.Text = string.Format(GetText("Status.SectionBoxOn"), level);
        }
        _picker = null;
        MarkSceneDirty();
        RebuildModelVisual();
        UpdateViewControlState();
    }

    private static readonly string[] SunPresetCycle = ["Off", "Morning", "Noon", "Afternoon"];

    /// <summary>Whether the 3D view uses parallel projection instead of perspective.</summary>
    private bool _modelOrthographic;

    /// <summary>
    /// Routes the View ribbon's 3D commands to the options of the model view that already exist in its toolbar and side
    /// panel: line weight, hidden lines, shadows, the sun, locking and the navigation bar. Each one first brings up the
    /// 3D view, because its result is only visible there.
    /// </summary>
    private bool TryExecuteView3DCommand(string commandId)
    {
        switch (commandId)
        {
            case "ThinLines":
            case "ShowHiddenLines":
            case "RemoveHiddenLines":
            case "Shadows":
            case "SunPath":
            case "LockView":
            case "Orbit":
            case "Pan":
            case "Zoom":
            case "ViewCube":
            case "Perspective":
            case "Orthographic":
            case "SectionBox":
                break;
            default:
                return false;
        }

        if (!_tileViews) ViewTabs.SelectedIndex = Model3DTabIndex;
        switch (commandId)
        {
            case "ThinLines":
                // The side panel's checkbox owns the setting, so the panel, the toolbar and the saved view agree.
                ThickLinesCheck.IsChecked = ThickLinesCheck.IsChecked != true;
                StatusText.Text = GetText(ThickLinesCheck.IsChecked == true ? "Status.Lines3DThick" : "Status.Lines3DThin");
                break;
            case "ShowHiddenLines":
                ExecuteModelViewCommand("Style.Wireframe");
                StatusText.Text = GetText("Status.Style3DWireframe");
                break;
            case "RemoveHiddenLines":
                ExecuteModelViewCommand("Style.HiddenLine");
                StatusText.Text = GetText("Status.Style3DHiddenLine");
                break;
            case "Shadows":
                ExecuteModelViewCommand("Shadows");
                StatusText.Text = GetText(_modelShadows ? "Status.Shadows3DOn" : "Status.Shadows3DOff");
                break;
            case "SunPath":
                var next = SunPresetCycle[(Array.IndexOf(SunPresetCycle, _modelSunPreset) + 1) % SunPresetCycle.Length];
                ExecuteModelViewCommand("Sun." + next);
                StatusText.Text = string.Format(GetText("Status.Sun3D"), GetText("SunPreset." + next));
                break;
            case "LockView":
                ExecuteModelViewCommand("Lock");
                StatusText.Text = GetText(_modelNavigator?.IsLocked == true ? "Status.View3DLocked" : "Status.View3DUnlocked");
                break;
            case "SectionBox":
                ToggleActiveLevelOnly();
                break;
            case "Perspective":
            case "Orthographic":
                _modelOrthographic = commandId == "Orthographic";
                UpdateModelCamera();
                StatusText.Text = GetText(_modelOrthographic ? "Status.Projection3DOrthographic" : "Status.Projection3DPerspective");
                break;
            default:
                // Orbit, pan, zoom and the view cube are done with the pointer; the bar brings them within reach.
                ExecuteModelViewCommand("NavigationBar");
                StatusText.Text = GetText("Status.Navigate3D");
                break;
        }
        return true;
    }
}
