namespace GuanacasteRender;

/// <summary>Starts the shared rendering host as a separate desktop application.</summary>
internal static class Program
{
    /// <summary>Initializes the desktop resources and enters the rendering workspace.</summary>
    [STAThread]
    private static void Main()
    {
        Guanacaste3D.Desktop.App.RenderOnly = true;
        var application = new Guanacaste3D.Desktop.App();
        application.InitializeComponent();
        application.Run();
    }
}
