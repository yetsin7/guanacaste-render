using System.Reflection;
using System.Windows;

namespace Guanacaste3D.Desktop;

/// <summary>Shows startup progress while the model libraries and the workspace load.</summary>
public partial class SplashWindow : Window
{
    /// <summary>Creates the splash with the running application version.</summary>
    public SplashWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? string.Empty;
        VersionText.Text = string.Format(
            Application.Current.TryFindResource("Splash.Version") as string ?? "{0}", version);
    }

    /// <summary>Updates the status line and the fraction of startup work completed.</summary>
    /// <param name="completedSteps">Steps finished before the current one.</param>
    /// <param name="totalSteps">Total number of startup steps.</param>
    /// <param name="message">Localized description of the current step.</param>
    public void Report(int completedSteps, int totalSteps, string message)
    {
        StatusText.Text = message;
        // A zero-width star column collapses the fill, so keep a sliver visible from the first step.
        double fraction = Math.Clamp((double)completedSteps / totalSteps, 0.01, 1.0);
        ProgressFilled.Width = new GridLength(fraction, GridUnitType.Star);
        ProgressRemaining.Width = new GridLength(1 - fraction, GridUnitType.Star);
    }
}
