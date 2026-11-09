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
    private bool _creatingGrid;
    private Point2? _gridStart;
    private Point2? _gridPointer;
    private Guid? _selectedGridId;
    private Point2? _axisDragStart;
    private bool _axisDragged;

    private void InitializeGridAxisInteraction()
    {
        PlanCanvas.MouseMove += GridAxis_MouseMove;
        PlanCanvas.MouseLeftButtonUp += GridAxis_MouseUp;
        PlanCanvas.LostMouseCapture += (_, _) => { _axisDragStart = null; _axisDragged = false; };
    }

    private void GridAxis_MouseMove(object sender, MouseEventArgs e)
    {
        if (_axisDragStart is null || _selectedGridId is null || e.LeftButton != MouseButtonState.Pressed) return;
        var current = CanvasToWorld(PlanPosition(e));
        if (_axisDragStart.Value.DistanceTo(current) > 0.02) _axisDragged = true;
        if (_axisDragged) StatusText.Text = "Suelta para mover el eje y sus muros alineados.";
    }

    private void GridAxis_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_axisDragStart is not Point2 start || _selectedGridId is not Guid id) return;
        var end = CanvasToWorld(PlanPosition(e));
        var shouldMove = _axisDragged && start.DistanceTo(end) > 0.02;
        _axisDragStart = null;
        _axisDragged = false;
        PlanCanvas.ReleaseMouseCapture();
        if (!shouldMove) return;
        try
        {
            _session.MoveGridAxis(id, end.X - start.X, end.Y - start.Y);
            RefreshAll();
            StatusText.Text = "Eje y muros alineados desplazados.";
        }
        catch (InvalidDataException ex)
        {
            MessageBox.Show(this, ex.Message, "No se pudo mover el eje",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        e.Handled = true;
    }

    private void NavigateToGridAxis(Guid id)
    {
        var axis = _session.Document.Grids.FirstOrDefault(item => item.Id == id);
        if (axis is null) return;
        if (StartScreenControl.Visibility == Visibility.Visible) HideStartScreen();
        ViewTabs.SelectedIndex = 0;
        _selectedGridId = id;
        _selectedWallId = _selectedOpeningId = null;
        // Bring the existing datum into view before highlighting it, including axes outside the current viewport.
        var center = new Point2((axis.Start.X + axis.End.X) / 2, (axis.Start.Y + axis.End.Y) / 2);
        _panOffset = new Point(-center.X * _scale, center.Y * _scale);
        RefreshProperties();
        RenderPlan();
        StatusText.Text = $"Eje existente seleccionado: {axis.Name}. Puedes editarlo o eliminarlo con el botón derecho.";
    }

    private void ShowDuplicateAxis(DuplicateGridAxisNameException ex)
    {
        var response = MessageBox.Show(this,
            $"Ya existe un eje llamado '{ex.AxisName}'. No se ha realizado ningún cambio.\n\n" +
            "¿Deseas localizar y seleccionar el eje existente?",
            "Nombre de eje duplicado", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (response == MessageBoxResult.Yes) NavigateToGridAxis(ex.ExistingAxisId);
    }

    /// <summary>Starts a project datum rather than toggling the unrelated background snap grid.</summary>
    private void BeginGridTool()
    {
        SetWallTool(false);
        _selectedElectricalId = _selectedPlumbingId = null;
        _creatingGrid = true;
        ViewTabs.SelectedIndex = 0;
        PlanCanvas.Cursor = Cursors.Cross;
        Keyboard.Focus(PlanCanvas);
        StatusText.Text = GetText("Documentation.GridFirstPoint");
    }

    /// <summary>Discards provisional endpoints and selection when another tool becomes active.</summary>
    private void CancelGridTool()
    {
        _creatingGrid = false;
        _gridStart = _gridPointer = null;
        _selectedGridId = null;
        _axisDragStart = null;
        _axisDragged = false;
    }

    /// <summary>Updates the full axis preview from snapped model coordinates.</summary>
    private bool TryUpdateGridPreview(MouseEventArgs e)
    {
        if (!_creatingGrid) return false;
        _gridPointer = SnapWallPoint(CanvasToWorld(PlanPosition(e)));
        RenderPlan();
        return true;
    }

    /// <summary>Accepts two endpoints and automatically offers the next unused numeric reference.</summary>
    private bool TryHandleGridPlacement(MouseButtonEventArgs e)
    {
        if (!_creatingGrid) return false;
        e.Handled = true;
        var point = SnapWallPoint(CanvasToWorld(PlanPosition(e)));
        if (_gridStart is not Point2 start)
        {
            _gridStart = _gridPointer = point;
            StatusText.Text = GetText("Documentation.GridSecondPoint");
            return true;
        }
        if (!new SectionCut(start, point).IsValid)
        {
            StatusText.Text = GetText("Documentation.SectionTooShort");
            return true;
        }
        var number = 1;
        while (_session.Document.Grids.Any(axis => string.Equals(axis.Name.Trim(), number.ToString(), StringComparison.OrdinalIgnoreCase))) number++;
        _selectedGridId = _session.AddGridAxis(number.ToString(), start, point);
        _gridStart = _gridPointer = null;
        RefreshAll();
        StatusText.Text = GetText("Documentation.GridFirstPoint");
        return true;
    }

    /// <summary>Draws named project axes on every plan, with bubble heads independent of zoom.</summary>
    private void RenderGridAxes()
    {
        foreach (var axis in _session.Document.Grids) DrawGridAxis(axis, false);
        if (_creatingGrid && _gridStart is Point2 start && _gridPointer is Point2 end)
            DrawGridAxis(new(Guid.Empty, GetText("Documentation.GridPreview"), start, end), true);
    }

    /// <summary>Builds thin dashed datums whose context menu edits the reference without touching the model.</summary>
    private void DrawGridAxis(GridAxis axis, bool preview)
    {
        var start = WorldToCanvas(axis.Start);
        var end = WorldToCanvas(axis.End);
        var brush = preview || axis.Id == _selectedGridId ? Brushes.RoyalBlue : Brushes.SlateGray;
        var menu = preview ? null : new ContextMenu();
        if (menu is not null)
        {
            var rename = new MenuItem { Header = GetText("Documentation.RenameAxis"), Tag = axis.Id };
            rename.Click += RenameGridAxis_Click;
            var remove = new MenuItem { Header = GetText("Documentation.DeleteAxis"), Tag = axis.Id };
            remove.Click += DeleteGridAxis_Click;
            var move = new MenuItem { Header = "Mover eje", Tag = axis.Id };
            move.Click += MoveGridAxis_Click;
            menu.Items.Add(rename);
            menu.Items.Add(move);
            menu.Items.Add(new Separator());
            menu.Items.Add(remove);
        }
        PlanCanvas.Children.Add(new Line { X1 = start.X, Y1 = start.Y, X2 = end.X, Y2 = end.Y,
            Stroke = brush, StrokeThickness = 1, StrokeDashArray = [10, 3, 2, 3],
            Tag = axis.Id, ContextMenu = menu, IsHitTestVisible = !preview });
        foreach (var point in new[] { start, end })
        {
            var head = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13),
                BorderBrush = brush, BorderThickness = new Thickness(1), Background = Brushes.White,
                Tag = axis.Id, ContextMenu = menu, IsHitTestVisible = !preview,
                Child = new TextBlock { Text = axis.Name, FontSize = 11, Foreground = brush,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false } };
            Canvas.SetLeft(head, point.X - 13);
            Canvas.SetTop(head, point.Y - 13);
            PlanCanvas.Children.Add(head);
        }
    }

    /// <summary>Selects a datum independently of nearby walls; double-click edits its name.</summary>
    private bool TrySelectGridAxis(MouseButtonEventArgs e)
    {
        _selectedGridId = null;
        if ((e.OriginalSource as FrameworkElement)?.Tag is not Guid id ||
            !_session.Document.Grids.Any(axis => axis.Id == id)) return false;
        _selectedGridId = id;
        _selectedElectricalId = _selectedPlumbingId = null;
        _selectedViewId = _selectedWallId = _selectedOpeningId = _selectedColumnId = _selectedStructuralId = null;
        _selectedRoomId = _selectedFloorId = null;
        if (e.ClickCount > 1) RenameGridAxis(id);
        else
        {
            RefreshProperties();
            _axisDragStart = CanvasToWorld(PlanPosition(e));
            _axisDragged = false;
            RenderPlan();
            PlanCanvas.CaptureMouse();
        }
        e.Handled = true;
        return true;
    }

    /// <summary>Routes the datum context menu to the shared rename operation.</summary>
    private void RenameGridAxis_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: Guid id }) RenameGridAxis(id);
    }

    /// <summary>Arms the existing move tool with the datum selected for two-click placement.</summary>
    private void MoveGridAxis_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: Guid id }) return;
        _selectedGridId = id;
        BeginMoveTool();
        _selectedGridId = id;
    }

    /// <summary>Centers the plan on the conflicting datum so the user can inspect or edit it.</summary>
    private void FocusExistingGridAxis(Guid existingId, string attemptedName)
    {
        var existing = _session.Document.Grids.FirstOrDefault(axis => axis.Id == existingId);
        if (existing is null) return;
        _selectedGridId = existing.Id;
        var midpoint = new Point2((existing.Start.X + existing.End.X) / 2,
            (existing.Start.Y + existing.End.Y) / 2);
        _panOffset = new Point(-midpoint.X * _scale, midpoint.Y * _scale);
        RefreshProperties();
        RenderPlan();
        StatusText.Text = $"Ya existe el eje '{existing.Name}'. Se ha seleccionado y centrado en el plano.";
        MessageBox.Show(this, $"El nombre '{attemptedName}' ya está ocupado.\nEl eje existente quedó seleccionado y centrado para editarlo o eliminarlo.",
            "Nombre de eje duplicado", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>Validates a user-supplied reference name and preserves dimensions attached to its identity.</summary>
    private void RenameGridAxis(Guid id)
    {
        if (_session.Document.Grids.FirstOrDefault(axis => axis.Id == id) is not { } axis) return;
        var name = PromptWindow.Ask(this, GetText("Documentation.RenameAxis"), axis.Name);
        if (name is null) return;
        try { _session.UpdateGridAxis(id, name, axis.Start, axis.End); RefreshAll(); }
        catch (DuplicateGridAxisNameException exception) { ShowDuplicateAxis(exception); }
        catch (InvalidDataException exception)
        {
            MessageBox.Show(this, LocalizedErrors.Translate(exception.Message), "Nombre de eje inválido",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Deletes one datum and its annotations as an undoable operation.</summary>
    private void DeleteGridAxis_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: Guid id }) return;
        var axis = _session.Document.Grids.FirstOrDefault(item => item.Id == id);
        if (axis is null) return;
        var linked = _session.Document.Walls.Count(w => w.GridAxisId == id);
        var message = $"¿Eliminar el eje '{axis.Name}'?\n\n" +
            $"Los {linked} muros vinculados, así como sus puertas y ventanas, permanecerán intactos. " +
            "Solo se elimina el eje y se liberan sus referencias.";
        if (MessageBox.Show(this, message, "Eliminar eje", MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _session.RemoveGridAxis(id);
        _selectedGridId = null;
        RefreshAll();
    }
}
