using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Guanacaste3D.Desktop.Controls;

public partial class RenderPanel
{
    private int _interactiveSceneRequest;

    /// <summary>Builds the latest model without starting the path tracer, discarding obsolete results.</summary>
    private async void RefreshInteractiveScene()
    {
        // The scene source may raise notifications on worker threads. WPF controls must only be read and updated
        // on their owning dispatcher, including the error-reporting path.
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted) _ = Dispatcher.BeginInvoke((Action)RefreshInteractiveScene);
            return;
        }
        if (_source is null || !IsVisible) return;
        var source = _source;
        var version = source.SceneVersion;
        var request = ++_interactiveSceneRequest;
        try
        {
            var snapshot = await source.GetInteractiveSceneAsync().ConfigureAwait(false);
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            await Dispatcher.InvokeAsync(() =>
            {
                if (request != _interactiveSceneRequest || source != _source ||
                    version != source.SceneVersion || !IsVisible) return;
                InteractiveViewport.Children.Clear();
                InteractiveViewport.Children.Add(new ModelVisual3D { Content = new AmbientLight(Color.FromRgb(150, 150, 150)) });
                InteractiveViewport.Children.Add(new ModelVisual3D
                {
                    Content = new DirectionalLight(Color.FromRgb(255, 246, 232), new Vector3D(-0.5, -1, -0.4))
                });
                foreach (var model in snapshot.Models)
                    InteractiveViewport.Children.Add(new ModelVisual3D { Content = model });
                UpdateInteractiveCamera();
                ScheduleStyledPreview();
            });
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError("The interactive render scene could not be built: {0}", exception);
            if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                _ = Dispatcher.BeginInvoke(() =>
                {
                    if (request == _interactiveSceneRequest && source == _source && IsVisible)
                        StatusText.Text = UiText.Get("RenderPanel.ModelViewFailed");
                });
        }
    }

    /// <summary>Matches the model camera to the shared navigation camera and current viewport aspect ratio.</summary>
    private void UpdateInteractiveCamera()
    {
        if (_source is null || ImageHost.ActualHeight <= 0) return;
        var camera = CurrentCamera();
        var aspect = ImageHost.ActualWidth / ImageHost.ActualHeight;
        // A shifted lens needs an off-centre frustum; otherwise the ordinary camera is used.
        InteractiveViewport.Camera = (Camera?)ScenePresentation.ToShiftedWpfCamera(camera, aspect, 0.05, 5000)
            ?? ScenePresentation.ToWpfCamera(camera, aspect);
    }

    /// <summary>Switches between the navigable scene and an explicitly requested final image.</summary>
    private void ShowInteractiveScene(bool interactive)
    {
        InteractiveViewport.Visibility = interactive ? Visibility.Visible : Visibility.Collapsed;
        FinalImageBorder.Visibility = interactive ? Visibility.Collapsed : Visibility.Visible;
        InteractiveHintCard.Visibility = interactive ? Visibility.Visible : Visibility.Collapsed;
        if (interactive) UpdateInteractiveCamera();
    }
}
