using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>Render-only AI handoff commands. No provider is invoked from the application.</summary>
public partial class RenderPanel
{
    private string? _projectFilePath;

    /// <summary>The sidecar render vault follows the currently opened or saved project.</summary>
    internal void SetProjectFilePath(string? projectPath) => _projectFilePath = projectPath;

    /// <summary>A stable copy of the actual rendered pixels, not a screenshot of ribbons or overlays.</summary>
    private BitmapSource? VisibleImageSnapshot()
    {
        if (InteractiveViewport.Visibility == Visibility.Visible && InteractiveViewport.ActualWidth > 0 && InteractiveViewport.ActualHeight > 0)
        {
            var width = Math.Max(1, (int)Math.Ceiling(InteractiveViewport.ActualWidth));
            var height = Math.Max(1, (int)Math.Ceiling(InteractiveViewport.ActualHeight));
            var snapshot = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            snapshot.Render(InteractiveViewport);
            snapshot.Freeze();
            return snapshot;
        }
        if (PreviewImage.Source is not BitmapSource image) return null;
        var copy = image.CloneCurrentValue();
        copy.Freeze();
        return copy;
    }

    private void CopyCurrentView_Click(object sender, RoutedEventArgs e)
    {
        var image = VisibleImageSnapshot();
        if (image is null)
        {
            StatusText.Text = UiText.Get("RenderPanel.NoViewForAi");
            return;
        }
        try
        {
            Clipboard.SetImage(image);
            StatusText.Text = UiText.Get("RenderPanel.ViewCopiedForAi");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            StatusText.Text = UiText.Get("RenderPanel.ClipboardBusy");
        }
    }

    private void AiRender_Click(object sender, RoutedEventArgs e)
    {
        var snapshot = VisibleImageSnapshot();
        if (snapshot is null)
        {
            StatusText.Text = UiText.Get("RenderPanel.NoViewForAi");
            return;
        }
        var dialog = new AiRenderWindow(_projectFilePath, _source?.ProjectName ?? UiText.Get("Project.Untitled"), snapshot)
        {
            Owner = Window.GetWindow(this)
        };
        dialog.ShowDialog();
    }
}
