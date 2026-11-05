using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Guanacaste3D.Desktop.Controls;
using Guanacaste3D.Desktop.Customization;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private readonly CommandPreferenceService _commandPreferenceService = new();
    private readonly DispatcherTimer _shortcutTimer = new() { Interval = TimeSpan.FromMilliseconds(1400) };
    private IReadOnlyList<ApplicationCommandDefinition> _applicationCommands = [];
    private CommandPreferences _commandPreferences = CommandPreferenceService.CreateDefaults();
    private string _shortcutBuffer = string.Empty;

    /// <summary>Loads user preferences and builds the customizable quick access toolbar.</summary>
    private void InitializeCommandCustomization()
    {
        _applicationCommands = ApplicationCommandCatalog.GetAll();
        _commandPreferences = _commandPreferenceService.Load();
        _shortcutTimer.Tick += ShortcutTimer_Tick;
        RefreshQuickAccessToolbar();
    }

    /// <summary>Opens the command editor and persists accepted quick access and shortcut choices.</summary>
    private void CustomizeCommands_Click(object sender, RoutedEventArgs e)
    {
        var editor = new CommandCustomizationWindow(_applicationCommands, _commandPreferences)
        {
            Owner = this
        };
        if (editor.ShowDialog() != true || editor.Preferences is null) return;

        try
        {
            _commandPreferenceService.Save(editor.Preferences);
            _commandPreferences = editor.Preferences;
            RefreshQuickAccessToolbar();
            StatusText.Text = GetText("CommandCustomization.Saved");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                GetText("CommandCustomization.SaveFailed"),
                GetText("CommandCustomization.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>Recreates quick access buttons in the exact order selected by the user.</summary>
    private void RefreshQuickAccessToolbar()
    {
        QuickAccessHost.Children.Clear();
        var definitions = _applicationCommands.ToDictionary(command => command.CommandId, StringComparer.Ordinal);
        foreach (string commandId in _commandPreferences.QuickAccessCommandIds)
        {
            if (!definitions.TryGetValue(commandId, out ApplicationCommandDefinition? definition)) continue;
            QuickAccessHost.Children.Add(CreateQuickAccessButton(definition));
        }
    }

    /// <summary>Creates an accessible icon button that executes a catalog command.</summary>
    private Button CreateQuickAccessButton(ApplicationCommandDefinition definition)
    {
        string label = GetText(definition.LabelResourceKey);
        string? shortcut = _commandPreferences.Shortcuts.TryGetValue(definition.CommandId, out string? assigned)
            ? assigned
            : null;
        var button = new Button
        {
            Style = (Style)FindResource("QuickAccessButtonStyle"),
            Content = new Image
            {
                Source = CommandIcons.For(definition.CommandId, definition.IconResourceKey),
                Width = 16,
                Height = 16
            },
            ToolTip = string.IsNullOrWhiteSpace(shortcut)
                ? label
                : $"{label} · {shortcut}"
        };
        button.Click += (_, _) => ExecuteCatalogCommand(definition);
        AutomationProperties.SetName(button, label);
        CommandDemoToolTip.Attach(button, definition.CommandId, string.IsNullOrWhiteSpace(shortcut) ? label : $"{label} · {shortcut}");
        return button;
    }

    /// <summary>Executes a catalog command or reports its planned implementation state.</summary>
    private void ExecuteCatalogCommand(ApplicationCommandDefinition definition)
    {
        if (ExecuteApplicationCommand(definition.CommandId)) return;
        StatusText.Text = string.Format(
            CultureInfo.CurrentCulture,
            GetText("Status.RibbonCommandPlanned"),
            GetText(definition.WorkspaceResourceKey),
            GetText(definition.LabelResourceKey));
    }

    /// <summary>Accumulates sequential alphanumeric shortcuts while preserving text entry controls.</summary>
    private void MainWindow_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (IsNavigating3D()) return;
        if (Keyboard.FocusedElement is TextBoxBase or ComboBox ||
            string.IsNullOrWhiteSpace(e.Text) ||
            !char.IsLetterOrDigit(e.Text[0])) return;

        string character = char.ToUpperInvariant(e.Text[0]).ToString(CultureInfo.InvariantCulture);
        string candidate = _shortcutBuffer + character;
        if (!HasShortcutPrefix(candidate))
        {
            candidate = character;
            if (!HasShortcutPrefix(candidate))
            {
                ResetShortcutBuffer();
                return;
            }
        }

        _shortcutBuffer = candidate;
        e.Handled = true;
        _shortcutTimer.Stop();
        _shortcutTimer.Start();

        KeyValuePair<string, string>? match = _commandPreferences.Shortcuts
            .FirstOrDefault(pair => string.Equals(pair.Value, candidate, StringComparison.OrdinalIgnoreCase));
        if (match is { Key.Length: > 0 } exactMatch)
        {
            ApplicationCommandDefinition? command = _applicationCommands.FirstOrDefault(
                definition => definition.CommandId == exactMatch.Key);
            ResetShortcutBuffer();
            if (command is not null) ExecuteCatalogCommand(command);
            return;
        }

        StatusText.Text = string.Format(
            CultureInfo.CurrentCulture,
            GetText("CommandCustomization.PendingSequence"),
            _shortcutBuffer);
    }

    /// <summary>Cancels an incomplete keyboard sequence when Escape is pressed.</summary>
    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryHandleDirectWallLengthKey(e))
        {
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Escape || _shortcutBuffer.Length == 0) return;
        ResetShortcutBuffer();
        StatusText.Text = GetText("Status.Ready");
        e.Handled = true;
    }

    /// <summary>Clears an incomplete keyboard sequence after its input window expires.</summary>
    private void ShortcutTimer_Tick(object? sender, EventArgs e)
    {
        ResetShortcutBuffer();
        StatusText.Text = GetText("Status.Ready");
    }

    /// <summary>Checks whether any configured shortcut begins with a candidate sequence.</summary>
    private bool HasShortcutPrefix(string candidate) => _commandPreferences.Shortcuts.Values.Any(
        shortcut => shortcut.StartsWith(candidate, StringComparison.OrdinalIgnoreCase));

    /// <summary>Stops shortcut timing and removes every buffered character.</summary>
    private void ResetShortcutBuffer()
    {
        _shortcutTimer.Stop();
        _shortcutBuffer = string.Empty;
    }
}
