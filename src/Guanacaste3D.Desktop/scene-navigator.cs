using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Guanacaste3D.Core.Navigation;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Walk-through navigation for a surface that shows the model (the 3D view or the render preview), in the manner of
/// Revit: the wheel zooms toward the point under the cursor, the middle button pans, Shift plus the middle button
/// orbits the model, the right button looks around, the left button only selects, W A S D fly, Q and E go down and up, Shift increases speed and Ctrl shortcuts remain available. Both views share one
/// <see cref="NavigationCamera"/>, so moving in either keeps the other in step.
/// </summary>
internal sealed partial class SceneNavigator
{
    private readonly FrameworkElement _surface;
    private Point _last;
    private MouseButton? _dragButton;
    private Vector3 _orbitPivot;
    private TimeSpan _lastTick;
    private bool _ticking;

    /// <summary>Connects shared camera navigation to the lifetime and pointer events of a model surface.</summary>
    public SceneNavigator(FrameworkElement surface, NavigationCamera camera)
    {
        _surface = surface;
        Camera = camera;
        _history.Record(camera);
        surface.Focusable = true;
        surface.MouseDown += OnMouseDown;
        surface.MouseMove += OnMouseMove;
        surface.MouseUp += OnMouseUp;
        surface.MouseWheel += OnMouseWheel;
        surface.LostMouseCapture += OnLostMouseCapture;
        surface.IsVisibleChanged += (_, _) => UpdateTicking();
        surface.Loaded += (_, _) => UpdateTicking();
        surface.Unloaded += (_, _) => StopTicking();
    }

    /// <summary>The camera shared with the other views. Replace it to switch the navigated camera.</summary>
    public NavigationCamera Camera { get; set; }

    /// <summary>Raised after every change of the camera so the owner can redraw.</summary>
    public event Action? Changed;

    /// <summary>A left click that did not turn into a drag, at the pointer position in the surface; used to select.</summary>
    public event Action<Point>? Clicked;

    /// <summary>A right click that did not turn into a look-around, at the pointer position; used to open the context menu.</summary>
    public event Action<Point>? ContextRequested;

    /// <summary>A left double-click on the surface.</summary>
    public event Action<Point>? DoubleClicked;

    /// <summary>Lets the host claim a left drag that began over the current selection.</summary>
    public Func<Point, bool>? BeginElementDrag { get; set; }

    public event Action<Point>? ElementDragMoved;
    public event Action<Point>? ElementDragCompleted;
    public event Action? ElementDragCancelled;
    public event Action<Point>? PointerMoved;
    public Func<bool>? EditingActive { get; set; }
    private bool _elementDragging;

    private Point _pressPoint;
    private bool _dragged;
    private bool _orbiting;

    /// <summary>Scene bounds, used for movement speed and for framing.</summary>
    public Func<(Vector3 Min, Vector3 Max)> Bounds { get; set; } = () => (new Vector3(-5, -5, 0), new Vector3(5, 5, 3));

    /// <summary>Resolves the selection or whole-project pivot once when an orbit gesture starts.</summary>
    public Func<Vector3>? OrbitPivot { get; set; }

    /// <summary>Distance along a ray to the nearest surface, or null; drives cursor-centered zoom.</summary>
    public Func<Vector3, Vector3, float?>? Pick { get; set; }

    /// <summary>Stops the camera at walls when set; null or <see cref="CollisionEnabled"/> false lets it pass through.</summary>
    public CameraCollision? Collision { get; set; }

    public bool CollisionEnabled { get; set; }

    /// <summary>Whether keys currently belong to navigation (the pointer is over the surface and no text box has focus).</summary>
    public bool IsActive => Window.GetWindow(_surface)?.IsActive == true && _surface.IsVisible && (_surface.IsMouseOver || _surface.IsKeyboardFocusWithin) &&
                            Keyboard.FocusedElement is not TextBox && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.Selector;

    /// <summary>Whether a key press is a navigation key that the window must not treat as a command shortcut.</summary>
    public bool OwnsKey(Key key) => IsActive && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && key is Key.W or Key.A or Key.S or Key.D or Key.Q or Key.E or Key.Up or Key.Down
        or Key.Left or Key.Right or Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or Key.Space;

    /// <summary>Scales navigation speed from project bounds while keeping empty projects usable.</summary>
    private float SceneRadius()
    {
        var (min, max) = Bounds();
        return MathF.Max(3, (max - min).Length() / 2);
    }

    /// <summary>Resolves a positive surface depth, falling back to project scale over empty background.</summary>
    private float PickDistance(Vector3 direction) =>
        Pick?.Invoke(Camera.Position, direction) is { } distance && distance > 0.01f ? distance : SceneRadius();

    /// <summary>Returns nonzero surface dimensions for projection during loading and compact layouts.</summary>
    private (float Width, float Height) Size() => ((float)Math.Max(1, _surface.ActualWidth), (float)Math.Max(1, _surface.ActualHeight));

    // ---------------------------------------------------------------- mouse

    /// <summary>Reserves the requested pointer gesture and captures its starting camera or selection position.</summary>
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _surface.Focus();
        if (IsLocked && e.ChangedButton != MouseButton.Left) return;
        _history.Record(Camera);
        if (_dragButton is not null) return;
        if (e.ChangedButton == MouseButton.Middle && e.ClickCount == 2 && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            // A middle double-click brings a lost user back to the model; it does not start a pan.
            CenterAll();
            e.Handled = true;
            return;
        }
        _dragButton = e.ChangedButton;
        _last = _pressPoint = e.GetPosition(_surface);
        _dragged = false;
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2) DoubleClicked?.Invoke(_pressPoint);
        _orbiting = false;
        if (e.ChangedButton == MouseButton.Middle) UpdateOrbitMode(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        if (e.ChangedButton is MouseButton.Right or MouseButton.Middle or MouseButton.Left)
        {
            _surface.CaptureMouse();
            _surface.Cursor = e.ChangedButton switch
            {
                MouseButton.Middle => _orbiting ? Cursors.Hand : Cursors.SizeAll,
                MouseButton.Right => Cursors.Cross,
                _ => Cursors.Arrow
            };
        }
        e.Handled = true;
    }

    /// <summary>Routes selected-element drags separately from pan, orbit, and look gestures.</summary>
    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        e.Handled = ProcessPointerMove(e.GetPosition(_surface), Keyboard.Modifiers);
    }

    /// <summary>Releases pointer capture and commits the completed gesture exactly once.</summary>
    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragButton != e.ChangedButton) return;
        var elementDragging = _elementDragging;
        _elementDragging = false;
        _dragButton = null;
        _history.Record(Camera);
        _surface.ReleaseMouseCapture();
        _surface.Cursor = Cursors.Arrow;
        e.Handled = true;
        if (elementDragging)
        {
            ElementDragCompleted?.Invoke(e.GetPosition(_surface));
            return;
        }
        if (e.ChangedButton == MouseButton.Left && !_dragged && e.ClickCount < 2) Clicked?.Invoke(e.GetPosition(_surface));
        // A right button released without a drag is a click, not a look-around, so it asks for the context menu.
        if (e.ChangedButton == MouseButton.Right && !_dragged) ContextRequested?.Invoke(e.GetPosition(_surface));
    }

    /// <summary>Cancels a claimed model drag when another control or window steals mouse capture.</summary>
    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        _dragButton = null;
        _surface.Cursor = Cursors.Arrow;
        if (!_elementDragging) return;
        _elementDragging = false;
        ElementDragCancelled?.Invoke();
    }

    /// <summary>Dollies toward the pointer's picked surface while retaining minimum camera clearance.</summary>
    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = ProcessWheel(e.GetPosition(_surface), e.Delta, Keyboard.Modifiers);
    }

    // ---------------------------------------------------------------- keyboard flight

    /// <summary>Runs flight updates only while the navigation surface participates in the visible window.</summary>
    private void UpdateTicking()
    {
        if (_surface.IsVisible && _surface.IsLoaded) StartTicking();
        else StopTicking();
    }

    /// <summary>Attaches one rendering callback and resets elapsed-time tracking.</summary>
    private void StartTicking()
    {
        if (_ticking) return;
        _ticking = true;
        _lastTick = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    /// <summary>Releases the rendering subscription when its surface is hidden or unloaded.</summary>
    private void StopTicking()
    {
        EndFlight();
        if (!_ticking) return;
        _ticking = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    /// <summary>Applies elapsed-time keyboard flight unless an editor, shortcut, or locked camera owns input.</summary>
    private void OnFrame(object? sender, EventArgs e)
    {
        var now = (e as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;
        var dt = _lastTick == TimeSpan.Zero ? 0 : Math.Min(0.1, (now - _lastTick).TotalSeconds);
        _lastTick = now;
        if (dt <= 0 || IsLocked || !IsActive || EditingActive?.Invoke() == true || Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            EndFlight();
            return;
        }

        /// <summary>Samples one navigation key as a normalized movement contribution.</summary>
        float Key(Key key) => Keyboard.IsKeyDown(key) ? 1 : 0;
        var forward = Key(System.Windows.Input.Key.W) + Key(System.Windows.Input.Key.Up) - Key(System.Windows.Input.Key.S) - Key(System.Windows.Input.Key.Down);
        var right = Key(System.Windows.Input.Key.D) + Key(System.Windows.Input.Key.Right) - Key(System.Windows.Input.Key.A) - Key(System.Windows.Input.Key.Left);
        if (LumionControls)
        {
            // Q rises and E descends, as in Lumion; Space slows, Shift speeds up and both together fly very fast.
            var rise = Key(System.Windows.Input.Key.Q) - Key(System.Windows.Input.Key.E);
            var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            var space = Keyboard.IsKeyDown(System.Windows.Input.Key.Space);
            ApplyFlightScaled(new Vector3(forward, right, rise), (float)dt, shift, space,
                shift && space ? 12f : shift ? 4f : space ? 0.25f : 1f);
            return;
        }
        var up = Key(System.Windows.Input.Key.E) - Key(System.Windows.Input.Key.Q);
        ApplyFlight(new Vector3(forward, right, up), (float)dt,
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
    }

    /// <summary>Applies a displacement, stopping at surfaces when collisions are on.</summary>
    private void MoveCamera(Vector3 delta)
    {
        Camera.Position = CollisionEnabled && Collision is { } collision
            ? collision.Move(Camera.Position, delta)
            : Camera.Position + delta;
    }

    /// <summary>Places the camera so the whole model is in view, from the usual three-quarter angle.</summary>
    public void FrameAll()
    {
        if (IsLocked) return;
        _history.Record(Camera);
        var (min, max) = Bounds();
        var framed = NavigationCamera.Frame(min, max);
        Camera.Position = framed.Position;
        Camera.YawDegrees = framed.YawDegrees;
        Camera.PitchDegrees = framed.PitchDegrees;
        Camera.FieldOfViewDegrees = framed.FieldOfViewDegrees;
        var (width, height) = Size();
        Camera.FrameBounds(min, max, width / height);
        NotifyChanged();
    }

    /// <summary>Looks at the model from the side the given direction points to, keeping the whole model in view.</summary>
    public void LookFrom(Vector3 direction)
    {
        if (IsLocked) return;
        _history.Record(Camera);
        var (min, max) = Bounds();
        var center = (min + max) / 2;
        var radius = MathF.Max(3, (max - min).Length() / 2);
        var distance = radius / MathF.Tan(Camera.FieldOfViewDegrees * MathF.PI / 360) * 1.1f;
        Camera.Position = center + Vector3.Normalize(direction) * distance;
        Camera.LookAt(center);
        var (width, height) = Size();
        Camera.FrameBounds(min, max, width / height);
        NotifyChanged();
    }

    /// <summary>Switches between pan and orbit while freezing the pivot for each continuous orbit gesture.</summary>
    private void UpdateOrbitMode(bool orbiting)
    {
        if (_orbiting == orbiting) return;
        _orbiting = orbiting;
        if (orbiting) _orbitPivot = ResolveOrbitPivot();
        _surface.Cursor = orbiting ? Cursors.Hand : Cursors.SizeAll;
    }

    /// <summary>Uses the host's project selection policy, with scene framing bounds as the standalone fallback.</summary>
    private Vector3 ResolveOrbitPivot()
    {
        if (OrbitPivot is { } resolve) return resolve();
        var (min, max) = Bounds();
        return (min + max) / 2;
    }

    /// <summary>Orbits around the selection or project center without resetting framing or the field of view.</summary>
    public void OrbitBy(float horizontal, float vertical)
    {
        if (IsLocked) return;
        if (!_externalGesture) _history.Record(Camera);
        Camera.OrbitAround(ResolveOrbitPivot(), horizontal, vertical);
        NotifyChanged();
    }

    /// <summary>Moves the camera toward (positive) or away from (negative) the model by a step scaled to its size.</summary>
    public void ZoomBy(float sign)
    {
        if (IsLocked) return;
        _history.Record(Camera);
        MoveCamera(Camera.Forward * SceneRadius() * 0.2f * Math.Sign(sign));
        NotifyChanged();
    }

    /// <summary>Recenters the view on the whole model, for when the user has lost it on screen.</summary>
    public void CenterAll()
    {
        if (IsLocked) return;
        _history.Record(Camera);
        var (min, max) = Bounds();
        var (width, height) = Size();
        Camera.FrameBounds(min, max, width / height);
        NotifyChanged();
    }
}
