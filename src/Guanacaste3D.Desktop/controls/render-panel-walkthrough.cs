using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Navigation;
using Guanacaste3D.Core.Rendering;
using Microsoft.Win32;

namespace Guanacaste3D.Desktop.Controls;

public partial class RenderPanel
{
    // ---------------------------------------------------------------- walk-through

    /// <summary>Parses the duration of a camera mark within the supported playback range.</summary>
    private float MarkSeconds()
    {
        var text = MarkSecondsInput.Text.Replace(',', '.');
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value is > 0.05f and < 600 ? value : 4;
    }

    /// <summary>Stores a valid duration on the currently selected camera mark.</summary>
    private void MarkSeconds_LostFocus(object sender, RoutedEventArgs e) =>
        MarkSecondsInput.Text = MarkSeconds().ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Captures the shared camera as a named step in the current walkthrough.</summary>
    private void AddMark_Click(object sender, RoutedEventArgs e)
    {
        if (_source is null) return;
        var camera = _source.Camera;
        var focus = _source.Query?.Pick(camera.Position, camera.Forward) is { } distance ? Math.Max(1f, distance) : 10f;
        _path.Add(new CameraMark(camera.Position, camera.Position + camera.Forward * focus, camera.FieldOfViewDegrees, MarkSeconds()));
        RefreshMarks();
        MarksList.SelectedIndex = _path.Marks.Count - 1;
    }

    /// <summary>Synchronizes the camera-mark list with the current walkthrough path.</summary>
    private void RefreshMarks()
    {
        var selected = MarksList.SelectedIndex;
        MarksList.Items.Clear();
        for (var i = 0; i < _path.Marks.Count; i++)
            MarksList.Items.Add(UiText.Format("RenderPanel.MarkName", i + 1, i == 0 ? 0 : _path.Marks[i].SecondsFromPrevious));
        MarksList.SelectedIndex = Math.Min(selected, MarksList.Items.Count - 1);
        _playTime = Math.Min(_playTime, _path.Duration);
        ScheduleMovieSave();
        RedrawTimeline();
    }

    /// <summary>Updates walkthrough controls for the selected camera mark.</summary>
    private void MarksList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        GoToMarkButton.IsEnabled = RemoveMarkButton.IsEnabled = MarksList.SelectedIndex >= 0;
        RedrawTimeline();
    }

    /// <summary>Applies the selected camera mark to the shared model and render camera.</summary>
    private void GoToMark_Click(object sender, RoutedEventArgs e)
    {
        if (_source is null || MarksList.SelectedIndex < 0) return;
        var mark = _path.Marks[MarksList.SelectedIndex];
        ApplyCamera(mark.Position, mark.Target, mark.FieldOfViewDegrees);
    }

    /// <summary>Removes the selected walkthrough mark and refreshes its list.</summary>
    private void RemoveMark_Click(object sender, RoutedEventArgs e)
    {
        if (MarksList.SelectedIndex < 0) return;
        _path.RemoveAt(MarksList.SelectedIndex);
        RefreshMarks();
    }

    /// <summary>Updates shared position, orientation, and lens parameters from a walkthrough sample.</summary>
    private void ApplyCamera(Vector3 position, Vector3 target, float fieldOfView)
    {
        if (_source is null) return;
        _source.Camera.Position = position;
        _source.Camera.FieldOfViewDegrees = fieldOfView;
        _source.Camera.LookAt(target);
        OnCameraChanged();
    }

    /// <summary>Starts or stops timed camera-path playback on the UI dispatcher.</summary>
    private void PlayTour_Click(object sender, RoutedEventArgs e)
    {
        if (_tourTimer is not null)
        {
            StopTour();
            return;
        }
        if (_path.Marks.Count < 2)
        {
            StatusText.Text = UiText.Get("RenderPanel.TourNeedsMarks");
            return;
        }
        _tourStart = DateTime.UtcNow;
        PlayTourButton.Content = UiText.Get("RenderPanel.StopTour");
        SyncPlayButton(true);
        _tourTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _tourTimer.Tick += (_, _) =>
        {
            var seconds = (float)(DateTime.UtcNow - _tourStart).TotalSeconds;
            var (position, target, fieldOfView) = _path.Sample(seconds);
            ApplyCamera(position, target, fieldOfView);
            UpdatePlayhead(seconds);
            if (seconds >= _path.Duration) StopTour();
        };
        _tourTimer.Start();
    }

    /// <summary>Releases the playback timer and restores the walkthrough action label.</summary>
    private void StopTour()
    {
        _tourTimer?.Stop();
        _tourTimer = null;
        PlayTourButton.Content = UiText.Get("RenderPanel.PlayTour");
        SyncPlayButton(false);
    }

    /// <summary>
    /// Renders the camera path into an .mp4 video (H.264, made with the encoder Windows has) with cancellation and memory checks. If this
    /// computer cannot encode, or the encoder fails, the frames are kept as numbered images in a folder instead, and the message says so.
    /// </summary>
    private async void RenderTour_Click(object sender, RoutedEventArgs e)
    {
        if (_source is null || _rendering || _renderingTour) return;
        if (_path.Marks.Count < 2)
        {
            StatusText.Text = UiText.Get("RenderPanel.TourNeedsMarks");
            return;
        }
        var dialog = new SaveFileDialog
        {
            Title = UiText.Get("RenderPanel.RenderVideo"), Filter = "Video MP4|*.mp4", DefaultExt = ".mp4", AddExtension = true,
            FileName = SafeName(_source.ProjectName), OverwritePrompt = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { await RunTourAsync(dialog.FileName); }
        catch (Exception ex)
        {
            // This is an async void button: whatever goes wrong must end here, with a message, never as a program that closes.
            CrashReporter.Log("movie", ex);
            StatusText.Text = UiText.Format("RenderPanel.Failed", ex.Message);
        }
    }

    /// <summary>Renders the camera path to the given .mp4 (the part after the file is chosen, so it can be run without a dialog).</summary>
    private async Task RunTourAsync(string videoPath)
    {
        if (_source is null || _rendering || _renderingTour) return;
        var scene = await _source.GetSceneAsync();
        if (scene is null) return;

        var quality = Quality();
        var (baseWidth, baseHeight) = SelectedBaseSize();
        var (width, height) = RenderPresets.OutputSize(baseWidth, baseHeight, quality);
        var fit = RenderSafety.FitToMemory(width, height, scene.TriangleCount, scene.EstimatedBytes, RenderSafety.ReadMemory());
        if (!fit.Fits)
        {
            StatusText.Text = UiText.Get("RenderPanel.NoMemory");
            return;
        }
        var fps = int.TryParse((FpsPicker.SelectedItem as ComboBoxItem)?.Tag as string, out var chosen) ? chosen : 30;
        // The frames only go to this folder if the video cannot be made.
        var folder = Path.Combine(Path.GetDirectoryName(videoPath) ?? ".", Path.GetFileNameWithoutExtension(videoPath) + "-cuadros");
        // Video frames need far fewer samples than a still: the next frame is nearly the same picture.
        // The denoiser and the frames made between rendered ones do part of the work, so each real frame needs fewer samples than a still.
        var samples = Math.Max(8, RenderPresets.Profile(quality).TargetSamples / 8);
        var keyFps = int.TryParse((KeyFpsPicker.SelectedItem as ComboBoxItem)?.Tag as string, out var keyChosen) ? keyChosen : 0;
        var lightingDivisor = int.TryParse((MovieLightingPicker.SelectedItem as ComboBoxItem)?.Tag as string, out var lightingChosen) ? lightingChosen : 1;

        _preview?.Suspend();
        _renderingTour = true;
        _cancel = new CancellationTokenSource();
        RenderButton.Content = UiText.Get("RenderPanel.Stop");
        RenderTourButton.IsEnabled = false;
        RenderProgressPanel.Visibility = Visibility.Visible;
        RenderProgress.Visibility = Visibility.Visible;
        RenderProgress.Value = 0;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var progress = new Progress<TourProgress>(p =>
        {
            var fraction = (p.Frame + (p.Samples > 0 ? p.Sample / (double)p.Samples : 0)) / Math.Max(1, p.Total);
            RenderProgress.Value = Math.Clamp(fraction, 0, 1);
            RenderElapsedText.Text = timer.Elapsed.ToString(@"hh\:mm\:ss");
            RenderRemainingText.Text = fraction > .001 ? TimeSpan.FromSeconds(timer.Elapsed.TotalSeconds * (1 - fraction) / fraction).ToString(@"hh\:mm\:ss") : "—";
            StatusText.Text = $"Película: {p.Frame}/{p.Total} fotogramas · {fraction:P0}" + (p.Samples > 0 ? $" · muestra {p.Sample}/{p.Samples}" : "");
        });
        // The encoder is tried first, before any frame is rendered, so a computer that cannot make video says so at once.
        Mp4VideoWriter? video = null;
        if (!Mp4VideoWriter.TryCreate(videoPath, fit.Width, fit.Height, fps, out video, out var problem))
            StatusText.Text = UiText.Format("RenderPanel.VideoFallback", folder, problem ?? "");
        TourResult? result = null;
        string? failure = null;
        try
        {
            result = await TourRenderer.RenderAsync(scene, _path, LightingFor(quality, ResourceUse()), fit.Width, fit.Height, fps, samples,
                CurrentFinish(), folder, progress, _cancel.Token, LayersForRender(), ActorRig.From(_source.GetActors(), CarLibrary.Load), video, keyFps, lightingDivisor);
        }
        catch (Exception ex)
        {
            // Whatever goes wrong, the panel must come back and say it, not stay "rendering" with nothing to show.
            failure = ex.Message;
        }
        finally
        {
            if (video is not null)
            {
                try { await Task.Run(video.Dispose); }
                catch (Exception ex) { CrashReporter.Log("movie file", ex); }
            }
            _renderingTour = false;
            RenderButton.Content = UiText.Get("RenderPanel.Render");
            RenderTourButton.IsEnabled = true;
            RenderProgressPanel.Visibility = Visibility.Collapsed;
            _preview?.Resume();
        }
        if (failure is not null)
        {
            StatusText.Text = UiText.Format("RenderPanel.Failed", failure);
            return;
        }
        if (result is null) return;
        var videoMade = video is { Failed: false } && File.Exists(videoPath) && result.FramesWritten > 0;
        if (video is { Failed: true })
            StatusText.Text = UiText.Format("RenderPanel.VideoFallback", folder, video.Problem ?? "");
        else if (videoMade)
        {
            StatusText.Text = result.FramesWritten == result.TotalFrames
                ? UiText.Format("RenderPanel.VideoDone", videoPath, result.FramesWritten)
                : UiText.Format("RenderPanel.VideoStopped", result.FramesWritten, videoPath);
            RevealInExplorer(videoPath);
        }
        else
            StatusText.Text = result.FramesWritten == result.TotalFrames
                ? UiText.Format("RenderPanel.TourDone", result.FramesWritten, result.Folder)
                : result.StoppedForMemory
                    ? UiText.Format("RenderPanel.MemoryStop", result.FramesWritten)
                    : UiText.Format("RenderPanel.TourStopped", result.FramesWritten, result.Folder);
    }

    /// <summary>Shows the finished file selected in a File Explorer window, so a movie is never lost in a folder.</summary>
    private static void RevealInExplorer(string path)
    {
        if (Environment.GetEnvironmentVariable("G3D_NO_EXPLORER") == "1") return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }
}
