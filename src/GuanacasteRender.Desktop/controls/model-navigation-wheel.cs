using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>Pointer-captured radial navigation with object, building, full, and compact layouts.</summary>
public sealed class ModelNavigationWheel : Canvas
{
    private string? _activeAction;
    private Point _previous;
    public event Action<string, float, float>? GestureRequested;
    public event Action<string>? CommandRequested;
    public event Action? GestureStarted;
    public event Action? GestureCompleted;
    public string Mode { get; private set; } = "Full";

    /// <summary>Creates a self-contained overlay that never captures model-editing clicks outside its circle.</summary>
    public ModelNavigationWheel()
    {
        Visibility = Visibility.Collapsed;
        LostMouseCapture += CaptureLost;
        MouseMove += PointerMoved;
        MouseLeftButtonUp += PointerReleased;
    }

    /// <summary>Rebuilds the wheel with meaningful sectors for the selected navigation task.</summary>
    public void Configure(string mode)
    {
        Mode = mode;
        Children.Clear();
        var compact = mode.StartsWith("Small", StringComparison.Ordinal);
        Width = Height = compact ? 158 : 242;
        var actions = mode switch
        {
            "SmallObject" or "BasicObject" => new[] { "Zoom", "Orbit", "Pan", "Rewind" },
            "SmallBuilding" or "BasicBuilding" => new[] { "Forward", "Look", "UpDown", "Rewind" },
            _ => new[] { "Zoom", "Orbit", "Pan", "Rewind", "Walk", "UpDown", "Look", "Center" }
        };
        var center = Width / 2;
        var outer = center - 4;
        var inner = compact ? 22 : 34;
        for (var index = 0; index < actions.Length; index++)
        {
            var action = actions[index];
            var start = -Math.PI / 2 + index * Math.Tau / actions.Length;
            var end = start + Math.Tau / actions.Length;
            var geometry = new StreamGeometry();
            using (var drawing = geometry.Open())
            {
                drawing.BeginFigure(Polar(center, inner, start), true, true);
                drawing.LineTo(Polar(center, outer, start), true, false);
                drawing.ArcTo(Polar(center, outer, end), new Size(outer, outer), 0, false, SweepDirection.Clockwise, true, false);
                drawing.LineTo(Polar(center, inner, end), true, false);
                drawing.ArcTo(Polar(center, inner, start), new Size(inner, inner), 0, false, SweepDirection.Counterclockwise, true, false);
            }
            geometry.Freeze();
            var sector = new Path { Data = geometry, Fill = new SolidColorBrush(Color.FromArgb(235, 65, 76, 84)),
                Stroke = Brushes.White, StrokeThickness = 1, Cursor = Cursors.Hand, Tag = action,
                ToolTip = UiText.Get("Navigation.Action." + action) };
            AutomationProperties.SetName(sector, UiText.Get("Navigation.Action." + action));
            sector.MouseLeftButtonDown += SectorPressed;
            sector.MouseEnter += SectorEntered;
            sector.MouseLeave += SectorLeft;
            Children.Add(sector);
            var label = new TextBlock { Text = UiText.Get("Navigation.Action." + action), Foreground = Brushes.White,
                FontSize = compact ? 9 : 11, Width = compact ? 58 : 76, TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false };
            var location = Polar(center, (inner + outer) / 2, (start + end) / 2);
            SetLeft(label, location.X - label.Width / 2);
            SetTop(label, location.Y - 7);
            Children.Add(label);
        }
        var close = new Button { Content = "\uE711", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11,
            Width = inner * 1.6, Height = inner * 1.6, MinWidth = 0, Padding = new Thickness(0),
            ToolTip = UiText.Get("Navigation.CloseWheel") };
        AutomationProperties.SetName(close, UiText.Get("Navigation.CloseWheel"));
        close.Click += CloseClicked;
        SetLeft(close, center - close.Width / 2);
        SetTop(close, center - close.Height / 2);
        Children.Add(close);
    }

    /// <summary>Converts an annular sector's polar point to local coordinates.</summary>
    private static Point Polar(double center, double radius, double angle) =>
        new(center + radius * Math.Cos(angle), center + radius * Math.Sin(angle));

    /// <summary>Begins a camera drag, or executes center/history once for non-drag sectors.</summary>
    private void SectorPressed(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Path { Tag: string action }) return;
        if (action is "Center" or "Rewind") CommandRequested?.Invoke(action);
        else { _activeAction = action; _previous = e.GetPosition(this); GestureStarted?.Invoke(); CaptureMouse(); }
        e.Handled = true;
    }

    /// <summary>Publishes relative motion while capture allows dragging beyond the wheel.</summary>
    private void PointerMoved(object sender, MouseEventArgs e)
    {
        if (_activeAction is not { } action) return;
        var position = e.GetPosition(this);
        GestureRequested?.Invoke(action, (float)(position.X - _previous.X), (float)(position.Y - _previous.Y));
        _previous = position;
        e.Handled = true;
    }

    /// <summary>Ends the active gesture without affecting model selection.</summary>
    private void PointerReleased(object sender, MouseButtonEventArgs e)
    {
        if (_activeAction is not null) GestureCompleted?.Invoke();
        _activeAction = null;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    /// <summary>Clears interrupted camera gestures after capture moves to another window.</summary>
    private void CaptureLost(object sender, MouseEventArgs e)
    {
        if (_activeAction is not null) GestureCompleted?.Invoke();
        _activeAction = null;
    }

    /// <summary>Highlights the actual sector under the pointer.</summary>
    private void SectorEntered(object sender, MouseEventArgs e) => ((Path)sender).Fill = new SolidColorBrush(Color.FromRgb(53, 132, 201));

    /// <summary>Restores the wheel's neutral navigation surface.</summary>
    private void SectorLeft(object sender, MouseEventArgs e) => ((Path)sender).Fill = new SolidColorBrush(Color.FromArgb(235, 65, 76, 84));

    /// <summary>Dismisses the transient wheel and releases any outstanding drag capture.</summary>
    private void CloseClicked(object sender, RoutedEventArgs e) => Dismiss();

    /// <summary>Allows Escape, view changes, and deactivation to cancel the overlay consistently.</summary>
    public void Dismiss()
    {
        if (_activeAction is not null) GestureCompleted?.Invoke();
        _activeAction = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        Visibility = Visibility.Collapsed;
    }
}
