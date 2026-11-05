using System.Windows;

namespace Guanacaste3D.Desktop.Controls;

public partial class RenderPanel
{
    /// <summary>Applies the render-only clay override and lets scene invalidation restart the preview.</summary>
    private void ClayMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_source is not null) _source.ClayMode = RenderClayCheck.IsChecked == true;
    }
}
