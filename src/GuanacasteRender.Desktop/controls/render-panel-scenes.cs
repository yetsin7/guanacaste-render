using System.Numerics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop.Controls;

public partial class RenderPanel
{
    /// <summary>Refreshes saved scene choices after project changes and undo operations.</summary>
    private void RefreshRenderScenes()
    {
        if (_source is null || ScenePicker is null) return;
        var selected = (ScenePicker.SelectedItem as ComboBoxItem)?.Tag as Guid?;
        ScenePicker.Items.Clear();
        foreach (var scene in _source.RenderScenes)
            ScenePicker.Items.Add(new ComboBoxItem { Content = scene.Name, Tag = scene.Id });
        ScenePicker.SelectedItem = ScenePicker.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (Guid)item.Tag == selected);
        RefreshPhotos();
    }

    /// <summary>Captures the current camera and render settings in the active project.</summary>
    private void SaveRenderScene_Click(object sender, RoutedEventArgs e)
    {
        if (_source is null) return;
        var name = PromptWindow.Ask(Window.GetWindow(this), UiText.Get("RenderPanel.SceneName"), "");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            var setup = _source.ProjectFilePath is { } path ? CaptureSetupForProject(_source.Camera, path) : CaptureSetup(_source.Camera);
            _source.AddRenderScene(name, setup);
            RefreshRenderScenes();
            ScenePicker.SelectedIndex = ScenePicker.Items.Count - 1;
        }
        catch (InvalidDataException error) { StatusText.Text = LocalizedErrors.Translate(error.Message); }
    }

    /// <summary>Restores a scene's camera, environment, and image settings without changing model geometry.</summary>
    private void OpenRenderScene_Click(object sender, RoutedEventArgs e)
    {
        if (_source is null || ScenePicker.SelectedItem is not ComboBoxItem { Tag: Guid id }) return;
        var scene = _source.RenderScenes.FirstOrDefault(item => item.Id == id);
        if (scene is null) return;
        var setup = scene.Setup;
        _source.Camera.Position = new Vector3((float)setup.CameraX, (float)setup.CameraY, (float)setup.CameraZ);
        _source.Camera.YawDegrees = (float)setup.YawDegrees;
        _source.Camera.PitchDegrees = (float)setup.PitchDegrees;
        _source.Camera.FieldOfViewDegrees = (float)setup.FieldOfViewDegrees;
        ApplySetupForProject(setup, _source.ProjectFilePath);
        OnCameraChanged();
    }

    /// <summary>Removes the selected saved scene while keeping the current camera unchanged.</summary>
    private void DeleteRenderScene_Click(object sender, RoutedEventArgs e)
    {
        if (_source is null || ScenePicker.SelectedItem is not ComboBoxItem { Tag: Guid id }) return;
        _source.RemoveRenderScene(id);
        RefreshRenderScenes();
    }
}
