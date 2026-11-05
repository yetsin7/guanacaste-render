using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace Guanacaste3D.Thumbnails;

/// <summary>Transfers WPF pixels into a Shell-owned top-down 32-bit DIB.</summary>
internal static class ThumbnailBitmap
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint ImageSize;
        public int HorizontalResolution;
        public int VerticalResolution;
        public uint ColorsUsed;
        public uint ImportantColors;
    }

    /// <summary>Allocates a DIB; the caller must release it using DeleteObject after consuming the thumbnail.</summary>
    internal static IntPtr Create(BitmapSource image)
    {
        var header = new BitmapHeader
        {
            Size = (uint)Marshal.SizeOf<BitmapHeader>(), Width = image.PixelWidth,
            Height = -image.PixelHeight, Planes = 1, BitCount = 32
        };
        var bitmap = CreateDIBSection(IntPtr.Zero, ref header, 0, out var pixels, IntPtr.Zero, 0);
        if (bitmap == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try { image.CopyPixels(System.Windows.Int32Rect.Empty, pixels, image.PixelWidth * image.PixelHeight * 4, image.PixelWidth * 4); }
        catch { DeleteObject(bitmap); throw; }
        return bitmap;
    }

    /// <summary>Allocates GDI memory compatible with the Windows thumbnail cache.</summary>
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(IntPtr deviceContext, ref BitmapHeader header, uint usage,
        out IntPtr pixels, IntPtr section, uint offset);

    /// <summary>Releases a bitmap when pixel transfer fails before ownership reaches the Shell.</summary>
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr bitmap);
}
