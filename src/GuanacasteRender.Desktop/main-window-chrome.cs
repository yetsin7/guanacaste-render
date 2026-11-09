using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>Opens the application file menu under whichever application entry point invoked it.</summary>
    private void FileMenuButton_Click(object sender, RoutedEventArgs e)
    {
        FileMenu.PlacementTarget = sender as UIElement ?? FileMenuButton;
        FileMenu.Placement = PlacementMode.Bottom;
        FileMenu.IsOpen = true;
    }

    /// <summary>Minimizes the main window through the native system command.</summary>
    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    /// <summary>Toggles the main window between its maximized and restored states.</summary>
    private void MaximizeWindow_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    /// <summary>Closes the main window through the native system command.</summary>
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    /// <summary>
    /// Starts a native window move when the person drags an unoccupied part of the custom title bar.
    /// Interactive child controls handle their own mouse presses, so their clicks never reach this handler.
    /// </summary>
    private void MainTitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (WindowState != WindowState.Normal || e.ChangedButton != MouseButton.Left || e.ClickCount != 1) return;
        DragMove();
        e.Handled = true;
    }

    /// <summary>
    /// Uses the unoccupied ribbon tab strip as a recovery drag surface when the title bar is outside the monitor.
    /// Tab headers keep their selection behavior, and the ribbon command area below the strip never moves the window.
    /// </summary>
    private void RibbonTabStrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (WindowState != WindowState.Normal || e.ChangedButton != MouseButton.Left || e.ClickCount != 1 ||
            sender is not TabControl tabControl || e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (!IsRibbonTabStripDragTarget(tabControl, source, e.GetPosition(tabControl))) return;

        DragMove();
        e.Handled = true;
    }

    /// <summary>
    /// Identifies only the empty header-row background as draggable, excluding tab headers and ribbon commands.
    /// This boundary is separate from mouse capture so layout tests can verify the recovery surface safely.
    /// </summary>
    private static bool IsRibbonTabStripDragTarget(TabControl tabControl, DependencyObject source, Point position)
    {
        var firstVisibleTab = tabControl.Items.OfType<TabItem>().FirstOrDefault(tab => tab.IsVisible);
        var tabStripHeight = (firstVisibleTab?.ActualHeight ?? 0) + 3;
        return tabStripHeight > 3 && position.Y >= 0 && position.Y <= tabStripHeight &&
               ItemsControl.ContainerFromElement(tabControl, source) is not TabItem;
    }

    /// <summary>
    /// Keeps the custom frame inside the monitor work area when maximized and swaps the caption glyph.
    /// </summary>
    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        bool maximized = WindowState == WindowState.Maximized;
        // A maximized chrome-less window overhangs the screen by the resize border on every side.
        WindowFrame.BorderThickness = maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(1);
        WindowFrame.BorderBrush = maximized
            ? (System.Windows.Media.Brush)FindResource("ChromeBrush")
            : (System.Windows.Media.Brush)FindResource("PanelBorderBrush");
        MaximizeButton.Content = maximized ? "" : "";
        MaximizeButton.ToolTip = GetText(maximized ? "Window.Restore" : "Window.Maximize");
    }

    /// <summary>Applies the frame for the initial window state once the native window exists.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        MainWindow_StateChanged(this, EventArgs.Empty);
    }

    /// <summary>Shows "product - project - active view" in the caption, marking unsaved changes.</summary>
    private void UpdateWindowTitle()
    {
        if (_session is null) return;
        if (StartScreenControl?.Visibility == Visibility.Visible)
        {
            Title = $"{GetText("App.Name")} - {GetText("Action.Home")}";
            return;
        }
        string view = (ViewTabs?.SelectedItem as TabItem)?.Header?.ToString() ?? string.Empty;
        string project = _session.Document.Name + (_dirty ? " *" : string.Empty);
        Title = string.IsNullOrEmpty(view)
            ? $"{GetText("App.Name")} - {project}"
            : $"{GetText("App.Name")} - {project} - {view}";
    }

    /// <summary>Shows the green contextual tab only while its tool runs, falling back to the first tab.</summary>
    private void UpdateContextualTabs()
    {
        if (WallContextTab is null) return;
        WallContextTab.Visibility = _drawingWall ? Visibility.Visible : Visibility.Collapsed;
        if (!_drawingWall && WallContextTab.IsSelected) RibbonTabControl.SelectedIndex = 0;
    }
}
