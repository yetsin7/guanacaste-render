using System.Windows;

namespace Guanacaste3D.Desktop.Controls;

public partial class RenderPanel
{
    private void SceneCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string command } && _source is not null)
        {
            try { StatusText.Text = _source.ExecuteSceneCommand(command); }
            catch (System.IO.InvalidDataException error) { StatusText.Text = error.Message; }
        }
    }
    /// <summary>Rotates around the selected element or project center using the shared model camera.</summary>
    private void OrbitLeft_Click(object sender, RoutedEventArgs e) => _navigator?.OrbitBy(-80, 0);

    /// <summary>Rotates around the selected element or project center using the shared model camera.</summary>
    private void OrbitRight_Click(object sender, RoutedEventArgs e) => _navigator?.OrbitBy(80, 0);

    /// <summary>Recovers a view of the whole project when the current camera is far from the model.</summary>
    private void FrameModel_Click(object sender, RoutedEventArgs e) => _navigator?.FrameAll();

    private void SceneEnvironment_Click(object sender, RoutedEventArgs e) => SectionTabs.SelectedIndex = 0;
    private void SceneCamera_Click(object sender, RoutedEventArgs e) => SectionTabs.SelectedIndex = CameraTab;
    private void SceneMaterials_Click(object sender, RoutedEventArgs e) => FocusMaterials();
    private void SceneScatter_Click(object sender, RoutedEventArgs e) => ShowScatterWindow();
    private void ScenePeople_Click(object sender, RoutedEventArgs e) => ShowPeopleWindow();
}
