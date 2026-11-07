using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// The lit ball a material is shown on in the library, like Lumion's: a sphere in a small photo studio (a big soft light at the
/// upper left, a thin strip light at the right, a bright sky over a dark floor), so the difference between chrome, brushed
/// steel, rough plastic and rubber can be read from the ball itself. It is drawn on the CPU in a few milliseconds from the
/// material's colour, pattern, roughness, metal and transparency; a photographic material passes its image as
/// <c>image</c>. It is a preview, not the path tracer.
/// </summary>
public static class MaterialBall
{
    private static readonly Vector3 Key = Vector3.Normalize(new Vector3(-0.5f, 0.55f, 0.67f));
    private static readonly Vector3 Strip = Vector3.Normalize(new Vector3(0.78f, 0.1f, 0.6f));

    /// <summary>How many metres of surface the ball's radius spans for each pattern, so blocks show as blocks and weave as weave.</summary>
    private static float BallRadius(SurfacePattern pattern) => pattern switch
    {
        SurfacePattern.Brushed => 0.04f, SurfacePattern.CarbonFiber => 0.035f, SurfacePattern.Perforated => 0.05f,
        SurfacePattern.Fabric => 0.03f, SurfacePattern.WornRubber => 0.12f, SurfacePattern.Patina => 0.2f,
        SurfacePattern.RustPaint => 0.2f, SurfacePattern.Concrete or SurfacePattern.PouredConcrete or SurfacePattern.Asphalt => 0.18f,
        SurfacePattern.Grass => 0.25f, SurfacePattern.Leaves or SurfacePattern.Foliage => 0.4f, SurfacePattern.Bark => 0.35f,
        _ => 0.5f
    };

    private static Vector3 Environment(Vector3 d)
    {
        // Above the horizon a sky from pale haze to blue, below it a warm brown floor that darkens with depth; the line between
        // them is crisp, which is what makes a polished metal read as polished.
        var horizon = new Vector3(0.80f, 0.82f, 0.84f);
        var above = Vector3.Lerp(horizon, new Vector3(0.24f, 0.40f, 0.72f), Smooth(0.0f, 0.75f, d.Y));
        var below = Vector3.Lerp(new Vector3(0.34f, 0.26f, 0.19f), new Vector3(0.04f, 0.032f, 0.025f), Smooth(0.0f, 0.7f, -d.Y));
        var sky = Vector3.Lerp(below, above, Smooth(-0.025f, 0.025f, d.Y));
        var key = 7f * Smooth(MathF.Cos(0.50f), MathF.Cos(0.34f), Vector3.Dot(d, Key));
        var strip = 3f * Smooth(0.12f, 0.03f, MathF.Abs(d.X - Strip.X)) * Smooth(0.0f, 0.15f, d.Z) * Smooth(-0.35f, -0.1f, d.Y) * Smooth(0.85f, 0.6f, d.Y);
        return sky + new Vector3(1f, 0.98f, 0.94f) * key + new Vector3(0.9f, 0.95f, 1f) * strip;
    }

    private static float Smooth(float a, float b, float x)
    {
        var t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    /// <summary>The studio seen through a lobe as wide as the surface is rough: a few directions around the mirror one, averaged.</summary>
    private static Vector3 Reflected(Vector3 mirror, float roughness)
    {
        var spread = roughness * roughness * 1.25f;
        if (spread < 0.01f) return Environment(mirror);
        var up = MathF.Abs(mirror.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY;
        var tangent = Vector3.Normalize(Vector3.Cross(up, mirror));
        var bitangent = Vector3.Cross(mirror, tangent);
        var total = Environment(mirror) * 2;
        for (var k = 0; k < 8; k++)
        {
            var angle = k * MathF.PI / 4;
            var ring = k % 2 == 0 ? 1f : 0.55f;
            total += Environment(Vector3.Normalize(mirror + (tangent * MathF.Cos(angle) + bitangent * MathF.Sin(angle)) * spread * ring));
        }
        return total / 10;
    }

    /// <summary>
    /// Draws the ball as BGRA32 bytes (straight alpha: transparent outside the sphere), <paramref name="size"/> pixels square.
    /// <paramref name="image"/> maps a photograph by longitude and latitude (both 0–1); <paramref name="relief"/> is how strongly
    /// that photograph's brightness raises the surface.
    /// </summary>
    public static byte[] Render(RenderMaterial material, int size, Func<float, float, Vector3>? image = null, float relief = 0)
    {
        ArgumentNullException.ThrowIfNull(material);
        size = Math.Clamp(size, 16, 512);
        var m = material.Sanitized();
        var pixels = new byte[size * size * 4];
        var glass = m.Transmission > 0.5f;
        var rough = Math.Clamp(m.Roughness, 0.03f, 1f);
        var metal = Math.Clamp(m.Metallic, 0f, 1f);
        var scale = MathF.Max(0.01f, m.PatternScale);
        var reach = BallRadius(m.Pattern) * scale;
        Parallel.For(0, size, row =>
        {
            for (var column = 0; column < size; column++)
            {
                float px = (column + 0.5f) / size * 2 - 1, py = 1 - (row + 0.5f) / size * 2;
                var d2 = px * px + py * py;
                if (d2 > 1.04f) continue;
                var z = MathF.Sqrt(MathF.Max(0, 1 - d2));
                var normal = Vector3.Normalize(new Vector3(px, py, MathF.Max(z, 0.02f)));
                var albedo = m.BaseColor;
                if (image is not null)
                {
                    var u = 0.5f + MathF.Atan2(px, z) / (2 * MathF.PI);
                    var v = 0.5f + MathF.Asin(Math.Clamp(py, -1, 1)) / MathF.PI;
                    albedo = image(u, v);
                    if (relief > 0 && d2 < 0.96f)
                    {
                        var step = 1f / 128;
                        var sx = (Sampling.Luminance(image(u + step, v)) - Sampling.Luminance(image(u - step, v))) * relief * 0.17f;
                        var sy = (Sampling.Luminance(image(u, v + step)) - Sampling.Luminance(image(u, v - step))) * relief * 0.17f;
                        normal = Vector3.Normalize(normal + new Vector3(-sx, -sy, 0));
                    }
                }
                else if (m.Pattern != SurfacePattern.None && !m.IsEmissive)
                {
                    var surface = new Vector3(px * reach, py * reach, 0);
                    albedo *= SurfacePatterns.Modulate(m.Pattern, scale, surface, Vector3.UnitZ);
                    if (d2 < 0.97f)
                    {
                        var bent = SurfacePatterns.Relief(m.Pattern, scale, surface, Vector3.UnitZ, 0);
                        normal = Vector3.Normalize(normal + new Vector3(bent.X, bent.Y, 0) * 2.2f);
                    }
                }

                var cosine = MathF.Max(0.02f, normal.Z);
                var mirror = Vector3.Normalize(2 * cosine * normal - Vector3.UnitZ);
                var f0 = Vector3.Lerp(new Vector3(m.DielectricF0), albedo, metal);
                var fresnel = f0 + (Vector3.One - f0) * MathF.Pow(1 - cosine, 5) * (1 - 0.6f * rough);
                var specular = fresnel * Reflected(mirror, rough);
                Vector3 color;
                if (glass)
                {
                    // Clear glass: the studio seen through it, tinted, plus the reflection on its surface.
                    var through = Environment(Vector3.Normalize(new Vector3(-normal.X * 0.35f, -normal.Y * 0.35f, -1)));
                    color = through * m.TransmissionTint * 0.55f * (Vector3.One - fresnel) + specular + new Vector3(0.05f) * m.TransmissionTint;
                }
                else
                {
                    var ambient = 0.12f + 0.42f * (normal.Y * 0.5f + 0.5f);
                    var irradiance = ambient + 0.95f * MathF.Max(0, Vector3.Dot(normal, Key)) + 0.22f * MathF.Max(0, Vector3.Dot(normal, Strip));
                    color = albedo * irradiance * (1 - metal) * (Vector3.One - fresnel) + specular;
                }
                var coverage = Math.Clamp((1 - MathF.Sqrt(d2)) * size * 0.5f + 0.5f, 0f, 1f);
                var offset = (row * size + column) * 4;
                pixels[offset] = Encode(color.Z);
                pixels[offset + 1] = Encode(color.Y);
                pixels[offset + 2] = Encode(color.X);
                pixels[offset + 3] = (byte)Math.Round(coverage * 255);
            }
        });
        return pixels;
    }

    private static byte Encode(float linear)
    {
        // A soft shoulder keeps the studio lights from clipping to flat white, then sRGB.
        var mapped = 1 - MathF.Exp(-1.15f * MathF.Max(0, float.IsFinite(linear) ? linear : 0));
        var srgb = mapped <= 0.0031308f ? mapped * 12.92f : 1.055f * MathF.Pow(mapped, 1 / 2.4f) - 0.055f;
        return (byte)Math.Round(Math.Clamp(srgb, 0, 1) * 255);
    }
}
