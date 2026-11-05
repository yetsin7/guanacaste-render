using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Desktop.Controls;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private Canvas _navigationOverlay = null!;
    private ModelNavigationWheel _navigationWheel = null!;
    private Rectangle _regionOutline = null!;
    private string? _regionPurpose;
    private Point? _regionStart;
    private DateTime _zoomPrefixAt;
    private Rect? _cropFraction;
    private bool _cropActive;
    private bool _cropBoundaryVisible = true;
    private Rectangle _cropOutline = null!;
    private Rectangle _viewModeOutline = null!;

    /// <summary>Creates transient navigation overlays and wires camera, view style, and lifecycle actions.</summary>
    private void InitializeModelViewControls()
    {
        _navigationOverlay = new Canvas { ClipToBounds = true, Margin = new Thickness(0, 0, 0, 36) };
        _navigationWheel = new ModelNavigationWheel();
        _regionOutline = new Rectangle { Stroke = Brushes.DodgerBlue, StrokeThickness = 1,
            Fill = new SolidColorBrush(Color.FromArgb(28, 30, 130, 230)), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        _cropOutline = new Rectangle { Stroke = Brushes.SlateGray, StrokeThickness = 1, StrokeDashArray = [5, 3],
            IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        _viewModeOutline = new Rectangle { StrokeThickness = 3, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        _navigationOverlay.Children.Add(_viewModeOutline);
        _navigationOverlay.Children.Add(_cropOutline);
        _navigationOverlay.Children.Add(_regionOutline);
        _navigationOverlay.Children.Add(_navigationWheel);
        ((Grid)ModelOverlay.Parent).Children.Add(_navigationOverlay);
        _navigationOverlay.MouseLeftButtonDown += RegionPressed;
        _navigationOverlay.MouseMove += RegionMoved;
        _navigationOverlay.MouseLeftButtonUp += RegionReleased;
        _navigationOverlay.LostMouseCapture += RegionCaptureLost;
        _navigationOverlay.SizeChanged += NavigationOverlayResized;
        _navigationWheel.GestureRequested += ApplyNavigationWheel;
        _navigationWheel.GestureStarted += () => _modelNavigator?.BeginExternalGesture();
        _navigationWheel.GestureCompleted += () => _modelNavigator?.EndExternalGesture();
        ViewNavigation.GestureStarted += () => _modelNavigator?.BeginExternalGesture();
        ViewNavigation.GestureCompleted += () => _modelNavigator?.EndExternalGesture();
        _navigationWheel.CommandRequested += ExecuteViewNavigation;
        ModelToolbar.CommandRequested += ExecuteModelViewCommand;
        ModelToolbar.IsCommandChecked = IsViewControlChecked;
        ModelToolbar.IsCommandEnabled = IsViewControlEnabled;
        Deactivated += NavigationDeactivated;
        Closed += NavigationClosed;
        ViewTabs.SelectionChanged += NavigationViewChanged;
        InitializeModelRealisticPreview();
    }

    /// <summary>Routes the cube, navigation strip, and wheel to the shared camera.</summary>
    private void ExecuteViewNavigation(string command)
    {
        if (command.StartsWith("Wheel.", StringComparison.Ordinal))
        {
            CancelNavigationOverlay();
            _navigationWheel.Configure(command[6..]);
            _navigationWheel.Visibility = Visibility.Visible;
            PositionNavigationWheel();
            return;
        }
        switch (command)
        {
            case "Home": _modelNavigator?.FrameAll(); break;
            case "Fit": _modelNavigator?.CenterAll(); break;
            case "FitAll": FrameEntireProject(); break;
            case "SheetSize": FitModelPrintFrame(); break;
            case "Reduce": _modelNavigator?.ZoomFactor(0.5f); break;
            case "ZoomIn": _modelNavigator?.ZoomFactor(1.25f); break;
            case "ZoomOut": _modelNavigator?.ZoomFactor(0.8f); break;
            case "Previous": case "Rewind": _modelNavigator?.RestoreView(false); break;
            case "Next": _modelNavigator?.RestoreView(true); break;
            case "Left": _modelNavigator?.OrbitBy(-225, 0); break;
            case "Right": _modelNavigator?.OrbitBy(225, 0); break;
            case "Region": case "Center": ArmNavigationRegion(command); break;
        }
    }

    /// <summary>Frames the full source model including elements hidden by the active view.</summary>
    private void FrameEntireProject()
    {
        if (_modelNavigator is null) return;
        var previous = _modelNavigator.Bounds;
        try
        {
            _modelNavigator.Bounds = FullProjectBounds;
            _modelNavigator.CenterAll();
        }
        finally { _modelNavigator.Bounds = previous; }
    }

    /// <summary>Derives complete native and linked bounds independently of transient view filtering.</summary>
    private (Vector3 Min, Vector3 Max) FullProjectBounds()
    {
        var bounds = Guanacaste3D.Core.Geometry.BuildingMesher.Build(_session.Document).Bounds();
        if (_links.Bounds() is { } linked)
            bounds = (Vector3.Min(bounds.Min, linked.Min), Vector3.Max(bounds.Max, linked.Max));
        return bounds;
    }

    /// <summary>Frames the print/crop area when one is defined, otherwise fits the model into the viewport.</summary>
    private void FitModelPrintFrame()
    {
        if (_cropActive && _cropFraction is { } crop)
            _modelNavigator?.FrameWithinRegion(new Rect(crop.X * ModelSurface.ActualWidth, crop.Y * ModelSurface.ActualHeight,
                crop.Width * ModelSurface.ActualWidth, crop.Height * ModelSurface.ActualHeight));
        else _modelNavigator?.CenterAll();
    }

    /// <summary>Reserves the next viewport drag or center pick, leaving the BIM document untouched.</summary>
    private void ArmNavigationRegion(string purpose)
    {
        if (_modelNavigator?.IsLocked == true) return;
        CancelNavigationOverlay();
        _regionPurpose = purpose;
        _navigationOverlay.Background = Brushes.Transparent;
        _navigationOverlay.Cursor = Cursors.Cross;
        StatusText.Text = GetText("Navigation.Prompt." + purpose);
    }

    /// <summary>Starts a screen region or resolves a wheel center from the clicked model surface.</summary>
    private void RegionPressed(object sender, MouseButtonEventArgs e)
    {
        if (_regionPurpose is null) return;
        var point = e.GetPosition(_navigationOverlay);
        if (_regionPurpose == "Center") { _modelNavigator?.CenterAt(point); CancelNavigationOverlay(); }
        else
        {
            _regionStart = point;
            _navigationOverlay.CaptureMouse();
            _regionOutline.Visibility = Visibility.Visible;
            SetScreenRectangle(_regionOutline, new Rect(point, point));
        }
        e.Handled = true;
    }

    /// <summary>Updates only the transient marquee until the user completes a valid region.</summary>
    private void RegionMoved(object sender, MouseEventArgs e)
    {
        if (_regionStart is not Point start) return;
        SetScreenRectangle(_regionOutline, new Rect(start, ClampedNavigationPoint(e.GetPosition(_navigationOverlay))));
        e.Handled = true;
    }

    /// <summary>Commits one region zoom or crop and releases the transient selection surface.</summary>
    private void RegionReleased(object sender, MouseButtonEventArgs e)
    {
        if (_regionStart is not Point start) return;
        var rectangle = new Rect(start, ClampedNavigationPoint(e.GetPosition(_navigationOverlay)));
        if (rectangle.Width >= 6 && rectangle.Height >= 6)
        {
            if (_regionPurpose == "Crop")
            {
                ApplyModelCrop(rectangle);
            }
            else _modelNavigator?.ZoomRegion(rectangle);
        }
        CancelNavigationOverlay();
        e.Handled = true;
    }

    /// <summary>Restricts marquee endpoints to the active viewport.</summary>
    private Point ClampedNavigationPoint(Point point) => new(Math.Clamp(point.X, 0, Math.Max(0, ModelSurface.ActualWidth)),
        Math.Clamp(point.Y, 0, Math.Max(0, ModelSurface.ActualHeight)));

    /// <summary>Places an overlay rectangle in viewport coordinates.</summary>
    private static void SetScreenRectangle(Rectangle shape, Rect rectangle)
    {
        Canvas.SetLeft(shape, rectangle.Left);
        Canvas.SetTop(shape, rectangle.Top);
        shape.Width = rectangle.Width;
        shape.Height = rectangle.Height;
    }

    /// <summary>Applies normalized crop coordinates after resizing without changing the camera projection.</summary>
    private void UpdateModelCrop()
    {
        if (_cropFraction is not { } fraction)
        {
            ModelSurface.Clip = ModelOverlay.Clip = null;
            _cropOutline.Visibility = Visibility.Collapsed;
            return;
        }
        var rectangle = new Rect(fraction.X * ModelSurface.ActualWidth, fraction.Y * ModelSurface.ActualHeight,
            fraction.Width * ModelSurface.ActualWidth, fraction.Height * ModelSurface.ActualHeight);
        ModelSurface.Clip = _cropActive ? new RectangleGeometry(rectangle) : null;
        ModelOverlay.Clip = _cropActive ? new RectangleGeometry(rectangle) : null;
        SetScreenRectangle(_cropOutline, rectangle);
        _cropOutline.Visibility = _cropBoundaryVisible && _cropActive ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Stores a validated viewport crop in normalized coordinates so it survives window resizing.</summary>
    private void ApplyModelCrop(Rect rectangle)
    {
        if (rectangle.Width < 6 || rectangle.Height < 6 || ModelSurface.ActualWidth <= 0 || ModelSurface.ActualHeight <= 0) return;
        _cropFraction = new Rect(rectangle.X / ModelSurface.ActualWidth, rectangle.Y / ModelSurface.ActualHeight,
            rectangle.Width / ModelSurface.ActualWidth, rectangle.Height / ModelSurface.ActualHeight);
        _cropActive = true;
        PersistModelViewGraphics();
        UpdateModelCrop();
        UpdateViewControlState();
    }

    /// <summary>Centers the wheel within the available canvas, including compact windows.</summary>
    private void PositionNavigationWheel()
    {
        var size = Math.Min(1, Math.Min(_navigationOverlay.ActualWidth / _navigationWheel.Width,
            _navigationOverlay.ActualHeight / _navigationWheel.Height));
        _navigationWheel.LayoutTransform = new ScaleTransform(Math.Max(0.1, size), Math.Max(0.1, size));
        Canvas.SetLeft(_navigationWheel, Math.Max(0, (_navigationOverlay.ActualWidth - _navigationWheel.Width * size) / 2));
        Canvas.SetTop(_navigationWheel, Math.Max(0, (_navigationOverlay.ActualHeight - _navigationWheel.Height * size) / 2));
    }

    /// <summary>Reflows transient camera controls and crop bounds after a viewport resize.</summary>
    private void NavigationOverlayResized(object sender, SizeChangedEventArgs e)
    {
        PositionNavigationWheel();
        UpdateModelCrop();
        UpdateViewControlState();
        UpdateModelRealisticCamera();
    }

    /// <summary>Routes radial gestures through the same camera service as direct viewport input.</summary>
    private void ApplyNavigationWheel(string action, float horizontal, float vertical) => _modelNavigator?.ApplyWheelGesture(action, horizontal, vertical);

    /// <summary>Discards incomplete marquees when capture is interrupted.</summary>
    private void RegionCaptureLost(object sender, MouseEventArgs e)
    {
        if (_regionStart is not null) CancelNavigationOverlay();
    }

    /// <summary>Dismisses transient input when another window becomes active.</summary>
    private void NavigationDeactivated(object? sender, EventArgs e) => CancelNavigationOverlay();

    /// <summary>Releases background preview workers when the model window closes.</summary>
    private void NavigationClosed(object? sender, EventArgs e) => _modelRealisticPreview?.Dispose();

    /// <summary>Stops hidden navigation overlays and suspends realistic rendering when changing views.</summary>
    private void NavigationViewChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, ViewTabs)) return;
        CancelNavigationOverlay();
        RefreshModelRealisticPreview();
    }

    /// <summary>Releases overlay capture and restores normal selection without creating a model transaction.</summary>
    private void CancelNavigationOverlay()
    {
        var hadRegionPrompt = _regionPurpose is not null;
        _regionStart = null;
        _regionPurpose = null;
        if (_navigationOverlay.IsMouseCaptured) _navigationOverlay.ReleaseMouseCapture();
        _navigationOverlay.Background = null;
        _navigationOverlay.Cursor = Cursors.Arrow;
        _regionOutline.Visibility = Visibility.Collapsed;
        _navigationWheel.Dismiss();
        if (hadRegionPrompt) StatusText.Text = GetText("Status.SelectionMode");
    }

    /// <summary>Handles Escape and two-letter zoom shortcuts before drawing tools or navigation keys.</summary>
    private bool TryHandleViewNavigationKey(KeyEventArgs e)
    {
        if (ViewTabs.SelectedIndex != Model3DTabIndex || Keyboard.FocusedElement is TextBox || Keyboard.Modifiers != ModifierKeys.None) return false;
        if (e.Key == Key.Escape && (_regionPurpose is not null || _navigationWheel.Visibility == Visibility.Visible))
        { CancelNavigationOverlay(); return true; }
        if (e.Key == Key.Z) { _zoomPrefixAt = DateTime.UtcNow; return true; }
        if ((DateTime.UtcNow - _zoomPrefixAt).TotalSeconds > 1.5) return false;
        _zoomPrefixAt = DateTime.MinValue;
        var command = e.Key switch { Key.R => "Region", Key.O => "Reduce", Key.F => "Fit", Key.A => "FitAll",
            Key.S => "SheetSize", Key.P => "Previous", Key.N => "Next", _ => null };
        if (command is null) return false;
        ExecuteViewNavigation(command);
        return true;
    }
}
