using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>
/// The context menu of the 3D view and the render preview, opened by a right click that does not turn into a look-around.
/// It gathers the view commands in one place next to the selection's editor.
/// </summary>
public partial class MainWindow
{
    /// <summary>Exposes editing and navigation actions next to the element picked in a model surface.</summary>
    private void ShowViewContextMenu(FrameworkElement surface)
    {
        var menu = new ContextMenu { PlacementTarget = surface, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        menu.Items.Add(ViewMenuItem("ViewMenu.EditSelection", (_, _) => OpenSelectionEditor()));
        menu.Items.Add(ViewMenuItem("Action.Move", (_, _) => BeginMoveTool(), SelectedMovableId() is not null));
        menu.Items.Add(new Separator());
        var hasSelection = CurrentSelection() is not null;
        menu.Items.Add(ViewMenuItem("ViewMenu.HideInView", (_, _) => HideSelectionInView(), hasSelection));
        menu.Items.Add(ViewMenuItem("ViewMenu.IsolateInView", (_, _) => IsolateSelectionInView(), hasSelection));
        menu.Items.Add(ViewMenuItem("ViewMenu.ShowAllInView", (_, _) => ShowAllInView()));
        menu.Items.Add(ViewMenuItem("ViewTemplate.SaveMenu", (_, _) => SaveViewTemplateFromView()));
        var apply = new MenuItem { Header = GetText("ViewTemplate.Apply") };
        AddTemplateItems(apply, [BuiltInProjectViews.Model3DId]);
        menu.Items.Add(apply);
        menu.Items.Add(new Separator());
        menu.Items.Add(ViewMenuItem("Encuadrar elemento en 3D", (_, _) => FrameSelectedElementIn3D(), hasSelection));
        menu.Items.Add(ViewMenuItem("ViewMenu.FrameAll", (_, _) => FrameCameraToScene()));
        menu.Items.Add(ViewMenuItem("ViewMenu.Center", (_, _) => _modelNavigator?.CenterAll()));
        menu.Items.Add(new Separator());
        menu.Items.Add(ViewMenuItem("ViewMenu.Render", (_, _) => ViewTabs.SelectedIndex = RenderTabIndex));
        menu.Items.Add(ViewMenuItem("ViewMenu.Plan", (_, _) => ViewTabs.SelectedIndex = 0));
        menu.IsOpen = true;
    }

    /// <summary>Creates a localized menu entry with an action and its current selection eligibility.</summary>
    private MenuItem ViewMenuItem(string textKey, RoutedEventHandler action, bool enabled = true)
    {
        var item = new MenuItem { Header = GetText(textKey), IsEnabled = enabled };
        item.Click += action;
        return item;
    }
}
