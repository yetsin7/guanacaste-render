using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using Guanacaste3D.Desktop.Controls;

namespace Guanacaste3D.Desktop;

/// <summary>
/// The strip of view tabs shows only the views that are open, as Revit does. Opening a view (from the browser, a ribbon
/// command or a tool) adds its tab; the cross on a tab, or the tab's context menu, closes it, and "close all" leaves just
/// the view being used. The current view can never be closed, so there is always something to draw in.
/// </summary>
public partial class MainWindow
{
    /// <summary>Hides every tab but the selected one and starts following the selection; called once from the constructor.</summary>
    private void InitializeViewTabs()
    {
        foreach (var tab in ViewTabs.Items.OfType<TabItem>())
        {
            if (!ReferenceEquals(tab, ViewTabs.SelectedItem)) tab.Visibility = Visibility.Collapsed;
            tab.ContextMenu = CreateViewTabMenu(tab);
            tab.PreviewMouseDown += (_, e) =>
            {
                // The event tunnels through the whole tab, so ignore clicks on the view's content (a middle click in the
                // 3D view must not close it); only a middle click on the tab header closes the view.
                var header = tab.Template?.FindName("ViewTabSurface", tab);
                if (e.ChangedButton == System.Windows.Input.MouseButton.Middle && IsInside(e.OriginalSource, header))
                {
                    CloseViewTab(tab);
                    e.Handled = true;
                }
            };
        }
        ViewTabs.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler(ViewTabCloseButton_Click));
        ViewTabs.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, ViewTabs)) return;
            if (ViewTabs.SelectedItem is TabItem selected) selected.Visibility = Visibility.Visible;
        };
    }

    /// <summary>Whether a visual is the given element or one of its descendants.</summary>
    private static bool IsInside(object? source, object? container)
    {
        if (source is not DependencyObject start || container is not DependencyObject root) return false;
        for (DependencyObject? node = start; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, root)) return true;
        }
        return false;
    }

    private ContextMenu CreateViewTabMenu(TabItem tab)
    {
        var close = new MenuItem { Header = GetText("View.Close") };
        close.Click += (_, _) => CloseViewTab(tab);
        var closeOthers = new MenuItem { Header = GetText("View.CloseAll") };
        closeOthers.Click += (_, _) => CloseInactiveViews();
        return new ContextMenu { Items = { close, closeOthers } };
    }

    /// <summary>The cross drawn on each tab by the tab template.</summary>
    private void ViewTabCloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button { Name: "CloseViewButton" } button) return;
        for (DependencyObject? node = button; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is TabItem tab)
            {
                CloseViewTab(tab);
                break;
            }
        }
        e.Handled = true;
    }

    /// <summary>Closes one tab. Closing the current one moves to the nearest open view; the last open view stays.</summary>
    private void CloseViewTab(TabItem tab)
    {
        var open = ViewTabs.Items.OfType<TabItem>().Where(item => item.Visibility == Visibility.Visible).ToList();
        if (open.Count <= 1 || !open.Contains(tab)) return;
        if (ReferenceEquals(tab, ViewTabs.SelectedItem))
        {
            var index = open.IndexOf(tab);
            ViewTabs.SelectedItem = open[index > 0 ? index - 1 : 1];
        }
        tab.Visibility = Visibility.Collapsed;
    }

    // ---------------------------------------------------------------- zoom of the elevation and section views

    private Point? _projectedPanStart;

    /// <summary>Gives the elevation and section views wheel zoom toward the cursor and middle-button panning.</summary>
    private void InitializeProjectedViewZoom()
    {
        foreach (var canvas in new[] { ElevationCanvas, SectionCanvas })
        {
            canvas.RenderTransform = new MatrixTransform();
            canvas.MouseWheel += ProjectedCanvas_MouseWheel;
            canvas.MouseDown += ProjectedCanvas_MouseDown;
            canvas.MouseMove += ProjectedCanvas_MouseMove;
            canvas.MouseUp += (_, _) =>
            {
                _projectedPanStart = null;
                Mouse.Capture(null);
            };
        }
    }

    private void ProjectedCanvas_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        var canvas = (Canvas)sender;
        var current = ((MatrixTransform)canvas.RenderTransform).Matrix;
        var factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
        factor = Math.Clamp(current.M11 * factor, 0.4, 16) / current.M11;
        var at = e.GetPosition(canvas);
        // Zoom about the point under the cursor in the canvas's own coordinates, then apply the existing transform.
        var zoom = Matrix.Identity;
        zoom.Translate(-at.X, -at.Y);
        zoom.Scale(factor, factor);
        zoom.Translate(at.X, at.Y);
        canvas.RenderTransform = new MatrixTransform(zoom * current);
        e.Handled = true;
    }

    private void ProjectedCanvas_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var canvas = (Canvas)sender;
        if (e.ChangedButton == System.Windows.Input.MouseButton.Middle)
        {
            _projectedPanStart = e.GetPosition((IInputElement)canvas.Parent);
            Mouse.Capture(canvas);
        }
        else if (e.ChangedButton == System.Windows.Input.MouseButton.Left && e.ClickCount == 2)
        {
            canvas.RenderTransform = new MatrixTransform();
        }
    }

    private void ProjectedCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_projectedPanStart is not { } start) return;
        var canvas = (Canvas)sender;
        var now = e.GetPosition((IInputElement)canvas.Parent);
        var matrix = ((MatrixTransform)canvas.RenderTransform).Matrix;
        matrix.Translate(now.X - start.X, now.Y - start.Y);
        canvas.RenderTransform = new MatrixTransform(matrix);
        _projectedPanStart = now;
    }

    /// <summary>
    /// Gives the project browser's toolbar buttons the same animated demonstration tooltip as the ribbon, using the command
    /// each one stands for.
    /// </summary>
    private void InitializeToolbarDemos()
    {
        string[] commands = ["Plan", "Schedule", "Sheet", "Levels", "Sheet"];
        var buttons = StandardSidebar.Children.OfType<DockPanel>().SelectMany(panel => panel.Children.OfType<Border>())
            .SelectMany(border => border.Child is StackPanel strip ? strip.Children.OfType<Button>() : []).ToList();
        for (var index = 0; index < buttons.Count && index < commands.Length; index++)
        {
            var title = buttons[index].ToolTip as string ?? AutomationProperties.GetName(buttons[index]);
            if (!string.IsNullOrEmpty(title)) CommandDemoToolTip.Attach(buttons[index], commands[index], title);
        }
        // Any command button a ribbon declared in markup, identified by its Tag, gets the same tooltip.
        Loaded += (_, _) => AttachMissingDemos(this);
    }

    private static void AttachMissingDemos(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is Button { Tag: string commandId } button && button.ToolTip is not System.Windows.Controls.ToolTip)
            {
                var title = button.Content as string ?? button.ToolTip as string ?? AutomationProperties.GetName(button);
                if (!string.IsNullOrEmpty(title)) CommandDemoToolTip.Attach(button, commandId, title);
            }
            AttachMissingDemos(child);
        }
    }

    /// <summary>Closes every open view except the one in use.</summary>
    private void CloseInactiveViews()
    {
        foreach (var tab in ViewTabs.Items.OfType<TabItem>().Where(item => !ReferenceEquals(item, ViewTabs.SelectedItem)))
            tab.Visibility = Visibility.Collapsed;
        StatusText.Text = GetText("Status.ViewsClosed");
    }
}
