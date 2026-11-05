using System.Windows;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>The "Fuego y humo" window of the Effects tab: campfires and columns of smoke placed where the view looks.</summary>
public partial class RenderPanel
{
    private FireDialog? _fireWindow;

    private void Fire_Click(object sender, RoutedEventArgs e) => ShowFireWindow();

    /// <summary>Opens the window of fires beside the view (one at a time).</summary>
    private void ShowFireWindow()
    {
        if (_source is null) return;
        if (_fireWindow is { IsLoaded: true })
        {
            _fireWindow.Activate();
            return;
        }
        var source = _source;
        _fireWindow = new FireDialog(source.GetFires(), PointUnderView, fires =>
        {
            source.SetFires(fires);
            StatusText.Text = fires.Count == 0 ? "No hay fuegos en el proyecto." : $"{fires.Count} fuego(s) en el proyecto; el render se actualiza.";
            // The picture shows them through the finish, which names them: refresh it as well as the scene.
            Finish_Changed(this, new RoutedEventArgs());
        })
        {
            Owner = Window.GetWindow(this)
        };
        var owner = _fireWindow.Owner;
        if (owner is not null)
        {
            _fireWindow.Left = owner.Left + Math.Max(0, owner.ActualWidth - 480);
            _fireWindow.Top = owner.Top + 110;
        }
        _fireWindow.Show();
    }

    /// <summary>The fires of the project for the picture's finish (null for none), burning at the movie's playhead.</summary>
    private IReadOnlyList<FireEmitter>? FiresForFinish() => _source?.GetFires() is { Count: > 0 } fires ? fires : null;
}
