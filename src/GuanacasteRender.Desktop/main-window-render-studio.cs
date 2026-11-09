using System.Windows;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>
/// View-scoped immersive Render mode. The BIM ribbon and palettes are restored by
/// UpdateRenderWorkspace when the document switches back to the three-dimensional view.
/// </summary>
public partial class MainWindow
{
    /// <summary>Opens the rendering workspace when hosted by Guanacaste Render.</summary>
    public void EnterRenderWorkspace()
    {
        WorkspaceToggleButton.Visibility = Visibility.Collapsed;
        QuickAccessHost.Visibility = Visibility.Collapsed;
        ViewTabs.SelectedIndex = RenderTabIndex;
    }

    private void RenderReturn3D_Click(object sender, RoutedEventArgs e)
    {
        // Navigation uses the same project-view selection path as the main BIM view controls.
        // This preserves shared camera, current model and any document selection.
        SelectProjectView(BuiltInProjectViews.Model3DId);
    }
}
