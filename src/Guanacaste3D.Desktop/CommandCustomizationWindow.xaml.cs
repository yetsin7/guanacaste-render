using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Guanacaste3D.Desktop.Customization;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Lets users choose quick access commands and assign sequential keyboard shortcuts.
/// </summary>
public partial class CommandCustomizationWindow : Window
{
    private static readonly Regex ShortcutPattern = new("^[A-Z0-9]{2,4}$", RegexOptions.Compiled);
    private readonly ObservableCollection<CommandCustomizationItem> _commands;
    private readonly ICollectionView _commandView;

    /// <summary>Creates an editable command list from the complete application command catalog.</summary>
    public CommandCustomizationWindow(
        IReadOnlyList<ApplicationCommandDefinition> definitions,
        CommandPreferences preferences)
    {
        InitializeComponent();
        var quickAccessIds = preferences.QuickAccessCommandIds.ToHashSet(StringComparer.Ordinal);
        _commands = new ObservableCollection<CommandCustomizationItem>(definitions
            .Select(definition => new CommandCustomizationItem
            {
                CommandId = definition.CommandId,
                Label = ResolveText(definition.LabelResourceKey),
                Workspace = ResolveText(definition.WorkspaceResourceKey),
                IconResourceKey = definition.IconResourceKey,
                IsQuickAccess = quickAccessIds.Contains(definition.CommandId),
                Shortcut = preferences.Shortcuts.TryGetValue(definition.CommandId, out string? shortcut)
                    ? shortcut
                    : string.Empty
            })
            .OrderBy(command => command.Workspace)
            .ThenBy(command => command.Label));
        _commandView = CollectionViewSource.GetDefaultView(_commands);
        CommandGrid.ItemsSource = _commandView;
    }

    public CommandPreferences? Preferences { get; private set; }

    /// <summary>Filters commands by their localized name or workspace as the user types.</summary>
    private void SearchInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        string search = SearchInput.Text.Trim();
        _commandView.Filter = commandObject => commandObject is CommandCustomizationItem command &&
            (search.Length == 0 ||
             command.Label.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
             command.Workspace.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        _commandView.Refresh();
    }

    /// <summary>Validates assignments and returns the selected customization to the owner.</summary>
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        CommandGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        CommandGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var assignedCommands = _commands.Where(command => !string.IsNullOrWhiteSpace(command.Shortcut)).ToList();
        CommandCustomizationItem? invalidCommand = assignedCommands.FirstOrDefault(command =>
            !ShortcutPattern.IsMatch(command.Shortcut.Trim().ToUpperInvariant()));
        if (invalidCommand is not null)
        {
            ShowValidation("CommandCustomization.InvalidShortcut", invalidCommand.Label);
            return;
        }

        string? duplicateShortcut = assignedCommands
            .GroupBy(command => command.Shortcut.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateShortcut is not null)
        {
            ShowValidation("CommandCustomization.DuplicateShortcut", duplicateShortcut);
            return;
        }

        Preferences = new CommandPreferences(
            _commands.Where(command => command.IsQuickAccess).Select(command => command.CommandId).ToList(),
            assignedCommands.ToDictionary(
                command => command.CommandId,
                command => command.Shortcut.Trim().ToUpperInvariant(),
                StringComparer.OrdinalIgnoreCase));
        DialogResult = true;
    }

    /// <summary>Restores the initial quick access and shortcut assignments.</summary>
    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        CommandPreferences defaults = CommandPreferenceService.CreateDefaults();
        var quickAccessIds = defaults.QuickAccessCommandIds.ToHashSet(StringComparer.Ordinal);
        foreach (CommandCustomizationItem command in _commands)
        {
            command.IsQuickAccess = quickAccessIds.Contains(command.CommandId);
            command.Shortcut = defaults.Shortcuts.TryGetValue(command.CommandId, out string? shortcut)
                ? shortcut
                : string.Empty;
        }
        CommandGrid.Items.Refresh();
    }

    /// <summary>Closes the editor without changing saved preferences.</summary>
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>Shows a localized validation message without closing the editor.</summary>
    private void ShowValidation(string resourceKey, string value) => MessageBox.Show(
        this,
        string.Format(System.Globalization.CultureInfo.CurrentCulture, ResolveText(resourceKey), value),
        ResolveText("CommandCustomization.Title"),
        MessageBoxButton.OK,
        MessageBoxImage.Warning);

    /// <summary>Resolves localized resources for the command editor.</summary>
    private static string ResolveText(string resourceKey) =>
        Application.Current.TryFindResource(resourceKey) as string ?? resourceKey;
}
