using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>
/// Renders a localized, accessible ribbon from declarative workspace definitions.
/// </summary>
public partial class ProfessionalRibbon : UserControl
{
    public static readonly DependencyProperty WorkspaceProperty = DependencyProperty.Register(
        nameof(Workspace),
        typeof(RibbonWorkspace),
        typeof(ProfessionalRibbon),
        new PropertyMetadata(RibbonWorkspace.Structure, OnWorkspaceChanged));

    /// <summary>
    /// Initializes the reusable ribbon and builds its initial workspace.
    /// </summary>
    public ProfessionalRibbon()
    {
        InitializeComponent();
        Loaded += HandleLoaded;
    }

    public event EventHandler<ProfessionalRibbonCommandEventArgs>? CommandInvoked;

    public RibbonWorkspace Workspace
    {
        get => (RibbonWorkspace)GetValue(WorkspaceProperty);
        set => SetValue(WorkspaceProperty, value);
    }

    /// <summary>
    /// Rebuilds the ribbon after XAML has completed loading its visual tree.
    /// </summary>
    private void HandleLoaded(object sender, RoutedEventArgs e) => BuildWorkspace();

    /// <summary>
    /// Refreshes the visual groups when a ribbon is assigned another workspace.
    /// </summary>
    private static void OnWorkspaceChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is ProfessionalRibbon ribbon && ribbon.IsLoaded)
        {
            ribbon.BuildWorkspace();
        }
    }

    /// <summary>Refits the ribbon when the window width changes.</summary>
    private void RibbonViewport_SizeChanged(object sender, SizeChangedEventArgs e) =>
        Dispatcher.BeginInvoke(FitRibbonToViewport, System.Windows.Threading.DispatcherPriority.Loaded);

    /// <summary>
    /// Shrinks a slightly too wide ribbon to fit, so no scroll bar is needed. A much narrower window keeps the
    /// natural size and the mouse wheel pans sideways.
    /// </summary>
    private void FitRibbonToViewport()
    {
        GroupHost.LayoutTransform = Transform.Identity;
        if (RibbonViewport.ActualWidth > 0) RibbonLabelFitter.Fit(GroupHost, RibbonViewport.ActualWidth - 6);
        GroupHost.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var naturalWidth = GroupHost.DesiredSize.Width + 2;
        if (naturalWidth <= 0 || RibbonViewport.ActualWidth <= 0) return;
        var scale = Math.Min(1, (RibbonViewport.ActualWidth - 4) / naturalWidth);
        if (scale < 0.75) scale = 1;
        if (scale < 1) GroupHost.LayoutTransform = new ScaleTransform(scale, scale);
    }

    /// <summary>
    /// Creates each group from the catalog while preserving compact three-row clusters.
    /// </summary>
    private void BuildWorkspace()
    {
        GroupHost.Children.Clear();
        foreach (RibbonGroupDefinition group in RibbonCatalog.GetGroups(Workspace))
        {
            GroupHost.Children.Add(CreateGroup(group));
        }
        Dispatcher.BeginInvoke(FitRibbonToViewport, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Creates one bordered command group with a localized footer.
    /// </summary>
    private Border CreateGroup(RibbonGroupDefinition definition)
    {
        var commandHost = new StackPanel { Orientation = Orientation.Horizontal };
        var compactColumn = new StackPanel { VerticalAlignment = VerticalAlignment.Top };

        foreach (RibbonCommandDefinition command in definition.Commands)
        {
            if (command.Size == RibbonCommandSize.Compact)
            {
                compactColumn.Children.Add(CreateButton(command));
                if (compactColumn.Children.Count == 3)
                {
                    commandHost.Children.Add(compactColumn);
                    compactColumn = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
                }
            }
            else
            {
                FlushCompactColumn(commandHost, compactColumn);
                compactColumn = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
                commandHost.Children.Add(CreateButton(command));
            }
        }

        FlushCompactColumn(commandHost, compactColumn);
        commandHost.VerticalAlignment = VerticalAlignment.Top;

        // Buttons on top, the group title centred underneath (Revit-style panel).
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(19) });
        Grid.SetRow(commandHost, 0);
        layout.Children.Add(commandHost);
        var label = CreateGroupLabel(definition.LabelResourceKey);
        Grid.SetRow(label, 1);
        layout.Children.Add(label);

        return new Border
        {
            Style = (Style)FindResource("RibbonGroupBorderStyle"),
            Child = layout
        };
    }

    /// <summary>
    /// Adds a partially filled compact command column to its group.
    /// </summary>
    private static void FlushCompactColumn(Panel commandHost, Panel compactColumn)
    {
        if (compactColumn.Children.Count > 0)
        {
            commandHost.Children.Add(compactColumn);
        }
    }

    /// <summary>
    /// Creates a localized footer that identifies a ribbon group.
    /// </summary>
    private TextBlock CreateGroupLabel(string resourceKey) => new()
    {
        Text = ResolveText(resourceKey),
        Style = (Style)FindResource("RibbonGroupLabelStyle")
    };

    /// <summary>
    /// Creates an accessible command button and binds it to the shared invocation event.
    /// </summary>
    private Button CreateButton(RibbonCommandDefinition definition)
    {
        string label = ResolveText(definition.LabelResourceKey);
        var button = new Button
        {
            Content = label,
            // The shared ribbon button templates bind the icon image to CommandParameter.
            CommandParameter = CommandIcons.For(definition.CommandId, definition.IconResourceKey) ?? TryFindResource(definition.IconResourceKey),
            Style = (Style)FindResource(definition.Size == RibbonCommandSize.Large
                ? "LargeRibbonButtonStyle"
                : "CompactRibbonButtonStyle")
        };
        button.Click += (_, _) => CommandInvoked?.Invoke(
            this,
            new ProfessionalRibbonCommandEventArgs(Workspace, definition.CommandId, label));
        AutomationProperties.SetName(button, label);
        CommandDemoToolTip.Attach(button, definition.CommandId, label);
        return button;
    }

    /// <summary>
    /// Resolves localized text and exposes a readable diagnostic fallback for missing resources.
    /// </summary>
    private string ResolveText(string resourceKey) =>
        TryFindResource(resourceKey) as string ?? resourceKey;
}
