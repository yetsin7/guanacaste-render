namespace GuanacasteRender;

/// <summary>Starts the shared rendering host as a separate desktop application.</summary>
internal static class Program
{
    /// <summary>Initializes the desktop resources and enters the rendering workspace.</summary>
    [STAThread]
    private static void Main()
    {
        using var instanceMutex = new System.Threading.Mutex(
            initiallyOwned: true, name: @"Local\GuanacasteRender", createdNew: out var isFirstInstance);
        if (!isFirstInstance) return;

        var state = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GuanacasteRender");
        if (Environment.GetEnvironmentVariable("G3D_RECOVERY_DIR") is null)
            Environment.SetEnvironmentVariable("G3D_RECOVERY_DIR", System.IO.Path.Combine(state, "recovery"));
        if (Environment.GetEnvironmentVariable("G3D_LOG_DIR") is null)
            Environment.SetEnvironmentVariable("G3D_LOG_DIR", System.IO.Path.Combine(state, "logs"));
        Guanacaste3D.Desktop.App.RenderOnly = true;
        var application = new Guanacaste3D.Desktop.App();
        application.InitializeComponent();
        try
        {
            application.Run();
        }
        finally
        {
            instanceMutex.ReleaseMutex();
        }
    }
}
