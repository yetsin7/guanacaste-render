using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Guanacaste3D.Desktop.Controls;

public partial class RenderPanel
{
    private DispatcherTimer? _skyBackgroundTimer;
    private Brush? _originalSceneBackground;

    // The interactive viewport has transparent pixels: put the photographed panorama behind it,
    // using the same camera directions and environment rotation as the renderer.
    private void ScheduleSkyBackground()
    {
        if (!_ready || ImageHost is null) return;
        _originalSceneBackground ??= ImageHost.Background;
        if (_hdriEnvironment is null)
        {
            _skyBackgroundTimer?.Stop();
            ImageHost.Background = _originalSceneBackground;
            HideStyledPreview();
            ScheduleStyledPreview();
            return;
        }
        _skyBackgroundTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _skyBackgroundTimer.Tick -= DrawSkyBackground;
        _skyBackgroundTimer.Tick += DrawSkyBackground;
        _skyBackgroundTimer.Stop();
        _skyBackgroundTimer.Start();
    }

    private void DrawSkyBackground(object? sender, EventArgs e)
    {
        _skyBackgroundTimer?.Stop();
        if (_hdriEnvironment is not { } environment || _source?.Camera is not { } camera || ImageHost.ActualHeight < 1) return;
        const int width = 400;
        var height = Math.Clamp((int)Math.Round(width * ImageHost.ActualHeight / Math.Max(1, ImageHost.ActualWidth)), 100, 400);
        var pixels = new byte[width * height * 4];
        var exposure = MathF.Pow(2, (float)HdriExposureSlider.Value);
        var yaw = (float)HdriYawSlider.Value;
        static byte Channel(float v)
        {
            v = Math.Max(0, v);
            v = v / (1 + v);
            return (byte)Math.Clamp((int)MathF.Round(MathF.Pow(v, 1 / 2.2f) * 255), 0, 255);
        }
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var color = environment.Sample(camera.PixelRay(x + .5f, y + .5f, width, height), yaw) * exposure;
            var index = (y * width + x) * 4;
            pixels[index] = Channel(color.Z); pixels[index + 1] = Channel(color.Y);
            pixels[index + 2] = Channel(color.X); pixels[index + 3] = 255;
        }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        image.Freeze();
        ImageHost.Background = new ImageBrush(image) { Stretch = Stretch.Fill };
        HideStyledPreview();
        ScheduleStyledPreview();
    }
}
