using Guanacaste3D.Core.Rendering;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Navigation;
using Guanacaste3D.Desktop;
using Guanacaste3D.Desktop.Controls;

internal static partial class InteractionChecks
{
    /// <summary>
    /// Started without a document, the application shows the Home screen; its breeze-block house sample opens in the
    /// Render tab with the saved camera, sun and filters, and opening it hides the Home screen.
    /// </summary>
    private static void StartupSampleChecks()
    {
        var opened = new MainWindow(showStartScreen: true) { Width = 1440, Height = 900, WindowState = WindowState.Normal };
        // Pump() drains this window; set it here so the checks also run alone (G3D_ONLY=render), as the cobogó ones do.
        var previous = _window;
        _window = opened;
        try
        {
            var home = (StartScreen)opened.FindName("StartScreenControl");
            var host = (IStartScreenHost)opened;
            // Evidence for visual review: the Home screen on its samples page.
            opened.Show();
            ((RadioButton)home.FindName("SamplesTab")).IsChecked = true;
            Pump();
            Directory.CreateDirectory("artifacts/interaction-evidence");
            var picture = new RenderTargetBitmap((int)opened.ActualWidth, (int)opened.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            picture.Render(opened);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(picture));
            using (var stream = File.Create("artifacts/interaction-evidence/home-screen.png")) encoder.Save(stream);
            Check(home.Visibility == Visibility.Visible && host.Samples.Any(sample => sample.Id == "cobogo") &&
                  host.Samples.All(sample => Application.Current.TryFindResource(sample.TitleKey) is string),
                "Startup shows the Home screen with its samples");
            Check(typeof(MainWindow).Assembly.GetManifestResourceInfo("Guanacaste3D.BreezeBlockHouse.g3d") is not null,
                "The complete sample is embedded in the desktop application");

            host.OpenSample(host.Samples.Single(sample => sample.Id == "cobogo"));
            var session = (DocumentSession)typeof(MainWindow).GetField("_session", PrivateInstance)!.GetValue(opened)!;
            var camera = (NavigationCamera)typeof(MainWindow).GetField("_navigation", PrivateInstance)!.GetValue(opened)!;
            var tabs = (TabControl)opened.FindName("ViewTabs");
            var setup = session.Document.RenderSetup
                ?? throw new InvalidOperationException("The breeze-block house sample has no saved render setup.");
            Check(home.Visibility == Visibility.Collapsed && session.Document.ComponentFamilies.Count >= 9 &&
                  session.Document.Sheets.Count == 6 &&
                  typeof(MainWindow).GetField("_filePath", PrivateInstance)!.GetValue(opened) is null,
                "The breeze-block house opens from Home, with its families and sheets, as an untitled copy");
            Check(tabs.SelectedIndex == 6 && Math.Abs(camera.Position.Y - setup.CameraY) < 1e-3 &&
                  Math.Abs(camera.PitchDegrees - setup.PitchDegrees) < 1e-3,
                "The sample opens on the Render tab from its saved camera");
            var panel = (RenderPanel)opened.FindName("RenderPanelControl");
            var viewport = (System.Windows.Controls.Viewport3D)panel.FindName("InteractiveViewport");
            var previewToggle = (System.Windows.Controls.Primitives.ToggleButton)panel.FindName("PreviewToggle");
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (viewport.Children.Count <= 2 && DateTime.UtcNow < deadline) Pump();
            Check(viewport.Visibility == Visibility.Visible && viewport.Children.Count > 2 &&
                  viewport.Camera is not null && previewToggle.IsChecked != true &&
                  typeof(RenderPanel).GetField("_preview", PrivateInstance)!.GetValue(panel) is null,
                "Render opens as an interactive 3D model without starting progressive ray tracing");
            // A worker-thread scene notification must rebuild the WPF viewport through its dispatcher.
            viewport.Children.Clear();
            Task.Run(() => typeof(RenderPanel).GetMethod("RefreshInteractiveScene", PrivateInstance)!.Invoke(panel, null))
                .GetAwaiter().GetResult();
            var workerDeadline = DateTime.UtcNow.AddSeconds(90);
            while (viewport.Children.Count <= 2 && DateTime.UtcNow < workerDeadline) Pump();
            Check(viewport.Children.Count > 2 && viewport.Camera is not null,
                "Worker-thread scene refresh safely returns to the WPF dispatcher");
            var oldPosition = ((System.Windows.Media.Media3D.PerspectiveCamera)viewport.Camera!).Position;
            camera.Position += new System.Numerics.Vector3(1, 0, 0);
            typeof(RenderPanel).GetMethod("OnCameraChanged", PrivateInstance)!.Invoke(panel, null);
            Check(((System.Windows.Media.Media3D.PerspectiveCamera)viewport.Camera).Position != oldPosition,
                "Render navigation updates the interactive model camera immediately");
            // Delay the real scene task: repeated clicks must not create competing render sessions.
            var sceneTaskField = typeof(MainWindow).GetField("_sceneTask", PrivateInstance)!;
            var originalSceneTask = sceneTaskField.GetValue(opened);
            var renderMethod = typeof(RenderPanel).GetMethod("RenderFinalAsync", PrivateInstance)!;
            var renderButton = (Button)panel.FindName("RenderButton");
            var pendingScene = new TaskCompletionSource<Guanacaste3D.Core.Rendering.RenderScene?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            sceneTaskField.SetValue(opened, pendingScene.Task);
            // This console-driven STA test does not install the DispatcherSynchronizationContext that WPF
            // normally supplies. Install it for awaited UI operations and restore the test context afterwards.
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(
                new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));
            try
            {
                var first = (Task)renderMethod.Invoke(panel, null)!;
                var duplicate = (Task)renderMethod.Invoke(panel, null)!;
                Check(!first.IsCompleted && duplicate.IsCompletedSuccessfully && !renderButton.IsEnabled &&
                      typeof(RenderPanel).GetField("_session", PrivateInstance)!.GetValue(panel) is null,
                    "A second render request cannot start while scene preparation is pending");
                pendingScene.SetResult(null);
                var renderDeadline = DateTime.UtcNow.AddSeconds(5);
                while (!first.IsCompleted && DateTime.UtcNow < renderDeadline) Pump();
                Check(first.IsCompletedSuccessfully && renderButton.IsEnabled,
                    "Finishing scene preparation releases the render-start guard");

                var failedScene = new TaskCompletionSource<Guanacaste3D.Core.Rendering.RenderScene?>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                sceneTaskField.SetValue(opened, failedScene.Task);
                var failed = (Task)renderMethod.Invoke(panel, null)!;
                failedScene.SetException(new InvalidDataException("simulated scene failure"));
                renderDeadline = DateTime.UtcNow.AddSeconds(5);
                while (!failed.IsCompleted && DateTime.UtcNow < renderDeadline) Pump();
                Check(failed.IsCompletedSuccessfully && renderButton.IsEnabled,
                    "Scene-build errors are reported without leaving Render disabled");
            }
            finally
            {
                pendingScene.TrySetResult(null);
                sceneTaskField.SetValue(opened, originalSceneTask);
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
            var captured = panel.CaptureSetup(camera);
            Check(captured.Width == setup.Width && captured.Height == setup.Height && captured.DayOfYear == setup.DayOfYear &&
                  Math.Abs(captured.SolarHour - setup.SolarHour) < 1e-6 && captured.Look == setup.Look && captured.Bloom == setup.Bloom &&
                  captured.Quality == setup.Quality,
                "The Render panel shows the sample's size, sun, quality and filters");
            var savedPosition = camera.Position;
            session.AddRenderScene("Exterior", captured);
            typeof(RenderPanel).GetMethod("RefreshRenderScenes", PrivateInstance)!.Invoke(panel, null);
            var scenePicker = (ComboBox)panel.FindName("ScenePicker");
            scenePicker.SelectedIndex = 0;
            camera.Position += new System.Numerics.Vector3(3, 0, 0);
            ((Button)panel.FindName("OpenSceneButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(System.Numerics.Vector3.Distance(camera.Position, savedPosition) < 0.001,
                "Opening a named render scene restores its saved camera");
            LayerAnimationChecks(opened, panel);
            SiteSunChecks(opened, panel);
            MovieStorageChecks(opened, panel);
            EffectControlChecks(panel);
            RenderExtrasChecks(opened, panel);

            // Photos and movies end to end, the way a person asks for them: a draft photo of the house, and a short movie saved as an .mp4.
            // Like the real window, the continuations of the async buttons must come back to the interface thread.
            var contextBefore = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));
            try
            {
            var qualityPicker = (ComboBox)panel.FindName("QualityPicker");
            var sizePicker = (ComboBox)panel.FindName("SizePicker");
            qualityPicker.SelectedIndex = 1;
            sizePicker.SelectedIndex = 0;
            var lastFinalField = typeof(RenderPanel).GetField("_lastFinal", PrivateInstance)!;
            var photoStart = DateTime.UtcNow;
            ((Button)panel.FindName("RenderButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var photoDeadline = DateTime.UtcNow.AddMinutes(4);
            while (lastFinalField.GetValue(panel) is null && DateTime.UtcNow < photoDeadline) Pump();
            var photo = lastFinalField.GetValue(panel) as RenderFrame;
            double mean = photo is null ? 0 : Enumerable.Range(0, photo.Bgra.Length / 16).Average(i => photo.Bgra[i * 16 + 1]);
            Check(photo is { Width: > 100, Height: > 50 } && photo.Samples >= 1 && mean > 8,
                $"A photo of the house renders from the Render button and shows a picture, not black ({photo?.Width}×{photo?.Height}, {photo?.Samples} samples, {(DateTime.UtcNow - photoStart).TotalSeconds:0} s)");

            var path = (CameraPath)typeof(RenderPanel).GetField("_path", PrivateInstance)!.GetValue(panel)!;
            path.Clear();
            path.Add(new CameraMark(camera.Position, camera.Position + camera.Forward * 5, 60, 0));
            path.Add(new CameraMark(camera.Position + new System.Numerics.Vector3(1.5f, 0, 0.3f), camera.Position + camera.Forward * 5, 60, 0.5f));
            var movieFile = Path.Combine(Path.GetTempPath(), $"g3d-movie-{Guid.NewGuid():N}.mp4");
            try
            {
                var movieTask = (Task)typeof(RenderPanel).GetMethod("RunTourAsync", PrivateInstance)!.Invoke(panel, [movieFile])!;
                var movieDeadline = DateTime.UtcNow.AddMinutes(6);
                while (!movieTask.IsCompleted && DateTime.UtcNow < movieDeadline) Pump();
                var movieStatus = ((TextBlock)panel.FindName("StatusText")).Text;
                if (!movieTask.IsCompleted) throw new InvalidOperationException("The movie did not finish in time: " + movieStatus);
                var movieBytes = File.Exists(movieFile) ? File.ReadAllBytes(movieFile) : [];
                var movieText = System.Text.Encoding.Latin1.GetString(movieBytes);
                Check(movieTask.IsCompletedSuccessfully && movieBytes.Length > 3000 && movieText.Substring(4, 4) == "ftyp" && movieText.Contains("moov") && movieText.Contains("avc1"),
                    $"A movie renders from the camera path into an .mp4 file ({movieBytes.Length} bytes): {movieStatus}");
                Check(!((TextBlock)panel.FindName("StatusText")).Text.Contains("No se pudo", StringComparison.Ordinal) && panel.FindName("RenderTourButton") is Button { IsEnabled: true },
                    "After the movie the panel is back to normal and says nothing went wrong");
            }
            finally { if (File.Exists(movieFile)) File.Delete(movieFile); }
            qualityPicker.SelectedIndex = 0;
            }
            finally { SynchronizationContext.SetSynchronizationContext(contextBefore); }


            // A recent list in a scratch folder: it keeps the newest first and forgets on request.
            var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"g3d-recents-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(folder);
            // RecentDocuments now prunes missing paths: create real files to exercise ordering and removal.
            System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "a.g3d"), "test");
            System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "b.g3d"), "test");
            var recents = new RecentDocuments(folder);
            recents.Add(System.IO.Path.Combine(folder, "a.g3d"));
            recents.Add(System.IO.Path.Combine(folder, "b.g3d"));
            recents.Add(System.IO.Path.Combine(folder, "a.g3d"));
            var listed = recents.Load();
            recents.Remove(System.IO.Path.Combine(folder, "b.g3d"));
            Check(listed.Count == 2 && listed[0].Path.EndsWith("a.g3d", StringComparison.OrdinalIgnoreCase) &&
                  recents.Load().Count == 1, "Recent projects keep the newest first, without duplicates, and can be removed");
            System.IO.Directory.Delete(folder, recursive: true);
        }
        finally
        {
            typeof(MainWindow).GetField("_dirty", PrivateInstance)!.SetValue(opened, false);
            opened.Close();
            _window = previous;
        }
    }
}
