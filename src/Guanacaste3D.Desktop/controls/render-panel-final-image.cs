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
    // A slow previous grading request must never overwrite a newer slider/filter change.
    private int _refinishVersion;
    private int _renderDocumentVersion;

    /// <summary>Discards a previous project's image and cancels its render when another project is opened.</summary>
    internal void ResetRenderedImageForDocument()
    {
        _renderDocumentVersion++;
        _refinishVersion++;
        _cancel?.Cancel();
        _lastFinal = null;
        _showingFinal = false;
        _activePhotoId = null;
        _photoBaseline = null;
        _batchCancel = true;
        HideStyledPreview();
        SaveButton.IsEnabled = CopyButton.IsEnabled = ShowImageButton.IsEnabled = false;
        ShowInteractiveScene(true);
        RefreshPhotos();
    }
    private bool _renderStarting;

    /// <summary>Re-applies exposure and grading to a finished render without rendering again.</summary>
    private async void RefinishFinal()
    {
        if (_session is null || _rendering || !_showingFinal) return;
        var version = ++_refinishVersion;
        var session = _session;
        var finish = CurrentFinish();
        var elapsed = _lastFinal?.Elapsed ?? TimeSpan.Zero;
        RenderFrame? frame;
        try
        {
            frame = await Task.Run(() => session.Refinish(finish, elapsed));
        }
        catch (ObjectDisposedException)
        {
            // The user closed the panel or started a new render while grading.
            return;
        }
        if (frame is null || version != _refinishVersion || session != _session ||
            _rendering || !_showingFinal) return;
        _lastFinal = frame with { IsFinal = true };
        Show(frame);
    }

    // ---------------------------------------------------------------- final render

    /// <summary>Starts rendering or requests cancellation of the active render operation.</summary>
    private async void RenderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_batchRunning)
        {
            _batchCancel = true;
            _cancel?.Cancel();
            return;
        }
        if (_rendering || _renderingTour)
        {
            _cancel?.Cancel();
            return;
        }
        await RenderFinalAsync();
    }

    /// <summary>Starts a final render with the panel's settings (used by the Render buttons elsewhere in the window).</summary>
    internal async void RequestRender()
    {
        if (_rendering || _renderingTour) return;
        await RenderFinalAsync();
    }

    /// <summary>Stops any render or video in progress; the picture so far is kept.</summary>
    internal void CancelAll() => _cancel?.Cancel();

    /// <summary>Runs a bounded progressive final render and restores controls after completion or cancellation.</summary>
    private async Task RenderFinalAsync()
    {
        if (_source is null || _renderStarting || _rendering || _renderingTour) return;
        if (PreviewToggle.IsChecked == true) PreviewToggle.IsChecked = false;
        var documentVersion = _renderDocumentVersion;
        var source = _source;
        var sceneVersion = source.SceneVersion;
        _renderStarting = true;
        StatusText.Text = UiText.Get("RenderPanel.PreparingScene");
        RenderButton.IsEnabled = false;
        RenderScene? scene;
        try
        {
            scene = await source.GetSceneAsync();
        }
        catch (OutOfMemoryException)
        {
            StatusText.Text = UiText.Get("RenderPanel.NoMemory");
            return;
        }
        catch (Exception ex)
        {
            StatusText.Text = UiText.Format("RenderPanel.Failed", ex.Message);
            return;
        }
        finally
        {
            _renderStarting = false;
            RenderButton.IsEnabled = true;
        }
        // Never render a scene belonging to a project or geometry version that was replaced while loading.
        if (documentVersion != _renderDocumentVersion || !ReferenceEquals(source, _source) || sceneVersion != source.SceneVersion) return;
        if (scene is null)
        {
            StatusText.Text = UiText.Get("RenderPanel.EmptyScene");
            return;
        }

        var quality = Quality();
        var (baseWidth, baseHeight) = SelectedBaseSize();
        var (width, height) = RenderPresets.OutputSize(baseWidth, baseHeight, quality);
        var fit = RenderSafety.FitToMemory(width, height, scene.TriangleCount, scene.EstimatedBytes, RenderSafety.ReadMemory());
        if (!fit.Fits)
        {
            StatusText.Text = UiText.Get("RenderPanel.NoMemory");
            return;
        }
        if (fit.Reduced) StatusText.Text = UiText.Format("RenderPanel.SizeReduced", fit.Width, fit.Height);

        // Respect the machine's RAM and battery plan, then the requested CPU thread cap.
        var plan = QualityPlanner.Plan(RequestedQuality(), _hardware, fit.Width, fit.Height, scene.TriangleCount, scene.EstimatedBytes);
        quality = plan.Quality;
        if (plan.OutputWidth != fit.Width || plan.OutputHeight != fit.Height)
            StatusText.Text = UiText.Format("RenderPanel.SizeReduced", plan.OutputWidth, plan.OutputHeight);
        var settings = LightingFor(quality, ResourceUse());
        settings = settings with
        {
            MaxThreads = Math.Min(settings.MaxThreads, plan.Threads),
            UseRadianceCache = plan.UsesRadianceCache
        };
        var camera = CurrentCamera();
        var job = new RenderJob(plan.OutputWidth, plan.OutputHeight, plan.TargetSamples, null, true,
            CurrentFinish(), Buckets: true) { BucketSize = SelectedBucketSize(), Actors = _actorRig, ActorSeconds = _playTime };

        // The preview stops and gives its memory back: the picture is frozen while the final render is made.
        _preview?.Suspend();
        _session?.Dispose();
        _session = new RenderSession(scene, camera, settings, job);
        _lastFinal = null;
        SaveButton.IsEnabled = CopyButton.IsEnabled = ShowImageButton.IsEnabled = false;
        _cancel = new CancellationTokenSource();
        SetRendering(true);
        StartRenderTelemetry();
        _showingFinal = true;
        ShowInteractiveScene(false);
        var session = _session;
        session.PhaseChanged += phase => Dispatcher.BeginInvoke(() =>
        {
            if (_rendering) StatusText.Text = UiText.Get(phase == "Render.Preparing" ? "RenderPanel.PreparingScene" : "RenderPanel.RenderingSimple");
        });
        session.FrameReady += frame => Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            if (session != _session || documentVersion != _renderDocumentVersion) return;
            if (_showingFinal) Show(frame);
            RenderProgress.Value = Math.Clamp(frame.Progress, 0, 1);
            _estimatedRemaining = frame.Remaining;
            if (!frame.IsFinal)
                StatusText.Text = (_batchLabel is null ? "" : _batchLabel + " · ") + UiText.Get("RenderPanel.RenderingSimple");
        });

        RenderResult? result = null;
        try
        {
            result = await session.RunAsync(_cancel.Token);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            StatusText.Text = UiText.Format("RenderPanel.Failed", ex.Message);
        }
        finally
        {
            SetRendering(false);
            StopRenderTelemetry(result?.Elapsed);
        }
        if (documentVersion != _renderDocumentVersion) return;
        if (result is null)
        {
            _showingFinal = false;
            ShowInteractiveScene(true);
            return;
        }

        _lastFinal = result.LastFrame;
        RenderProgressPanel.Visibility = _lastFinal is not null ? Visibility.Visible : Visibility.Collapsed;
        if (_lastFinal is not null) RenderProgress.Value = Math.Clamp(_lastFinal.Progress, 0, 1);
        RenderRemainingText.Text = "—";
        SaveButton.IsEnabled = CopyButton.IsEnabled = _lastFinal is not null;
        ShowImageButton.IsEnabled = _lastFinal is not null;
        if (result.StoppedForMemory)
            StatusText.Text = UiText.Format("RenderPanel.MemoryStop", result.Samples);
        else if (result.Cancelled)
            StatusText.Text = UiText.Format("RenderPanel.RenderStopped", result.Samples);
        else
            StatusText.Text = UiText.Format("RenderPanel.Rendered", result.Samples, session.Tracer?.Width ?? fit.Width,
                session.Tracer?.Height ?? fit.Height, UiText.Duration(result.Elapsed), result.MegaRaysPerSecond);
        if (result.StoppedForMemory)
        {
            // Let go of the working buffers; the picture itself is already kept.
            session.Dispose();
            _session = null;
            GC.Collect();
        }
    }

    /// <summary>Restores the last completed image after the user returns to interactive navigation.</summary>
    private void ShowImage_Click(object sender, RoutedEventArgs e)
    {
        if (_lastFinal is not { } frame) return;
        _showingFinal = true;
        Show(frame);
        ShowInteractiveScene(false);
    }

    /// <summary>Keeps render controls consistent while final-image work is active.</summary>
    private void SetRendering(bool rendering)
    {
        _rendering = rendering;
        RenderButton.Content = UiText.Get(rendering ? "RenderPanel.Stop" : "RenderPanel.Render");
        PauseButton.IsEnabled = rendering;
        PauseButton.Content = UiText.Get("RenderPanel.Pause");
        if (rendering)
        {
            RenderProgressPanel.Visibility = Visibility.Visible;
            RenderProgress.Value = 0;
        }
        PreviewToggle.IsEnabled = !rendering;
        RenderTourButton.IsEnabled = !rendering;
    }

    /// <summary>Pauses or resumes accumulation while retaining the image rendered so far.</summary>
    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || !_rendering) return;
        if (_session.IsPaused)
        {
            _session.Resume();
            PauseButton.Content = UiText.Get("RenderPanel.Pause");
        }
        else
        {
            _session.Pause();
            PauseButton.Content = UiText.Get("RenderPanel.Resume");
        }
    }

    // ---------------------------------------------------------------- saving

    /// <summary>Returns the displayed image, optionally converting it to an opaque output bitmap.</summary>
    private BitmapSource? CurrentImage(bool opaque)
    {
        if (_lastFinal is not { } frame) return null;
        var source = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, frame.Bgra, frame.Width * 4);
        if (!opaque) return source;
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0);
        converted.Freeze();
        return converted;
    }

    /// <summary>Exports the displayed image to a user-selected file with format-appropriate encoding.</summary>
    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastFinal is null)
        {
            StatusText.Text = UiText.Get("RenderPanel.NothingToSave");
            return;
        }
        var dialog = new SaveFileDialog
        {
            Filter = UiText.Get("RenderPanel.ImageFilter"),
            FileName = SafeName(_source?.ProjectName ?? "render"),
            AddExtension = true,
            DefaultExt = ".png"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            var jpeg = dialog.FilterIndex == 2 || Path.GetExtension(dialog.FileName).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                       Path.GetExtension(dialog.FileName).Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
            BitmapEncoder encoder = jpeg ? new JpegBitmapEncoder { QualityLevel = 95 } : new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(CurrentImage(opaque: jpeg)!));
            using var stream = File.Create(dialog.FileName);
            encoder.Save(stream);
            StatusText.Text = UiText.Format("RenderPanel.Saved", dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, UiText.Get("Dialog.SaveFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Copies the current rendered image to the clipboard after checking image availability.</summary>
    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentImage(opaque: true) is not { } image)
        {
            StatusText.Text = UiText.Get("RenderPanel.NothingToSave");
            return;
        }
        try
        {
            Clipboard.SetImage(image);
            StatusText.Text = UiText.Get("RenderPanel.Copied");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another program holds the clipboard; the user can simply try again.
        }
    }

    /// <summary>Removes unsupported file-name characters before proposing an export name.</summary>
    private static string SafeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "render" : name;
    }

}
