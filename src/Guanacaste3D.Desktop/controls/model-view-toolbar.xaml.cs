using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>Responsive modeling styles and camera actions on a readable surface above scene colors.</summary>
public partial class ModelViewToolbar : UserControl
{
    /// <summary>Initializes localized presentation controls independently of their document host.</summary>
    public ModelViewToolbar() => InitializeComponent();

    public event RoutedEventHandler? StyleChanged;
    public event RoutedEventHandler? ResetRequested;
    public event RoutedEventHandler? RenderRequested;
    public event Action<string>? CommandRequested;
    public Func<string, bool>? IsCommandChecked { get; set; }
    public Func<string, bool>? IsCommandEnabled { get; set; }

    /// <summary>Dispatches view actions through the window's camera and presentation services.</summary>
    private void Action_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string command }) CommandRequested?.Invoke(command);
    }

    /// <summary>Builds selectors on demand so checkmarks and availability follow the current view.</summary>
    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string command } anchor) return;
        var menu = CreateMenu(command);
        menu.PlacementTarget = anchor;
        menu.Placement = PlacementMode.Top;
        menu.IsOpen = true;
    }

    /// <summary>Exposes the six styles, temporary visibility actions, solar presets, and perspective lenses.</summary>
    public ContextMenu CreateMenu(string kind)
    {
        var commands = kind switch
        {
            "Styles" => new[] { "Graphics", "Style.Wireframe", "Style.HiddenLine", "Style.Shaded", "Style.ConsistentColors", "Style.Textures", "Style.Realistic" },
            "Temporary" => new[] { "Temporary.Hide", "Temporary.Isolate", "Temporary.HideCategory", "Temporary.IsolateCategory", "Temporary.Reset", "Temporary.Apply", "Unhide" },
            "Sun" => new[] { "Sun.Off", "Sun.Morning", "Sun.Noon", "Sun.Afternoon" },
            "Crop" => new[] { "Crop.Toggle", "Crop.Edit", "Crop.Reset" },
            "Detail" => new[] { "Detail.Coarse", "Detail.Medium", "Detail.Fine" },
            _ => new[] { "Lens.Wide", "Lens.Normal", "Lens.Telephoto" }
        };
        var menu = new ContextMenu();
        foreach (var command in commands)
        {
            var requested = command;
            var item = new MenuItem { Header = UiText.Get("Display." + command), Tag = command,
                IsCheckable = command.StartsWith("Style.") || command.StartsWith("Sun.") || command.StartsWith("Lens.") || command.StartsWith("Detail.") || command == "Crop.Toggle",
                IsChecked = IsCommandChecked?.Invoke(command) == true,
                IsEnabled = IsCommandEnabled?.Invoke(command) != false };
            item.Click += (_, _) => CommandRequested?.Invoke(requested);
            menu.Items.Add(item);
            if (command == "Graphics") menu.Items.Add(new Separator());
        }
        return menu;
    }

    /// <summary>Marks active view controls without relying only on their tooltip text.</summary>
    public void SetActive(Button button, bool active) => button.Background = active
        ? new SolidColorBrush(Color.FromRgb(204, 231, 252)) : Brushes.Transparent;

    /// <summary>Publishes edge and service visibility changes after initial control construction.</summary>
    private void Style_Changed(object sender, RoutedEventArgs e) => StyleChanged?.Invoke(this, e);

    /// <summary>Asks the host to restore its initial camera orientation.</summary>
    private void Reset_Click(object sender, RoutedEventArgs e) => ResetRequested?.Invoke(this, e);

    /// <summary>Asks the host to open its render workspace with the shared camera.</summary>
    private void Render_Click(object sender, RoutedEventArgs e) => RenderRequested?.Invoke(this, e);

    /// <summary>Reserves compact window width for actionable controls instead of truncating their buttons.</summary>
    private void Toolbar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (NavigationHint is not null) NavigationHint.Visibility = Visibility.Collapsed;
    }
}
