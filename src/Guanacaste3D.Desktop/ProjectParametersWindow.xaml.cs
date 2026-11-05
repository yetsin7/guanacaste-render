using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>A localized label paired with the domain value it represents, for combo boxes.</summary>
public sealed record LabeledValue<T>(string Label, T Value)
{
    /// <summary>Returns the label so screen readers and UI Automation announce the visible text.</summary>
    public override string ToString() => Label;
}

/// <summary>
/// Manages project parameter definitions and edits their values in a live
/// category schedule. All edits go through the shared <see cref="DocumentSession"/>,
/// so they are validated transactions that participate in undo and redo and
/// refresh every other view of the main window.
/// </summary>
public partial class ProjectParametersWindow : Window
{
    private readonly DocumentSession _session;
    private readonly Dictionary<ElementCategory, CheckBox> _categoryChecks = [];
    private Guid? _editingDefinitionId;

    /// <summary>Opens the manager for a session, optionally focusing the category of a selected element.</summary>
    public ProjectParametersWindow(DocumentSession session, ElementCategory? initialCategory = null)
    {
        InitializeComponent();
        _session = session;
        SpecBox.ItemsSource = Enum.GetValues<ParameterSpec>()
            .Select(spec => new LabeledValue<ParameterSpec>(Text($"ParameterSpec.{spec}"), spec)).ToArray();
        BindingBox.ItemsSource = Enum.GetValues<ParameterBinding>()
            .Select(binding => new LabeledValue<ParameterBinding>(Text($"ParameterBinding.{binding}"), binding)).ToArray();
        var categories = Enum.GetValues<ElementCategory>()
            .Select(category => new LabeledValue<ElementCategory>(Text($"ElementCategory.{category}"), category)).ToArray();
        CategoryBox.ItemsSource = categories;
        foreach (var category in categories)
        {
            var box = new CheckBox { Content = category.Label, Margin = new Thickness(0, 2, 10, 2) };
            _categoryChecks[category.Value] = box;
            CategoryPanel.Children.Add(box);
        }
        CategoryBox.SelectedValue = initialCategory ?? ElementCategory.Walls;
        _session.Changed += Session_Changed;
        Closed += (_, _) => _session.Changed -= Session_Changed;
        ResetEditor();
        RefreshAll();
    }

    /// <summary>Resolves a localized string, falling back to its key when missing.</summary>
    internal static string Text(string key) =>
        Application.Current.TryFindResource(key) as string ?? key;

    /// <summary>Formats a localized pattern with culture-aware arguments.</summary>
    internal static string Format(string key, params object[] values) =>
        string.Format(CultureInfo.CurrentCulture, Text(key), values);

    /// <summary>Refreshes definitions, schedule, and history buttons after any document change.</summary>
    private void Session_Changed(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(RefreshAll, System.Windows.Threading.DispatcherPriority.Background);

    /// <summary>Rebuilds every data-dependent part of the window from the current document.</summary>
    private void RefreshAll()
    {
        RefreshDefinitions();
        RefreshSchedule();
        RefreshHistoryButtons();
    }

    /// <summary>Lists definitions with spec, binding, and categories, keeping the current selection.</summary>
    private void RefreshDefinitions()
    {
        var items = _session.Document.ParameterDefinitions
            .OrderBy(definition => definition.Group, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(definition => definition.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(definition => new LabeledValue<Guid>(DescribeDefinition(definition), definition.Id))
            .ToArray();
        DefinitionsList.DisplayMemberPath = nameof(LabeledValue<Guid>.Label);
        DefinitionsList.SelectedValuePath = nameof(LabeledValue<Guid>.Value);
        DefinitionsList.ItemsSource = items;
        if (_editingDefinitionId is Guid id && items.Any(item => item.Value == id))
            DefinitionsList.SelectedValue = id;
    }

    /// <summary>Builds the two-line list text for a definition.</summary>
    private static string DescribeDefinition(ParameterDefinition definition)
    {
        var categories = string.Join(", ", definition.Categories.Select(category => Text($"ElementCategory.{category}")));
        var group = definition.Group.Length == 0 ? string.Empty : $"[{definition.Group}] ";
        return $"{group}{definition.Name}\n{Text($"ParameterSpec.{definition.Spec}")} · " +
               $"{Text($"ParameterBinding.{definition.Binding}")} · {categories}";
    }

    /// <summary>Shows the names of the transactions that undo and redo would affect.</summary>
    private void RefreshHistoryButtons()
    {
        UndoButton.IsEnabled = _session.CanUndo;
        RedoButton.IsEnabled = _session.CanRedo;
        UndoButton.ToolTip = _session.UndoName is { } undo ? Format("Parameters.UndoNamed", Text(undo)) : null;
        RedoButton.ToolTip = _session.RedoName is { } redo ? Format("Parameters.RedoNamed", Text(redo)) : null;
    }

    /// <summary>Loads the selected definition into the editor form.</summary>
    private void DefinitionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DefinitionsList.SelectedValue is not Guid id) return;
        var definition = _session.Document.ParameterDefinitions.FirstOrDefault(candidate => candidate.Id == id);
        if (definition is null) return;
        _editingDefinitionId = id;
        NameBox.Text = definition.Name;
        SpecBox.SelectedValue = definition.Spec;
        BindingBox.SelectedValue = definition.Binding;
        GroupBox.Text = definition.Group;
        DescriptionBox.Text = definition.Description;
        FormulaBox.Text = definition.Formula;
        SpecBox.IsEnabled = BindingBox.IsEnabled = false;
        foreach (var (category, box) in _categoryChecks) box.IsChecked = definition.Categories.Contains(category);
    }

    /// <summary>Clears the editor so the next apply creates a new definition.</summary>
    private void ResetEditor()
    {
        _editingDefinitionId = null;
        DefinitionsList.SelectedItem = null;
        NameBox.Text = GroupBox.Text = DescriptionBox.Text = FormulaBox.Text = string.Empty;
        SpecBox.SelectedValue = ParameterSpec.Text;
        BindingBox.SelectedValue = ParameterBinding.Instance;
        SpecBox.IsEnabled = BindingBox.IsEnabled = true;
        var current = CategoryBox.SelectedValue as ElementCategory?;
        foreach (var (category, box) in _categoryChecks) box.IsChecked = category == current;
    }

    /// <summary>
    /// Lists the names a formula can use for the checked categories and forces
    /// instance binding, because calculated values are computed per element.
    /// </summary>
    private void FormulaBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var hasFormula = FormulaBox.Text.Trim().Length > 0;
        if (hasFormula) BindingBox.SelectedValue = ParameterBinding.Instance;
        BindingBox.IsEnabled = !hasFormula && _editingDefinitionId is null;
        var category = _categoryChecks.FirstOrDefault(pair => pair.Value.IsChecked == true).Key;
        var quantities = ScheduleQuantities.ColumnsFor(category)
            .Where(column => column.Unit.StartsWith('m')).Select(column => Text($"Formula.Name.{column.Key}"));
        var parameters = ParameterRules.ForCategory(_session.Document, category)
            .Where(definition => definition.Id != _editingDefinitionId)
            .Select(definition => definition.Name.Contains(' ') ? $"[{definition.Name}]" : definition.Name);
        FormulaHelp.Text = Format("Parameters.FormulaHelp", string.Join(", ", quantities.Concat(parameters)));
    }

    /// <summary>Starts a new definition.</summary>
    private void NewDefinition_Click(object sender, RoutedEventArgs e)
    {
        ResetEditor();
        NameBox.Focus();
    }

    /// <summary>Creates or updates a definition from the editor form as one transaction.</summary>
    private void ApplyDefinition_Click(object sender, RoutedEventArgs e)
    {
        var categories = _categoryChecks.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToArray();
        if (string.IsNullOrWhiteSpace(NameBox.Text) || categories.Length == 0)
        {
            StatusText.Text = Text("Parameters.NeedNameAndCategory");
            return;
        }
        RunTransaction(() =>
        {
            if (_editingDefinitionId is Guid id)
                _session.UpdateParameter(id, NameBox.Text, categories, GroupBox.Text, DescriptionBox.Text,
                    FormulaBox.Text);
            else
                _editingDefinitionId = _session.AddParameter(NameBox.Text, (ParameterSpec)SpecBox.SelectedValue,
                    FormulaBox.Text.Trim().Length > 0 ? ParameterBinding.Instance : (ParameterBinding)BindingBox.SelectedValue,
                    categories, GroupBox.Text, DescriptionBox.Text, FormulaBox.Text);
        });
    }

    /// <summary>Deletes the selected definition and its values after confirmation.</summary>
    private void DeleteDefinition_Click(object sender, RoutedEventArgs e)
    {
        if (_editingDefinitionId is not Guid id) return;
        var name = _session.Document.ParameterDefinitions.First(definition => definition.Id == id).Name;
        if (MessageBox.Show(this, Format("Parameters.ConfirmDelete", name), Text("Parameters.WindowTitle"),
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        RunTransaction(() => _session.RemoveParameter(id));
        ResetEditor();
    }

    /// <summary>Runs a session operation, reporting the committed transaction or the validation failure.</summary>
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
