using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core.Interop;

/// <summary>
/// Exports the shared scene mesh to visualization formats (Wavefront OBJ/MTL and binary glTF 2.0)
/// for tools such as Blender, Lumion, Twinmotion, or SketchUp. Both formats use Y-up meters,
/// so model coordinates (x, y, z) map to (x, z, −y).
/// </summary>
public static class MeshExporter
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Surface appearance shared by both exporters (linear RGB, alpha 1 = opaque).</summary>
    private static (string Name, Vector3 Color, float Alpha, float Roughness) Appearance(SurfaceKind kind) => kind switch
    {
        SurfaceKind.DoorLeaf => ("G3D_DoorLeaf", new Vector3(0.60f, 0.39f, 0.22f), 1, 0.6f),
        SurfaceKind.OpeningFrame => ("G3D_OpeningFrame", new Vector3(0.25f, 0.27f, 0.29f), 1, 0.4f),
        SurfaceKind.Wall => ("G3D_Wall", new Vector3(0.80f, 0.78f, 0.73f), 1, 0.9f),
        SurfaceKind.Floor => ("G3D_Floor", new Vector3(0.56f, 0.55f, 0.52f), 1, 0.8f),
        SurfaceKind.Structure => ("G3D_Structure", new Vector3(0.45f, 0.42f, 0.38f), 1, 0.7f),
        SurfaceKind.Foliage => ("G3D_Foliage", new Vector3(0.12f, 0.24f, 0.07f), 1, 0.65f),
        SurfaceKind.Bark => ("G3D_Bark", new Vector3(0.24f, 0.19f, 0.14f), 1, 0.95f),
        SurfaceKind.Earth => ("G3D_Earth", new Vector3(0.30f, 0.22f, 0.15f), 1, 0.97f),
        SurfaceKind.Rock => ("G3D_Rock", new Vector3(0.34f, 0.32f, 0.29f), 1, 0.93f),
        SurfaceKind.Roof => ("G3D_Roof", new Vector3(0.50f, 0.22f, 0.12f), 1, 0.72f),
        SurfaceKind.Leaf => ("G3D_Leaf", new Vector3(0.10f, 0.20f, 0.05f), 1, 0.45f),
        SurfaceKind.InteriorWall => ("G3D_InteriorWall", new Vector3(0.80f, 0.78f, 0.73f), 1, 0.9f),
        SurfaceKind.Metal => ("G3D_Metal", new Vector3(0.62f, 0.62f, 0.63f), 1, 0.32f),
        SurfaceKind.LightEmitter => ("G3D_Light", new Vector3(0.95f, 0.92f, 0.85f), 1, 0.6f),
        SurfaceKind.SpotEmitter => ("G3D_Spot", new Vector3(0.95f, 0.92f, 0.85f), 1, 0.6f),
        SurfaceKind.WallBrick => ("G3D_Brick", new Vector3(0.52f, 0.24f, 0.15f), 1, 0.9f),
        SurfaceKind.WallBlock => ("G3D_Block", new Vector3(0.50f, 0.50f, 0.48f), 1, 0.92f),
        SurfaceKind.WallConcrete => ("G3D_Concrete", new Vector3(0.52f, 0.51f, 0.49f), 1, 0.85f),
        SurfaceKind.WallStone => ("G3D_Stone", new Vector3(0.46f, 0.43f, 0.38f), 1, 0.9f),
        SurfaceKind.WallWood => ("G3D_Cladding", new Vector3(0.50f, 0.34f, 0.20f), 1, 0.7f),
        SurfaceKind.WallMetal => ("G3D_MetalPanel", new Vector3(0.70f, 0.72f, 0.74f), 1, 0.4f),
        SurfaceKind.Mirror => ("G3D_Mirror", new Vector3(0.92f, 0.93f, 0.93f), 1, 0.02f),
        SurfaceKind.PipeCold => ("G3D_PipeCold", new Vector3(0.16f, 0.36f, 0.72f), 1, 0.35f),
        SurfaceKind.PipeHot => ("G3D_PipeHot", new Vector3(0.72f, 0.16f, 0.12f), 1, 0.35f),
        SurfaceKind.PipeDrain => ("G3D_PipeDrain", new Vector3(0.78f, 0.76f, 0.70f), 1, 0.4f),
        SurfaceKind.PipeStorm => ("G3D_PipeStorm", new Vector3(0.30f, 0.55f, 0.32f), 1, 0.4f),
        SurfaceKind.SolarCell => ("G3D_SolarCell", new Vector3(0.02f, 0.04f, 0.10f), 1, 0.06f),
        SurfaceKind.FireRed => ("G3D_FireRed", new Vector3(0.70f, 0.05f, 0.04f), 1, 0.25f),
        SurfaceKind.LightOff => ("G3D_LightOff", new Vector3(0.9f, 0.88f, 0.84f), 1, 0.6f),
        SurfaceKind.Upholstery => ("G3D_Upholstery", new Vector3(0.30f, 0.28f, 0.25f), 1, 0.95f),
        SurfaceKind.Bedding => ("G3D_Bedding", new Vector3(0.80f, 0.79f, 0.76f), 1, 0.92f),
        SurfaceKind.Ceramic => ("G3D_Ceramic", new Vector3(0.86f, 0.86f, 0.84f), 1, 0.07f),
        SurfaceKind.Water => ("G3D_Water", new Vector3(0.015f, 0.05f, 0.06f), 1, 0.02f),
        SurfaceKind.CarPaint => ("G3D_CarPaint", new Vector3(0.018f, 0.024f, 0.045f), 1, 0.08f),
        SurfaceKind.CarPaintSilver => ("G3D_CarPaintSilver", new Vector3(0.72f, 0.73f, 0.74f), 1, 0.16f),
        SurfaceKind.TintedGlass => ("G3D_TintedGlass", new Vector3(0.006f, 0.008f, 0.010f), 1, 0.03f),
        SurfaceKind.Rubber => ("G3D_Rubber", new Vector3(0.025f, 0.025f, 0.025f), 1, 0.9f),
        SurfaceKind.Asphalt => ("G3D_Asphalt", new Vector3(0.11f, 0.11f, 0.11f), 1, 0.95f),
        SurfaceKind.Glass => ("G3D_Glass", new Vector3(0.60f, 0.75f, 0.82f), 0.3f, 0.05f),
        _ => ("G3D_Ground", new Vector3(0.30f, 0.40f, 0.24f), 1, 1)
    };

    private static Vector3 ToYUp(Vector3 point) => new(point.X, point.Z, -point.Y);

    /// <summary>Returns OBJ text and its companion MTL text; faces are grouped per material.</summary>
    public static (string Obj, string Mtl) ToObj(SceneMesh mesh, string materialLibraryName)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var obj = new StringBuilder();
        obj.Append("# Guanacaste 3D export (meters; Y-up)\n");
        obj.Append("mtllib ").Append(materialLibraryName).Append('\n');
        foreach (var position in mesh.Positions)
        {
            var p = ToYUp(position);
            obj.Append("v ").Append(Number(p.X)).Append(' ').Append(Number(p.Y)).Append(' ').Append(Number(p.Z)).Append('\n');
        }
        var mtl = new StringBuilder("# Guanacaste 3D materials\n");
        foreach (var group in Enumerable.Range(0, mesh.TriangleCount).GroupBy(t => mesh.TriangleKinds[t]).OrderBy(g => g.Key))
        {
            var (name, color, alpha, roughness) = Appearance(group.Key);
            obj.Append("g ").Append(name).Append("\nusemtl ").Append(name).Append('\n');
            foreach (var triangle in group)
            {
                obj.Append('f');
                for (var corner = 0; corner < 3; corner++)
                    obj.Append(' ').Append((mesh.Indices[triangle * 3 + corner] + 1).ToString(Invariant));
                obj.Append('\n');
            }
            mtl.Append("newmtl ").Append(name).Append('\n')
                .Append("Kd ").Append(Number(color.X)).Append(' ').Append(Number(color.Y)).Append(' ').Append(Number(color.Z)).Append('\n')
                .Append("d ").Append(Number(alpha)).Append('\n')
                .Append("Ns ").Append(Number((1 - roughness) * 900 + 10)).Append('\n')
                .Append("illum 2\n");
        }
        return (obj.ToString(), mtl.ToString());
    }

    /// <summary>Builds a self-contained GLB (glTF 2.0 binary) with one primitive per material.</summary>
    public static byte[] ToGlb(SceneMesh mesh, string sceneName)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var binary = new MemoryStream();
        var bufferViews = new List<object>();
        var accessors = new List<object>();
        var materials = new List<object>();
        var primitives = new List<object>();

        foreach (var group in Enumerable.Range(0, mesh.TriangleCount).GroupBy(t => mesh.TriangleKinds[t]).OrderBy(g => g.Key))
        {
            // Flat-shaded, unindexed triangles: each gets its own vertices and a face normal.
            var triangles = group.ToArray();
            var positions = new Vector3[triangles.Length * 3];
            var normals = new Vector3[triangles.Length * 3];
            for (var i = 0; i < triangles.Length; i++)
            {
                var a = ToYUp(mesh.Positions[mesh.Indices[triangles[i] * 3]]);
                var b = ToYUp(mesh.Positions[mesh.Indices[triangles[i] * 3 + 1]]);
                var c = ToYUp(mesh.Positions[mesh.Indices[triangles[i] * 3 + 2]]);
                var cross = Vector3.Cross(b - a, c - a);
                var normal = cross.LengthSquared() > 0 ? Vector3.Normalize(cross) : Vector3.UnitY;
                (positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]) = (a, b, c);
                normals[i * 3] = normals[i * 3 + 1] = normals[i * 3 + 2] = normal;
            }
            var min = positions.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
            var max = positions.Aggregate(new Vector3(float.MinValue), Vector3.Max);
            var positionAccessor = AddVec3(binary, bufferViews, accessors, positions, min, max);
            var normalAccessor = AddVec3(binary, bufferViews, accessors, normals, null, null);

            var (name, color, alpha, roughness) = Appearance(group.Key);
            materials.Add(new Dictionary<string, object>
            {
                ["name"] = name,
                ["doubleSided"] = true,
                ["alphaMode"] = alpha < 1 ? "BLEND" : "OPAQUE",
                ["pbrMetallicRoughness"] = new Dictionary<string, object>
                {
                    ["baseColorFactor"] = new[] { color.X, color.Y, color.Z, alpha },
                    ["metallicFactor"] = 0,
                    ["roughnessFactor"] = roughness
                }
            });
            primitives.Add(new Dictionary<string, object>
            {
                ["attributes"] = new Dictionary<string, int> { ["POSITION"] = positionAccessor, ["NORMAL"] = normalAccessor },
                ["material"] = materials.Count - 1,
                ["mode"] = 4
            });
        }

        var gltf = new Dictionary<string, object>
        {
            ["asset"] = new Dictionary<string, string> { ["version"] = "2.0", ["generator"] = "Guanacaste 3D" },
            ["scene"] = 0,
            ["scenes"] = new[] { new Dictionary<string, object> { ["name"] = sceneName, ["nodes"] = new[] { 0 } } },
            ["nodes"] = new[] { new Dictionary<string, object> { ["name"] = sceneName, ["mesh"] = 0 } },
            ["meshes"] = new[] { new Dictionary<string, object> { ["name"] = sceneName, ["primitives"] = primitives } },
            ["materials"] = materials,
            ["accessors"] = accessors,
            ["bufferViews"] = bufferViews,
            ["buffers"] = new[] { new Dictionary<string, long> { ["byteLength"] = binary.Length } }
        };
        if (primitives.Count == 0)
        {
            // glTF forbids meshes without primitives; emit an empty scene instead.
            gltf.Remove("meshes"); gltf.Remove("materials"); gltf.Remove("accessors");
            gltf.Remove("bufferViews"); gltf.Remove("buffers");
            gltf["nodes"] = new[] { new Dictionary<string, object> { ["name"] = sceneName } };
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(gltf);
        var jsonPadded = Pad(json, (byte)' ');
        var binaryPadded = Pad(binary.ToArray(), 0);
        var hasBinary = binaryPadded.Length > 0;
        var totalLength = 12 + 8 + jsonPadded.Length + (hasBinary ? 8 + binaryPadded.Length : 0);

        using var output = new MemoryStream(totalLength);
        using var writer = new BinaryWriter(output);
        writer.Write(0x46546C67u); // "glTF"
        writer.Write(2u);
        writer.Write((uint)totalLength);
        writer.Write((uint)jsonPadded.Length);
        writer.Write(0x4E4F534Au); // "JSON"
        writer.Write(jsonPadded);
        if (hasBinary)
        {
            writer.Write((uint)binaryPadded.Length);
            writer.Write(0x004E4942u); // "BIN\0"
            writer.Write(binaryPadded);
        }
        writer.Flush();
        return output.ToArray();
    }

    /// <summary>Appends a float VEC3 array as a 4-byte-aligned buffer view and accessor.</summary>
    private static int AddVec3(MemoryStream binary, List<object> bufferViews, List<object> accessors,
        Vector3[] values, Vector3? min, Vector3? max)
    {
        var offset = (int)binary.Length;
        var bytes = new byte[values.Length * 12];
        for (var i = 0; i < values.Length; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 12), values[i].X);
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 12 + 4), values[i].Y);
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 12 + 8), values[i].Z);
        }
        binary.Write(bytes);
        bufferViews.Add(new Dictionary<string, int>
        {
            ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = bytes.Length, ["target"] = 34962
        });
        var accessor = new Dictionary<string, object>
        {
            ["bufferView"] = bufferViews.Count - 1,
            ["componentType"] = 5126,
            ["count"] = values.Length,
            ["type"] = "VEC3"
        };
        if (min is Vector3 low && max is Vector3 high)
        {
            accessor["min"] = new[] { low.X, low.Y, low.Z };
            accessor["max"] = new[] { high.X, high.Y, high.Z };
        }
        accessors.Add(accessor);
        return accessors.Count - 1;
    }

    /// <summary>Pads a GLB chunk to a 4-byte boundary as the specification requires.</summary>
    private static byte[] Pad(byte[] data, byte filler)
    {
        var padded = new byte[(data.Length + 3) & ~3];
        data.CopyTo(padded, 0);
        padded.AsSpan(data.Length).Fill(filler);
        return padded;
    }

    private static string Number(float value) => value.ToString("0.######", Invariant);
}
