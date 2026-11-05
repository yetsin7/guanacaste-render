using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;

internal static partial class InteractionChecks
{
    /// <summary>Tiling puts the plan and the 3D view side by side, keeps tab-index commands working, and undoes cleanly.</summary>
    private static void TileViewChecks()
    {
        var tabs = (TabControl)_window.FindName("ViewTabs");
        var viewport = (Viewport3D)_window.FindName("ModelViewport");
        tabs.SelectedIndex = 0;
        Pump();

        Invoke("EnterTileViews");
        Pump();
        var planTab = (TabItem)tabs.Items[0];
        var modelTab = (TabItem)tabs.Items[1];
        Check(tabs.Visibility == Visibility.Collapsed && planTab.Content is null && modelTab.Content is null,
            "Tiling hides the tab strip and takes both views out of their tabs");
        Check(_canvas.IsVisible && viewport.IsVisible && _canvas.ActualWidth > 100 && viewport.ActualWidth > 100,
            "The plan and the 3D view are both on screen while tiled");

        // The 3D scene is built while the plan tab is the selected one, because both views are visible.
        for (var attempt = 0; attempt < 60 && viewport.Children.Count == 0; attempt++) Pump();
        Check(viewport.Children.Count > 0, "The 3D view builds its scene while tiled, even with the plan active");

        // Set G3_TILE_SHOT to a .png path to keep a picture of the real tiled window for visual review.
        if (Environment.GetEnvironmentVariable("G3_TILE_SHOT") is { Length: > 0 } shot) SaveBitmap(shot);

        tabs.SelectedIndex = 1;
        Pump();
        Check(Field<bool>("_tileViews") && tabs.SelectedIndex == 1, "Choosing the 3D pane keeps the tiling and activates it");

        tabs.SelectedIndex = 4;
        Pump();
        Check(!Field<bool>("_tileViews") && tabs.Visibility == Visibility.Visible,
            "Opening another kind of view returns to the tabs");
        Check(ReferenceEquals(planTab.Content, _canvas) && modelTab.Content is not null && _canvas.Parent is not null,
            "Each view goes back into its own tab");

        Invoke("EnterTileViews");
        Pump();
        Invoke("ExitTileViews");
        Pump();
        Check(!Field<bool>("_tileViews") && ReferenceEquals(planTab.Content, _canvas),
            "Tiling can be switched on and off repeatedly");
        tabs.SelectedIndex = 0;
        Pump();
    }
}
