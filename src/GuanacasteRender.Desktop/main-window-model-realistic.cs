using Guanacaste3D.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private LivePreview? _modelRealisticPreview;
    private Image _modelRealisticImage = null!;
    private int _modelPreviewVersion = -1;
    private ModelDetailLevel _modelPreviewDetail;
    private int _modelPreviewRequest;
    private int _modelPreviewFramePending;
    private Exception? _modelPreviewFailure;

    /// <summary>Places a progressive image over the model while retaining its normal ray-picking and navigation surface.</summary>
    private void InitializeModelRealisticPreview()
    {
        _modelRealisticImage = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        ModelSurface.Child = null;
        var layers = new Grid();
        layers.Children.Add(ModelViewport);
        layers.Children.Add(_modelRealisticImage);
        ModelSurface.Child = layers;
        SceneChanged += RefreshModelRealisticPreview;
    }

    /// <summary>Starts or suspends realistic rendering according to the active style, view, and reveal mode.</summary>
    private async void RefreshModelRealisticPreview()
    {
        if (_modelRealisticImage is null) return;
        var enabled = _modelDisplayStyle == ModelDisplayStyle.Realistic && Model3DVisible && !_revealHidden && !_xRay;
        if (!enabled)
        {
            _modelRealisticPreview?.Suspend();
            _modelRealisticImage.Visibility = Visibility.Collapsed;
            _modelPreviewVersion = -1;
            return;
        }
        if (_modelRealisticPreview is null)
        {
            _modelRealisticPreview = new LivePreview { MaximumSamples = 24 };
            _modelRealisticPreview.FrameReady += ModelPreviewFrameReady;
            _modelRealisticPreview.Failed += ModelPreviewFailed;
            _modelRealisticPreview.Start();
        }
        _modelRealisticPreview.SetSettings(new RenderSettings
        {
            SunDirection = ModelSunDirection(), SunIntensity = _modelSunPreset == "Off" ? 0 : 2,
            CastShadows = _modelShadows,
            SkyIntensity = (float)(_modelAmbient * 2), MaxBounces = 2, MaxThreads = Math.Max(1, Environment.ProcessorCount / 3)
        });
        UpdateModelRealisticCamera();
        if (_modelPreviewVersion == _sceneVersion && _modelPreviewDetail == _modelDetail) return;
        var request = ++_modelPreviewRequest;
        var version = _sceneVersion;
        var document = ModelDisplayDocument();
        var detail = _modelDetail;
        var materials = document.RenderMaterials.ToList();
        var projectFile = _filePath;
        var linkedModels = _links.Visible().ToArray();
        try
        {
            var scene = await Task.Run(() =>
            {
                var mesh = BuildingMesher.Build(document, includeGround: true, plants: detail == ModelDetailLevel.Fine ? PlantDetail.Render : PlantDetail.Model);
                if (detail == ModelDetailLevel.Coarse) mesh = SceneDetailFilter.Coarse(mesh);
                var builder = new RenderSceneBuilder().AddBuilding(mesh,
                    RenderTextureLibrary.Resolver(materials, projectFile, _artificialLights));
                foreach (var (link, loaded) in linkedModels) builder.AddModel(loaded.Model, link.Placement);
                return builder.Build();
            });
            if (request != _modelPreviewRequest || version != _sceneVersion || _modelDisplayStyle != ModelDisplayStyle.Realistic ||
                !Model3DVisible || _revealHidden || _xRay || !IsLoaded) return;
            _modelPreviewVersion = version;
            _modelPreviewDetail = detail;
            _modelRealisticPreview.SetScene(scene);
            _modelRealisticPreview.Resume();
        }
        catch (Exception exception) { ModelPreviewFailed(exception); }
    }

    /// <summary>Updates realistic projection using the exact viewport aspect ratio and shared navigation camera.</summary>
    private void UpdateModelRealisticCamera()
    {
        if (_modelRealisticPreview is null || _modelDisplayStyle != ModelDisplayStyle.Realistic || !Model3DVisible) return;
        var aspect = Math.Max(1, ModelSurface.ActualWidth) / Math.Max(1, ModelSurface.ActualHeight);
        var height = Math.Min(288, Math.Max(48, (int)(960 / aspect)));
        _modelRealisticPreview.SetSize(Math.Max(64, (int)Math.Round(height * aspect)), height);
        _modelRealisticPreview.SetCamera(_navigation.ToRenderCamera());
    }

    /// <summary>Copies worker-owned pixels before queuing at most one UI publication at a time.</summary>
    private void ModelPreviewFrameReady(RenderFrame frame)
    {
        if (Interlocked.Exchange(ref _modelPreviewFramePending, 1) != 0 || Dispatcher.HasShutdownStarted) return;
        var pixels = frame.Bgra.ToArray();
        Dispatcher.BeginInvoke(() =>
        {
            Interlocked.Exchange(ref _modelPreviewFramePending, 0);
            if (_modelDisplayStyle != ModelDisplayStyle.Realistic || _revealHidden || _xRay || !Model3DVisible) return;
            var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, pixels, frame.Width * 4);
            bitmap.Freeze();
            var firstFrame = _modelRealisticImage.Visibility != Visibility.Visible;
            _modelRealisticImage.Source = bitmap;
            _modelRealisticImage.Visibility = Visibility.Visible;
            if (firstFrame || _modelPreviewFailure is not null)
            {
                _modelPreviewFailure = null;
                StatusText.Text = GetText("Display.RealisticReady");
            }
        });
    }

    /// <summary>Preserves the editable WPF model and reports preview failures without an unhandled async exception.</summary>
    private void ModelPreviewFailed(Exception exception)
    {
        System.Diagnostics.Trace.TraceError("The realistic model preview failed: {0}", exception);
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (_modelDisplayStyle != ModelDisplayStyle.Realistic || !Model3DVisible) return;
            _modelPreviewFailure = exception;
            _modelRealisticImage.Visibility = Visibility.Collapsed;
            StatusText.Text = GetText("Display.RealisticFailed");
        });
    }
}
