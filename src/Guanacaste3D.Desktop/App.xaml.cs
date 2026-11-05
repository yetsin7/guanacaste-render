using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Guanacaste3D.Desktop;

public partial class App : Application
{
    private const int StartupStepCount = 4;

    /// <summary>Starts the shared desktop host in the dedicated rendering workspace.</summary>
    public static bool RenderOnly { get; set; }

    /// <summary>Shows the splash, loads the model libraries and workspace, then reveals the main window.</summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CrashReporter.Install(this);
        var projectPath = StartupDocumentResolver.FindSupportedPath(e.Args);
        if (RenderOnly) Resources["App.Name"] = Resources["RenderProduct.Name"];
        var splash = new SplashWindow();
        splash.Show();

        Core.Import.Revit.RevitBridgeRunner.RecordApplicationPath(Environment.ProcessPath);
        await ReportStepAsync(splash, 0, "Splash.Step.Model");
        // Core model and geometry code is pure computation, so it warms up off the UI thread.
        await Task.Run(StartupWarmup.LoadModelLibraries);

        await ReportStepAsync(splash, 1, "Splash.Step.Interface");
        // Started without a document, the application shows the Home screen so the person chooses what to work on.
        var window = new MainWindow(projectPath, showStartScreen: !RenderOnly);
        if (RenderOnly) window.EnterRenderWorkspace();
        MainWindow = window;

        await ReportStepAsync(splash, 2, "Splash.Step.Workspace");
        var rendered = new TaskCompletionSource();
        window.ContentRendered += (_, _) => rendered.TrySetResult();
        window.WindowState = WindowState.Maximized;
        window.Show();
        await rendered.Task;

        splash.Report(StartupStepCount, StartupStepCount, GetText("Splash.Step.Ready"));
        splash.Close();
        CrashReporter.OfferPendingReport(window);
        // A copy left by a session that ended without saving is offered before anything else is opened.
        if (projectPath is null && !RenderOnly) window.OfferRecovery();
    }

    /// <summary>Publishes a startup step and yields so the splash paints before the next blocking step.</summary>
    private async Task ReportStepAsync(SplashWindow splash, int completedSteps, string key)
    {
        splash.Report(completedSteps, StartupStepCount, GetText(key));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    /// <summary>
    /// Keeps each tooltip inside its active Guanacaste 3D window and registers it for focus-aware dismissal.
    /// </summary>
    private void ToolTip_Opened(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is ToolTip toolTip) WindowBoundToolTip.Open(toolTip);
    }

    /// <summary>Releases the window activation listener associated with a tooltip after it closes.</summary>
    private void ToolTip_Closed(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is ToolTip toolTip) WindowBoundToolTip.Close(toolTip);
    }

    /// <summary>Returns a localized application string, or the resource key when no translation exists.</summary>
    private static string GetText(string key) =>
        Current.TryFindResource(key) as string ?? key;
}
