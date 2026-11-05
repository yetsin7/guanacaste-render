using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Manages levels as parametric datums: rename, change elevation, add,
/// delete when unused, and tie or release the tops of a level's walls to the
/// level above. Every action is a session transaction, so constrained walls
/// regenerate, views refresh, and undo restores the previous state.
/// </summary>
public partial class LevelsWindow : Window
{
    private readonly DocumentSession _session;
    private Guid? _editingLevelId;

    /// <summary>Opens the level manager, optionally preselecting a level.</summary>
    public LevelsWindow(DocumentSession session, Guid? initialLevelId = null)
    {
        InitializeComponent();
        _session = session;
        _editingLevelId = initialLevelId;
        _session.Changed += Session_Changed;
        Closed += (_, _) => _session.Changed -= Session_Changed;
        RefreshAll();
    }

    /// <summary>Resolves a localized string, falling back to its key when missing.</summary>
    private static string Text(string key) => ProjectParametersWindow.Text(key);

    /// <summary>Formats a localized pattern with culture-aware arguments.</summary>
    private static string Format(string key, params object[] values) => ProjectParametersWindow.Format(key, values);

    /// <summary>Formats a metric length for display in the current culture.</summary>
    private static string Meters(double value) => value.ToString("0.###", CultureInfo.CurrentCulture);

    /// <summary>Refreshes after any committed change, including undo and redo.</summary>
    private void Session_Changed(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(RefreshAll, System.Windows.Threading.DispatcherPriority.Background);

    /// <summary>Lists levels from highest to lowest with their wall and constraint counts.</summary>
    private void RefreshAll()
    {
        var document = _session.Document;
        var items = document.Levels.OrderByDescending(level => level.Elevation).Select(level =>
        {
            var walls = document.Walls.Count(wall => wall.LevelId == level.Id);
            var constrained = document.Walls.Count(wall => wall.TopLevelId == level.Id);
            return new LabeledValue<Guid>(
                $"{level.Name} · {Meters(level.Elevation)} m\n" + Format("Levels.ListDetail", walls, constrained), level.Id);
        }).ToArray();
        LevelsList.DisplayMemberPath = nameof(LabeledValue<Guid>.Label);
        LevelsList.SelectedValuePath = nameof(LabeledValue<Guid>.Value);
        LevelsList.ItemsSource = items;
        if (_editingLevelId is Guid id && items.Any(item => item.Value == id)) LevelsList.SelectedValue = id;
        else if (items.Length > 0) LevelsList.SelectedIndex = items.Length - 1;
        UndoButton.IsEnabled = _session.CanUndo;
        RedoButton.IsEnabled = _session.CanRedo;
        UndoButton.ToolTip = _session.UndoName is { } undo ? Format("Parameters.UndoNamed", Text(undo)) : null;
        RedoButton.ToolTip = _session.RedoName is { } redo ? Format("Parameters.RedoNamed", Text(redo)) : null;
    }

    /// <summary>Loads the selected level into the editor.</summary>
    private void LevelsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LevelsList.SelectedValue is not Guid id ||
            _session.Document.Levels.FirstOrDefault(level => level.Id == id) is not { } level) return;
        _editingLevelId = id;
        NameBox.Text = level.Name;
        ElevationBox.Text = Meters(level.Elevation);
        DeleteLevelButton.IsEnabled = !BaseLevels.IsProtected(level);
        DeleteLevelButton.ToolTip = BaseLevels.IsProtected(level) ? Text("Levels.ProtectedBase") : null;
        var above = LevelConstraints.LevelAbove(_session.Document, id);
        DependentsText.Text = Format("Levels.Dependents", LevelConstraints.CountDependents(_session.Document, id),
            above?.Name ?? Text("Levels.NoLevelAbove"));
    }

    /// <summary>Adds a level three meters above the highest one.</summary>
    private void Add_Click(object sender, RoutedEventArgs e) =>
        RunTransaction(() => _editingLevelId = _session.AddLevel());

    /// <summary>Applies the edited name and elevation as one transaction.</summary>
    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_editingLevelId is not Guid id || !TryReadLength(ElevationBox, out var elevation)) return;
        RunTransaction(() => _session.UpdateLevel(id, NameBox.Text, elevation));
    }

    /// <summary>Deletes the selected level after confirmation; protected levels report why.</summary>
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_editingLevelId is not Guid id) return;
        if (BaseLevels.IsProtected(_session.Document.Levels.Single(level => level.Id == id)))
        {
            StatusText.Text = Text("Levels.ProtectedBase");
            return;
        }
        var name = _session.Document.Levels.First(level => level.Id == id).Name;
        if (MessageBox.Show(this, Format("Levels.ConfirmDelete", name), Text("Levels.WindowTitle"),
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        RunTransaction(() =>
        {
            _session.RemoveLevel(id);
            _editingLevelId = null;
        });
    }

    /// <summary>Ties the tops of the selected level's walls to the level above.</summary>
    private void Attach_Click(object sender, RoutedEventArgs e)
    {
        if (_editingLevelId is not Guid id || !TryReadLength(OffsetBox, out var offset)) return;
        RunTransaction(() =>
        {
            var count = _session.AttachWallsToLevelAbove(id, offset);
            if (count == 0) throw new System.IO.InvalidDataException(Text("Levels.NothingToAttach"));
        });
    }

    /// <summary>Releases the top constraints of the selected level's walls.</summary>
    private void Detach_Click(object sender, RoutedEventArgs e)
    {
        if (_editingLevelId is not Guid id) return;
        RunTransaction(() =>
        {
            if (_session.DetachWallsFromLevelAbove(id) == 0)
                throw new System.IO.InvalidDataException(Text("Levels.NothingToDetach"));
        });
    }

    /// <summary>Parses a length field in meters; negative values are allowed for elevations and offsets.</summary>
    private bool TryReadLength(TextBox box, out double value)
    {
        value = 0;
        if (ParameterValueText.TryParse(ParameterSpec.Number, box.Text.Replace("m", "").Trim(),
                CultureInfo.CurrentCulture, out var number, out _) && number is double parsed)
        {
            value = parsed;
            return true;
        }
        StatusText.Text = Format("Parameters.InvalidValue", box.Text, Text("ParameterSpec.Length"));
        box.Focus();
        return false;
    }

    /// <summary>Runs a session operation, reporting the committed transaction or the localized failure.</summary>
    private void RunTransaction(Action operation)
    {
        try
        {
            operation();
            StatusText.Text = _session.UndoName is { } name ? Format("Parameters.Committed", Text(name)) : string.Empty;
        }
        catch (System.IO.InvalidDataException exception)
        {
            StatusText.Text = Format("Parameters.Rejected", LocalizedErrors.Translate(exception.Message));
        }
    }

    /// <summary>Undoes the most recent transaction in the shared session.</summary>
    private void Undo_Click(object sender, RoutedEventArgs e) => _session.Undo();

    /// <summary>Redoes the most recently undone transaction in the shared session.</summary>
    private void Redo_Click(object sender, RoutedEventArgs e) => _session.Redo();

    /// <summary>Closes the manager; all changes are already committed transactions.</summary>
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
