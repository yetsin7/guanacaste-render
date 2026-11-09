using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>Walls copied or cut with the Modify ribbon, waiting to be pasted.</summary>
    private WallClipboard? _wallClipboard;

    /// <summary>
    /// Routes Modify and Manage commands that work on the selection or on the whole project: clipboard, properties,
    /// measuring, selection by identifier and the warnings review.
    /// </summary>
    private bool TryExecuteManageCommand(string commandId)
    {
        switch (commandId)
        {
            case "Copy": CopySelectedWall(cut: false); return true;
            case "Cut": CopySelectedWall(cut: true); return true;
            case "Paste": PasteWallClipboard(); return true;
            case "Properties": ShowSelectionProperties(); return true;
            case "Measure": BeginMeasureBetweenPoints(); return true;
            case "MeasureAlong": BeginMeasureWall(); return true;
            case "SelectById": SelectElementByTypedId(); return true;
            case "ReviewWarnings": ShowModelWarnings(); return true;
            case "AreaTag": ShowPlanCategoryTags(TagCategory.Area); return true;
            default: return false;
        }
    }

    /// <summary>
    /// Ctrl+C copies, Ctrl+X cuts and Ctrl+V pastes the selected wall, as the Modify ribbon's buttons do. Takes the
    /// modifiers as an argument so it can be exercised without a real keyboard.
    /// </summary>
    private bool TryHandleClipboardShortcut(Key key, ModifierKeys modifiers)
    {
        if (modifiers != ModifierKeys.Control) return false;
        switch (key)
        {
            case Key.C: CopySelectedWall(cut: false); return true;
            case Key.X: CopySelectedWall(cut: true); return true;
            case Key.V: PasteWallClipboard(); return true;
            default: return false;
        }
    }

    private void CopySelectedWall(bool cut)
    {
        if (_selectedWallId is not Guid id) { StatusText.Text = GetText("Status.SelectWallFirst"); return; }
        try
        {
            _wallClipboard = WallClipboard.Capture(_session.Document, [id]);
            if (cut)
            {
                using var group = _session.BeginGroup("Transaction.CutWalls");
                _session.RemoveWall(id);
                group.Assimilate();
                _selectedWallId = null;
                RefreshAll();
            }
            StatusText.Text = GetText(cut ? "Status.WallCut" : "Status.WallCopied");
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
        }
    }

    private void PasteWallClipboard()
    {
        if (_wallClipboard is not { } clipboard) { StatusText.Text = GetText("Status.NothingToPaste"); return; }
        // On the same level the copy would sit exactly on its source, so it is moved a meter to stay visible.
        var sameLevel = clipboard.Walls.All(wall => wall.LevelId == _activeLevelId);
        var shift = sameLevel ? 1.0 : 0.0;
        RunWallEdit(() => _selectedWallId = _session.PasteWalls(clipboard, _activeLevelId, shift, shift)[0], "Status.WallPasted");
    }

    private void ShowSelectionProperties()
    {
        if (CurrentSelection() is null) { StatusText.Text = GetText("Status.SelectElementFirst"); return; }
        OpenSelectionEditor();
    }

    private void BeginMeasureBetweenPoints()
    {
        Point2? first = null;
        BeginWallPick(wall =>
        {
            var point = PointerModelPoint();
            if (first is null)
            {
                first = point;
                StatusText.Text = GetText("Status.MeasureSecond");
                return false;
            }
            var distance = first.Value.DistanceTo(point);
            first = null;
            StatusText.Text = string.Format(CultureInfo.CurrentCulture, GetText("Status.MeasureResult"), distance);
            return false;
        }, "Status.MeasureFirst");
    }

    private void BeginMeasureWall() =>
        BeginWallPick(id =>
        {
            var wall = _session.Document.Walls.Single(candidate => candidate.Id == id);
            StatusText.Text = string.Format(CultureInfo.CurrentCulture, GetText("Status.MeasureWallResult"),
                wall.Length, wall.Thickness, wall.Height, wall.ElevationArea);
            return false;
        }, "Status.MeasureWallPick");

    private void SelectElementByTypedId()
    {
        var text = TextPrompt.Ask(this, GetText("Prompt.SelectById.Title"), GetText("Prompt.SelectById.Label"),
            GetText("Prompt.Accept"), GetText("Prompt.Cancel"));
        if (text is null) return;
        if (!Guid.TryParse(text.Trim(), out var id) || !SelectElement(id))
            StatusText.Text = GetText("Status.ElementNotFound");
    }

    /// <summary>Selects an element of any plan kind by identifier, switching to its level; false when none matches.</summary>
    private bool SelectElement(Guid id)
    {
        var document = _session.Document;
        var hostWallId = document.Openings.FirstOrDefault(opening => opening.Id == id)?.WallId;
        Guid? levelId = document.Walls.FirstOrDefault(wall => wall.Id == id || wall.Id == hostWallId)?.LevelId
            ?? document.Rooms.FirstOrDefault(room => room.Id == id)?.LevelId
            ?? document.Floors.FirstOrDefault(floor => floor.Id == id)?.LevelId
            ?? document.Columns.FirstOrDefault(column => column.Id == id)?.LevelId
            ?? document.StructuralElements.FirstOrDefault(element => element.Id == id)?.LevelId;
        if (levelId is not { } level) return false;

        _activeLevelId = level;
        // The element is what is selected now, so the panel describes it and not the plan view of its level.
        _selectedViewId = null;
        _selectedWallId = document.Walls.Any(wall => wall.Id == id) ? id : null;
        _selectedOpeningId = document.Openings.Any(opening => opening.Id == id) ? id : null;
        _selectedRoomId = document.Rooms.Any(room => room.Id == id) ? id : null;
        _selectedFloorId = document.Floors.Any(floor => floor.Id == id) ? id : null;
        _selectedColumnId = document.Columns.Any(column => column.Id == id) ? id : null;
        _selectedStructuralId = document.StructuralElements.Any(element => element.Id == id) ? id : null;
        _selectedWallOffset = null;
        RefreshLevelPicker();
        RefreshProperties();
        RenderPlan();
        StatusText.Text = GetText("Status.ElementSelected");
        return true;
    }

    private void ShowModelWarnings()
    {
        var warnings = ModelWarnings.Analyze(_session.Document);
        var list = new ListBox { MinWidth = 520, MinHeight = 200, MaxHeight = 420 };
        foreach (var warning in warnings)
            list.Items.Add(new ListBoxItem
            {
                Content = (warning.Severity == WarningSeverity.Warning ? "⚠ " : "ⓘ ") +
                          string.Format(CultureInfo.CurrentCulture, GetText("Warning." + warning.Code), warning.Value),
                Tag = warning
            });

        var window = new Window
        {
            Title = GetText("Dialog.Warnings") + " · " + string.Format(CultureInfo.CurrentCulture, GetText("Dialog.Warnings.Count"), warnings.Count),
            Owner = this,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var select = new Button { Content = GetText("Dialog.Warnings.Select"), Width = 110, Margin = new Thickness(0, 12, 8, 0) };
        var close = new Button { Content = GetText("Dialog.Close"), IsCancel = true, IsDefault = true, Width = 90, Margin = new Thickness(0, 12, 0, 0) };
        void SelectCurrent()
        {
            if ((list.SelectedItem as ListBoxItem)?.Tag is ModelWarning { ElementIds.Count: > 0 } picked && SelectElement(picked.ElementIds[0]))
                window.Close();
        }
        select.Click += (_, _) => SelectCurrent();
        list.MouseDoubleClick += (_, _) => SelectCurrent();
        close.Click += (_, _) => window.Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(select);
        buttons.Children.Add(close);
        var layout = new StackPanel { Margin = new Thickness(16) };
        layout.Children.Add(warnings.Count == 0
            ? new TextBlock { Text = GetText("Dialog.Warnings.None"), Margin = new Thickness(0, 8, 0, 8) }
            : list);
        layout.Children.Add(buttons);
        window.Content = layout;
        window.ShowDialog();
    }
}
