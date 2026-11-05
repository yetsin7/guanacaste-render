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

/// <summary>
/// The Render tab: an interactive model for navigation and an explicitly requested final render. Pressing Render
/// freezes the picture while a high-quality image (or a walk-through video) is
/// generated. Rendering is protected: it is fitted to the free memory beforehand and stopped, keeping the picture so
/// far, if memory runs short.
/// </summary>
public partial class RenderPanel : UserControl
{
    private IRenderSource? _source;
    private SceneNavigator? _navigator;
    private LivePreview? _preview;
    private WriteableBitmap? _bitmap;
    private int _framePending;
    // Monotonic request number: an older, slower scene build must never replace a newer preview.
    private int _sceneRefreshVersion;
    private bool _ready;

    private RenderSession? _session;
    private CancellationTokenSource? _cancel;
    private bool _rendering;
    private RenderFrame? _lastFinal;
    private bool _showingFinal;
    private long _sceneTriangles;
    private long _sceneBytes;

    private readonly CameraPath _path = new();
    private DispatcherTimer? _tourTimer;
    private DateTime _tourStart;
    private bool _renderingTour;

    /// <summary>
    /// Detected hardware without a measurement until calibration finishes; AUTO and the preview size then follow the
    /// measured speed instead of the core count.
    /// </summary>
    private HardwareProfile _hardware = HardwareProfile.Detect();

    public RenderPanel()
    {
        InitializeComponent();
        FillOceanPicker();
        InitializeRenderModeRail();
        InitializeLooks();
        InitializeMaterialGallery();
        SunDatePicker.SelectedDate = DateTime.Today;
        _ready = true;
        SyncFxFromControls();
        IsVisibleChanged += (_, _) => OnVisibilityChanged();
        Unloaded += (_, _) => Shutdown();
        UpdateMemoryEstimate();
        _ = CalibrateAsync(force: false);
    }

    // ---------------------------------------------------------------- wiring

    /// <summary>Optional geometric stone details are isolated to the path-traced scene.</summary>
    private void StoneDetail_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _source is null) return;
        _source.StoneMicrogeometry = StoneDetailCheck.IsChecked == true;
    }

    /// <summary>Connects shared scene, camera, query, and material notifications to the render panel.</summary>
    internal void Attach(IRenderSource source)
    {
        _source = source;
        StoneDetailCheck.IsChecked = source.StoneMicrogeometry;
        _navigator = new SceneNavigator(ImageHost, source.Camera)
        {
            Bounds = source.SceneBounds,
            Pick = (origin, direction) => source.Query?.Pick(origin, direction)
        };
        InitializeFlightControls();
        _navigator.LumionControls = true;
        InitializePhotos();
        InitializeLens();
        InitializeModes();
        _navigator.Changed += OnCameraChanged;
        source.SceneChanged += () => Dispatcher.BeginInvoke(RefreshScene);
        source.SceneChanged += () => Dispatcher.BeginInvoke(RefreshInteractiveScene);
        source.SceneChanged += () => Dispatcher.BeginInvoke(RefreshMaterialRows);
        source.SceneChanged += () => Dispatcher.BeginInvoke(RefreshRenderScenes);
        RefreshMaterialRows();
        RefreshRenderScenes();
        RefreshPhotos();
        SyncProjectSite();
        source.QueryChanged += () => Dispatcher.BeginInvoke(ApplyCollision);
        ApplyCollision();
    }

    /// <summary>Lets the main window reuse the navigator's collision and key handling.</summary>
    internal SceneNavigator? Navigator => _navigator;

    /// <summary>The element that receives the mouse, whose size maps clicks to rays through the picture.</summary>
    internal FrameworkElement Surface => ImageHost;

    /// <summary>Refreshes the navigator collision structure after a scene query becomes available.</summary>
    private void ApplyCollision()
    {
        if (_navigator is null || _source is null) return;
        _navigator.Collision = _source.Query?.Collision();
        _navigator.CollisionEnabled = CollisionCheck.IsChecked == true;
    }

    /// <summary>Suspends hidden previews and resumes them when this workspace becomes visible.</summary>
    private void OnVisibilityChanged()
    {
        if (!IsVisible)
        {
            _interactiveSceneRequest++;
            StopInteractivePreview();
            HideStyledPreview();
            _flightTimer?.Stop();
            _preview?.Suspend();
            StopTour();
            return;
        }
        SyncProjectSite();
        ShowInteractiveScene(!_showingFinal);
        RefreshRenderScenes();
        RefreshInteractiveScene();
        if (_preview is null && PreviewToggle.IsChecked == true) StartPreview();
        else if (PreviewToggle.IsChecked == true && !_showingFinal) ResumePreview();
        UpdateMemoryEstimate();
    }

    /// <summary>Stops preview workers, camera playback, and outstanding render cancellation tokens.</summary>
    private void Shutdown()
    {
        _interactiveSceneRequest++;
        StopInteractivePreview();
        HideStyledPreview();
        _flightTimer?.Stop();
        _photoWatch?.Stop();
        try { SaveMovieNow(); } catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException) { }
        // Invalidate asynchronous scene requests before disposing the preview.
        _sceneRefreshVersion++;
        _cancel?.Cancel();
        StopRenderTelemetry();
        StopTour();
        _preview?.Dispose();
        _preview = null;
        _session?.Dispose();
        _session = null;
    }

    // ---------------------------------------------------------------- live preview

    /// <summary>Starts progressive rendering and marshals frame and status notifications onto the dispatcher.</summary>
    private void StartPreview()
    {
        if (_source is null) return;
        _preview = new LivePreview { MaximumSamples = 40 };
        _preview.FrameReady += OnPreviewFrame;
        _preview.PhaseChanged += phase => Dispatcher.BeginInvoke(() =>
        {
            if (!_showingFinal && !_rendering)
                StatusText.Text = UiText.Get(phase == "Render.Preparing" ? "RenderPanel.PreparingScene" : "RenderPanel.Idle");
        });
        _preview.Failed += _ => Dispatcher.BeginInvoke(() => StatusText.Text = UiText.Get("RenderPanel.PreviewFailed"));
        _preview.Start();
        PushPreviewState();
        RefreshScene();
    }

    /// <summary>Resumes the preview or starts its worker when the workspace first becomes visible.</summary>
    private void ResumePreview()
    {
        if (_preview is null)
        {
            StartPreview();
            return;
        }
        PushPreviewState();
        _preview.Resume();
        RefreshScene();
    }

    /// <summary>Publishes the current camera, lighting, finish, and output dimensions to the preview worker.</summary>
    private void PushPreviewState()
    {
        if (_preview is null) return;
        var (width, height) = PreviewSize();
        _preview.SetSize(width, height);
        _preview.SetSettings(PreviewSettings());
        _preview.SetFinish(CurrentFinish());
        _preview.SetCamera(CurrentCamera());
    }

    /// <summary>
    /// Refreshes the live scene without letting a slow, superseded build overwrite a newer edit.
    /// A failed build is reported in the panel instead of escaping from an async-void event handler.
    /// </summary>
    private async void RefreshScene()
    {
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted) _ = Dispatcher.BeginInvoke((Action)RefreshScene);
            return;
        }
        if (_source is null || !IsVisible) return;
        var source = _source;
        var sceneVersion = source.SceneVersion;
        var request = ++_sceneRefreshVersion;
        RenderScene? scene;
        try
        {
            scene = await source.GetSceneAsync();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            if (request == _sceneRefreshVersion && sceneVersion == source.SceneVersion && !Dispatcher.HasShutdownStarted)
                StatusText.Text = UiText.Get("RenderPanel.PreviewFailed") + ": " + exception.Message;
            return;
        }

        // WPF's synchronization context normally resumes on the dispatcher. Guard against
        // alternate source implementations that complete without it.
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted)
                _ = Dispatcher.BeginInvoke((Action)(() =>
                {
                    if (request == _sceneRefreshVersion && sceneVersion == source.SceneVersion && ReferenceEquals(source, _source) && IsVisible)
                        ApplyPreviewScene(scene);
                }));
            return;
        }
        if (request != _sceneRefreshVersion || sceneVersion != source.SceneVersion || !ReferenceEquals(source, _source) || !IsVisible) return;
        ApplyPreviewScene(scene);
    }

    /// <summary>Applies an asynchronously built scene only after returning to its owning UI dispatcher.</summary>
    private void ApplyPreviewScene(RenderScene? scene)
    {
        RefreshHighlightPicker(scene);
        if (scene is null)
        {
            _sceneTriangles = 0;
            if (!_rendering && !_showingFinal) StatusText.Text = UiText.Get("RenderPanel.EmptyScene");
            return;
        }
        _sceneTriangles = scene.TriangleCount;
        _sceneBytes = scene.EstimatedBytes;
        UpdateMemoryEstimate();
        RefreshActorRig();
        if (PreviewToggle.IsChecked == true && !_showingFinal && !_rendering)
        {
            _preview?.SetScene(scene);
            ApplyMotionPreview();
        }
    }

    /// <summary>Coalesces worker frames into dispatcher updates to avoid accumulating UI work.</summary>
    private void OnPreviewFrame(RenderFrame frame)
    {
        // Drop the picture if the previous one has not been shown yet: the newest always wins.
        if (Interlocked.Exchange(ref _framePending, 1) == 1) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            Volatile.Write(ref _framePending, 0);
            if (_showingFinal || _rendering || PreviewToggle.IsChecked != true || !IsVisible) return;
            Show(frame);
            StatusText.Text = frame.Samples >= frame.TargetSamples
                ? UiText.Format("RenderPanel.PreviewReady", frame.Samples)
                : UiText.Format("RenderPanel.PreviewStatus", frame.Samples, frame.TargetSamples);
        });
    }

    /// <summary>Copies a completed render frame into the image displayed by the workspace.</summary>
    private void Show(RenderFrame frame)
    {
        if (_bitmap is null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
        {
            _bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null);
            PreviewImage.Source = _bitmap;
        }
        _bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Bgra, frame.Width * 4, 0);
    }

    /// <summary>Bounds preview resolution to the visible image surface and interactive rendering budget.</summary>
    private (int Width, int Height) PreviewSize()
    {
        var aspect = ImageHost.ActualHeight > 0 ? ImageHost.ActualWidth / ImageHost.ActualHeight : 16.0 / 9;
        var height = QualityPlanner.PreviewHeight(_hardware, ResourceUse());
        if (_interactivePreview) height = Math.Min(height, 144);
        return ((int)Math.Round(height * Math.Clamp(aspect, 0.5, 3)), height);
    }

    /// <summary>Adapts preview dimensions and shared-camera projection after layout changes.</summary>
    private void ImageHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_ready) return;
        UpdateInteractiveCamera();
        NotifySelectionViewportChanged();
        if (_preview is not null)
        {
            var (width, height) = PreviewSize();
            _preview.SetSize(width, height);
        }
        UpdateMemoryEstimate();
    }

}
