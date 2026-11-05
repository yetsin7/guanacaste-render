using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using Guanacaste3D.Desktop.Controls;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private ModelDisplayStyle _modelDisplayStyle = ModelDisplayStyle.Textures;
    private ModelDetailLevel _modelDetail = ModelDetailLevel.Medium;
    private string _modelSunPreset = "Noon";
    private bool _modelShadows;
    private double _modelAmbient = 0.62;
    private string _modelBackground = "White";

    /// <summary>Resolves a consistent model-space sun direction for the selected time-of-day preset.</summary>
    private Vector3 ModelSunDirection()
    {
        var sun = Vector3.Normalize(_modelSunPreset switch
        {
            "Morning" => new Vector3(1, -0.4f, 0.4f),
            "Afternoon" => new Vector3(-1, -0.4f, 0.4f),
            _ => new Vector3(-0.45f, -0.55f, 0.85f)
        });
        // The presets are given against true north (x east, y north); the project may be turned away from it.
        var (x, y) = _session.Document.Site.TrueToProject(sun.X, sun.Y);
        return new Vector3((float)x, (float)y, sun.Z);
    }

    /// <summary>Routes the view control bar to functional view-only operations.</summary>
    private void ExecuteModelViewCommand(string command)
    {
        if (command.StartsWith("Temporary.") || command == "Unhide") { ExecuteTemporaryVisibility(command); return; }
        if (command.StartsWith("Style.") && Enum.TryParse<ModelDisplayStyle>(command[6..], out var style))
        {
            _modelDisplayStyle = style;
            _xRay = false;
            XRayCheck.IsChecked = false;
        }
        else if (command.StartsWith("Detail.") && Enum.TryParse<ModelDetailLevel>(command[7..], out var detail)) _modelDetail = detail;
        else if (command.StartsWith("Sun.")) _modelSunPreset = command[4..];
        else if (command.StartsWith("Lens."))
        {
            if (_modelNavigator?.IsLocked == true) return;
            var field = command[5..] switch { "Wide" => 65, "Telephoto" => 25, _ => 45 };
            _modelNavigator?.SetFieldOfView(field);
            PersistModelViewGraphics();
            UpdateViewControlState();
            return;
        }
        else switch (command)
        {
            case "Shadows": _modelShadows = !_modelShadows; break;
            case "Crop": case "Crop.Toggle":
                if (_cropFraction is null) ArmNavigationRegion("Crop");
                else { _cropActive = !_cropActive; UpdateModelCrop(); }
                PersistModelViewGraphics();
                UpdateViewControlState();
                return;
            case "Crop.Edit": ArmNavigationRegion("Crop"); return;
            case "Crop.Reset": _cropActive = false; _cropFraction = null; PersistModelViewGraphics(); UpdateModelCrop(); UpdateViewControlState(); return;
            case "CropBoundary": _cropBoundaryVisible = !_cropBoundaryVisible; PersistModelViewGraphics(); UpdateModelCrop(); return;
            case "Lock":
                if (_modelNavigator is not null) _modelNavigator.IsLocked = !_modelNavigator.IsLocked;
                if (RenderPanelControl.Navigator is { } renderer) renderer.IsLocked = _modelNavigator?.IsLocked == true;
                PersistModelViewGraphics();
                UpdateViewControlState();
                return;
            case "Reveal": _revealHidden = !_revealHidden; _picker = null; break;
            case "NavigationBar": ViewNavigation.NavigationBar.Visibility = Visibility.Visible; return;
            case "Graphics": OpenModelGraphicsOptions(); return;
            default: return;
        }
        PersistModelViewGraphics();
        UpdateViewControlState();
        RebuildModelVisual();
        RefreshModelRealisticPreview();
    }

    /// <summary>Reports selected style, sun preset, and lens without using localized display strings as identifiers.</summary>
    private bool IsViewControlChecked(string command) => command switch
    {
        _ when command.StartsWith("Style.") => command[6..] == _modelDisplayStyle.ToString(),
        _ when command.StartsWith("Detail.") => command[7..] == _modelDetail.ToString(),
        _ when command.StartsWith("Sun.") => command[4..] == _modelSunPreset,
        "Lens.Wide" => Math.Abs(_navigation.FieldOfViewDegrees - 65) < 0.1,
        "Lens.Normal" => Math.Abs(_navigation.FieldOfViewDegrees - 45) < 0.1,
        "Lens.Telephoto" => Math.Abs(_navigation.FieldOfViewDegrees - 25) < 0.1,
        "Crop.Toggle" => _cropActive,
        _ => false
    };

    /// <summary>Disables actions that require a selection or an existing temporary override.</summary>
    private bool IsViewControlEnabled(string command) => command switch
    {
        "Temporary.Reset" or "Temporary.Apply" => _temporaryActive,
        "Unhide" => _revealHidden && SelectedIds().Count > 0,
        _ when command.StartsWith("Temporary.") => SelectedIds().Count > 0,
        _ when command.StartsWith("Lens.") => _modelNavigator?.IsLocked != true,
        _ => true
    };

    /// <summary>Keeps active-state indicators synchronized with the actual view settings.</summary>
    private void UpdateViewControlState()
    {
        ModelToolbar.SetActive(ModelToolbar.ShadowsButton, _modelShadows);
        ModelToolbar.SetActive(ModelToolbar.SunButton, _modelSunPreset != "Off");
        ModelToolbar.SetActive(ModelToolbar.CropButton, _cropActive);
        ModelToolbar.SetActive(ModelToolbar.LockButton, _modelNavigator?.IsLocked == true);
        ModelToolbar.SetActive(ModelToolbar.TemporaryButton, _temporaryActive);
        ModelToolbar.SetActive(ModelToolbar.RevealButton, _revealHidden);
        ModelToolbar.DisplayStyleButton.ToolTip = GetText("Display.Style." + _modelDisplayStyle);
        _viewModeOutline.Stroke = _revealHidden ? Brushes.Magenta : Brushes.DodgerBlue;
        _viewModeOutline.Visibility = _revealHidden || _temporaryActive ? Visibility.Visible : Visibility.Collapsed;
        SetScreenRectangle(_viewModeOutline, new Rect(1, 1, Math.Max(0, _navigationOverlay.ActualWidth - 2), Math.Max(0, _navigationOverlay.ActualHeight - 2)));
    }

    /// <summary>Edits ambient light, background, edges, and camera lens through localized graphics controls.</summary>
    private void OpenModelGraphicsOptions()
    {
        var editor = new ModelGraphicsWindow(_modelAmbient, _modelBackground, _showEdges, _navigation.FieldOfViewDegrees,
            _modelNavigator?.IsLocked == true) { Owner = this };
        if (editor.ShowDialog() != true) return;
        _modelAmbient = editor.Ambient;
        _modelBackground = editor.BackgroundMode;
        _synchronizingGraphics = true;
        try { EdgesCheck.IsChecked = editor.ShowEdges; _showEdges = editor.ShowEdges; }
        finally { _synchronizingGraphics = false; }
        _modelNavigator?.SetFieldOfView(editor.FieldOfView);
        ApplyModelBackground();
        PersistModelViewGraphics();
        RebuildModelVisual();
        RefreshModelRealisticPreview();
    }
}
