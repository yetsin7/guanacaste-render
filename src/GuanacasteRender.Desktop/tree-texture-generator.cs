using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Generates original, deterministic, seamless nature textures on demand in the user's writable cache.
/// No internet access, subscription, proprietary photographs or install-directory write access is needed.
/// These are procedural albedo/normal materials, not photographs or photogrammetry scans.
/// </summary>
internal static class TreeTextureGenerator
{
    internal sealed record Species(string Key, string Label, bool Bark, byte R, byte G, byte B,
        int Seed, int Detail);

    internal static IReadOnlyList<Species> Catalog { get; } =
    [
        new("tree-bark-guanacaste", "Corteza · Guanacaste", true, 110, 88, 67, 11, 19),
        new("tree-bark-ceiba", "Corteza · Ceiba", true, 139, 139, 119, 12, 11),
        new("tree-bark-mango", "Corteza · Mango", true, 103, 86, 74, 13, 25),
        new("tree-bark-jacaranda", "Corteza · Jacaranda", true, 124, 110, 96, 14, 17),
        new("tree-bark-pine", "Corteza · Pino", true, 128, 80, 53, 15, 29),
        new("tree-bark-cedar", "Corteza · Cedro", true, 122, 82, 61, 16, 37),
        new("tree-bark-oak", "Corteza · Roble", true, 104, 93, 77, 17, 39),
        new("tree-bark-mahogany", "Corteza · Caoba", true, 111, 73, 56, 18, 25),
        new("tree-bark-eucalyptus", "Corteza · Eucalipto", true, 165, 159, 132, 19, 11),
        new("tree-bark-birch", "Corteza · Abedul", true, 205, 197, 177, 20, 14),
        new("tree-bark-palm", "Tronco · Palmera", true, 143, 119, 86, 21, 23),
        new("tree-bark-aged", "Corteza · Antigua y musgo", true, 81, 75, 60, 22, 37),

        new("tree-leaves-guanacaste", "Follaje · Guanacaste", false, 65, 113, 46, 101, 27),
        new("tree-leaves-tropical", "Follaje · Tropical luminoso", false, 77, 139, 44, 102, 23),
        new("tree-leaves-mango", "Follaje · Mango", false, 49, 92, 39, 103, 21),
        new("tree-leaves-jacaranda", "Follaje · Jacaranda", false, 66, 106, 66, 104, 25),
        new("tree-leaves-conifer", "Follaje · Conífera", false, 44, 75, 56, 105, 47),
        new("tree-leaves-palm", "Follaje · Palmera", false, 57, 119, 44, 106, 19),
        new("tree-leaves-young", "Hojas · Brotes tiernos", false, 109, 151, 48, 107, 27),
        new("tree-leaves-dark", "Hojas · Verde profundo", false, 37, 73, 37, 108, 31),
        new("tree-leaves-dry", "Hojas · Estación seca", false, 135, 116, 60, 109, 21),
        new("tree-leaves-silver", "Hojas · Verde plateado", false, 119, 142, 111, 110, 25),
        new("tree-leaves-forest", "Follaje · Bosque denso", false, 46, 100, 42, 111, 39),
        new("tree-leaves-autumn", "Hojas · Otoño cálido", false, 151, 93, 42, 112, 19)
    ];

    private static readonly object Gate = new();
    internal static bool Contains(string key) => Catalog.Any(item => item.Key == key);
    private static string CacheFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Guanacaste3D", "nature-textures-v1");

    /// <summary>Returns a generated 512px PNG, alongside an OpenGL tangent-space normal map.</summary>
    internal static string? Resolve(string key)
    {
        var species = Catalog.FirstOrDefault(item => item.Key == key);
        if (species is null) return null;
        try
        {
            lock (Gate)
            {
                var root = CacheFolder;
                var colorFile = Path.Combine(root, key + ".png");
                var normalFile = Path.Combine(root, key + "_NormalGL.png");
                if (File.Exists(colorFile) && File.Exists(normalFile)) return colorFile;
                Directory.CreateDirectory(root);
                var (rgb, normals) = Draw(species);
                WritePngAtomic(colorFile, rgb, 512, 512);
                WritePngAtomic(normalFile, normals, 512, 512);
                return colorFile;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // No texture is preferable to crashing an unsaved BIM session on a locked-down workstation.
            return null;
        }
    }

    private static (byte[] Color, byte[] Normal) Draw(Species species)
    {
        const int size = 512;
        var rgb = new byte[size * size * 3];
        var height = new float[size * size];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var u = x / (float)size;
            var v = y / (float)size;
            var smooth = Fbm(u, v, species.Seed, 2, 2, 4);
            var grain = Fbm(u, v, species.Seed + 14, 16, 16, 3);
            float tone, relief, moss = 0;
            if (species.Bark)
            {
                // Longitudinal fissures, rounded flaky plates and micro-pores. Integer repetition is tile-safe.
                var drift = (Fbm(u, v, species.Seed + 3, 2, 3, 3) - 0.5f) * 0.22f;
                var wave = MathF.Abs(MathF.Sin(MathF.PI * (species.Detail * u + drift + 0.03f * grain)));
                var crack = MathF.Pow(1 - wave, 8);
                var plates = Fbm(u, v, species.Seed + 36, 5, 7, 4);
                var scratches = Fbm(u, v, species.Seed + 46, 32, 4, 2);
                tone = 0.69f + 0.37f * smooth + 0.35f * plates - 0.42f * crack + 0.12f * scratches;
                relief = 0.46f + 0.32f * plates - 0.24f * crack + 0.08f * grain;
                if (species.Key.EndsWith("aged", StringComparison.Ordinal))
                    moss = Math.Clamp((Fbm(u, v, species.Seed + 77, 3, 3, 4) - 0.57f) * 2.2f, 0, 0.7f);
                if (species.Key.EndsWith("birch", StringComparison.Ordinal))
                {
                    var scars = MathF.Pow(1 - MathF.Abs(MathF.Sin(MathF.PI * (v * 20 + drift))), 12);
                    tone -= scars * 0.25f;
                }
            }
            else
            {
                // Rounded leaf-scale highlights and tiny branch shadows; avoids flat green plastic canopies.
                var clusters = Fbm(u, v, species.Seed + 12, 5, 5, 4);
                var needles = Fbm(u, v, species.Seed + 31, species.Detail, species.Detail, 2);
                var leafLight = 0.65f * clusters + 0.35f * needles;
                tone = 0.48f + 0.83f * leafLight + 0.17f * (smooth - 0.5f);
                relief = 0.36f + 0.55f * leafLight;
            }

            var index = (y * size + x) * 3;
            rgb[index] = Channel(species.R, tone * (1 - moss) + 0.60f * moss);
            rgb[index + 1] = Channel(species.G, tone * (1 - moss) + 1.35f * moss);
            rgb[index + 2] = Channel(species.B, tone * (1 - moss) + 0.65f * moss);
            height[y * size + x] = Math.Clamp(relief, 0, 1);
        }

        // OpenGL tangent normal (green faces +V), periodic central differences keep seams seamless.
        var normal = new byte[rgb.Length];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            float H(int px, int py) => height[((py + size) % size) * size + (px + size) % size];
            var dx = (H((x + 1) % size, y) - H((x + size - 1) % size, y)) * 1.15f;
            var dy = (H(x, (y + 1) % size) - H(x, (y + size - 1) % size)) * 1.15f;
            var length = MathF.Sqrt(dx * dx + dy * dy + 1);
            var index = (y * size + x) * 3;
            normal[index] = (byte)Math.Clamp(128 - 127 * dx / length, 0, 255);
            normal[index + 1] = (byte)Math.Clamp(128 + 127 * dy / length, 0, 255);
            normal[index + 2] = (byte)Math.Clamp(128 + 127 / length, 0, 255);
        }
        return (rgb, normal);
    }

    private static byte Channel(byte baseColor, float tone) =>
        (byte)Math.Clamp((int)MathF.Round(baseColor * tone), 0, 255);

    private static float Fbm(float u, float v, int seed, int repeatX, int repeatY, int octaves)
    {
        float sum = 0, amplitude = 1, total = 0;
        for (var octave = 0; octave < octaves; octave++)
        {
            sum += SmoothNoise(u, v, repeatX << octave, repeatY << octave, seed + octave * 101) * amplitude;
            total += amplitude;
            amplitude *= 0.5f;
        }
        return sum / total;
    }

    private static float SmoothNoise(float u, float v, int periodX, int periodY, int seed)
    {
        var sx = u * periodX;
        var sy = v * periodY;
        var ix = (int)MathF.Floor(sx);
        var iy = (int)MathF.Floor(sy);
        var fx = sx - ix;
        var fy = sy - iy;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        float N(int x, int y)
        {
            var nx = ((x % periodX) + periodX) % periodX;
            var ny = ((y % periodY) + periodY) % periodY;
            unchecked
            {
                var hash = (uint)(nx * 73856093 ^ ny * 19349663 ^ seed * 83492791);
                hash ^= hash >> 16;
                hash *= 0x7feb352du;
                hash ^= hash >> 15;
                hash *= 0x846ca68bu;
                hash ^= hash >> 16;
                return (hash & 0xFFFFFF) / 16777215f;
            }
        }
        // Hash each corner just once: saves millions of operations in a 512px texture on slower CPUs.
        var n00 = N(ix, iy);
        var n10 = N(ix + 1, iy);
        var n01 = N(ix, iy + 1);
        var n11 = N(ix + 1, iy + 1);
        var a = n00 + (n10 - n00) * fx;
        var b = n01 + (n11 - n01) * fx;
        return a + (b - a) * fy;
    }

    // Minimal standards-compliant PNG writer using the framework's zlib stream. WPF is not involved:
    // cache generation is safe on a render worker thread.
    private static void WritePngAtomic(string path, byte[] rgb, int width, int height)
    {
        var temp = path + ".tmp";
        try
        {
            using (var stream = File.Create(temp))
            {
                stream.Write([137, 80, 78, 71, 13, 10, 26, 10]);
                Span<byte> header = stackalloc byte[13];
                header.Clear();
                BinaryPrimitives.WriteInt32BigEndian(header, width);
                BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
                header[8] = 8;
                header[9] = 2; // RGB
                WriteChunk(stream, "IHDR", header);
                using var packed = new MemoryStream();
                using (var zipped = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true))
                    for (var row = 0; row < height; row++)
                    {
                        zipped.WriteByte(0); // PNG filter: None
                        zipped.Write(rgb, row * width * 3, width * 3);
                    }
                WriteChunk(stream, "IDAT", packed.ToArray());
                WriteChunk(stream, "IEND", ReadOnlySpan<byte>.Empty);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> payload)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, payload.Length);
        stream.Write(length);
        Span<byte> tag = stackalloc byte[4];
        for (var i = 0; i < tag.Length; i++) tag[i] = (byte)type[i];
        stream.Write(tag);
        stream.Write(payload);
        uint crc = 0xffffffff;
        void Update(byte value)
        {
            crc ^= value;
            for (var i = 0; i < 8; i++)
                crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1));
        }
        foreach (var b in tag) Update(b);
        foreach (var b in payload) Update(b);
        BinaryPrimitives.WriteUInt32BigEndian(length, ~crc);
        stream.Write(length);
    }
}
