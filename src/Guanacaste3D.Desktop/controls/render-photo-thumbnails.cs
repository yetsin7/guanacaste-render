using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>Decodes and remembers the small previews saved with each photo.</summary>
internal static class PhotoThumbnails
{
    // The bookmark is immutable: editing its preview creates a new record. Cache by object identity rather
    // than by Guid and encoded length, since two different JPEGs can have exactly the same length.
    // Weak keys also allow thumbnails from closed projects to be collected instead of accumulating forever.
    private static readonly ConditionalWeakTable<RenderSceneBookmark, ImageSource> Cache = new();

    /// <summary>The photo's preview, or null when none was captured or the stored data is not a picture.</summary>
    public static ImageSource? Of(RenderSceneBookmark photo)
    {
        if (string.IsNullOrEmpty(photo.Thumbnail)) return null;
        try
        {
            return Cache.GetValue(photo, static bookmark => Decode(bookmark.Thumbnail!));
        }
        catch (Exception error) when (error is FormatException or NotSupportedException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private static ImageSource Decode(string encoded)
    {
        using var stream = new MemoryStream(Convert.FromBase64String(encoded));
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
