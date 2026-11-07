using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>Coordinates the visual output-size tiles with the render settings model.</summary>
public partial class RenderPanel
{
    private static readonly Brush OutputPresetActiveBackground = new SolidColorBrush(Color.FromRgb(22, 119, 200));
    private static readonly Brush OutputPresetInactiveBackground = new SolidColorBrush(Color.FromRgb(52, 52, 56));
    private static readonly Brush OutputPresetActiveBorder = new SolidColorBrush(Color.FromRgb(83, 166, 235));
    private static readonly Brush OutputPresetInactiveBorder = new SolidColorBrush(Color.FromRgb(69, 69, 74));

    /// <summary>Selects the output size represented by a visual preset tile.</summary>
    private void OutputPreset_Click(object sender, RoutedEventArgs eventArguments)
    {
        if (sender is not Button { Tag: string sizeTag }) return;
        SizePicker.SelectedItem = SizePicker.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, sizeTag, StringComparison.Ordinal));
    }

    /// <summary>Refreshes render estimates and the selected output tile after a size change.</summary>
    private void SizePicker_SelectionChanged(object sender, SelectionChangedEventArgs eventArguments)
    {
        Quality_Changed(sender, eventArguments);
        UpdateOutputPresetSelection();
    }

    /// <summary>Applies the initial selection styling after the preset grid enters the visual tree.</summary>
    private void OutputPresetGrid_Loaded(object sender, RoutedEventArgs eventArguments) => UpdateOutputPresetSelection();

    /// <summary>Keeps output tiles visually synchronized with the hidden settings selector.</summary>
    private void UpdateOutputPresetSelection()
    {
        if (OutputPresetGrid is null || SizePicker?.SelectedItem is not ComboBoxItem { Tag: string selectedTag }) return;
        foreach (var button in OutputPresetGrid.Children.OfType<Button>())
        {
            var active = string.Equals(button.Tag as string, selectedTag, StringComparison.Ordinal);
            button.Background = active ? OutputPresetActiveBackground : OutputPresetInactiveBackground;
            button.BorderBrush = active ? OutputPresetActiveBorder : OutputPresetInactiveBorder;
            button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }
}
