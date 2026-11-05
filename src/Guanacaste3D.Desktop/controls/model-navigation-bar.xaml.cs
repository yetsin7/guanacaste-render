using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>Compact wheel and zoom selectors matching the model viewport's navigation tools.</summary>
public partial class ModelNavigationBar : UserControl
{
    public event Action<string>? CommandRequested;
    public string WheelMode { get; private set; } = "SmallBuilding";
    public Func<(bool Back, bool Forward)>? HistoryAvailability { get; set; }
    public Func<bool>? HasPrintFrame { get; set; }

    /// <summary>Creates the localized navigation strip independently of its host camera.</summary>
    public ModelNavigationBar() => InitializeComponent();

    /// <summary>Routes primary actions and opens the appropriate anchored selector.</summary>
    private void Action_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string command } button) return;
        if (command == "WheelMenu") OpenMenu(button, CreateWheelMenu());
        else if (command == "ZoomMenu") OpenMenu(button, CreateZoomMenu());
        else if (command == "Close") Visibility = Visibility.Collapsed;
        else CommandRequested?.Invoke(command == "Wheel" ? "Wheel." + WheelMode : command);
    }

    /// <summary>Offers all six 3D wheel layouts and identifies the 2D-only layout explicitly.</summary>
    public ContextMenu CreateWheelMenu()
    {
        var menu = new ContextMenu();
        foreach (var mode in new[] { "Full", "SmallFull", "SmallObject", "SmallBuilding", "BasicObject", "BasicBuilding" })
        {
            var selected = mode;
            var item = new MenuItem { Header = UiText.Get("Navigation.Wheel." + mode), Tag = mode,
                IsCheckable = true, IsChecked = WheelMode == mode };
            item.Click += (_, _) => { WheelMode = selected; CommandRequested?.Invoke("Wheel." + selected); };
            menu.Items.Add(item);
            if (mode == "SmallBuilding") menu.Items.Add(new Separator());
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = UiText.Get("Navigation.Wheel.TwoD"), IsEnabled = false });
        return menu;
    }

    /// <summary>Builds zoom commands with live backward/forward availability.</summary>
    public ContextMenu CreateZoomMenu()
    {
        var menu = new ContextMenu();
        var history = HistoryAvailability?.Invoke() ?? (false, false);
        foreach (var command in new[] { "Region", "Reduce", "Fit", "FitAll", "SheetSize", "Previous", "Next" })
        {
            var requested = command;
            var item = new MenuItem { Header = UiText.Get("Navigation." + command), Tag = command,
                IsEnabled = command switch { "Previous" => history.Item1, "Next" => history.Item2,
                    "SheetSize" => HasPrintFrame?.Invoke() == true, _ => true } };
            item.Click += (_, _) => CommandRequested?.Invoke(requested);
            menu.Items.Add(item);
        }
        return menu;
    }

    /// <summary>Keeps selectors attached to their button and within the active window's work area.</summary>
    private static void OpenMenu(Button anchor, ContextMenu menu)
    {
        menu.PlacementTarget = anchor;
        menu.Placement = PlacementMode.Left;
        menu.IsOpen = true;
    }
}
