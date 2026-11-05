using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>Field, filter, and sort/group editors of the schedule designer.</summary>
public partial class SchedulesWindow
{
    private const int FilterRowCount = 3;
    private const int SortRowCount = 2;
    private readonly List<(ComboBox Field, ComboBox Operator, TextBox Value)> _filters = [];
    private readonly List<(ComboBox Field, CheckBox Descending, CheckBox Header, CheckBox Footer)> _sorts = [];
    private List<LabeledValue<string>> _availableFields = [];

    /// <summary>Creates the fixed filter and sort rows once; their choices are reloaded per schedule.</summary>
    private void BuildFilterAndSortRows()
    {
        var operators = Enum.GetValues<ScheduleFilterOperator>()
            .Select(op => new LabeledValue<ScheduleFilterOperator>(Text($"ScheduleFilter.{op}"), op)).ToArray();
        for (var index = 0; index < FilterRowCount; index++)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            foreach (var width in new[] { 150.0, 4, 110, 4, 1 })
                row.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = width == 1 ? new GridLength(1, GridUnitType.Star) : new GridLength(width)
                });
            var field = new ComboBox { DisplayMemberPath = "Label", SelectedValuePath = "Value" };
            var op = new ComboBox { DisplayMemberPath = "Label", SelectedValuePath = "Value", ItemsSource = operators, SelectedIndex = 0 };
            var value = new TextBox();
            Grid.SetColumn(op, 2);
            Grid.SetColumn(value, 4);
            row.Children.Add(field);
            row.Children.Add(op);
            row.Children.Add(value);
            FilterRows.Children.Add(row);
            _filters.Add((field, op, value));
        }
        for (var index = 0; index < SortRowCount; index++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            var field = new ComboBox { Width = 150, DisplayMemberPath = "Label", SelectedValuePath = "Value" };
            var descending = new CheckBox { Content = Text("Schedules.Descending"), Margin = new Thickness(8, 3, 0, 0) };
            var header = new CheckBox { Content = Text("Schedules.Header"), Margin = new Thickness(8, 3, 0, 0) };
            var footer = new CheckBox { Content = Text("Schedules.Footer"), Margin = new Thickness(8, 3, 0, 0) };
            row.Children.Add(field);
            row.Children.Add(descending);
            row.Children.Add(header);
            row.Children.Add(footer);
            SortRows.Children.Add(row);
            _sorts.Add((field, descending, header, footer));
        }
    }

    /// <summary>Shows a schedule in the designer, or disables the designer when none is selected.</summary>
    private void LoadDesigner(ScheduleDefinition? schedule)
    {
        DesignerPanel.IsEnabled = schedule is not null;
        if (schedule is null)
        {
            NameBox.Text = string.Empty;
            AvailableFieldsList.ItemsSource = SelectedFieldsList.ItemsSource = null;
            PreviewGrid.Columns.Clear();
            PreviewGrid.ItemsSource = null;
            return;
        }
        NameBox.Text = schedule.Name;
        _availableFields = ScheduleDefinitionRules.AvailableFields(_session.Document, schedule.Category)
            .Select(key => new LabeledValue<string>(FieldLabel(schedule.Category, key), key)).ToList();
        SelectedFieldsList.ItemsSource = schedule.Fields
            .Select(key => _availableFields.FirstOrDefault(field => field.Value == key))
            .OfType<LabeledValue<string>>().ToList();
        RefreshAvailableList();
        var none = new LabeledValue<string>(Text("Schedules.None"), string.Empty);
        for (var index = 0; index < _filters.Count; index++)
        {
            var (field, op, value) = _filters[index];
            field.ItemsSource = _availableFields.Prepend(none).ToList();
            var filter = index < schedule.Filters.Count ? schedule.Filters[index] : null;
            field.SelectedValue = filter?.FieldKey ?? string.Empty;
            op.SelectedValue = filter?.Operator ?? ScheduleFilterOperator.Equal;
            value.Text = filter?.Value ?? string.Empty;
        }
        for (var index = 0; index < _sorts.Count; index++)
        {
            var (field, descending, header, footer) = _sorts[index];
            field.ItemsSource = _availableFields.Prepend(none).ToList();
            var sort = index < schedule.SortGroups.Count ? schedule.SortGroups[index] : null;
            field.SelectedValue = sort?.FieldKey ?? string.Empty;
            descending.IsChecked = sort?.Descending ?? false;
            header.IsChecked = sort?.GroupHeader ?? false;
            footer.IsChecked = sort?.GroupFooter ?? false;
        }
        ItemizeBox.IsChecked = schedule.Itemize;
        GrandTotalsBox.IsChecked = schedule.GrandTotals;
        RefreshPreview(schedule);
    }

    /// <summary>Builds an updated definition from the designer, or null when a filter value is invalid.</summary>
    private ScheduleDefinition? ReadDesigner(ScheduleDefinition schedule)
    {
        var fields = SelectedFields().Select(field => field.Value).ToList();
        var filters = new List<ScheduleFilter>();
        foreach (var (field, op, value) in _filters)
        {
            if (field.SelectedValue is not string key || key.Length == 0) continue;
            filters.Add(new ScheduleFilter(key, (ScheduleFilterOperator)op.SelectedValue, NormalizeNumber(value.Text.Trim())));
        }
        var sorts = _sorts.Where(sort => sort.Field.SelectedValue is string key && key.Length > 0)
            .Select(sort => new ScheduleSortGroup((string)sort.Field.SelectedValue, sort.Descending.IsChecked == true,
                sort.Header.IsChecked == true, sort.Footer.IsChecked == true)).ToList();
        if (sorts.Any(sort => !fields.Contains(sort.FieldKey)))
        {
            StatusText.Text = Text("Schedules.SortNeedsField");
            return null;
        }
        return schedule with
        {
            Name = NameBox.Text, Fields = fields, Filters = filters, SortGroups = sorts,
            Itemize = ItemizeBox.IsChecked == true, GrandTotals = GrandTotalsBox.IsChecked == true
        };
    }

    /// <summary>Stores filter numbers in invariant notation so "0,9" and "0.9" mean the same.</summary>
    private static string NormalizeNumber(string text) =>
        ParameterValueText.TryParse(ParameterSpec.Number, text, CultureInfo.CurrentCulture, out var number, out _) &&
        number is double value ? value.ToString(CultureInfo.InvariantCulture) : text;

    /// <summary>Returns the localized label of a field: built-in column text or the parameter name.</summary>
    private string FieldLabel(ElementCategory category, string key)
    {
        var definition = ParameterRules.ForCategory(_session.Document, category)
            .FirstOrDefault(candidate => candidate.Id.ToString("N") == key);
        return definition?.Name ?? Text($"Parameters.Column.{key}");
    }

    /// <summary>Returns the fields currently chosen, in order.</summary>
    private List<LabeledValue<string>> SelectedFields() =>
        (SelectedFieldsList.ItemsSource as List<LabeledValue<string>>) ?? [];

    /// <summary>Shows the fields not yet chosen.</summary>
    private void RefreshAvailableList()
    {
        var chosen = SelectedFields().Select(field => field.Value).ToHashSet();
        AvailableFieldsList.ItemsSource = _availableFields.Where(field => !chosen.Contains(field.Value)).ToList();
    }

    /// <summary>Moves the highlighted available field to the end of the chosen fields.</summary>
    private void AddField_Click(object sender, RoutedEventArgs e)
    {
        if (AvailableFieldsList.SelectedItem is not LabeledValue<string> field) return;
        SelectedFieldsList.ItemsSource = SelectedFields().Append(field).ToList();
        RefreshAvailableList();
    }

    /// <summary>Removes the highlighted chosen field.</summary>
    private void RemoveField_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedFieldsList.SelectedItem is not LabeledValue<string> field) return;
        SelectedFieldsList.ItemsSource = SelectedFields().Where(candidate => candidate != field).ToList();
        RefreshAvailableList();
    }

    /// <summary>Moves the highlighted chosen field one position up.</summary>
    private void MoveFieldUp_Click(object sender, RoutedEventArgs e) => MoveField(-1);

    /// <summary>Moves the highlighted chosen field one position down.</summary>
    private void MoveFieldDown_Click(object sender, RoutedEventArgs e) => MoveField(1);

    /// <summary>Reorders the chosen fields, keeping the moved field selected.</summary>
    private void MoveField(int delta)
    {
        var fields = SelectedFields();
        var index = SelectedFieldsList.SelectedIndex;
        var target = index + delta;
        if (index < 0 || target < 0 || target >= fields.Count) return;
        (fields[index], fields[target]) = (fields[target], fields[index]);
        SelectedFieldsList.ItemsSource = fields.ToList();
        SelectedFieldsList.SelectedIndex = target;
    }
}
