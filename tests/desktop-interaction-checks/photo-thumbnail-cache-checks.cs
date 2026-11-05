using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Guanacaste3D.Core.Rendering;
using Guanacaste3D.Desktop.Controls;

internal static partial class InteractionChecks
{
    /// <summary>Updated photos must not show a stale thumbnail merely because the encoded JPEG/PNG length matches.</summary>
    private static void PhotoThumbnailCacheChecks()
    {
        static string Preview(byte red, byte green, byte blue)
        {
            byte[] pixels = [];
            for (var i = 0; i < 4; i++)
                pixels = [.. pixels, blue, green, red, 255];
            var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return Convert.ToBase64String(stream.ToArray());
        }

        var red = Preview(255, 0, 0);
        var blue = Preview(0, 0, 255);
        var length = Math.Max(red.Length, blue.Length);
        // Whitespace is ignored by Convert.FromBase64String; equal lengths reproduce the old cache-key bug.
        red = red.PadRight(length, ' ');
        blue = blue.PadRight(length, ' ');
        Check(red.Length == blue.Length && red != blue, "Photo previews have different pixels but equal encoded length");

        var original = new RenderSceneBookmark(Guid.NewGuid(), "Foto", new RenderSetup()) { Thumbnail = red };
        var changed = original with { Thumbnail = blue };
        var type = typeof(RenderPanel).Assembly.GetType("Guanacaste3D.Desktop.Controls.PhotoThumbnails", throwOnError: true)!;
        var method = type.GetMethod("Of", BindingFlags.Static | BindingFlags.Public)!;
        var first = (BitmapSource)method.Invoke(null, [original])!;
        var firstAgain = (BitmapSource)method.Invoke(null, [original])!;
        var second = (BitmapSource)method.Invoke(null, [changed])!;

        static byte[] Pixel(BitmapSource image)
        {
            var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            var pixel = new byte[4];
            bgra.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), pixel, 4, 0);
            return pixel;
        }

        Check(ReferenceEquals(first, firstAgain), "Unchanged photo reuses its decoded thumbnail");
        Check(!ReferenceEquals(first, second) &&
              Pixel(first)[2] == 255 && Pixel(first)[0] == 0 &&
              Pixel(second)[0] == 255 && Pixel(second)[2] == 0,
            "Photo preview refreshes when pixels change without a change in encoded length");
    }
}
