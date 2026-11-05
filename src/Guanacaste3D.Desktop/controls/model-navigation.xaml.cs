using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core.Navigation;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>An orientation cube with face, edge, corner, orbit, home, and fit navigation.</summary>
public partial class ModelNavigation : UserControl
{
    private const double HalfSize = 26;
    private Point? _pressPoint;
    private Point _lastPointer;
    private bool _dragged;
    private Vector3? _requestedDirection;
    private static readonly (string Key, Vector3 Normal)[] Faces =
    [
        ("ViewCube.Front", -Vector3.UnitY), ("ViewCube.Back", Vector3.UnitY),
        ("ViewCube.Left", -Vector3.UnitX), ("ViewCube.Right", Vector3.UnitX),
        ("ViewCube.Top", Vector3.UnitZ), ("ViewCube.Bottom", -Vector3.UnitZ)
    ];

    /// <summary>Creates the widget without owning or resetting its host's camera.</summary>
    public ModelNavigation()
    {
        InitializeComponent();
        NavigationBar.CommandRequested += ForwardCommand;
    }

    /// <summary>Applies the user's ViewCube visibility without disabling other navigation controls.</summary>
    public void SetCubeVisible(bool visible)
    {
        CubeCanvas.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Forwards navigation selectors through the same host command route as the cube.</summary>
    private void ForwardCommand(string command) => CommandRequested?.Invoke(command);

    public NavigationCamera? Camera { get; set; }
    public event Action<Vector3>? DirectionRequested;
    public event Action<float, float>? OrbitRequested;
    public event Action<string>? CommandRequested;
    public event Action? GestureStarted;
    public event Action? GestureCompleted;

    /// <summary>Projects visible faces and their text into the camera's current orientation.</summary>
    public void Redraw()
    {
        if (Camera is null) return;
        CubeCanvas.Children.Clear();
        DrawCompass();
        foreach (var (key, normal) in Faces)
        {
            if (Vector3.Dot(normal, Camera.Forward) >= -0.001f) continue;
            var across = Math.Abs(normal.Z) > 0.5 ? Vector3.UnitX : Vector3.Cross(Vector3.UnitZ, normal);
            if (Vector3.Dot(across, Camera.Right) < 0) across = -across;
            var up = Math.Abs(normal.Z) > 0.5 ? Vector3.Cross(normal, across) : Vector3.UnitZ;
            if (Vector3.Dot(up, Camera.Up) < 0) up = -up;
            var origin = Project(normal - across + up);
            var horizontal = Project(normal + across + up) - origin;
            var vertical = Project(normal - across - up) - origin;
            var matrix = new Matrix(horizontal.X / 70, horizontal.Y / 70, vertical.X / 70, vertical.Y / 70, origin.X, origin.Y);
            var shade = (byte)(220 + Math.Clamp(Vector3.Dot(normal, new Vector3(-0.3f, -0.4f, 0.7f)), 0, 1) * 29);
            var face = new Polygon
            {
                Points = new PointCollection { origin, origin + horizontal, origin + horizontal + vertical, origin + vertical },
                Fill = new LinearGradientBrush(Color.FromRgb((byte)(shade + 5), (byte)(shade + 5), (byte)(shade + 5)),
                    Color.FromRgb(shade, shade, shade), 90),
                Stroke = new SolidColorBrush(Color.FromRgb(153, 153, 153)), StrokeThickness = 0.65,
                Cursor = Cursors.Hand, Tag = (normal, across, up, matrix)
            };
            face.MouseEnter += FaceEntered;
            face.MouseLeave += FaceLeft;
            CubeCanvas.Children.Add(face);
            DrawBevel(normal, across, up);
            var label = new Border
            {
                Width = 70, Height = 70, IsHitTestVisible = false, RenderTransform = new MatrixTransform(matrix),
                Child = new TextBlock
                {
                    Text = (TryFindResource(key) as string)?.ToUpperInvariant(), FontSize = 11,
                    FontFamily = new FontFamily("Arial Narrow"), FontStretch = FontStretches.Condensed,
                    Foreground = new SolidColorBrush(Color.FromRgb(119, 125, 129)),
                    TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            CubeCanvas.Children.Add(label);
        }
        DrawCornerTargets();
    }

    /// <summary>Maps an orientation-space corner to the widget's orthographic projection.</summary>
    private Point Project(Vector3 corner) => new(78 + Vector3.Dot(corner, Camera!.Right) * HalfSize,
        60 - Vector3.Dot(corner, Camera.Up) * HalfSize);

    /// <summary>Highlights face regions without changing their hit classification.</summary>
    private void FaceEntered(object sender, MouseEventArgs e)
    {
        var face = (Polygon)sender;
        face.SetValue(OriginalFaceFillProperty, face.Fill);
        face.Fill = new SolidColorBrush(Color.FromRgb(191, 222, 247));
    }

    private static readonly DependencyProperty OriginalFaceFillProperty = DependencyProperty.RegisterAttached(
        "OriginalFaceFill", typeof(Brush), typeof(ModelNavigation));

    /// <summary>Restores the shaded face after hover ends.</summary>
    private void FaceLeft(object sender, MouseEventArgs e)
    {
        var face = (Polygon)sender;
        if (face.GetValue(OriginalFaceFillProperty) is Brush fill) face.Fill = fill;
    }

    /// <summary>Classifies the clicked face region into one of the 26 supported viewing directions.</summary>
    private void Cube_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _pressPoint = _lastPointer = e.GetPosition(CubeCanvas);
        GestureStarted?.Invoke();
        _dragged = false;
        _requestedDirection = null;
        if (e.OriginalSource is Rectangle { Tag: Vector3 corner }) _requestedDirection = corner;
        if (e.OriginalSource is Polygon { Tag: ValueTuple<Vector3, Vector3, Vector3, Matrix> face })
        {
            var inverse = face.Item4;
            if (inverse.HasInverse)
            {
                inverse.Invert();
                var local = inverse.Transform(_lastPointer);
                _requestedDirection = ViewCubeDirections.FromFace(face.Item1, face.Item2, face.Item3,
                    new Vector2((float)local.X / 70, (float)local.Y / 70));
            }
        }
        CubeCanvas.CaptureMouse();
        e.Handled = true;
    }

    /// <summary>Orbits only after the pointer crosses the normal click threshold.</summary>
    private void Cube_MouseMove(object sender, MouseEventArgs e)
    {
        if (_pressPoint is not Point press) return;
        var position = e.GetPosition(CubeCanvas);
        if (!_dragged && (position - press).Length <= 4) return;
        var previous = _dragged ? _lastPointer : press;
        _dragged = true;
        _lastPointer = position;
        OrbitRequested?.Invoke((float)(position.X - previous.X), (float)(position.Y - previous.Y));
        e.Handled = true;
    }

    /// <summary>Snaps a click to its selected orientation, while releasing a drag preserves its final angle.</summary>
    private void Cube_MouseUp(object sender, MouseButtonEventArgs e)
    {
        var direction = !_dragged ? _requestedDirection : null;
        _pressPoint = null;
        GestureCompleted?.Invoke();
        CubeCanvas.ReleaseMouseCapture();
        if (direction is Vector3 requested) DirectionRequested?.Invoke(requested);
        e.Handled = true;
    }

    /// <summary>Abandons an interrupted orbit without applying a pending face click.</summary>
    private void Cube_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_pressPoint is not null) GestureCompleted?.Invoke();
        _pressPoint = null;
    }

    /// <summary>Routes localized toolbar actions without coupling this control to a document window.</summary>
    private void Command_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string command }) CommandRequested?.Invoke(command);
    }
}
