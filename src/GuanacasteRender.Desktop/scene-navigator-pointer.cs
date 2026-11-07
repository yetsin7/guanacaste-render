using System.Numerics;
using System.Windows;
using System.Windows.Input;
using Guanacaste3D.Core.Navigation;

namespace Guanacaste3D.Desktop;

/// <summary>Processes pointer samples independently of WPF event transport without synthesizing operating-system input.</summary>
internal sealed partial class SceneNavigator
{
    private const double DragThreshold = 4;

    /// <summary>
    /// Distinguishes click jitter from an intentional drag and routes it to editing or navigation.
    /// The press position remains the origin until the threshold is crossed, preserving the complete first movement.
    /// </summary>
    private bool ProcessPointerMove(Point position, ModifierKeys modifiers)
    {
        if (_dragButton is not { } button)
        {
            PointerMoved?.Invoke(position);
            return false;
        }
        if (IsLocked && button != MouseButton.Left)
        {
            _last = position;
            return true;
        }
        if (!_dragged)
        {
            if ((position - _pressPoint).Length <= DragThreshold) return true;
            _dragged = true;
            if (button == MouseButton.Left) _elementDragging = BeginElementDrag?.Invoke(_pressPoint) == true;
        }
        if (_elementDragging)
        {
            _surface.Cursor = Cursors.SizeAll;
            ElementDragMoved?.Invoke(position);
            return true;
        }
        var horizontal = (float)(position.X - _last.X);
        var vertical = (float)(position.Y - _last.Y);
        _last = position;
        if (button == MouseButton.Middle) UpdateOrbitMode(modifiers.HasFlag(ModifierKeys.Shift));
        if (horizontal == 0 && vertical == 0) return true;
        switch (button)
        {
            case MouseButton.Right:
                Camera.Look(horizontal, vertical);
                break;
            case MouseButton.Left when LumionControls:
                WalkAndTurn(horizontal, vertical);
                break;
            case MouseButton.Middle when _orbiting:
                Camera.OrbitAround(_orbitPivot, horizontal, vertical);
                break;
            case MouseButton.Middle:
                var (width, height) = Size();
                var distance = PickDistance(Camera.PixelRay((float)position.X, (float)position.Y, width, height));
                var unitsPerPixel = 2 * distance * MathF.Tan(Camera.FieldOfViewDegrees * MathF.PI / 360) / height;
                MoveCamera(Camera.PanDelta(horizontal, vertical, unitsPerPixel));
                break;
        }
        if (button != MouseButton.Left || LumionControls) NotifyChanged();
        return true;
    }

    /// <summary>
    /// Lumion's left-button gesture: dragging up or down walks along the horizon (scaled by the travel speed) while
    /// dragging sideways turns the view, so the camera can be driven with a single hand on the mouse.
    /// </summary>
    private void WalkAndTurn(float horizontal, float vertical)
    {
        Camera.Look(horizontal * 0.6f, 0);
        var heading = new Vector3(Camera.Forward.X, Camera.Forward.Y, 0);
        if (heading.LengthSquared() < 1e-6f) return;
        MoveCamera(Vector3.Normalize(heading) * (-vertical * (TravelSpeed ?? 2f) * 0.02f));
    }

    /// <summary>Applies the full wheel delta along the picked ray, retaining smooth-wheel precision and camera clearance.</summary>
    private bool ProcessWheel(Point position, int wheelDelta, ModifierKeys modifiers)
    {
        if (IsLocked || wheelDelta == 0) return false;
        var (width, height) = Size();
        var ray = Camera.PixelRay((float)position.X, (float)position.Y, width, height);
        if (FreeTravel)
        {
            _history.Record(Camera);
            var speed = TravelSpeed ?? 2f;
            var multiplier = modifiers.HasFlag(ModifierKeys.Alt) ? 0.2f : modifiers.HasFlag(ModifierKeys.Shift) ? 4f : 1f;
            MoveCamera(ray * (wheelDelta / 120f) * speed * 0.25f * multiplier);
            NotifyChanged();
            return true;
        }
        var step = NavigationZoom.DollyStep(PickDistance(ray), wheelDelta, SceneRadius(),
            modifiers.HasFlag(ModifierKeys.Shift));
        if (step == 0) return true;
        _history.Record(Camera);
        MoveCamera(ray * step);
        NotifyChanged();
        return true;
    }
}
