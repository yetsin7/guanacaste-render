using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Designs saved schedules: choose and order fields, filter, sort and group
/// with headers and footers, merge identical rows, and show totals. Every
/// change is a session transaction; the preview is evaluated from the live
/// document, so it updates after any model edit, undo, or redo.
/// </summary>
public partial class SchedulesWindow : Window
{
    private readonly DocumentSession _session;
    private Guid? _scheduleId;

    /// <summary>Opens the schedule designer for a session.</summary>
    public SchedulesWindow(DocumentSession session)
    {
        InitializeComponent();
        _session = session;
        NewCategoryBox.ItemsSource = Enum.GetValues<ElementCategory>()
            .Select(category => new LabeledValue<ElementCategory>(Text($"ElementCategory.{category}"), category)).ToArray();
        NewCategoryBox.SelectedValue = ElementCategory.Doors;
        BuildFilterAndSortRows();
        _session.Changed += Session_Changed;
        Closed += (_, _) => _session.Changed -= Session_Changed;
        RefreshAll();
    }

    /// <summary>Resolves a localized string, falling back to its key when missing.</summary>
    private static string Text(string key) => ProjectParametersWindow.Text(key);

    /// <summary>Formats a localized pattern with culture-aware arguments.</summary>
    private static string Format(string key, params object[] values) => ProjectParametersWindow.Format(key, values);

    /// <summary>Refreshes after any committed change, including undo and redo.</summary>
    private void Session_Changed(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(RefreshAll, System.Windows.Threading.DispatcherPriority.Background);

    /// <summary>Returns the selected saved schedule, if it still exists.</summary>
    private ScheduleDefinition? Current =>
        _session.Document.Schedules.FirstOrDefault(schedule => schedule.Id == _scheduleId);

    /// <summary>Lists saved schedules and reloads the designer and preview for the selection.</summary>
    private void RefreshAll()
    {
        var items = _session.Document.Schedules
            .OrderBy(schedule => schedule.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(schedule => new LabeledValue<Guid>(
                $"{schedule.Name}\n{Text($"ElementCategory.{schedule.Category}")}", schedule.Id))
            .ToArray();
        SchedulesList.DisplayMemberPath = nameof(LabeledValue<Guid>.Label);
        SchedulesList.SelectedValuePath = nameof(LabeledValue<Guid>.Value);
        SchedulesList.ItemsSource = items;
        if (_scheduleId is Guid id && items.Any(item => item.Value == id)) SchedulesList.SelectedValue = id;
        else if (items.Length > 0) SchedulesList.SelectedIndex = 0;
        else LoadDesigner(null);
        UndoButton.IsEnabled = _session.CanUndo;
        RedoButton.IsEnabled = _session.CanRedo;
        UndoButton.ToolTip = _session.UndoName is { } undo ? Format("Parameters.UndoNamed", Text(undo)) : null;
        RedoButton.ToolTip = _session.RedoName is { } redo ? Format("Parameters.RedoNamed", Text(redo)) : null;
    }

    /// <summary>Loads the selected schedule into the designer and preview.</summary>
    private void SchedulesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SchedulesList.SelectedValue is not Guid id) return;
        _scheduleId = id;
        LoadDesigner(Current);
    }

    /// <summary>Creates a schedule for the chosen category with a unique localized name.</summary>
    private void NewSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (NewCategoryBox.SelectedValue is not ElementCategory category) return;
        var baseName = Format("Schedules.DefaultName", Text($"ElementCategory.{category}"));
        var name = baseName;
        for (var index = 2; _session.Document.Schedules.Any(schedule =>
                 string.Equals(schedule.Name, name, StringComparison.OrdinalIgnoreCase)); index++)
            name = $"{baseName} {index.ToString(CultureInfo.CurrentCulture)}";
        RunTransaction(() => _scheduleId = _session.AddSchedule(name, category));
    }

    /// <summary>Duplicates the selected schedule under a unique copy name.</summary>
    private void DuplicateSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } schedule) return;
        var name = Format("OpeningTypes.CopyName", schedule.Name);
        for (var index = 2; _session.Document.Schedules.Any(candidate =>
                 string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)); index++)
            name = Format("OpeningTypes.CopyNameIndexed", schedule.Name, index);
        RunTransaction(() => _scheduleId = _session.DuplicateSchedule(schedule.Id, name));
    }

    /// <summary>Deletes the selected schedule after confirmation.</summary>
    private void DeleteSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } schedule ||
            MessageBox.Show(this, Format("Schedules.ConfirmDelete", schedule.Name), Text("Schedules.WindowTitle"),
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        RunTransaction(() =>
        {
            _session.RemoveSchedule(schedule.Id);
            _scheduleId = null;
        });
    }

    /// <summary>Applies the designer state to the selected schedule as one transaction.</summary>
    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } schedule) return;
        if (ReadDesigner(schedule) is { } updated) RunTransaction(() => _session.UpdateSchedule(updated));
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

    /// <summary>Closes the designer; all changes are already committed transactions.</summary>
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
