using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Desktop;

/// <summary>Creates compact, shaded isometric previews for the Home screen.</summary>
internal static class ProjectThumbnail
{
    public const int Width = 384;
    public const int Height = 384;
    private const int MaximumDetailTrianglesPerKind = 3500;
    private const int MaximumPreviewTriangles = 40000;
    // Home-screen thumbnails are generated at 384x384. A much larger cache file
    // is unexpected and must not be decoded into the desktop process.
    private const long MaximumCachedPngBytes = 16L * 1024 * 1024;
    // A small compressed PNG can claim enormous dimensions and exhaust memory on decode.
    private const uint MaximumCachedDimension = 1024;

    /// <summary>A projected surface with a depth used to paint distant surfaces first.</summary>
    public readonly record struct Face(Point A, Point B, Point C, double Depth, Color Color);

    /// <summary>Builds a preview mesh off the UI thread, omitting terrain and reducing dense details.</summary>
    public static IReadOnlyList<Face> Faces(BuildingDocument document)
    {
        var mesh = BuildingMesher.Build(document);
        var selected = SelectTriangleIndices(mesh.TriangleKinds);
        var faces = new List<Face>(selected.Count);
        // The light direction is constant for every face of this thumbnail.
        var lightDirection = Vector3.Normalize(new Vector3(-1, -2, 3));
        foreach (var triangle in selected)
        {
            var kind = mesh.TriangleKinds[triangle];
            var a = mesh.Positions[mesh.Indices[triangle * 3]];
            var b = mesh.Positions[mesh.Indices[triangle * 3 + 1]];
            var c = mesh.Positions[mesh.Indices[triangle * 3 + 2]];
            var normal = Vector3.Cross(b - a, c - a);
            if (normal.LengthSquared() < 1e-12f) continue;
            normal = Vector3.Normalize(normal);
            var light = 0.68 + 0.32 * Math.Abs(Vector3.Dot(normal, lightDirection));
            var color = SurfaceColor(kind);
            color = Color.FromRgb((byte)(color.R * light), (byte)(color.G * light), (byte)(color.B * light));
            faces.Add(new Face(Project(a), Project(b), Project(c),
                (a.X + b.X + c.X + a.Y + b.Y + c.Y - a.Z - b.Z - c.Z) / 3, color));
        }
        faces.Sort((left, right) => right.Depth.CompareTo(left.Depth));
        return faces;
    }

    /// <summary>
    /// Selects at most 40,000 faces before WPF allocates drawing geometry. Decorative
    /// categories retain their own detail budgets; architectural faces use the
    /// remaining global budget rather than growing without limit on large models.
    /// This bounds preview output, not the upstream BuildingMesher allocation.
    /// </summary>
    internal static IReadOnlyList<int> SelectTriangleIndices(IReadOnlyList<SurfaceKind> kinds)
    {
        var counts = new Dictionary<SurfaceKind, int>();
        foreach (var kind in kinds)
        {
            if (kind is SurfaceKind.Ground or SurfaceKind.Earth) continue;
            counts[kind] = counts.GetValueOrDefault(kind) + 1;
        }

        var detailStrides = new Dictionary<SurfaceKind, int>();
        long eligibleCount = 0;
        foreach (var (kind, count) in counts)
        {
            if (!IsFineDetail(kind))
            {
                eligibleCount += count;
                continue;
            }

            var stride = Math.Max(1, (int)Math.Ceiling(count / (double)MaximumDetailTrianglesPerKind));
            detailStrides[kind] = stride;
            eligibleCount += (count - 1) / stride + 1;
        }

        var globalStride = Math.Max(1, (int)Math.Ceiling(eligibleCount / (double)MaximumPreviewTriangles));
        var selected = new List<int>((int)Math.Min(MaximumPreviewTriangles, (eligibleCount + globalStride - 1) / globalStride));
        var detailOffsets = new Dictionary<SurfaceKind, int>();
        long eligibleOffset = 0;
        for (var triangle = 0; triangle < kinds.Count; triangle++)
        {
            var kind = kinds[triangle];
            if (kind is SurfaceKind.Ground or SurfaceKind.Earth) continue;
            if (detailStrides.TryGetValue(kind, out var stride))
            {
                var offset = detailOffsets.GetValueOrDefault(kind);
                detailOffsets[kind] = offset + 1;
                if (offset % stride != 0) continue;
            }

            if (eligibleOffset % globalStride == 0) selected.Add(triangle);
            eligibleOffset++;
        }

        return selected;
    }

    /// <summary>Keeps architecture intact while reducing dense decorative meshes.</summary>
    private static bool IsFineDetail(SurfaceKind kind) => kind is
        SurfaceKind.Leaf or SurfaceKind.Foliage or SurfaceKind.Bark or SurfaceKind.CarPaint or
        SurfaceKind.CarPaintSilver or SurfaceKind.Rubber or SurfaceKind.TintedGlass or SurfaceKind.Metal;

    /// <summary>Projects model coordinates onto an isometric view.</summary>
    private static Point Project(Vector3 point) =>
        new((point.X - point.Y) * 0.70710678, -((point.X + point.Y) * 0.40824829 + point.Z * 0.81649658));

    /// <summary>Maps surface categories to a restrained architectural material palette.</summary>
    private static Color SurfaceColor(SurfaceKind kind) => kind switch
    {
        SurfaceKind.Foliage or SurfaceKind.Leaf => Color.FromRgb(104, 151, 98),
        SurfaceKind.Bark or SurfaceKind.WallWood or SurfaceKind.DoorLeaf => Color.FromRgb(152, 114, 82),
        SurfaceKind.Glass or SurfaceKind.TintedGlass or SurfaceKind.Mirror => Color.FromRgb(126, 164, 177),
        SurfaceKind.Water => Color.FromRgb(96, 157, 174),
        SurfaceKind.Roof or SurfaceKind.WallBrick => Color.FromRgb(169, 113, 91),
        SurfaceKind.Metal or SurfaceKind.WallMetal or SurfaceKind.Structure => Color.FromRgb(126, 139, 143),
        SurfaceKind.Asphalt or SurfaceKind.Rubber => Color.FromRgb(95, 106, 109),
        SurfaceKind.Floor or SurfaceKind.WallConcrete or SurfaceKind.WallBlock => Color.FromRgb(197, 199, 188),
        _ => Color.FromRgb(226, 224, 211)
    };

    /// <summary>Paints projected faces on the UI thread and fits the model to the card.</summary>
    public static BitmapSource Render(IReadOnlyList<Face> faces)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(232, 239, 235)), null, new Rect(0, 0, Width, Height));
            if (faces.Count > 0)
            {
                // Compute bounds in one pass. SelectMany previously allocated a
                // three-point array for every face plus a second 120,000-point
                // array for a full 40,000-face Home preview.
                var minX = double.PositiveInfinity;
                var maxX = double.NegativeInfinity;
                var minY = double.PositiveInfinity;
                var maxY = double.NegativeInfinity;
                void Include(Point point)
                {
                    minX = Math.Min(minX, point.X);
                    maxX = Math.Max(maxX, point.X);
                    minY = Math.Min(minY, point.Y);
                    maxY = Math.Max(maxY, point.Y);
                }
                foreach (var face in faces)
                {
                    Include(face.A);
                    Include(face.B);
                    Include(face.C);
                }
                var scale = Math.Min((Width - 28) / Math.Max(1e-6, maxX - minX), (Height - 28) / Math.Max(1e-6, maxY - minY));
                Point Map(Point point) => new(Width / 2 + (point.X - (minX + maxX) / 2) * scale, Height / 2 + (point.Y - (minY + maxY) / 2) * scale);
                var brushes = new Dictionary<Color, SolidColorBrush>();
                foreach (var face in faces)
                {
                    if (!brushes.TryGetValue(face.Color, out var brush))
                    {
                        brush = new SolidColorBrush(face.Color);
                        brush.Freeze();
                        brushes.Add(face.Color, brush);
                    }
                    var geometry = new StreamGeometry();
                    using (var path = geometry.Open())
                    {
                        path.BeginFigure(Map(face.A), true, true);
                        path.LineTo(Map(face.B), true, false);
                        path.LineTo(Map(face.C), true, false);
                    }
                    geometry.Freeze();
                    context.DrawGeometry(brush, null, geometry);
                }
            }
        }
        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Publishes a complete PNG cache file atomically, preserving the previous image on failure.</summary>
    public static void Save(BitmapSource image, string path)
    {
        var destination = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        // A unique sibling file keeps concurrent thumbnail generations independent.
        // The final same-directory rename prevents Home from observing a partial PNG.
        var temporary = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                encoder.Save(stream);
            // ReplaceFile handles a destination opened with FILE_SHARE_DELETE on Windows.
            // A plain MoveFileEx overwrite can return ACCESS_DENIED for that case.
            if (File.Exists(destination))
                File.Replace(temporary, destination, null);
            else
                File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    /// <summary>Loads a cached PNG without locking it; invalid caches are ignored.</summary>
    public static BitmapSource? Load(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length is < 24 or > MaximumCachedPngBytes) return null;

            // Validate the PNG signature and IHDR dimensions before WPF allocates pixels.
            // Decode from this same stream to avoid a file-swap race between the check and load.
            Span<byte> header = stackalloc byte[24];
            stream.ReadExactly(header);
            if (!header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
                BinaryPrimitives.ReadUInt32BigEndian(header[8..12]) != 13 ||
                !header[12..16].SequenceEqual("IHDR"u8))
                return null;
            var width = BinaryPrimitives.ReadUInt32BigEndian(header[16..20]);
            var height = BinaryPrimitives.ReadUInt32BigEndian(header[20..24]);
            if (width is 0 or > MaximumCachedDimension || height is 0 or > MaximumCachedDimension)
                return null;

            stream.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            // StreamSource does not use the URI image cache (IgnoreImageCache requires UriSource).
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException
            or ArgumentException or FileFormatException)
        {
            return null;
        }
    }
}
