
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>Independent renderer-only navigation; the BIM ribbon is never repurposed.</summary>
public partial class RenderPanel
{
    private static readonly Brush RailActive = new SolidColorBrush(Color.FromRgb(52, 95, 145));
    private static readonly Brush RailInactive = new SolidColorBrush(Color.FromRgb(28, 29, 32));
    private static readonly Brush RailActiveText = new SolidColorBrush(Color.FromRgb(255, 255, 255));
    private static readonly Brush RailInactiveText = new SolidColorBrush(Color.FromRgb(201, 201, 205));

    /// <summary>Connects the compact studio rail to the existing settings sections.</summary>
    private void InitializeRenderModeRail()
    {
        SectionTabs.SelectionChanged += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, SectionTabs))
                UpdateRenderRailSelection();
        };
        UpdateRenderRailSelection();
    }

    /// <summary>Opens the settings section identified by the selected rail button.</summary>
    private void RenderRail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var index) &&
            index >= 0 && index < SectionTabs.Items.Count)
            SectionTabs.SelectedIndex = index;
    }

    /// <summary>Synchronizes rail emphasis with the active settings section.</summary>
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
        if (SectionHeading is not null && SectionDescription is not null)
        {
            var (heading, description) = SectionTabs.SelectedIndex switch
            {
                1 => ("Imagen y efectos", "Construye el acabado final de tu imagen."),
                2 => ("Cámara", "Encuadre, perspectiva y recorrido."),
                3 => ("Materiales", "Elige una superficie y define su apariencia."),
                4 => ("Calidad de render", "Equilibra detalle, tiempo y recursos."),
                _ => ("Entorno", "Define la luz y la atmósfera de tu imagen.")
            };
            SectionHeading.Text = heading;
            SectionDescription.Text = description;
            SectionContext.Visibility = SectionTabs.SelectedIndex == 3 ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
