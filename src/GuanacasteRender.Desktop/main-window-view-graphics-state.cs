using System.Windows;
using System.Windows.Media;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private DocumentSession? _graphicsSession;
    private ViewGraphicsSettings? _lastGraphicsSettings;
    private bool _synchronizingGraphics;

    /// <summary>Restores saved graphics when opening documents or undoing view changes.</summary>
    private void SynchronizeModelViewGraphics()
    {
        if (_navigationOverlay is null) return;
        var settings = Model3DView()?.Graphics ?? new ViewGraphicsSettings();
        if (ReferenceEquals(_graphicsSession, _session) && Equals(settings, _lastGraphicsSettings)) return;
        settings.Validate();
        var differentDocument = !ReferenceEquals(_graphicsSession, _session);
        _graphicsSession = _session;
        _lastGraphicsSettings = settings;
        _modelDisplayStyle = settings.Style;
        _modelDetail = settings.Detail;
        _modelSunPreset = settings.SunPreset;
        _modelShadows = settings.Shadows;
        _modelAmbient = settings.Ambient;
        _modelBackground = settings.Background;
        _navigation.FieldOfViewDegrees = settings.FieldOfView;
        if (settings.CameraLocked && settings.CameraPose is { } pose)
        {
            _navigation.Position = new System.Numerics.Vector3(pose.X, pose.Y, pose.Z);
            _navigation.YawDegrees = pose.Yaw;
            _navigation.PitchDegrees = pose.Pitch;
        }
        _showEdges = settings.ShowEdges;
        _thickLines = settings.ThickLines;
        _synchronizingGraphics = true;
        try { EdgesCheck.IsChecked = settings.ShowEdges; ThickLinesCheck.IsChecked = settings.ThickLines; }
        finally { _synchronizingGraphics = false; }
        if (_modelNavigator is not null) _modelNavigator.IsLocked = settings.CameraLocked;
        if (RenderPanelControl.Navigator is { } renderer) renderer.IsLocked = settings.CameraLocked;
        if (differentDocument)
        {
            _modelNavigator?.ResetHistory();
            RenderPanelControl.Navigator?.ResetHistory();
        }
        _cropActive = settings.CropActive;
        _cropBoundaryVisible = settings.CropBoundaryVisible;
        _cropFraction = settings.Crop is { } crop ? new Rect(crop.X, crop.Y, crop.Width, crop.Height) : null;
        ApplyModelBackground();
        UpdateModelCrop();
        UpdateViewControlState();
        RefreshModelRealisticPreview();
    }

    /// <summary>Stores graphics with the active model view; temporary isolation never enters this snapshot.</summary>
    private void PersistModelViewGraphics()
    {
        if (_navigationOverlay is null || Model3DView() is not { } view) return;
        var settings = new ViewGraphicsSettings
        {
            Style = _modelDisplayStyle, Detail = _modelDetail, SunPreset = _modelSunPreset, Shadows = _modelShadows, Ambient = _modelAmbient,
            Background = _modelBackground, FieldOfView = _navigation.FieldOfViewDegrees, ShowEdges = _showEdges, ThickLines = _thickLines,
            CameraLocked = _modelNavigator?.IsLocked == true, CropActive = _cropActive, CropBoundaryVisible = _cropBoundaryVisible,
            CameraPose = _modelNavigator?.IsLocked == true ? new ViewCameraPose(_navigation.Position.X, _navigation.Position.Y,
                _navigation.Position.Z, _navigation.YawDegrees, _navigation.PitchDegrees) : null,
            Crop = _cropFraction is { } crop ? new ViewCropRegion(crop.X, crop.Y, crop.Width, crop.Height) : null
        };
        _graphicsSession = _session;
        _lastGraphicsSettings = settings;
        _session.SetViewGraphics(view.Id, settings);
    }

    /// <summary>Applies a saved background independently of the current lighting or material mode.</summary>
    private void ApplyModelBackground() => ModelSurface.Background = new SolidColorBrush(_modelBackground switch
    { "Gray" => Color.FromRgb(235, 237, 240), "Sky" => Color.FromRgb(220, 235, 248), _ => Colors.White });
}
