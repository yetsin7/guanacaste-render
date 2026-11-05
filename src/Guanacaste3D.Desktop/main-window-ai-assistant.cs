namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private AiAssistantWindow? _aiAssistant;

    /// <summary>Opens the AI assistant beside the main window, or brings the open one to the front.</summary>
    private void OpenAiAssistant()
    {
        if (_aiAssistant is not null)
        {
            _aiAssistant.Activate();
            return;
        }
        // The window asks for the session each time because opening or creating a project replaces it.
        _aiAssistant = new AiAssistantWindow(() => _session, RefreshAll) { Owner = this };
        _aiAssistant.Closed += (_, _) => _aiAssistant = null;
        _aiAssistant.Left = Math.Max(0, Left + Width - _aiAssistant.Width - 24);
        _aiAssistant.Top = Top + 96;
        _aiAssistant.Show();
    }
}
