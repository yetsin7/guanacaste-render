using System.Numerics;
using System.Windows;
using Guanacaste3D.Core.Navigation;

namespace Guanacaste3D.Desktop;

internal sealed partial class SceneNavigator
{
    private readonly CameraViewHistory _history = new();
    private Vector3? _wheelPivot;
    private bool _externalGesture;
    public bool IsLocked { get; set; }
    public bool CanGoBack => _history.CanGoBack;
    public bool CanGoForward => _history.CanGoForward;

    /// <summary>Clears project-specific camera history and custom wheel pivots after opening another document.</summary>
    public void ResetHistory()
    {
        _history.Reset(Camera);
        _wheelPivot = null;
        _externalGesture = false;
    }

    /// <summary>Applies a bounded perspective lens as an independent view-history operation.</summary>
    public void SetFieldOfView(float degrees)
    {
        if (IsLocked) return;
        _history.Record(Camera);
        Camera.FieldOfViewDegrees = Math.Clamp(degrees, 8, 110);
        NotifyChanged();
    }

    /// <summary>Publishes camera changes, keeping pointer drags as a single history operation.</summary>
    private void NotifyChanged()
    {
        if (_dragButton is null && !_externalGesture) _history.Record(Camera);
        Changed?.Invoke();
    }

    /// <summary>Groups a cube or wheel drag into one camera-history operation.</summary>
    public void BeginExternalGesture()
    {
        _history.Record(Camera);
        _externalGesture = true;
    }

    /// <summary>Stores the completed cube or wheel position for a single rewind action.</summary>
    public void EndExternalGesture()
    {
        _externalGesture = false;
        _history.Record(Camera);
    }

    /// <summary>Restores the previous or following pan/zoom without touching model undo.</summary>
    public void RestoreView(bool forward)
    {
        if (IsLocked) return;
        if (_history.Restore(Camera, forward)) Changed?.Invoke();
    }

    /// <summary>Zooms about the viewport center by a true magnification factor at the current focus plane.</summary>
    public void ZoomFactor(float factor)
    {
        if (IsLocked || !float.IsFinite(factor) || factor <= 0) return;
        _history.Record(Camera);
        var (width, height) = Size();
        var distance = PickDistance(Camera.PixelRay(width / 2, height / 2, width, height));
        MoveCamera(Camera.Forward * distance * (1 - 1 / factor));
        NotifyChanged();
    }

    /// <summary>Frames a screen rectangle at its picked depth while preserving orientation and perspective.</summary>
    public void ZoomRegion(Rect region)
    {
        if (IsLocked || region.Width < 6 || region.Height < 6) return;
        _history.Record(Camera);
        var (width, height) = Size();
        var ray = Camera.PixelRay((float)(region.Left + region.Width / 2), (float)(region.Top + region.Height / 2), width, height);
        var depth = PickDistance(ray);
        var focus = Camera.Position + ray * depth;
        var forwardDepth = Vector3.Dot(focus - Camera.Position, Camera.Forward);
        var fraction = Math.Clamp(Math.Max(region.Width / width, region.Height / height), 0.01, 1);
        Camera.Position = focus - Camera.Forward * Math.Max(0.1f, forwardDepth * (float)fraction);
        NotifyChanged();
    }

    /// <summary>Uses a picked surface as the orbit center and centers it without changing the viewing direction.</summary>
    public void CenterAt(Point point)
    {
        if (IsLocked) return;
        _history.Record(Camera);
        var (width, height) = Size();
        var ray = Camera.PixelRay((float)point.X, (float)point.Y, width, height);
        var distance = PickDistance(ray);
        _wheelPivot = Camera.Position + ray * distance;
        Camera.Position = _wheelPivot.Value - Camera.Forward * distance;
        NotifyChanged();
    }

    /// <summary>Applies wheel-sector gestures using scene-scaled pan, orbit, dolly, walk, look, and elevation.</summary>
    public void ApplyWheelGesture(string action, float horizontal, float vertical)
    {
        if (IsLocked) return;
        if (!_externalGesture) _history.Record(Camera);
        var (min, max) = Bounds();
        var pivot = _wheelPivot ?? (min + max) / 2;
        var distance = Math.Max(0.1f, Vector3.Distance(Camera.Position, pivot));
        var units = 2 * distance * MathF.Tan(Camera.FieldOfViewDegrees * MathF.PI / 360) / Size().Height;
        switch (action)
        {
            case "Orbit": Camera.OrbitAround(pivot, horizontal, vertical); break;
            case "Pan": MoveCamera(Camera.PanDelta(horizontal, vertical, units)); break;
            case "Look": Camera.Look(horizontal, vertical); break;
            case "UpDown": MoveCamera(Vector3.UnitZ * -vertical * units); break;
            case "Walk":
                var forward = new Vector3(Camera.Forward.X, Camera.Forward.Y, 0);
                if (forward.LengthSquared() > 1e-6f) forward = Vector3.Normalize(forward);
                MoveCamera((forward * -vertical + Camera.Right * horizontal) * units);
                break;
            case "Zoom": MoveCamera(Camera.Forward * -vertical * units); break;
            case "Forward": MoveCamera(Camera.Forward * -vertical * units); break;
        }
        NotifyChanged();
    }
}
