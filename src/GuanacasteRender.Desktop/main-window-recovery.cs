using System.Globalization;
using System.IO;
using System.Windows;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private AutosaveScheduler? _autosave;

    // Optional answer for non-interactive WPF checks. Real users always see the question.
    private Func<string, bool>? _recoveryPromptSink;

    /// <summary>Starts protecting the project being edited with automatic recovery copies.</summary>
    private void InitializeRecovery()
    {
        _autosave = new AutosaveScheduler(Dispatcher, () => _dirty ? (_session.Document, _filePath) : null);
        CrashReporter.Watch(this);
    }

    /// <summary>Writes the recovery copy immediately. Called when something has gone wrong and the program may stop.</summary>
    internal void EmergencySave() => _autosave?.SaveNow();

    /// <summary>Tells the person an error was survived and where the details were written; the work is already copied.</summary>
    internal void ReportRecoveredError(string logDirectory)
    {
        StatusText.Text = string.Format(CultureInfo.CurrentCulture, GetText("Status.RecoveredError"), logDirectory);
    }

    /// <summary>Removes the recovery copy of the project in the window, once its content is safe or was given up on purpose.</summary>
    private void DiscardRecoveryCopy() => AutoRecovery.Discard(_session.Document.Id);

    /// <summary>
    /// Offers the newest automatic copy left by a session that did not end normally. Accepting opens it as the current
    /// project, still marked as unsaved, so closing or opening something else asks about it. Declining deletes it.
    /// </summary>
    internal void OfferRecovery()
    {
        if (AutoRecovery.Find().FirstOrDefault() is not { } copy) return;
        var question = string.Format(CultureInfo.CurrentCulture, GetText("Dialog.RecoverCopy"), copy.Name,
            copy.SavedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
        var accepted = _recoveryPromptSink is { } sink
            ? sink(question)
            : MessageBox.Show(this, question, "Guanacaste 3D", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        if (!accepted)
        {
            AutoRecovery.Discard(copy.DocumentId);
            return;
        }
        try
        {
            AttachDocument(AutoRecovery.Load(copy), null);
            _dirty = true;
            UpdateWindowTitle();
            StatusText.Text = string.Format(CultureInfo.CurrentCulture, GetText("Status.CopyRecovered"),
                copy.OriginalPath ?? GetText("Status.CopyRecovered.NoFile"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.Text.Json.JsonException or InvalidDataException)
        {
            MessageBox.Show(this, LocalizedErrors.Translate(exception.Message), GetText("Dialog.OpenFailed"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
