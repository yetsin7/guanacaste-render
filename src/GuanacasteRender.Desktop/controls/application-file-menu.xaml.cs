using System.Windows;
using System.Windows.Controls;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>Localized application menu whose host owns document operations and history.</summary>
public partial class ApplicationFileMenu : ContextMenu
{
    /// <summary>Builds the menu without retaining a document or a main-window reference.</summary>
    public ApplicationFileMenu() => InitializeComponent();

    public event Action<string>? CommandInvoked;
    public Func<IReadOnlyList<RecentDocument>>? RecentItemsProvider { get; set; }


    private void ApplicationMenu_Opened(object sender, RoutedEventArgs e)
    {
        RecentProjectsMenu.Items.Clear();
        var recent = RecentItemsProvider?.Invoke() ?? [];
        foreach (var item in recent.Take(15))
        {
            var path = item.Path;
            var entry = new MenuItem
            {
                Header = System.IO.Path.GetFileName(path),
                ToolTip = path
            };
            entry.Click += (_, _) => RecentPathSelected?.Invoke(path);
            RecentProjectsMenu.Items.Add(entry);
        }
        if (RecentProjectsMenu.Items.Count == 0)
            RecentProjectsMenu.Items.Add(new MenuItem { Header = "No hay proyectos recientes", IsEnabled = false });
    }

    public event Action<string>? RecentPathSelected;

    /// <summary>Forwards a menu command identifier to the owning application window.</summary>
    private void Command_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string command }) CommandInvoked?.Invoke(command);
    }
}
