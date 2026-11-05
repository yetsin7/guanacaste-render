using System.Numerics;
using System.Text;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// Bounded Radiance RGBE (.hdr) equirectangular sky in scene-linear radiance.
/// Supports the standard modern per-channel RLE scanlines and plain RGBE scanlines;
/// orientation -Y +X only. It is immutable and safe to share between CPU render workers.
/// </summary>
public sealed class HdriEnvironment
{
    private readonly Vector3[] _pixels;
    private HdriEnvironment(int width, int height, Vector3[] pixels)
    {
        Width = width;
        Height = height;
        _pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public long EstimatedBytes => _pixels.Length * 12L;
    public const int MaximumFileBytes = 32 * 1024 * 1024;

    public static HdriEnvironment FromRgbe(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length > MaximumFileBytes || data.Length < 32)
            throw new InvalidDataException("HDRI must be a Radiance file smaller than 32 MiB.");
        var offset = 0;
        string ReadLine()
        {
            var start = offset;
            while (offset < data.Length && data[offset] != (byte)'\n')
            {
                if (offset - start >= 256)
                    throw new InvalidDataException("HDRI header contains a line longer than 256 bytes.");
                offset++;
            }
            if (offset == data.Length) throw new InvalidDataException("HDRI header is incomplete.");
            var line = Encoding.ASCII.GetString(data, start, offset - start).TrimEnd('\r');
            offset++;
            return line;
        }

        var signature = ReadLine();
        if (signature is not ("#?RADIANCE" or "#?RGBE"))
            throw new InvalidDataException("Unsupported HDRI signature.");
        var format = false;
        var separator = false;
        for (var line = 0; line < 32; line++)
        {
            var text = ReadLine();
            if (text.Length == 0) { separator = true; break; }
            if (text == "FORMAT=32-bit_rle_rgbe") format = true;
        }
        if (!separator || !format) throw new InvalidDataException("Radiance RGBE format is required.");

        var dimensions = ReadLine().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (dimensions.Length != 4 || dimensions[0] != "-Y" || dimensions[2] != "+X" ||
            !int.TryParse(dimensions[1], out var height) || !int.TryParse(dimensions[3], out var width) ||
            width < 1 || width > 2048 || height < 1 || height > 1024 || (long)width * height > 2_097_152)
            throw new InvalidDataException("HDRI must use -Y +X orientation, at most 2048 x 1024 pixels.");

        var pixels = new Vector3[width * height];
        var scanline = new byte[width * 4];
        for (var y = 0; y < height; y++)
        {
            if (offset + 4 > data.Length) throw new InvalidDataException("HDRI scanline is truncated.");
            var newRle = width is >= 8 and <= 32767 && data[offset] == 2 && data[offset + 1] == 2 &&
                ((data[offset + 2] << 8) | data[offset + 3]) == width;
            if (newRle)
            {
                offset += 4;
                // Radiance RLE stores all reds, greens, blues and exponents in four consecutive channel runs.
                var channels = new byte[width * 4];
                for (var channel = 0; channel < 4; channel++)
                {
                    var x = 0;
                    while (x < width)
                    {
                        if (offset >= data.Length) throw new InvalidDataException("HDRI RLE channel is truncated.");
                        var run = data[offset++];
                        if (run == 0) throw new InvalidDataException("HDRI RLE run length is zero.");
                        var repeated = run > 128;
                        var count = repeated ? run - 128 : run;
                        if (count > width - x || count < 1) throw new InvalidDataException("HDRI RLE exceeds its scanline.");
                        if (repeated)
                        {
                            if (offset >= data.Length) throw new InvalidDataException("HDRI RLE repeat value is missing.");
                            var value = data[offset++];
                            Array.Fill(channels, value, channel * width + x, count);
                        }
                        else
                        {
                            if (count > data.Length - offset) throw new InvalidDataException("HDRI RLE literals are missing.");
                            Array.Copy(data, offset, channels, channel * width + x, count);
                            offset += count;
                        }
                        x += count;
                    }
                }
                for (var x = 0; x < width; x++)
                for (var channel = 0; channel < 4; channel++)
                    scanline[x * 4 + channel] = channels[channel * width + x];
            }
            else
            {
                if ((long)offset + width * 4 > data.Length)
                    throw new InvalidDataException("HDRI uncompressed scanline is truncated.");
                Array.Copy(data, offset, scanline, 0, width * 4);
                offset += width * 4;
            }

            for (var x = 0; x < width; x++)
            {
                var i = x * 4;
                var exponent = scanline[i + 3];
                if (exponent == 0) continue;
                var scale = Math.Pow(2, exponent - 136);
                pixels[y * width + x] = new Vector3(
                    ToRadiance(scanline[i] * scale),
                    ToRadiance(scanline[i + 1] * scale),
                    ToRadiance(scanline[i + 2] * scale));
            }
        }
        if (offset != data.Length) throw new InvalidDataException("HDRI has unexpected data after its final scanline.");
        return new HdriEnvironment(width, height, pixels);
    }

    private static float ToRadiance(double value) => (float)Math.Clamp(value, 0d, 10_000d);

    /// <summary>
    /// Bilinear equirectangular environment lookup in Z-up world coordinates.
    /// Longitudes repeat seamlessly; polar rows clamp, and an optional yaw rotates the environment.
    /// </summary>
    public Vector3 Sample(Vector3 direction, float yawDegrees = 0)
    {
        if (!float.IsFinite(direction.X + direction.Y + direction.Z) || direction.LengthSquared() < 1e-12f)
            return Vector3.Zero;
        direction = Vector3.Normalize(direction);
        // Reduce degrees before converting to radians: even a finite float yaw can
        // overflow float multiplication, producing NaN UVs and contaminating the sky.
        // Double precision preserves the remainder of large finite float inputs.
        var yaw = float.IsFinite(yawDegrees)
            ? (float)(((double)yawDegrees % 360.0) * (Math.PI / 180.0))
            : 0f;
        var longitude = MathF.Atan2(direction.Y, direction.X) + yaw;
        var u = longitude / (2 * MathF.PI) + 0.5f;
        u -= MathF.Floor(u);
        var v = MathF.Acos(Math.Clamp(direction.Z, -1f, 1f)) / MathF.PI;
        var x = u * Width - 0.5f;
        var y = Math.Clamp(v * Height - 0.5f, 0, Height - 1);
        var left = (int)MathF.Floor(x);
        var top = (int)y;
        var dx = x - left;
        var dy = y - top;
        var a = Texel(left, top);
        var b = Texel(left + 1, top);
        var c = Texel(left, top + 1);
        var d = Texel(left + 1, top + 1);
        return Vector3.Lerp(Vector3.Lerp(a, b, dx), Vector3.Lerp(c, d, dx), dy);
    }

    private Vector3 Texel(int x, int y) =>
        _pixels[Math.Clamp(y, 0, Height - 1) * Width + ((x % Width + Width) % Width)];
}
