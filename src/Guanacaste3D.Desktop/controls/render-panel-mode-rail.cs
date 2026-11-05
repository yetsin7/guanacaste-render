using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>Independent renderer-only navigation; the BIM ribbon is never repurposed.</summary>
public partial class RenderPanel
{
    private static readonly Brush RailActive = new SolidColorBrush(Color.FromRgb(22, 125, 119));
    private static readonly Brush RailInactive = new SolidColorBrush(Color.FromRgb(28, 37, 45));
    private static readonly Brush RailActiveText = new SolidColorBrush(Color.FromRgb(255, 255, 255));
    private static readonly Brush RailInactiveText = new SolidColorBrush(Color.FromRgb(221, 235, 245));

    private void InitializeRenderModeRail()
    {
        SectionTabs.SelectionChanged += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, SectionTabs))
                UpdateRenderRailSelection();
        };
        UpdateRenderRailSelection();
    }

    private void RenderRail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var index) &&
            index >= 0 && index < SectionTabs.Items.Count)
            SectionTabs.SelectedIndex = index;
    }

    private void UpdateRenderRailSelection()
    {
        if (RenderModeRail is null || SectionTabs is null) return;
        foreach (var button in RenderModeRail.Children.OfType<Button>())
        {
            var selected = int.TryParse(button.Tag as string, out var index) && index == SectionTabs.SelectedIndex;
            button.Background = selected ? RailActive : RailInactive;
            button.Foreground = selected ? RailActiveText : RailInactiveText;
            button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }
}
