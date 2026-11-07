using System.IO;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core;
using Guanacaste3D.Desktop.Controls;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private Window? _automationWindow;
    private Window? _playerWindow;

    // Steps being recorded while the recorder is on; null otherwise. Commands played from a script are never recorded.
    private List<AutomationStep>? _recording;
    private bool _playingAutomation;

    /// <summary>Commands that a script may not run: the ones that leave the project, change the history under it or start another script.</summary>
    private static readonly HashSet<string> AutomationBlocked = ["Automation", "AutomationPlayer", "NewProject", "OpenProject", "SaveAs", "Save", "Undo", "Redo", "Exit"];

    /// <summary>Routes the visual programming buttons: the script editor with its recorder, and the player.</summary>
    private bool TryExecuteAutomationCommand(string commandId)
    {
        switch (commandId)
        {
            case "Automation": ShowAutomationWindow(); return true;
            case "AutomationPlayer": ShowPlayerWindow(); return true;
            default: return false;
        }
    }

    /// <summary>Called for every command the person runs; while the recorder is on, it becomes a step of the script.</summary>
    private void RecordAutomationStep(string commandId)
    {
        if (_recording is null || _playingAutomation || AutomationBlocked.Contains(commandId)) return;
        if (_recording.Count < AutomationScripts.MaximumSteps) _recording.Add(new AutomationStep(commandId, []));
    }

    private void RecordAutomationAnswer(string answer)
    {
        if (_recording is not { Count: > 0 } || _playingAutomation) return;
        var last = _recording[^1];
        if (last.Answers.Count < AutomationScripts.MaximumAnswers && answer.Length <= AutomationScripts.MaximumAnswerLength && !answer.Contains('|') && !answer.Contains('\n')) last.Answers.Add(answer);
    }

    private static HashSet<string> KnownCommandIds() =>
        Enum.GetValues<RibbonWorkspace>().SelectMany(RibbonCatalog.GetGroups).SelectMany(group => group.Commands).Select(command => command.CommandId).ToHashSet();

    // ------------------------------------------------------------------ editor and recorder

    private void ShowAutomationWindow()
    {
        _automationWindow?.Close();
        var window = NewManageWindow("Automation.Title", 600);
        window.Closed += (_, _) =>
        {
            _automationWindow = null;
            _recording = null;
            TextPrompt.Recorder = null;
        };
        _automationWindow = window;
        var list = new ListBox { Name = "AutoList", Height = 100, Margin = new Thickness(0, 0, 0, 6) };
        var name = new TextBox { Name = "AutoName", Margin = new Thickness(0, 2, 0, 6), Padding = new Thickness(4, 2, 4, 2) };
        var text = new TextBox { Name = "AutoText", Height = 170, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Margin = new Thickness(0, 2, 0, 6) };
        var message = new TextBlock { Name = "AutoMessage", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        void Refill()
        {
            list.Items.Clear();
            foreach (var script in _session.Document.Automations) list.Items.Add($"{script.Name} · {FormatText("Automation.Steps", script.Steps.Count)}");
        }
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= _session.Document.Automations.Count) return;
            var script = _session.Document.Automations[list.SelectedIndex];
            name.Text = script.Name;
            text.Text = AutomationScripts.ToText(script.Steps);
        };
        Button? record = null;
        void SetRecordLabel() => record!.Content = GetText(_recording is null ? "Automation.Record" : "Automation.Stop");
        var buttons = new WrapPanel();
        buttons.Children.Add(ManageButton("AutoSave", GetText("Automation.Save"), (_, _) =>
        {
            try
            {
                var steps = AutomationScripts.Parse(text.Text);
                var unknown = UnknownAutomationCommands(steps);
                if (unknown.Count > 0) { message.Text = FormatText("Automation.Unknown", string.Join(", ", unknown)); return; }
                _session.SaveAutomation(name.Text, text.Text);
                message.Text = FormatText("Automation.Saved", name.Text.Trim(), steps.Count);
                Refill();
            }
            catch (InvalidDataException exception) { message.Text = LocalizedErrors.Translate(exception.Message); }
        }));
        buttons.Children.Add(ManageButton("AutoRemove", GetText("Automation.Remove"), (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= _session.Document.Automations.Count) return;
            try
            {
                _session.RemoveAutomation(_session.Document.Automations[list.SelectedIndex].Id);
                message.Text = GetText("Automation.Removed");
                name.Text = string.Empty;
                text.Text = string.Empty;
                Refill();
            }
            catch (InvalidDataException exception) { message.Text = LocalizedErrors.Translate(exception.Message); }
        }));
        record = ManageButton("AutoRecord", GetText("Automation.Record"), (_, _) =>
        {
            if (_recording is null)
            {
                _recording = [];
                TextPrompt.Recorder = RecordAutomationAnswer;
                message.Text = GetText("Automation.Recording");
            }
            else
            {
                var steps = _recording;
                _recording = null;
                TextPrompt.Recorder = null;
                text.Text += AutomationScripts.ToText(steps);
                message.Text = FormatText("Automation.Recorded", steps.Count);
            }
            SetRecordLabel();
        });
        buttons.Children.Add(record);
        buttons.Children.Add(ManageButton("AutoClose", GetText("Phases.Close"), (_, _) => window.Close()));
        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText("Automation.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(list);
        layout.Children.Add(new TextBlock { Text = GetText("Automation.Name") });
        layout.Children.Add(name);
        layout.Children.Add(new TextBlock { Text = GetText("Automation.Steps.Label") });
        layout.Children.Add(text);
        layout.Children.Add(buttons);
        layout.Children.Add(message);
        window.Content = new ScrollViewer { Content = layout, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Refill();
        window.Show();
    }

    private static List<string> UnknownAutomationCommands(IEnumerable<AutomationStep> steps)
    {
        var known = KnownCommandIds();
        return steps.Select(step => step.CommandId).Where(id => !known.Contains(id)).Distinct().ToList();
    }

    // ------------------------------------------------------------------ player

    /// <summary>
    /// Plays a script: every command runs as if it were chosen from the ribbon, and the questions it asks are answered from the step.
    /// The whole run is one undoable step. It stops at the first command that fails, and nothing runs if the script uses a command that
    /// does not exist or one that scripts may not run. Returns the log of what happened.
    /// </summary>
    internal string RunAutomation(AutomationScript script)
    {
        var log = new List<string>();
        if (_recording is not null) return GetText("Automation.BusyRecording");
        var unknown = UnknownAutomationCommands(script.Steps);
        var blocked = script.Steps.Select(step => step.CommandId).Where(AutomationBlocked.Contains).Distinct().ToList();
        if (blocked.Count > 0) return FormatText("Automation.Blocked", string.Join(", ", blocked));
        if (unknown.Count > 0) return FormatText("Automation.Unknown", string.Join(", ", unknown));

        var previousResponder = TextPrompt.Responder;
        var answers = new Queue<string?>();
        TextPrompt.Responder = _ => answers.Count > 0 ? answers.Dequeue() : null;
        _playingAutomation = true;
        var done = 0;
        try
        {
            var mark = _session.MarkHistory();
            foreach (var step in script.Steps)
            {
                answers.Clear();
                foreach (var answer in step.Answers) answers.Enqueue(answer);
                StatusText.Text = string.Empty;
                try
                {
                    if (!ExecuteApplicationCommand(step.CommandId)) { log.Add(FormatText("Automation.StepFailed", done + 1, step.CommandId, GetText("Automation.NoHandler"))); break; }
                }
                catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or FormatException or ArgumentException)
                {
                    log.Add(FormatText("Automation.StepFailed", done + 1, step.CommandId, exception is InvalidDataException ? LocalizedErrors.Translate(exception.Message) : exception.Message));
                    break;
                }
                done++;
                log.Add(FormatText("Automation.StepDone", done, step.CommandId, StatusText.Text));
            }
            _session.CollapseHistorySince(mark, "Transaction.RunAutomation");
        }
        finally
        {
            _playingAutomation = false;
            TextPrompt.Responder = previousResponder;
        }
        RefreshAll();
        log.Add(FormatText(done == script.Steps.Count ? "Automation.Finished" : "Automation.Stopped", done, script.Steps.Count));
        StatusText.Text = log[^1];
        return string.Join("\n", log);
    }

    private void ShowPlayerWindow()
    {
        _playerWindow?.Close();
        var window = NewManageWindow("Player.Title", 580);
        window.Closed += (_, _) => _playerWindow = null;
        _playerWindow = window;
        var scripts = _session.Document.Automations.ToList();
        var list = new ListBox { Name = "PlayerList", Height = 130, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var script in scripts) list.Items.Add($"{script.Name} · {FormatText("Automation.Steps", script.Steps.Count)}");
        var log = new TextBox { Name = "PlayerLog", IsReadOnly = true, Height = 160, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 6, 0, 6) };
        if (scripts.Count == 0) log.Text = GetText("Player.None");
        var buttons = new WrapPanel();
        buttons.Children.Add(ManageButton("PlayerRun", GetText("Player.Run"), (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= scripts.Count) { log.Text = GetText("Player.Pick"); return; }
            log.Text = RunAutomation(scripts[list.SelectedIndex]);
        }));
        buttons.Children.Add(ManageButton("PlayerClose", GetText("Phases.Close"), (_, _) => window.Close()));
        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText("Player.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(list);
        layout.Children.Add(buttons);
        layout.Children.Add(log);
        window.Content = layout;
        window.Show();
    }
}
