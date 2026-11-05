using System.Globalization;
using System.Numerics;
using System.Text;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Core.Import;

/// <summary>Reads binary and ASCII STL meshes, which carry no units: millimeters are the common convention.</summary>
internal static class StlReader
{
    public static ImportedModel Read(string path, ModelImportOptions options, IProgress<ImportProgress>? progress,
        CancellationToken cancellation)
    {
        var bytes = File.ReadAllBytes(path);
        var mesh = new MeshAccumulator { Normals = null };
        var material = mesh.MaterialIndex("stl", () => MaterialLibrary.Imported with { Name = "STL" });
        if (IsBinary(bytes)) ReadBinary(bytes, mesh, material, cancellation);
        else ReadAscii(Encoding.ASCII.GetString(bytes), mesh, material, cancellation);
        if (mesh.TriangleCount == 0) throw new InvalidDataException("The model file contains no geometry.");

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var i = 0; i < mesh.Positions.Count; i++)
        {
            min = Vector3.Min(min, mesh.Positions[i]);
            max = Vector3.Max(max, mesh.Positions[i]);
        }
        // STL has no unit; CAD tools overwhelmingly write millimeters, so assume them unless the size says otherwise.
        var extent = MathF.Max(max.X - min.X, MathF.Max(max.Y - min.Y, max.Z - min.Z));
        var scale = options.MetersPerUnit ?? (extent > 400 ? 0.001 : extent > 40 ? 0.01 : 1);
        progress?.Report(new ImportProgress(0.95, "Import.Finishing"));
        return mesh.Build(path, "STL", ModelFrame.ZUp, scale, options);
    }

    /// <summary>A binary STL's size is exactly 84 + 50 × triangle count; ASCII files that start with "solid" are not.</summary>
    private static bool IsBinary(byte[] bytes)
    {
        if (bytes.Length < 84) return false;
        var count = BitConverter.ToUInt32(bytes, 80);
        return 84L + 50L * count == bytes.Length;
    }

    private static void ReadBinary(byte[] bytes, MeshAccumulator mesh, int material, CancellationToken cancellation)
    {
        var count = (int)BitConverter.ToUInt32(bytes, 80);
        for (var i = 0; i < count; i++)
        {
            if ((i & 0xFFFF) == 0) cancellation.ThrowIfCancellationRequested();
            var offset = 84 + i * 50 + 12; // skip the stored normal
            var a = ReadVector(bytes, offset);
            var b = ReadVector(bytes, offset + 12);
            var c = ReadVector(bytes, offset + 24);
            if (!float.IsFinite(a.X + a.Y + a.Z + b.X + b.Y + b.Z + c.X + c.Y + c.Z)) continue;
            var index = mesh.Positions.Count;
            mesh.Positions.Add(a);
            mesh.Positions.Add(b);
            mesh.Positions.Add(c);
            mesh.AddTriangle(index, index + 1, index + 2, material);
        }
    }

    private static Vector3 ReadVector(byte[] bytes, int offset) =>
        new(BitConverter.ToSingle(bytes, offset), BitConverter.ToSingle(bytes, offset + 4), BitConverter.ToSingle(bytes, offset + 8));

    private static void ReadAscii(string text, MeshAccumulator mesh, int material, CancellationToken cancellation)
    {
        var corner = new List<Vector3>(3);
        var lineNumber = 0;
        foreach (var raw in text.Split('\n'))
        {
            if ((++lineNumber & 0xFFFF) == 0) cancellation.ThrowIfCancellationRequested();
            var line = raw.Trim();
            if (!line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4 ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)) continue;
            corner.Add(new Vector3(x, y, z));
            if (corner.Count < 3) continue;
            var index = mesh.Positions.Count;
            foreach (var point in corner) mesh.Positions.Add(point);
            mesh.AddTriangle(index, index + 1, index + 2, material);
            corner.Clear();
        }
    }
}
