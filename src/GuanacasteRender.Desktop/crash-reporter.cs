using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Keeps the program alive and the work safe when something goes wrong. An error on the interface thread is logged,
/// the project's recovery copy is written and the program carries on, because most such errors spoil one action, not the
/// session. An error that cannot be survived still gets its log and a last recovery copy before the process ends.
/// </summary>
internal static class CrashReporter
{
    /// <summary>Errors on the interface thread survived in one session before the program stops pretending nothing happened.</summary>
    private const int SurvivableErrors = 25;

    private static int _survived;
    private static MainWindow? _window;

    /// <summary>Where the error logs are written; redirectable for checks.</summary>
    public static string LogDirectory =>
        Environment.GetEnvironmentVariable("G3D_LOG_DIR") is { Length: > 0 } redirected
            ? redirected
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Guanacaste 3D", "Logs");

    /// <summary>Tells the reporter which window's project to save when something fails.</summary>
    public static void Watch(MainWindow window) => _window = window;

    /// <summary>Hooks the three places an error can escape from: the interface thread, any thread, and forgotten tasks.</summary>
    public static void Install(Application application)
    {
        application.DispatcherUnhandledException += OnInterfaceError;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var exception = e.ExceptionObject as Exception ?? new InvalidOperationException(e.ExceptionObject?.ToString());
            Log("fatal error", exception);
            try
            {
                Directory.CreateDirectory(LogDirectory);
                File.WriteAllText(Path.Combine(LogDirectory, "pending-crash.txt"),
                    $"Guanacaste 3D {typeof(CrashReporter).Assembly.GetName().Version}\n{DateTime.Now:O}\n{exception}");
            }
            catch (Exception) { /* The log may be unavailable during shutdown. */ }
            SaveWork();
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log("background task", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>Shows a fatal error from the preceding session once the interface is usable.</summary>
    public static void OfferPendingReport(Window owner)
    {
        var path = Path.Combine(LogDirectory, "pending-crash.txt");
        string report;
        try { if (!File.Exists(path)) return; report = File.ReadAllText(path); }
        catch (Exception) { return; }
        var dialog = new Window { Title = "Guanacaste 3D — informe de error", Owner = owner,
            Width = 700, Height = 520, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new System.Windows.Controls.DockPanel { Margin = new Thickness(16) };
        var description = new System.Windows.Controls.TextBlock { Text = "La sesión anterior terminó por un error. Revisa los detalles antes de compartirlos: pueden contener rutas o información del proyecto.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        System.Windows.Controls.DockPanel.SetDock(description, System.Windows.Controls.Dock.Top); panel.Children.Add(description);
        var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        System.Windows.Controls.DockPanel.SetDock(buttons, System.Windows.Controls.Dock.Bottom); panel.Children.Add(buttons);
        var copy = new System.Windows.Controls.Button { Content = "Copiar errores", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        var send = new System.Windows.Controls.Button { Content = "Abrir reporte en GitHub", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        var close = new System.Windows.Controls.Button { Content = "Cerrar", Padding = new Thickness(12, 6, 12, 6) };
        buttons.Children.Add(copy); buttons.Children.Add(send); buttons.Children.Add(close);
        var details = new System.Windows.Controls.TextBox { Text = report, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto };
        panel.Children.Add(details); dialog.Content = panel;
        copy.Click += (_, _) => { try { Clipboard.SetText(report); } catch (Exception ex) { MessageBox.Show(dialog, ex.Message); } };
        send.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/yetsin7/guanacaste-3d/issues/new") { UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show(dialog, ex.Message); } };
        close.Click += (_, _) => dialog.Close();
        dialog.ShowDialog();
        try { File.Delete(path); } catch (Exception) { }
    }

    private static void OnInterfaceError(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log("interface", e.Exception);
        SaveWork();
        if (Interlocked.Increment(ref _survived) > SurvivableErrors) return;
        e.Handled = true;
        _window?.ReportRecoveredError(LogDirectory);
    }

    /// <summary>Writes the project's recovery copy now, ignoring any failure: this runs while things are already failing.</summary>
    private static void SaveWork()
    {
        try { _window?.EmergencySave(); }
        catch (Exception exception) { Log("saving after an error", exception); }
    }

    /// <summary>Appends an error to the day's log. Logging never throws.</summary>
    public static void Log(string where, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var version = typeof(CrashReporter).Assembly.GetName().Version;
            File.AppendAllText(Path.Combine(LogDirectory, $"errors-{DateTime.Now:yyyyMMdd}.log"),
                $"[{DateTime.Now:O}] {where} (v{version})\n{exception}\n\n");
        }
        catch (Exception)
        {
            // Nowhere left to report to.
        }
    }
}
