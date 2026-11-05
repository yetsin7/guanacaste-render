using System.Windows.Controls;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private CheckBox EdgesCheck => ModelToolbar.EdgeSwitch;
    private CheckBox XRayCheck => ModelToolbar.ServiceSwitch;
    private CheckBox ThickLinesCheck => ModelToolbar.ThickLinesSwitch;

    /// <summary>Preserves named access to style controls for application commands and existing WPF regression tools.</summary>
    private void RegisterModelStyleControls()
    {
        RegisterName(nameof(EdgesCheck), EdgesCheck);
        RegisterName(nameof(XRayCheck), XRayCheck);
        RegisterName(nameof(ThickLinesCheck), ThickLinesCheck);
    }
}
