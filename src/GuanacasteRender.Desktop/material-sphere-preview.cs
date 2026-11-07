using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Fast, deterministic, physically informed material-ball thumbnails.
/// This is a bounded CPU preview (not a replacement for the final path tracer).
/// Source photographs are decoded at a small resolution before entering this function.
/// </summary>
internal static class MaterialSpherePreview
{
    internal const int DefaultWidth = 132;
    internal const int DefaultHeight = 84;
    internal const float DefaultRadius = 36.5f;
    private static readonly Vector3 Light = Vector3.Normalize(new Vector3(-0.57f, -0.62f, 0.55f));
    private static readonly Vector3 HalfVector = Vector3.Normalize(Light + Vector3.UnitZ);

    internal static BitmapSource Create(BitmapSource source, double roughness, double metallic, double relief) =>
        CreateSized(source, roughness, metallic, relief, DefaultWidth, DefaultHeight, DefaultRadius);

    /// <summary>The same shader on a canvas of any size, for the large ball beside the sliders.</summary>
    internal static BitmapSource CreateSized(BitmapSource source, double roughness, double metallic, double relief,
        int width, int height, float radius)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.PixelWidth < 1 || source.PixelHeight < 1)
            throw new ArgumentException("Material thumbnail is empty.", nameof(source));

        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);

        var output = new byte[width * height * 4];
        var r = float.IsFinite((float)roughness) ? Math.Clamp((float)roughness, .04f, 1) : .8f;
        var metal = float.IsFinite((float)metallic) ? Math.Clamp((float)metallic, 0, 1) : 0;
        var bump = float.IsFinite((float)relief) ? Math.Clamp((float)relief, 0, 1) : 0;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var index = (y * width + x) * 4;
                var px = (x + .5f - width * .5f) / radius;
                var py = (y + .5f - height * .5f) / radius;
                var distanceSquared = px * px + py * py;
                var background = 0.10f + .055f * (1 - y / (float)height);
                var backgroundColor = new Vector3(background * .74f, background * .86f, background);
                Vector3 color = backgroundColor;
                if (distanceSquared <= 1.055f)
                {
                    var z = MathF.Sqrt(MathF.Max(0, 1 - distanceSquared));
                    var normal = new Vector3(px, py, z);
                    var u = .5f + MathF.Atan2(px, z) / (2 * MathF.PI);
                    var v = .5f + MathF.Asin(Math.Clamp(py, -1, 1)) / MathF.PI;
                    var albedo = Sample(pixels, bitmap.PixelWidth, bitmap.PixelHeight, u, v);
                    if (bump > 0 && distanceSquared < .97f)
                    {
                        // Subtle per-texel gradients communicate joints/pores on a sphere without inventing geometry.
                        var du = 1f / bitmap.PixelWidth;
                        var dv = 1f / bitmap.PixelHeight;
                        var sx = (Luminance(Sample(pixels, bitmap.PixelWidth, bitmap.PixelHeight, u + du, v)) -
                                  Luminance(Sample(pixels, bitmap.PixelWidth, bitmap.PixelHeight, u - du, v))) * bump * .17f;
                        var sy = (Luminance(Sample(pixels, bitmap.PixelWidth, bitmap.PixelHeight, u, v + dv)) -
                                  Luminance(Sample(pixels, bitmap.PixelWidth, bitmap.PixelHeight, u, v - dv))) * bump * .17f;
                        normal = Vector3.Normalize(normal + new Vector3(-sx, -sy, 0));
                    }

                    var ndl = MathF.Max(0, Vector3.Dot(normal, Light));
                    var ndh = MathF.Max(0, Vector3.Dot(normal, HalfVector));
                    var power = 6f + 300f * (1 - r) * (1 - r) * (1 - r);
                    var highlight = MathF.Pow(ndh, power) * (.25f + .85f * (1 - r));
                    var fresnel = MathF.Pow(1 - MathF.Max(0, normal.Z), 5);
                    var diffuse = (.30f + .76f * ndl) * (1 - metal * .65f);
                    color = albedo * diffuse +
                            Vector3.Lerp(new Vector3(.38f), albedo, metal) * highlight * (1 + metal) +
                            Vector3.Lerp(new Vector3(.045f), albedo * .35f, metal) * fresnel;
                    var coverage = Math.Clamp((1 - MathF.Sqrt(distanceSquared)) * radius * 1.5f + .5f, 0, 1);
                    color = Vector3.Lerp(backgroundColor, color, coverage);
                }

                output[index] = ToSrgb(color.Z);     // B
                output[index + 1] = ToSrgb(color.Y); // G
                output[index + 2] = ToSrgb(color.X); // R
                output[index + 3] = 255;
            }

        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, output, width * 4);
        result.Freeze();
        return result;
    }

    private static float Luminance(Vector3 value) => value.X * .2126f + value.Y * .7152f + value.Z * .0722f;

    private static Vector3 Sample(byte[] pixels, int width, int height, float u, float v)
    {
        // The texture wraps around the sphere. Interpolating in linear light keeps the
        // small preview consistent with the path tracer's treatment of sRGB albedo.
        var xx = (u - MathF.Floor(u)) * width - .5f;
        var yy = Math.Clamp(v, 0, 1) * (height - 1);
        var left = (int)MathF.Floor(xx);
        var top = Math.Clamp((int)MathF.Floor(yy), 0, height - 1);
        var right = (left + 1 + width) % width;
        left = (left % width + width) % width;
        var bottom = Math.Min(height - 1, top + 1);
        var tx = xx - MathF.Floor(xx);
        var ty = yy - top;
        return Vector3.Lerp(Vector3.Lerp(Read(pixels, width, left, top), Read(pixels, width, right, top), tx),
            Vector3.Lerp(Read(pixels, width, left, bottom), Read(pixels, width, right, bottom), tx), ty);
    }

    private static Vector3 Read(byte[] pixels, int width, int x, int y)
    {
        var offset = (y * width + x) * 4;
        return new Vector3(
            ToLinear(pixels[offset + 2]), ToLinear(pixels[offset + 1]), ToLinear(pixels[offset]));
    }

    private static float ToLinear(byte value)
    {
        var srgb = value / 255f;
        return srgb <= .04045f ? srgb / 12.92f : MathF.Pow((srgb + .055f) / 1.055f, 2.4f);
    }

    private static byte ToSrgb(float linear)
    {
        linear = Math.Clamp(linear, 0, 1);
        var srgb = linear <= .0031308f ? 12.92f * linear : 1.055f * MathF.Pow(linear, 1 / 2.4f) - .055f;
        return (byte)Math.Clamp((int)MathF.Round(srgb * 255), 0, 255);
    }
}
