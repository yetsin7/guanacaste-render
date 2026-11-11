using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// Writes a triangulated <see cref="RenderScene"/> as a flat little-endian binary file (<c>.grscene</c>) that the Rust
/// engine in <c>engine/</c> reads without knowing anything about the BIM model. The layout is specified in
/// <c>engine/crates/gr-core/src/scene_file.rs</c>; both sides must change together and bump <see cref="Version"/>.
/// Textures, IES profiles and procedural patterns beyond their identifier are not carried yet.
/// </summary>
public static class SceneExchange
{
    public const string Extension = ".grscene";
    public const uint Version = 1;
    private const uint HasNormals = 1, HasUvs = 2;

    public static void Write(string path, RenderScene scene, RenderCamera camera, RenderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(settings);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        using var writer = new BinaryWriter(stream, Encoding.UTF8);
        var normals = scene.Normals is { } n && n.Length == scene.Positions.Length ? n : null;
        var uvs = scene.TriangleUvs is { } u && u.Length == scene.TriangleCount * 3 ? u : null;

        writer.Write("GRSCENE1"u8);
        writer.Write(Version);
        writer.Write((normals is null ? 0 : HasNormals) | (uvs is null ? 0 : HasUvs));
        writer.Write((uint)scene.Positions.Length);
        writer.Write((uint)scene.TriangleCount);
        writer.Write((uint)scene.Materials.Length);
        writer.Write((uint)scene.Lights.Length);

        Write(writer, camera.Position);
        Write(writer, camera.Target);
        writer.Write(camera.VerticalFieldOfViewDegrees);
        Write(writer, settings.SunDirection);
        writer.Write(settings.SunIntensity);
        writer.Write(settings.SkyIntensity);
        writer.Write(settings.Exposure);

        writer.Write(MemoryMarshal.AsBytes(scene.Positions.AsSpan()));
        writer.Write(MemoryMarshal.AsBytes(scene.Indices.AsSpan()));
        writer.Write(MemoryMarshal.AsBytes(scene.TriangleMaterial.AsSpan()));
        if (normals is not null) writer.Write(MemoryMarshal.AsBytes(normals.AsSpan()));
        if (uvs is not null) writer.Write(MemoryMarshal.AsBytes(uvs.AsSpan()));

        foreach (var material in scene.Materials)
        {
            // 64 bytes per material: fifteen floats and the procedural pattern identifier.
            Write(writer, material.BaseColor);
            writer.Write(material.Roughness);
            writer.Write(material.Metallic);
            writer.Write(material.Ior);
            Write(writer, material.Emission);
            writer.Write(material.Transmission);
            Write(writer, material.TransmissionTint);
            writer.Write(material.Translucency);
            writer.Write(material.AlphaCutoff);
            writer.Write((uint)material.Pattern);
        }
        foreach (var light in scene.Lights)
        {
            Write(writer, light.Position);
            Write(writer, light.Intensity);
            writer.Write(light.Radius);
        }
    }

    /// <summary>Writes rays (origin, direction) so two engines can be timed on exactly the same queries.</summary>
    public static void WriteRays(string path, ReadOnlySpan<(Vector3 Origin, Vector3 Direction)> rays)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        using var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write("GRRAYS01"u8);
        writer.Write((uint)rays.Length);
        foreach (var (origin, direction) in rays)
        {
            Write(writer, origin);
            Write(writer, direction);
        }
    }

    private static void Write(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }
}
