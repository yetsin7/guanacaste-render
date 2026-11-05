using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Guanacaste3D.Agent;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Chat with an AI model that designs and calculates through the program's own tools. Every model edit goes through
/// the document session, so the user can always undo what the assistant did.
/// </summary>
internal sealed class AiAssistantWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    private readonly Func<DocumentSession> _getSession;
    private readonly Action _refresh;
    private readonly AiSettingsStore _store = new();
    private readonly StackPanel _transcript = new() { Margin = new Thickness(8) };
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox _input = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 54, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Button _send = new() { Content = UiText.Get("Ai.Send"), MinWidth = 80, Margin = new Thickness(8, 0, 0, 0), IsDefault = false };
    private readonly ComboBox _provider = new() { MinWidth = 200 };
    private readonly TextBox _model = new();
    private readonly PasswordBox _key = new();
    private readonly TextBox _baseUrl = new();
    private readonly TextBlock _keyNote = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11 };
    private readonly Expander _settingsPanel = new() { Header = UiText.Get("Ai.Provider"), Margin = new Thickness(8, 8, 8, 0) };

    private AiStoredSettings _settings;
    private AgentRunner? _runner;
    private DocumentSession? _runnerSession;
    private CancellationTokenSource? _running;

    public AiAssistantWindow(Func<DocumentSession> getSession, Action refresh)
    {
        _getSession = getSession;
        _refresh = refresh;
        _settings = _store.Load();
        Title = UiText.Get("Ai.Title");
        Width = 520;
        Height = 700;
        MinWidth = 380;
        MinHeight = 420;
        FontSize = 12;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SetResourceReference(BackgroundProperty, "AppBackgroundBrush");
        Content = BuildLayout();
        LoadProviderFields(_settings.Selected);
        _settingsPanel.IsExpanded = !_settings.Current.IsUsable;
        AppendNote(UiText.Get(_settings.Current.IsUsable ? "Ai.Welcome" : "Ai.SetupNeeded"));
        Closed += (_, _) => _running?.Cancel();
    }

    private UIElement BuildLayout()
    {
        foreach (var (kind, key) in new[] { (AiProviderKind.Anthropic, "Ai.ProviderAnthropic"),
            (AiProviderKind.OpenAi, "Ai.ProviderOpenAi"), (AiProviderKind.Local, "Ai.ProviderLocal") })
            _provider.Items.Add(new ComboBoxItem { Content = UiText.Get(key), Tag = kind });
        _provider.SelectionChanged += (_, _) =>
        {
            if (_provider.SelectedItem is ComboBoxItem { Tag: AiProviderKind kind } && kind != _loadedKind) LoadProviderFields(kind);
        };

        var form = new StackPanel { Margin = new Thickness(0, 6, 0, 4) };
        form.Children.Add(Labeled(UiText.Get("Ai.Provider"), _provider));
        form.Children.Add(Labeled(UiText.Get("Ai.Model"), _model));
        form.Children.Add(Labeled(UiText.Get("Ai.ApiKey"), _key));
        form.Children.Add(_keyNote);
        form.Children.Add(Labeled(UiText.Get("Ai.BaseUrl"), _baseUrl));
        var save = new Button { Content = UiText.Get("Ai.Save"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 6, 0, 0) };
        save.Click += (_, _) => SaveSettings();
        form.Children.Add(save);
        _settingsPanel.Content = form;

        _scroll.Content = _transcript;
        var border = new Border { BorderThickness = new Thickness(1), Margin = new Thickness(8), Child = _scroll };
        border.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");

        _send.Click += async (_, _) => await SendOrStopAsync();
        _input.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
            e.Handled = true;
            if (_running is null) await SendOrStopAsync();
        };
        _input.ToolTip = UiText.Get("Ai.InputHint");
        var reset = new Button { Content = UiText.Get("Ai.NewChat"), Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 3, 8, 3) };
        reset.Click += (_, _) =>
        {
            _runner?.Reset();
            _transcript.Children.Clear();
            AppendNote(UiText.Get("Ai.Welcome"));
        };

        var buttons = new StackPanel { Orientation = Orientation.Vertical };
        buttons.Children.Add(_send);
        var inputRow = new DockPanel { Margin = new Thickness(8, 0, 8, 8) };
        DockPanel.SetDock(buttons, Dock.Right);
        inputRow.Children.Add(buttons);
        inputRow.Children.Add(_input);

        var resetRow = new DockPanel { Margin = new Thickness(8, 0, 8, 4) };
        resetRow.Children.Add(new TextBlock { Text = UiText.Get("Ai.PriceNotice"), TextWrapping = TextWrapping.Wrap, Opacity = 0.6, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Width = 330 });
        resetRow.Children.Add(reset);

        var root = new DockPanel();
        DockPanel.SetDock(_settingsPanel, Dock.Top);
        DockPanel.SetDock(inputRow, Dock.Bottom);
        DockPanel.SetDock(resetRow, Dock.Bottom);
        root.Children.Add(_settingsPanel);
        root.Children.Add(inputRow);
        root.Children.Add(resetRow);
        root.Children.Add(border);
        return root;
    }

    private AiProviderKind _loadedKind = AiProviderKind.Anthropic;

    private static StackPanel Labeled(string label, Control control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
        panel.Children.Add(new TextBlock { Text = label, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 2) });
        panel.Children.Add(control);
        return panel;
    }

    private void LoadProviderFields(AiProviderKind kind)
    {
        _loadedKind = kind;
        var settings = _settings.Providers.TryGetValue(kind, out var stored) ? stored : AiProviderSettings.DefaultFor(kind);
        _provider.SelectedItem = _provider.Items.Cast<ComboBoxItem>().First(item => (AiProviderKind)item.Tag == kind);
        _model.Text = settings.Model;
        _baseUrl.Text = settings.BaseUrl;
        _key.Password = string.Empty;
        _keyNote.Text = string.IsNullOrEmpty(settings.ApiKey) ? string.Empty : UiText.Get("Ai.ApiKeyStored");
        _key.IsEnabled = kind != AiProviderKind.Local;
    }

    private void SaveSettings()
    {
        var kind = _loadedKind;
        var previous = _settings.Providers.TryGetValue(kind, out var stored) ? stored : AiProviderSettings.DefaultFor(kind);
        // An empty key box keeps the stored key, so reopening the window never forces the user to retype it.
        var apiKey = _key.Password.Length > 0 ? _key.Password.Trim() : previous.ApiKey;
        var updated = new AiProviderSettings(kind, _model.Text.Trim(), apiKey, _baseUrl.Text.Trim());
        var providers = new Dictionary<AiProviderKind, AiProviderSettings>(_settings.Providers) { [kind] = updated };
        _settings = new AiStoredSettings(kind, providers);
        _store.Save(_settings);
        _runner = null;
        LoadProviderFields(kind);
        _settingsPanel.IsExpanded = !updated.IsUsable;
        AppendNote(UiText.Get("Ai.Saved"));
    }

    private AgentRunner GetRunner()
    {
        var session = _getSession();
        if (_runner is not null && ReferenceEquals(session, _runnerSession)) return _runner;
        var provider = AiProviderFactory.Create(_settings.Current, Http);
        _runner = new AgentRunner(provider, new AgentContext(session), ToolRegistry.CreateDefault(), ConfirmAsync);
        _runnerSession = session;
        _runner.Event += OnAgentEvent;
        return _runner;
    }

    private Task<bool> ConfirmAsync(ToolCall call, AgentTool tool)
    {
        var text = UiText.Format("Ai.ConfirmText", tool.Name, call.Arguments.ToJsonString());
        var answer = MessageBox.Show(this, text, UiText.Get("Ai.ConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        return Task.FromResult(answer == MessageBoxResult.Yes);
    }

    private void OnAgentEvent(AgentEvent agentEvent)
    {
        switch (agentEvent.Kind)
        {
            case AgentEventKind.AssistantText:
                AppendBubble(UiText.Get("Ai.Assistant"), agentEvent.Text, "PanelBrush");
                _refresh();
                break;
            case AgentEventKind.ToolStarted:
                AppendNote($"{UiText.Get("Ai.ToolRunning")}: {agentEvent.ToolName}");
                break;
            case AgentEventKind.ToolFinished when agentEvent.IsError:
                AppendNote($"{UiText.Get("Ai.Error")} · {agentEvent.ToolName}: {agentEvent.Text}");
                break;
            case AgentEventKind.ToolDeclined:
                AppendNote($"{agentEvent.ToolName}: {UiText.Get("Ai.ToolDeclined")}");
                break;
            case AgentEventKind.Error:
                AppendNote(agentEvent.Text);
                break;
        }
    }

    private async Task SendOrStopAsync()
    {
        if (_running is not null)
        {
            _running.Cancel();
            return;
        }
        var text = _input.Text.Trim();
        if (text.Length == 0) return;
        if (!_settings.Current.IsUsable)
        {
            _settingsPanel.IsExpanded = true;
            AppendNote(UiText.Get("Ai.SetupNeeded"));
            return;
        }
        _input.Clear();
        AppendBubble(UiText.Get("Ai.You"), text, "AccentSoftBrush");
        _running = new CancellationTokenSource();
        _send.Content = UiText.Get("Ai.Stop");
        try
        {
            var runner = GetRunner();
            var answer = await runner.SendAsync(text, _running.Token);
            // The final text was already shown by the AssistantText event; nothing more to print.
            _ = answer;
        }
        catch (OperationCanceledException)
        {
            AppendNote(UiText.Get("Ai.Cancelled"));
        }
        catch (Exception ex) when (ex is AiProviderException or HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            AppendNote($"{UiText.Get("Ai.Error")}: {ex.Message}");
            _runner = null;
        }
        finally
        {
            _running.Dispose();
            _running = null;
            _send.Content = UiText.Get("Ai.Send");
            _refresh();
            _input.Focus();
        }
    }

    private void AppendBubble(string who, string text, string brushKey)
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = who, FontWeight = FontWeights.SemiBold, Opacity = 0.7, FontSize = 11 });
        body.Children.Add(new TextBox
        {
            Text = text, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0)
        });
        var bubble = new Border { Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 6), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Child = body };
        bubble.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
        bubble.SetResourceReference(Border.BackgroundProperty, brushKey);
        _transcript.Children.Add(bubble);
        _scroll.ScrollToEnd();
    }

    private void AppendNote(string text)
    {
        _transcript.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11, Margin = new Thickness(2, 0, 2, 6) });
        _scroll.ScrollToEnd();
    }
}
