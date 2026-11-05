using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>Shows editable measurements for the selected element while no placement operation is active.</summary>
    private void RenderTemporaryDimensions()
    {
        _dimensionEditorDrawn = false;
        if (PlanToolArmed()) return;
        var selected = _selectedOpeningId ?? _selectedColumnId ?? _selectedStructuralId ??
            _selectedRoomId ?? _selectedFloorId ?? _selectedWallId;
        if (selected is not Guid id) return;
        foreach (var dimension in PlanDimensions.ForSelection(_session.Document, id))
        {
            if (_session.Document.Dimensions.Any(item => item.OwnerId == dimension.OwnerId &&
                item.Kind == dimension.Kind && item.ReferenceId == dimension.ReferenceId)) continue;
            DrawPlanDimension(dimension, false);
        }
    }

    /// <summary>Recomputes saved annotation geometry instead of displaying stale text after a model edit.</summary>
    private void RenderPermanentDimensions()
    {
        foreach (var dimension in _session.Document.Dimensions.Where(item => item.LevelId == _activeLevelId))
            DrawPlanDimension(dimension, true);
        // A selection change or tool that hides the edited dimension ends the edit instead of reviving it later.
        if (_dimensionEditorDrawn) return;
        _editingDimension = null;
        _dimensionEditText = null;
    }

    /// <summary>Draws the numeric editor target and a separate promotion arrow that consumes its own click.</summary>
    private void DrawPlanDimension(PlanDimension dimension, bool permanent)
    {
        if (PlanDimensions.Resolve(_session.Document, dimension) is not { } measurement) return;
        var start = WorldToCanvas(measurement.From);
        var end = WorldToCanvas(measurement.To);
        var brush = permanent ? Brushes.DarkSlateGray : Brushes.RoyalBlue;
        PlanCanvas.Children.Add(new Line { X1 = start.X, Y1 = start.Y, X2 = end.X, Y2 = end.Y,
            Stroke = brush, StrokeThickness = 1, IsHitTestVisible = false });
        AddDimensionTick(start, brush);
        AddDimensionTick(end, brush);
        if (IsEditing(dimension))
        {
            _dimensionEditorDrawn = true;
            var editor = new TextBox
            {
                Text = _dimensionEditText ?? (measurement.IsAngle ? measurement.Value.ToString("0.0", CultureInfo.CurrentCulture) : Units.LengthNumber(measurement.Value)),
                Tag = dimension, FontSize = 11, MinWidth = 56, Padding = new Thickness(2, 0, 2, 0),
                BorderBrush = brush, BorderThickness = new Thickness(1.5), ToolTip = GetText("Dimension.EnterMeters")
            };
            editor.KeyDown += DimensionEditor_KeyDown;
            editor.TextChanged += (_, _) => _dimensionEditText = editor.Text;
            editor.LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                // A re-render detaches this editor and draws a fresh one, so only a still-attached editor
                // losing focus means the user clicked away from the edit.
                if (editor.Parent is not null && !editor.IsKeyboardFocusWithin) CancelDimensionEdit();
            });
            Canvas.SetLeft(editor, (start.X + end.X) / 2 + 4);
            Canvas.SetTop(editor, (start.Y + end.Y) / 2 - 25);
            PlanCanvas.Children.Add(editor);
            editor.Loaded += (_, _) =>
            {
                Keyboard.Focus(editor);
                if (_dimensionEditText is null) editor.SelectAll();
                else editor.CaretIndex = editor.Text.Length;
            };
        }
        else
        {
            var label = new Button
            {
                Content = measurement.IsAngle ? FormatText("Dimension.Degrees", measurement.Value) : Units.FormatLength(measurement.Value), Tag = dimension,
                Foreground = brush, Background = Brushes.White, FontSize = 11,
                Padding = new Thickness(3, 1, 3, 1), MinWidth = 0, MinHeight = 0,
                ToolTip = GetText("Dimension.Edit"), Cursor = Cursors.Hand
            };
            label.Click += DimensionValue_Click;
            Canvas.SetLeft(label, (start.X + end.X) / 2 + 4);
            Canvas.SetTop(label, (start.Y + end.Y) / 2 - 25);
            PlanCanvas.Children.Add(label);
        }
        var action = new Button
        {
            Content = GetText(permanent ? "Dimension.RemoveGlyph" : "Dimension.PromoteGlyph"),
            Tag = dimension, Padding = new Thickness(3, 0, 3, 0), MinWidth = 18, MinHeight = 18,
            ToolTip = GetText(permanent ? "Dimension.Remove" : "Dimension.Promote"), Cursor = Cursors.Hand
        };
        action.Click += DimensionAction_Click;
        Canvas.SetLeft(action, (start.X + end.X) / 2 - 19);
        Canvas.SetTop(action, (start.Y + end.Y) / 2 - 25);
        PlanCanvas.Children.Add(action);
    }

    /// <summary>The measurement whose number is being typed in place, if any, and its unsent text.</summary>
    private PlanDimension? _editingDimension;
    private string? _dimensionEditText;
    private bool _dimensionEditorDrawn;

    /// <summary>Matches by semantic reference so a temporary dimension survives being rebuilt on every render.</summary>
    private bool IsEditing(PlanDimension dimension) => _editingDimension is { } editing &&
        editing.Id == dimension.Id && editing.OwnerId == dimension.OwnerId &&
        editing.ReferenceId == dimension.ReferenceId && editing.Kind == dimension.Kind;

    /// <summary>Turns the clicked number into an in-place editor over the same dimension line.</summary>
    private void DimensionValue_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: PlanDimension dimension }) return;
        _editingDimension = dimension;
        _dimensionEditText = null;
        StatusText.Text = GetText("Dimension.EnterMeters");
        RenderPlan();
    }

    /// <summary>Enter applies the typed distance; Escape restores the number without touching the model.</summary>
    private void DimensionEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { Tag: PlanDimension dimension } editor) return;
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CancelDimensionEdit();
            return;
        }
        if (e.Key is not (Key.Enter or Key.Return)) return;
        e.Handled = true;
        _editingDimension = null;
        _dimensionEditText = null;
        ApplyDimensionText(dimension, editor.Text);
    }

    /// <summary>Leaves in-place editing and redraws the plain number.</summary>
    private void CancelDimensionEdit()
    {
        if (_editingDimension is null) return;
        _editingDimension = null;
        _dimensionEditText = null;
        Dispatcher.BeginInvoke(RenderPlan);
    }

    /// <summary>Edits a measurement through the transactional solver; invalid input leaves the model intact.</summary>
    private void ApplyDimensionText(PlanDimension dimension, string input)
    {
        // A plain number is in the project's unit; a unit after it (m, cm, mm, ft, in) says otherwise.
        if (!Units.TryParseLength(input, out var value) || value <= 0)
        {
            StatusText.Text = GetText("Dimension.Invalid");
            RenderPlan();
            return;
        }
        try
        {
            _session.EditDimension(dimension, value);
            RefreshAll();
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
            RenderPlan();
        }
    }

    /// <summary>Promotes a temporary annotation or removes a permanent one without selecting the underlying wall.</summary>
    private void DimensionAction_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: PlanDimension dimension }) return;
        if (dimension.Id == Guid.Empty) _session.AddDimension(dimension);
        else _session.RemoveDimension(dimension.Id);
        RefreshAll();
    }

    /// <summary>Adds a diagonal architectural tick in screen space.</summary>
    private void AddDimensionTick(Point center, Brush brush) => PlanCanvas.Children.Add(new Line
    {
        X1 = center.X - 4, Y1 = center.Y + 4, X2 = center.X + 4, Y2 = center.Y - 4,
        Stroke = brush, StrokeThickness = 1.5, IsHitTestVisible = false
    });
}
