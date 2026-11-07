using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Guanacaste3D.Core;
using Microsoft.Win32;

namespace Guanacaste3D.Desktop;

/// <summary>
/// The live category schedule of the parameter manager. Rows are rebuilt from
/// the document after every transaction; edits never write to the table
/// directly, so the grid can never drift from the model.
/// </summary>
public partial class ProjectParametersWindow
{
    private const string ElementIdColumn = "__element";
    private const string ParameterPrefix = "p_";
    private CategorySchedule? _schedule;
    private bool _rebuildPending;

    /// <summary>Rebuilds the schedule when another category is chosen.</summary>
    private void CategoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) RefreshSchedule();
    }

    /// <summary>Recreates schedule columns and rows from the current document state.</summary>
    private void RefreshSchedule()
    {
        if (CategoryBox.SelectedValue is not ElementCategory category) return;
        // The document is the authority: discard any half-finished grid edit
        // instead of committing it, which would re-enter CellEditEnding.
        ScheduleGrid.CancelEdit(DataGridEditingUnit.Row);
        _schedule = CategoryScheduleService.Build(_session.Document, category);
        var table = new DataTable { Locale = CultureInfo.CurrentCulture };
        table.Columns.Add(ElementIdColumn, typeof(Guid));
        ScheduleGrid.Columns.Clear();
        foreach (var column in _schedule.Columns)
        {
            var name = column.Definition is { } definition ? ParameterPrefix + definition.Id.ToString("N") : column.Key;
            table.Columns.Add(name, typeof(string));
            ScheduleGrid.Columns.Add(new DataGridTextColumn
            {
                Header = HeaderFor(column),
                Binding = new Binding($"[{name}]") { Mode = BindingMode.TwoWay },
                IsReadOnly = !column.IsEditable,
                SortMemberPath = name,
                Width = column.IsEditable ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto,
                MinWidth = 70
            });
        }
        foreach (var row in _schedule.Rows)
        {
            var values = new List<object> { row.Element.Id };
            values.AddRange(_schedule.Columns.Select(column => (object)CategoryScheduleService.FormatCell(
                row, column, CultureInfo.CurrentCulture, Text("Parameters.Yes"), Text("Parameters.No"))));
            table.Rows.Add([.. values]);
        }
        ScheduleGrid.ItemsSource = table.DefaultView;
        if (_schedule.Rows.Count == 0) StatusText.Text = Text("Parameters.EmptyCategory");
    }

    /// <summary>Returns a localized header with its unit; type-bound parameters carry a type marker.</summary>
    private static string HeaderFor(CategoryScheduleColumn column)
    {
        var name = column.Definition?.Name ?? Text($"Parameters.Column.{column.Key}");
        var typeMarker = column.Definition?.Binding == ParameterBinding.Type ? Text("Parameters.TypeMarker") : "";
        var formulaMarker = column.Definition is { } definition && FormulaEngine.IsCalculated(definition)
            ? Text("Parameters.FormulaMarker") : "";
        return CategoryScheduleService.HeaderWithUnit(name, column.Unit) + typeMarker + formulaMarker;
    }

    /// <summary>Commits one cell edit as a validated transaction, then rebuilds the grid.</summary>
    private void ScheduleGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || e.EditingElement is not TextBox editor ||
            e.Row.Item is not DataRowView row || DefinitionFor(e.Column) is not { } definition) return;
        var elementId = (Guid)row[ElementIdColumn];
        if (TryBuildEdit(elementId, definition, editor.Text) is { } edit)
            RunTransaction(() => _session.SetParameterValues([edit]));
        ScheduleRebuild();
    }

    /// <summary>Supports Ctrl+V (fill selected cells with one value) and Delete (clear selected cells).</summary>
    private void ScheduleGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ScheduleGrid.CurrentCell.Column is not null && IsEditingCell()) return;
        string? input = null;
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && Clipboard.ContainsText())
            input = Clipboard.GetText().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        else if (e.Key == Key.Delete) input = string.Empty;
        if (input is null) return;
        e.Handled = true;
        var edits = new List<ParameterEdit>();
        foreach (var cell in ScheduleGrid.SelectedCells)
        {
            if (cell.Item is not DataRowView row || DefinitionFor(cell.Column) is not { } definition ||
                FormulaEngine.IsCalculated(definition)) continue;
            var edit = TryBuildEdit((Guid)row[ElementIdColumn], definition, input);
            if (edit is null) return;
            edits.Add(edit);
        }
        if (edits.Count == 0) return;
        RunTransaction(() => _session.SetParameterValues(edits));
    }

    /// <summary>Returns whether any grid cell currently hosts an active text editor.</summary>
    private bool IsEditingCell() =>
        Keyboard.FocusedElement is TextBox box && ScheduleGrid.IsAncestorOf(box);

    /// <summary>Parses user input into an edit, reporting a localized error when it is invalid.</summary>
    private ParameterEdit? TryBuildEdit(Guid elementId, ParameterDefinition definition, string input)
    {
        var normalized = definition.Spec == ParameterSpec.YesNo ? NormalizeYesNo(input) : input;
        if (ParameterValueText.TryParse(definition.Spec, normalized, CultureInfo.CurrentCulture,
                out var number, out var text))
            return new ParameterEdit(elementId, definition.Id, number, text);
        StatusText.Text = Format("Parameters.InvalidValue", input, Text($"ParameterSpec.{definition.Spec}"));
        return null;
    }

    /// <summary>Maps the localized yes/no labels shown in the grid back to parseable tokens.</summary>
    private static string NormalizeYesNo(string input)
    {
        var trimmed = input.Trim();
        if (string.Equals(trimmed, Text("Parameters.Yes"), StringComparison.CurrentCultureIgnoreCase)) return "1";
        if (string.Equals(trimmed, Text("Parameters.No"), StringComparison.CurrentCultureIgnoreCase)) return "0";
        return trimmed;
    }

    /// <summary>Finds the parameter definition behind a grid column, or null for built-in columns.</summary>
    private ParameterDefinition? DefinitionFor(DataGridColumn? column)
    {
        if (_schedule is null || column is null) return null;
        var index = ScheduleGrid.Columns.IndexOf(column);
        return index >= 0 && index < _schedule.Columns.Count ? _schedule.Columns[index].Definition : null;
    }

    /// <summary>Defers a rebuild until the grid has finished its own edit bookkeeping.</summary>
    private void ScheduleRebuild()
    {
        if (_rebuildPending) return;
        _rebuildPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            _rebuildPending = false;
            RefreshSchedule();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>Exports the visible category schedule as CSV.</summary>
    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_schedule is null) return;
        var dialog = new SaveFileDialog
        {
            Filter = Text("Parameters.CsvFilter"),
            FileName = Text($"ElementCategory.{_schedule.Category}"),
            DefaultExt = ".csv",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            System.IO.File.WriteAllText(dialog.FileName, CategoryScheduleService.ToCsv(_schedule),
                new System.Text.UTF8Encoding(true));
            StatusText.Text = Format("Parameters.Exported", dialog.FileName);
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
            StatusText.Text = Format("Parameters.Rejected", LocalizedErrors.Translate(exception.Message));
        }
    }
}
