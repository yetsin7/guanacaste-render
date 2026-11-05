using System.Windows;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>Routes presentation commands to the shared camera, live renderer, and project material editor.</summary>
    private bool TryExecutePresentationCommand(string commandId)
    {
        switch (commandId)
        {
            case "Materials":
                ViewTabs.SelectedIndex = RenderTabIndex;
                RenderPanelControl.FocusMaterials();
                return true;
            case "Walkthrough":
                ViewTabs.SelectedIndex = RenderTabIndex;
                RenderPanelControl.FocusWalkthrough();
                return true;
            case "Decal":
                ShowDecalWindow();
                return true;
            case "CloudRender":
                // There is no render farm: the same job runs here, picture after picture, and lands in a folder.
                ViewTabs.SelectedIndex = RenderTabIndex;
                RenderPanelControl.OpenRenderPhotosDialog(null);
                return true;
            case "RealtimeVisualization":
                ViewTabs.SelectedIndex = RenderTabIndex;
                return true;
            case "Camera":
                ViewTabs.SelectedIndex = Model3DTabIndex;
                ModelSurface.Focus();
                return true;
            case "ResetCamera":
                ViewTabs.SelectedIndex = Model3DTabIndex;
                FrameCameraToScene();
                return true;
            case "VisualStyle":
                ViewTabs.SelectedIndex = Model3DTabIndex;
                XRayCheck.IsChecked = XRayCheck.IsChecked != true;
                return true;
            default:
                return false;
        }
    }
}
