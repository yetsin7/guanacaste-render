using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Guanacaste3D.ExchangePreview;

/// <summary>Draws exchange geometry without Revit, native BIM conversion, or external resource access.</summary>
internal static class ExchangeThumbnail
{
    private const int MaximumTriangles = 500000;
    private const int MaximumMeshVertices = 1_500_000;
    private const int MaximumExchangeMeshes = 2048;
    private const int MaximumExchangeMaterials = 16384;
    private const int MaximumExchangeElements = 50000;
    private const long MaximumDecodedMeshBytes = 64L * 1024 * 1024;
    private sealed record Mesh(Vector3[] Positions, int[] Indices, int[] Materials);
    private readonly record struct Face(Point A, Point B, Point C, double Depth, Color Color);

    /// <summary>Reads an optional bounded PNG or generates a shaded isometric preview from the exported meshes.</summary>
    internal static BitmapSource Render(JsonElement root, int size, bool useEmbedded = true)
    {
        if (size is < 1 or > 1024 || root.ValueKind != JsonValueKind.Object ||
            root.GetProperty("format").GetString() != "guanacaste-exchange" || root.GetProperty("version").GetInt32() != 1)
            throw new InvalidDataException("Unsupported exchange thumbnail input.");
        if (useEmbedded && root.TryGetProperty("thumbnailPng", out var embedded) && embedded.ValueKind == JsonValueKind.String)
        {
            try { return DecodePng(embedded.GetString()!, size); }
            catch (Exception exception) when (exception is FormatException or InvalidDataException or NotSupportedException or ArgumentException)
            {
                // A damaged optional preview must not hide otherwise usable exchange geometry.
            }
        }
        // Limit counts and decoded buffers before materializing untrusted exchange geometry.
        // The Shell provider can run inside Explorer, so input size alone is not a memory budget.
        var meshItems = root.GetProperty("meshes");
        var materialItems = root.GetProperty("materials");
        var elementItems = root.GetProperty("elements");
        if (meshItems.GetArrayLength() > MaximumExchangeMeshes ||
            materialItems.GetArrayLength() > MaximumExchangeMaterials ||
            elementItems.GetArrayLength() > MaximumExchangeElements)
            throw new InvalidDataException("The exchange thumbnail exceeds its collection budget.");
        long decodedBytes = 0;
        var meshes = new List<Mesh>(meshItems.GetArrayLength());
        foreach (var item in meshItems.EnumerateArray())
            meshes.Add(ReadMesh(item, ref decodedBytes));
        var materials = materialItems.EnumerateArray().Select(ReadColor).ToArray();
        var elements = elementItems.EnumerateArray().ToArray();
        foreach (var element in elements)
            if (element.TryGetProperty("mesh", out var reference) && reference.ValueKind == JsonValueKind.Number)
            {
                var index = reference.GetInt32();
                if ((uint)index >= (uint)meshes.Count) throw new InvalidDataException("Invalid exchange mesh reference.");
            }
        var faces = new List<Face>();
        foreach (var element in elements.OrderBy(ElementPriority))
        {
            if (!element.TryGetProperty("mesh", out var reference) || reference.ValueKind != JsonValueKind.Number) continue;
            var mesh = meshes[reference.GetInt32()];
            // Preserve complete surfaces: subsampling individual triangles produces holes in walls and roofs.
            if (faces.Count + mesh.Indices.Length / 3 > MaximumTriangles) continue;
            var origin = element.TryGetProperty("origin", out var coordinates) ? ReadOrigin(coordinates) : Vector3.Zero;
            var rotation = element.TryGetProperty("rotation", out var angle) ? angle.GetDouble() : 0;
            if (!double.IsFinite(rotation)) throw new InvalidDataException("Invalid exchange rotation.");
            var transform = Matrix4x4.CreateRotationZ((float)rotation) * Matrix4x4.CreateTranslation(origin);
            for (var triangle = 0; triangle < mesh.Indices.Length / 3; triangle++)
            {
                var a = Vector3.Transform(mesh.Positions[mesh.Indices[triangle * 3]], transform);
                var b = Vector3.Transform(mesh.Positions[mesh.Indices[triangle * 3 + 1]], transform);
                var c = Vector3.Transform(mesh.Positions[mesh.Indices[triangle * 3 + 2]], transform);
                var normal = Vector3.Cross(b - a, c - a);
                if (normal.LengthSquared() < 1e-14f) continue;
                normal = Vector3.Normalize(normal);
                var shade = 0.55 + 0.45 * Math.Abs(Vector3.Dot(normal, Vector3.Normalize(new Vector3(1, -2, 3))));
                var material = mesh.Materials[triangle];
                var color = (uint)material < (uint)materials.Length ? materials[material] : Colors.Silver;
                color = Color.FromRgb((byte)(color.R * shade), (byte)(color.G * shade), (byte)(color.B * shade));
                faces.Add(new(Project(a), Project(b), Project(c), a.X + a.Y + a.Z + b.X + b.Y + b.Z + c.X + c.Y + c.Z, color));
                if (faces.Count >= MaximumTriangles) break;
            }
            if (faces.Count >= MaximumTriangles) break;
        }
        if (faces.Count == 0) throw new InvalidDataException("The exchange has no previewable triangles.");
        return Draw(faces, size);
    }

    /// <summary>Retains the building envelope before detailed furnishings when a large exchange reaches the preview budget.</summary>
    private static int ElementPriority(JsonElement element)
    {
        var category = element.TryGetProperty("category", out var value) ? value.GetString() : null;
        return category is "OST_Furniture" or "OST_LightingFixtures" or "OST_PlumbingFixtures" or "OST_Planting" ? 1 : 0;
    }

    /// <summary>Encodes a frozen preview for a portable optional field in exchange version 1.</summary>
    internal static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    /// <summary>Checks encoded size and PNG dimensions before asking WIC to allocate a decoded bitmap.</summary>
    private static BitmapSource DecodePng(string encoded, int size)
    {
        if (encoded.Length > 2800000) throw new InvalidDataException("The embedded thumbnail is too large.");
        var bytes = Convert.FromBase64String(encoded);
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(signature))
            throw new InvalidDataException("The embedded thumbnail is not PNG.");
        var width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
        var height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        if (width is < 1 or > 1024 || height is < 1 or > 1024) throw new InvalidDataException("Invalid embedded thumbnail dimensions.");
        using var input = new MemoryStream(bytes, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = size;
        image.StreamSource = input;
        image.EndInit();
        image.Freeze();
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, size, size));
            var scale = size / (double)Math.Max(width, height);
            context.DrawImage(image, new Rect((size - width * scale) / 2, (size - height * scale) / 2, width * scale, height * scale));
        }
        return Rasterize(visual, size);
    }

    /// <summary>Validates binary mesh alignment, finite coordinates, and every vertex index before drawing.</summary>
    private static Mesh ReadMesh(JsonElement mesh, ref long decodedBytes)
    {
        var positionsBase64 = mesh.GetProperty("positions").GetString()!;
        var indicesBase64 = mesh.GetProperty("indices").GetString()!;
        var materialsBase64 = mesh.GetProperty("materials").GetString()!;
        // Base64 may contain far more decoded geometry than the final 500k-triangle preview.
        // Check the upper bound before allocating the decoded buffers and their typed copies.
        var estimatedBytes = (long)(positionsBase64.Length / 4) * 3 +
                             (long)(indicesBase64.Length / 4) * 3 +
                             (long)(materialsBase64.Length / 4) * 3;
        if (estimatedBytes > MaximumDecodedMeshBytes - decodedBytes)
            throw new InvalidDataException("The exchange thumbnail exceeds its decoded mesh budget.");
        var positions = ReadArray<Vector3>(positionsBase64, MaximumMeshVertices);
        var indices = ReadArray<int>(indicesBase64, MaximumTriangles * 3);
        var materials = ReadArray<int>(materialsBase64, MaximumTriangles);
        if (indices.Length != materials.Length * 3 || indices.Any(index => (uint)index >= (uint)positions.Length) ||
            positions.Any(point => !float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z) || point.Length() > 10000000))
            throw new InvalidDataException("Invalid exchange thumbnail mesh.");
        decodedBytes += estimatedBytes;
        return new Mesh(positions, indices, materials);
    }

    /// <summary>Rejects oversized and misaligned base64 buffers before copying them to typed arrays.</summary>
    private static T[] ReadArray<T>(string encoded, int maximumItems) where T : unmanaged
    {
        var stride = Marshal.SizeOf<T>();
        var maximumEncodedLength = ((long)maximumItems * stride + 2) / 3 * 4;
        if (encoded.Length > maximumEncodedLength)
            throw new InvalidDataException("The exchange thumbnail mesh buffer is too large.");
        var bytes = Convert.FromBase64String(encoded);
        if (bytes.Length % stride != 0 || bytes.Length / stride > maximumItems)
            throw new InvalidDataException("Invalid exchange thumbnail mesh buffer length.");
        return MemoryMarshal.Cast<byte, T>(bytes).ToArray();
    }

    /// <summary>Reads the instance translation in meters and bounds it before matrix arithmetic.</summary>
    private static Vector3 ReadOrigin(JsonElement coordinates)
    {
        if (coordinates.ValueKind == JsonValueKind.Null) return Vector3.Zero;
        if (coordinates.GetArrayLength() != 3) throw new InvalidDataException("Invalid exchange origin.");
        var origin = new Vector3(coordinates[0].GetSingle(), coordinates[1].GetSingle(), coordinates[2].GetSingle());
        if (!float.IsFinite(origin.Length()) || origin.Length() > 10000000) throw new InvalidDataException("Invalid exchange origin.");
        return origin;
    }

    /// <summary>Uses only the exchange's RGB color; materials never trigger external texture loading.</summary>
    private static Color ReadColor(JsonElement material)
    {
        var text = material.TryGetProperty("color", out var value) ? value.GetString() : null;
        if (text is { Length: 7 } && text[0] == '#' && uint.TryParse(text.AsSpan(1), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var rgb))
            return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return Colors.Silver;
    }

    /// <summary>Looks down on the model from a fixed diagonal camera, preserving Z-up geometry.</summary>
    private static Point Project(Vector3 point) => new((point.X - point.Y) * 0.70710678, (point.X + point.Y) * 0.40824829 - point.Z * 0.81649658);

    /// <summary>Fits shaded triangles to the image and paints farther faces before nearer faces.</summary>
    private static BitmapSource Draw(List<Face> faces, int size)
    {
        var points = faces.SelectMany(face => new[] { face.A, face.B, face.C }).ToArray();
        var left = points.Min(point => point.X);
        var top = points.Min(point => point.Y);
        var width = points.Max(point => point.X) - left;
        var height = points.Max(point => point.Y) - top;
        var scale = size * 0.9 / Math.Max(0.001, Math.Max(width, height));
        var visual = new DrawingVisual();
        var brushes = new Dictionary<Color, SolidColorBrush>();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, size, size));
            context.PushTransform(new MatrixTransform(scale, 0, 0, scale, (size - width * scale) / 2 - left * scale, (size - height * scale) / 2 - top * scale));
            foreach (var face in faces.OrderBy(face => face.Depth))
            {
                var geometry = new StreamGeometry();
                using (var outline = geometry.Open())
                {
                    outline.BeginFigure(face.A, true, true);
                    outline.LineTo(face.B, true, false);
                    outline.LineTo(face.C, true, false);
                }
                geometry.Freeze();
                if (!brushes.TryGetValue(face.Color, out var brush))
                {
                    brush = new SolidColorBrush(face.Color);
                    brush.Freeze();
                    brushes.Add(face.Color, brush);
                }
                context.DrawGeometry(brush, null, geometry);
            }
            context.Pop();
        }
        return Rasterize(visual, size);
    }

    /// <summary>Returns an immutable 32-bit bitmap suitable for PNG and Shell HBITMAP conversion.</summary>
    private static BitmapSource Rasterize(DrawingVisual visual, int size)
    {
        var image = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        image.Freeze();
        return image;
    }
}
