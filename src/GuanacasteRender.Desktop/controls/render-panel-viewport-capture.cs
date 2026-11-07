using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>
/// A still of the navigable model with the active style and filters applied. The lightweight WPF model is never
/// slowed down: the stylised picture is produced once, in the background, after the camera has been still for a moment.
/// </summary>
public partial class RenderPanel
{
    private DispatcherTimer? _styledTimer;
    private int _styledVersion;
    private ImageFinish? _lastStyledFinish;
    private static readonly float[] SrgbToLinear = Enumerable.Range(0, 256)
        .Select(value => { var c = value / 255f; return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f); })
        .ToArray();

    /// <summary>The model viewport over the sky, scaled to the requested width, as raw BGRA pixels.</summary>
    private (byte[] Bgra, int Width, int Height)? CaptureViewportPixels(int maximumWidth)
    {
        var sourceWidth = (int)Math.Round(InteractiveViewport.ActualWidth);
        var sourceHeight = (int)Math.Round(InteractiveViewport.ActualHeight);
        if (sourceWidth < 16 || sourceHeight < 16 || InteractiveViewport.Visibility != Visibility.Visible) return null;
        var width = Math.Min(maximumWidth, sourceWidth);
        var height = Math.Max(8, (int)Math.Round(width * (double)sourceHeight / sourceWidth));
        var full = new RenderTargetBitmap(sourceWidth, sourceHeight, 96, 96, PixelFormats.Pbgra32);
        full.Render(InteractiveViewport);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, width, height);
            context.DrawRectangle(ImageHost.Background, null, bounds);
            context.DrawImage(full, bounds);
        }
        var scaled = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        scaled.Render(visual);
        var pixels = new byte[width * height * 4];
        scaled.CopyPixels(pixels, width * 4, 0);
        return (pixels, width, height);
    }

    /// <summary>Runs the image finish (tone curve, grading, LUT, vignette, bloom...) over display-referred pixels.</summary>
    private static byte[] FinishPixels(byte[] bgra, int width, int height, ImageFinish finish)
    {
        var linear = new Vector3[width * height];
        for (var index = 0; index < linear.Length; index++)
            linear[index] = new Vector3(SrgbToLinear[bgra[index * 4 + 2]], SrgbToLinear[bgra[index * 4 + 1]], SrgbToLinear[bgra[index * 4]]);
        return ImageProcessor.ToBgra32(linear, width, height, 1.35f, finish with { AutoExposure = false, Dither = false }, null);
    }

    /// <summary>A small JPEG (Base64) of the current view with the photo's finish, or null when it cannot be captured now.</summary>
    private string? MakeThumbnail()
    {
        try
        {
            // While a render is on screen the viewport is hidden (and the picture may be half finished): keep the old preview.
            if (_showingFinal || _rendering || CaptureViewportPixels(320) is not var (bgra, width, height)) return null;
            var styled = FinishPixels(bgra, width, height, CurrentFinish());
            var picture = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, styled, width * 4);
            var encoder = new JpegBitmapEncoder { QualityLevel = 80 };
            encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(picture, PixelFormats.Bgr32, null, 0)));
            using var stream = new System.IO.MemoryStream();
            encoder.Save(stream);
            return Convert.ToBase64String(stream.ToArray());
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or System.IO.IOException
            or ArgumentException)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- styled preview

    private void StylePreview_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (StylePreviewCheck.IsChecked == true) ScheduleStyledPreview();
        else HideStyledPreview();
    }

    /// <summary>Hides the stylised still at once; navigation always shows the live model.</summary>
    private void HideStyledPreview()
    {
        _styledVersion++;
        _styledTimer?.Stop();
        if (StyledPreviewImage is not null) StyledPreviewImage.Visibility = Visibility.Collapsed;
    }

    /// <summary>Waits for the camera and sliders to settle, then paints the styled still once.</summary>
    private void ScheduleStyledPreview()
    {
        if (!_ready || StylePreviewCheck?.IsChecked != true || !IsVisible || _showingFinal || _rendering) return;
        if (_styledTimer is null)
        {
            _styledTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            _styledTimer.Tick += async (_, _) =>
            {
                _styledTimer.Stop();
                await ShowStyledPreviewAsync();
            };
        }
        _styledTimer.Stop();
        _styledTimer.Start();
    }

    private async Task ShowStyledPreviewAsync()
    {
        if (!IsVisible || _showingFinal || _rendering || StylePreviewCheck.IsChecked != true) return;
        var version = ++_styledVersion;
        var finish = CurrentFinish();
        _lastStyledFinish = finish;
        if (CaptureViewportPixels(960) is not var (bgra, width, height)) return;
        byte[] styled;
        try { styled = await Task.Run(() => FinishPixels(bgra, width, height, finish)); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OutOfMemoryException) { return; }
        if (version != _styledVersion || !IsVisible || _showingFinal || _rendering) return;
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, styled, width * 4);
        image.Freeze();
        StyledPreviewImage.Source = image;
        StyledPreviewImage.Visibility = Visibility.Visible;
    }

    /// <summary>Repaints the styled still when a slider, filter or style changed since the last one.</summary>
    private void CheckStyledPreviewStale()
    {
        if (!_ready) return;
        try
        {
            if (CurrentFinish() != _lastStyledFinish) ScheduleStyledPreview();
        }
        catch (InvalidOperationException) { }
    }
}
