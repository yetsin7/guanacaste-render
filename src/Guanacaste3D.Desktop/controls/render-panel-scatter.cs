using System.Windows;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>The "Árboles y rocas" window of the Movie bar: a brush that scatters trees, plants and rocks where the view looks.</summary>
public partial class RenderPanel
{
    private ScatterDialog? _scatterWindow;

    private void ShowScatterWindow()
    {
        if (_source is null) return;
        if (_scatterWindow is { IsLoaded: true })
        {
            _scatterWindow.Activate();
            return;
        }
        var source = _source;
        var families = source.ScatterFamilies();
        if (families.Count == 0)
        {
            StatusText.Text = "No hay árboles, plantas ni rocas que esparcir.";
            return;
        }
        _scatterWindow = new ScatterDialog(families, PointUnderView, (family, placements, height) =>
        {
            var sentence = source.PlaceScattered(family, placements, height);
            StatusText.Text = sentence;
            return sentence;
        })
        {
            Owner = Window.GetWindow(this)
        };
        if (_scatterWindow.Owner is { } owner)
        {
            _scatterWindow.Left = owner.Left + Math.Max(0, owner.ActualWidth - 470);
            _scatterWindow.Top = owner.Top + 90;
        }
        _scatterWindow.Show();
    }
}
