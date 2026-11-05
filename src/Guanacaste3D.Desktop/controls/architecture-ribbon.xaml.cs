using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>
/// Presents the Architecture tab laid out like the reference BIM application: panels of large and stacked
/// buttons, split buttons whose arrow opens a menu of related commands, and slide-out panel titles. Execution is
/// delegated to the owning workspace through <see cref="CommandInvoked"/>.
/// </summary>
public partial class ArchitectureRibbon : UserControl
{
    /// <summary>One entry of a split-button menu.</summary>
    private sealed record MenuEntry(string Tag, string LabelKey, string? IconKey = null);

    /// <summary>One ribbon button; <see cref="Menu"/> turns it into a split button.</summary>
    private sealed record Button_(string Tag, string LabelKey, string IconKey, bool Large = false,
        params MenuEntry[] Menu)
    {
        public bool HasMenu => Menu.Length > 0;
    }

    /// <summary>A panel: columns of one large button or up to three stacked small ones, plus a footer title.</summary>
    private sealed record Panel(string TitleKey, bool SlideOut, Button_[][] Columns);

    /// <summary>Initializes the ribbon and builds its panels.</summary>
    public ArchitectureRibbon()
    {
        InitializeComponent();
        foreach (var panel in Layout()) GroupHost.Children.Add(BuildPanel(panel));
        WallTypeGroup.SizeChanged += RibbonViewport_SizeChanged;
        Loaded += RibbonLoaded;
    }

    /// <summary>Fits a moderately wide ribbon inside the available width after its first layout pass.</summary>
    private void RibbonLoaded(object sender, RoutedEventArgs e) => ScheduleFit();

    /// <summary>Recalculates the ribbon fit when the window or a contextual panel changes size.</summary>
    private void RibbonViewport_SizeChanged(object sender, SizeChangedEventArgs e) => ScheduleFit();

    private bool _fitScheduled;

    /// <summary>Runs the fit once, after the current layout pass, because captions are measured with real layouts.</summary>
    private void ScheduleFit()
    {
        if (_fitScheduled) return;
        _fitScheduled = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            _fitScheduled = false;
            FitRibbonToViewport();
        });
    }

    /// <summary>Keeps every panel visible on wide screens and leaves readable horizontal scrolling on narrow ones.</summary>
    private void FitRibbonToViewport()
    {
        if (RibbonViewport.ActualWidth <= 0) return;
        var wallTypeWidth = WallTypeGroup.Visibility == Visibility.Visible ? WallTypeGroup.ActualWidth : 0;
        RibbonLabelFitter.Fit(GroupHost, RibbonViewport.ActualWidth - 6 - 4 - wallTypeWidth);
        var naturalWidth = GroupHost.DesiredSize.Width + wallTypeWidth + 4;
        if (naturalWidth <= 0) return;
        var requiredScale = (RibbonViewport.ActualWidth - 6) / naturalWidth;
        var scale = requiredScale >= 0.8 ? Math.Min(1, requiredScale) : 1;
        if (RibbonContent.LayoutTransform is ScaleTransform current && Math.Abs(current.ScaleX - scale) < 0.005) return;
        RibbonContent.LayoutTransform = new ScaleTransform(scale, scale);
    }

    /// <summary>Occurs when the user selects an architecture command.</summary>
    public event EventHandler<ArchitectureCommandEventArgs>? CommandInvoked;

    /// <summary>Gets the wall type selector so the workspace can synchronize document types.</summary>
    public ComboBox WallTypePickerControl => WallTypePicker;

    /// <summary>Gets the wall button so the workspace can reflect drawing mode.</summary>
    public Button WallCommandButtonControl => _wallButton ?? new Button();

    private Button? _wallButton;

    /// <summary>The panels of the Architecture tab, in order.</summary>
    private static IReadOnlyList<Panel> Layout() =>
    [
        new("RibbonGroup.Selection", true, [
            [new("Modify", "Tool.Modify", "Icon.Apply", true,
                new MenuEntry("Modify", "Tool.Modify"), new MenuEntry("Deselect", "Arch.Deselect"))]]),
        new("RibbonGroup.Build", false, [
            [new("Wall", "Tool.Wall", "Icon.Wall", true,
                new MenuEntry("WallArchitectural", "Arch.WallArchitectural"), new MenuEntry("WallStructural", "Arch.WallStructural"),
                new MenuEntry("WallByFace", "Arch.WallByFace"), new MenuEntry("WallSweep", "Arch.WallSweep"),
                new MenuEntry("WallReveal", "Arch.WallReveal"))],
            [new("Door", "Tool.Door", "Icon.Door", true)],
            [new("Window", "Tool.Window", "Icon.Window"),
             new("Component", "Tool.Component", "Icon.OpeningTypes", false,
                new MenuEntry("ComponentPlace", "Arch.ComponentPlace"), new MenuEntry("ComponentInPlace", "Arch.ComponentInPlace")),
             new("Column", "Tool.Column", "Icon.Column", false,
                new MenuEntry("ColumnArchitectural", "Arch.ColumnArchitectural"), new MenuEntry("ColumnStructural", "Arch.ColumnStructural"))],
            [new("Roof", "Tool.Roof", "Icon.Floor", false,
                new MenuEntry("RoofByFootprint", "Arch.RoofByFootprint"), new MenuEntry("RoofByExtrusion", "Arch.RoofByExtrusion"),
                new MenuEntry("RoofByFace", "Arch.RoofByFace"), new MenuEntry("RoofSoffit", "Arch.RoofSoffit"),
                new MenuEntry("RoofFascia", "Arch.RoofFascia"), new MenuEntry("RoofGutter", "Arch.RoofGutter")),
             new("Ceiling", "Tool.Ceiling", "Icon.Floor", false,
                new MenuEntry("CeilingAutomatic", "Arch.CeilingAutomatic"), new MenuEntry("CeilingSketch", "Arch.CeilingSketch")),
             new("Floor", "Tool.Floor", "Icon.Floor", false,
                new MenuEntry("FloorArchitectural", "Arch.FloorArchitectural"), new MenuEntry("FloorStructural", "Arch.FloorStructural"),
                new MenuEntry("FloorByFace", "Arch.FloorByFace"), new MenuEntry("SlabEdge", "Arch.SlabEdge"))],
            [new("CurtainSystem", "Tool.CurtainSystem", "Icon.Window"),
             new("CurtainGrid", "Tool.CurtainGrid", "Icon.Window"),
             new("Mullion", "Tool.Mullion", "Icon.Window")]]),
        new("RibbonGroup.Circulation", false, [
            [new("Railing", "Tool.Railing", "Icon.Wall", false,
                new MenuEntry("RailingSketch", "Arch.RailingSketch"), new MenuEntry("RailingOnHost", "Arch.RailingOnHost")),
             new("Ramp", "Tool.Ramp", "Icon.Floor"),
             new("Stair", "Tool.Stair", "Icon.Levels")]]),
        new("RibbonGroup.Model", false, [
            [new("ModelText", "Tool.ModelText", "Icon.Parameters"),
             new("ModelLine", "Tool.ModelLine", "Icon.Section"),
             new("ModelGroup", "Tool.ModelGroup", "Icon.Cube", false,
                new MenuEntry("GroupCreate", "Arch.GroupCreate"), new MenuEntry("GroupPlace", "Arch.GroupPlace"))]]),
        new("RibbonGroup.RoomAndArea", true, [
            [new("Room", "Tool.Room", "Icon.Room", false,
                new MenuEntry("Room", "Tool.Room"), new MenuEntry("RoomAutomatic", "Arch.RoomAutomatic")),
             new("RoomSeparator", "Tool.RoomSeparator", "Icon.Section", false,
                new MenuEntry("RoomSeparator", "Arch.RoomSeparatorStraight"),
                new MenuEntry("RoomSeparatorCurve", "Arch.RoomSeparatorCurved")),
             new("TagRoom", "Tool.TagRoom", "Icon.Apply", false,
                new MenuEntry("TagRoom", "Tool.TagRoom"), new MenuEntry("TagAllRooms", "Arch.TagAllRooms"))],
            [new("Area", "Tool.Area", "Icon.Floor", false,
                new MenuEntry("Area", "Tool.Area"), new MenuEntry("AreaByBoundary", "Arch.AreaByBoundary")),
             new("AreaBoundary", "Tool.AreaBoundary", "Icon.Plan"),
             new("TagArea", "Tool.TagArea", "Icon.Apply", false,
                new MenuEntry("TagArea", "Tool.TagArea"), new MenuEntry("TagAllAreas", "Arch.TagAllAreas"))]]),
        new("RibbonGroup.Opening", false, [
            [new("FaceOpening", "Tool.FaceOpening", "Icon.OpeningTypes", true)],
            [new("ShaftOpening", "Tool.ShaftOpening", "Icon.OpeningTypes", true)],
            [new("WallOpening", "Tool.WallOpening", "Icon.Wall"),
             new("VerticalOpening", "Tool.VerticalOpening", "Icon.OpeningTypes"),
             new("DormerOpening", "Tool.DormerOpening", "Icon.OpeningTypes")]]),
        new("RibbonGroup.Datum", false, [
            [new("Level", "Tool.Level", "Icon.Level"), new("Grid", "Tool.Grid", "Icon.Grid")]]),
        new("RibbonGroup.WorkPlane", false, [
            [new("SetWorkPlane", "Tool.SetWorkPlane", "Icon.Plan", true,
                new MenuEntry("WorkPlaneByLevel", "Arch.WorkPlaneByLevel"), new MenuEntry("WorkPlanePickReference", "Arch.WorkPlanePickReference"))],
            [new("ShowWorkPlane", "Tool.ShowWorkPlane", "Icon.Plan"),
             new("ReferencePlane", "Tool.ReferencePlane", "Icon.Section"),
             new("Viewer", "Tool.Viewer", "Icon.Cube")]])
    ];

    private static string Text(string key) => Application.Current.TryFindResource(key) as string ?? key;

    /// <summary>Builds one bordered panel with its columns and footer title.</summary>
    private Border BuildPanel(Panel panel)
    {
        var columns = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var column in panel.Columns)
        {
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            foreach (var definition in column) stack.Children.Add(BuildButton(definition));
            columns.Children.Add(stack);
        }
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(17) });
        grid.Children.Add(columns);
        var title = new TextBlock { Text = Text(panel.TitleKey) + (panel.SlideOut ? "  ▾" : string.Empty) };
        title.SetResourceReference(StyleProperty, "RibbonGroupLabelStyle");
        Grid.SetRow(title, 1);
        grid.Children.Add(title);
        if (panel.SlideOut)
        {
            // The title arrow opens the panel's slide-out: its commands again, as a menu.
            title.Cursor = Cursors.Hand;
            title.MouseLeftButtonUp += (_, _) => ShowMenu(title, panel.Columns.SelectMany(column => column)
                .Select(button => new MenuEntry(button.Tag, button.LabelKey, button.IconKey)).ToArray());
        }
        var border = new Border { Child = grid };
        border.SetResourceReference(StyleProperty, "RibbonGroupBorderStyle");
        return border;
    }

    /// <summary>Builds a button; one with a menu is a split button whose arrow area opens the menu.</summary>
    private Button BuildButton(Button_ definition)
    {
        var label = Text(definition.LabelKey);
        var arrow = definition.HasMenu;
        var button = new Button
        {
            // The button shows only its icon; the name lives in the tooltip and the accessibility tree.
            Content = label,
            Tag = definition.Tag,
            CommandParameter = CommandIcons.For(definition.Tag, definition.IconKey),
            ToolTip = null
        };
        RibbonButton.SetHasMenu(button, arrow);
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        button.SetResourceReference(StyleProperty, definition.Large ? "ArchitectureLargeButtonStyle" : "ArchitectureCompactButtonStyle");
        button.Click += (_, e) =>
        {
            if (arrow && IsOverArrow(button, definition.Large))
            {
                ShowMenu(button, definition.Menu);
                return;
            }
            Raise(definition.Tag);
        };
        CommandDemoToolTip.Attach(button, definition.Tag, label);
        if (definition.Tag == "Wall") _wallButton = button;
        return button;
    }

    /// <summary>Whether the pointer is on the arrow strip of a split button.</summary>
    private static bool IsOverArrow(Button button, bool large)
    {
        var position = Mouse.GetPosition(button);
        return large ? position.Y > button.ActualHeight - 22 : position.X > button.ActualWidth - 14;
    }

    /// <summary>Opens a menu of commands under an element.</summary>
    private void ShowMenu(FrameworkElement anchor, IReadOnlyList<MenuEntry> entries)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var entry in entries)
        {
            var item = new MenuItem { Header = Text(entry.LabelKey) };
            if (CommandIcons.For(entry.Tag, entry.IconKey) is { } icon)
                item.Icon = new Image { Source = icon, Width = 16, Height = 16 };
            var tag = entry.Tag;
            item.Click += (_, _) => Raise(tag);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void Raise(string commandName)
    {
        if (!Enum.TryParse(commandName, out ArchitectureCommand command)) return;
        WallTypeGroup.Visibility = command is ArchitectureCommand.Wall or ArchitectureCommand.WallArchitectural
            or ArchitectureCommand.WallStructural ? Visibility.Visible : Visibility.Collapsed;
        CommandInvoked?.Invoke(this, new ArchitectureCommandEventArgs(command));
    }

    /// <summary>Handles the wall type group's apply button.</summary>
    private void ArchitectureButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string commandName }) Raise(commandName);
    }
}
