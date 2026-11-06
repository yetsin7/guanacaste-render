using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Desktop.Controls;

public partial class RenderPanel
{
    private IReadOnlyList<EdgeSegment> _studioSelectionEdges = [];
    private bool _showMoveGizmo;
    private bool _draggingGizmo;

    private void MoveGizmo_Click(object sender, RoutedEventArgs e)
    {
        _showMoveGizmo = !_showMoveGizmo;
        MoveGizmoButton.Background = _showMoveGizmo ? RailActive : RailInactive;
        DrawTransformGizmo();
        StatusText.Text = _showMoveGizmo ? "Selecciona un objeto y arrastra un eje. Mayús: pasos de 10 cm. Esc: cancelar arrastre." : "Herramienta de selección.";
    }

    /// <summary>Projected world axes. A drag is preview-only until release, then commits one undoable edit.</summary>
    private void DrawTransformGizmo()
    {
        if (_draggingGizmo || TransformGizmo is null) return;
        TransformGizmo.Children.Clear();
        if (!_showMoveGizmo || _source?.CanMoveSelection != true || _studioSelectionEdges.Count == 0 || _showingFinal) return;
        var points = _studioSelectionEdges.SelectMany(x => new[] { x.A, x.B }).ToArray();
        var min = points.Aggregate(Vector3.Min);
        var max = points.Aggregate(Vector3.Max);
        var center = (min + max) * .5f;
        var width = (float)ImageHost.ActualWidth;
        var height = (float)ImageHost.ActualHeight;
        var origin = _source.Camera.Project(center, width, height);
        if (origin is null) return;
        var axes = new[] { (Vector3.UnitX, "X", Colors.IndianRed), (Vector3.UnitY, "Y", Colors.LimeGreen), (Vector3.UnitZ, "Z", Colors.DodgerBlue) };
        foreach (var (axis, label, color) in axes)
        {
            if (label == "Z" && !_source.CanElevateSelection) continue;
            var unit = _source.Camera.Project(center + axis, width, height);
            if (unit is null) continue;
            var screen = new System.Windows.Vector(unit.Value.X - origin.Value.X, unit.Value.Y - origin.Value.Y);
            var pixelsPerMeter = screen.Length;
            if (pixelsPerMeter < 2) continue;
            screen.Normalize();
            var end = new Point(origin.Value.X + screen.X * 90, origin.Value.Y + screen.Y * 90);
            var brush = new SolidColorBrush(color);
            var line = new Line { X1 = origin.Value.X, Y1 = origin.Value.Y, X2 = end.X, Y2 = end.Y,
                Stroke = brush, StrokeThickness = 3, IsHitTestVisible = false };
            TransformGizmo.Children.Add(line);
            var thumb = new Thumb { Width = 28, Height = 28, Cursor = Cursors.SizeAll, ToolTip = "Mover en " + label };
            var template = new ControlTemplate(typeof(Thumb));
            var face = new FrameworkElementFactory(typeof(Border));
            face.SetValue(Border.BackgroundProperty, brush);
            face.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetValue(TextBlock.TextProperty, label);
            text.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            text.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            text.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            face.AppendChild(text); template.VisualTree = face; thumb.Template = template;
            System.Windows.Automation.AutomationProperties.SetName(thumb, "Mover en " + label);
            Canvas.SetLeft(thumb, end.X - 14); Canvas.SetTop(thumb, end.Y - 14);
            double travel = 0;
            thumb.DragStarted += (_, _) => { _draggingGizmo = true; travel = 0; };
            thumb.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { thumb.CancelDrag(); e.Handled = true; } };
            thumb.DragDelta += (_, e) =>
            {
                travel += e.HorizontalChange * screen.X + e.VerticalChange * screen.Y;
                Canvas.SetLeft(thumb, end.X - 14 + screen.X * travel);
                Canvas.SetTop(thumb, end.Y - 14 + screen.Y * travel);
                StatusText.Text = $"{label}: {travel / pixelsPerMeter:0.00} m · suelta para aplicar";
            };
            thumb.DragCompleted += (_, e) =>
            {
                _draggingGizmo = false;
                if (!e.Canceled && Math.Abs(travel) > 1)
                {
                    var delta = travel / pixelsPerMeter;
                    if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) delta = Math.Round(delta * 10) / 10;
                    StatusText.Text = _source.MoveSelection(axis.X * delta, axis.Y * delta, axis.Z * delta);
                }
                DrawTransformGizmo();
            };
            TransformGizmo.Children.Add(thumb);
        }
    }
}
