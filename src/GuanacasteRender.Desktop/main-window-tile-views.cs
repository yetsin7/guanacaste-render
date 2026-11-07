using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private bool _tileViews;
    private Grid? _tileHost;
    private Panel? _tileParent;
    private readonly Border[] _tilePaneHeaders = new Border[2];
    private readonly Panel[] _tilePanes = new Panel[2];

    /// <summary>
    /// Whether the 3D view is on screen: it is when its tab is selected, and always while the views are tiled. Work that
    /// only matters for a visible 3D view (building the scene, the realistic preview) is gated on this, not on the tab.
    /// </summary>
    private bool Model3DVisible => _tileViews || ViewTabs.SelectedIndex == Model3DTabIndex;

    /// <summary>
    /// Shows the plan and the 3D view side by side. The two views keep their own tab items, so every command that selects
    /// a tab by index still works; the tab control is only hidden. Opening any other kind of view ends the tiling.
    /// </summary>
    private void EnterTileViews()
    {
        if (_tileViews || ViewTabs.Parent is not Panel parent) return;
        var planTab = (TabItem)ViewTabs.Items[0];
        var modelTab = (TabItem)ViewTabs.Items[Model3DTabIndex];
        if (planTab.Content is not UIElement plan || modelTab.Content is not UIElement model) return;

        planTab.Content = null;
        modelTab.Content = null;
        var host = new Grid();
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 200 });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 200 });
        var left = BuildTilePane(0, "View.Plan", plan);
        var right = BuildTilePane(1, "View.Model3D", model);
        var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        splitter.SetResourceReference(BackgroundProperty, "PanelBorderBrush");
        Grid.SetColumn(left, 0);
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(right, 2);
        host.Children.Add(left);
        host.Children.Add(splitter);
        host.Children.Add(right);

        parent.Children.Add(host);
        ViewTabs.Visibility = Visibility.Collapsed;
        _tileHost = host;
        _tileParent = parent;
        _tileViews = true;
        if (ViewTabs.SelectedIndex is not (0 or Model3DTabIndex)) ViewTabs.SelectedIndex = 0;
        HighlightTilePane();
        RefreshAll();
        StatusText.Text = GetText("Status.TileViewsOn");
    }

    /// <summary>Returns to the tabbed layout, handing each view back to its own tab.</summary>
    private void ExitTileViews()
    {
        if (!_tileViews || _tileHost is null || _tileParent is null) return;
        var planTab = (TabItem)ViewTabs.Items[0];
        var modelTab = (TabItem)ViewTabs.Items[Model3DTabIndex];
        var plan = _tilePanes[0].Children.OfType<UIElement>().Last();
        var model = _tilePanes[1].Children.OfType<UIElement>().Last();
        _tilePanes[0].Children.Remove(plan);
        _tilePanes[1].Children.Remove(model);
        _tileParent.Children.Remove(_tileHost);
        planTab.Content = plan;
        modelTab.Content = model;
        _tileHost = null;
        _tileParent = null;
        _tileViews = false;
        ViewTabs.Visibility = Visibility.Visible;
        RefreshAll();
        StatusText.Text = GetText("Status.TileViewsOff");
    }

    /// <summary>A titled pane around one view. Pressing anywhere in it makes it the active view.</summary>
    private DockPanel BuildTilePane(int slot, string titleKey, UIElement content)
    {
        var header = new Border { Padding = new Thickness(8, 3, 8, 3), BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new TextBlock { Text = GetText(titleKey), FontWeight = FontWeights.SemiBold, FontSize = 12 } };
        header.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
        var pane = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(header, Dock.Top);
        pane.Children.Add(header);
        pane.Children.Add(content);
        var tabIndex = slot == 0 ? 0 : Model3DTabIndex;
        pane.PreviewMouseDown += (_, _) =>
        {
            if (ViewTabs.SelectedIndex != tabIndex) ViewTabs.SelectedIndex = tabIndex;
            HighlightTilePane();
        };
        _tilePaneHeaders[slot] = header;
        _tilePanes[slot] = pane;
        return pane;
    }

    /// <summary>Marks the pane whose view commands act on, which is the one the tab control has selected.</summary>
    private void HighlightTilePane()
    {
        if (!_tileViews) return;
        for (var slot = 0; slot < 2; slot++)
        {
            var active = ViewTabs.SelectedIndex == (slot == 0 ? 0 : Model3DTabIndex);
            _tilePaneHeaders[slot].SetResourceReference(Border.BackgroundProperty, active ? "AccentSoftBrush" : "PanelHeaderBrush");
        }
    }
}
