using System.Numerics;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Core.Import;

/// <summary>File formats the importer recognizes by content, not only by extension.</summary>
public enum ImportFormat
{
    Unknown,
    Obj,
    Dxf,
    Dwg,
    Stl,
    Ifc,
    Skp,
    /// <summary>Revit project, family or template (an OLE compound file).</summary>
    Revit,
    Gltf,
    Collada,

    /// <summary>Another Guanacaste 3D project, read as a model to link.</summary>
    Native
}

/// <summary>Which model axis points up in the source file.</summary>
public enum UpAxis
{
    /// <summary>Z up when the file says so (CAD, IFC, Revit exports), otherwise Y up as most mesh formats do.</summary>
    Auto,
    Z,
    Y
}

/// <summary>How an import converts the file into the project frame (meters, Z up).</summary>
public sealed record ModelImportOptions
{
    /// <summary>Meters per source unit; null reads the unit from the file or guesses it from the model's size.</summary>
    public double? MetersPerUnit { get; init; }
    public UpAxis UpAxis { get; init; } = UpAxis.Auto;
    /// <summary>Moves the model so its horizontal center sits at the origin.</summary>
    public bool CenterHorizontally { get; init; }
    /// <summary>Moves the model vertically so its lowest point is at Z = 0.</summary>
    public bool PlaceOnGround { get; init; }
    /// <summary>Largest triangle count accepted; protects memory when a file is much bigger than expected.</summary>
    public int MaximumTriangles { get; init; } = 12_000_000;
}

/// <summary>Progress of a long import, 0–1, with a resource key naming the current stage.</summary>
public readonly record struct ImportProgress(double Fraction, string Stage);

/// <summary>What a quick look at a file reveals before the user commits to importing it.</summary>
public sealed record ImportInspection(ImportFormat Format, string FormatName, long FileBytes, bool CanImport,
    string Summary, IReadOnlyList<string> Notes, byte[]? PreviewPng = null);

/// <summary>Outcome of an import: the geometry plus anything the user should know about.</summary>
public sealed record ImportResult(ImportedModel Model, ImportFormat Format, TimeSpan Elapsed);

/// <summary>Collects triangles from a reader and finishes them into an <see cref="ImportedModel"/>.</summary>
internal sealed class MeshAccumulator
{
    private readonly Dictionary<string, int> _materialByName = new(StringComparer.Ordinal);
    private readonly List<RenderMaterial> _materials = [];
    private readonly List<string> _warnings = [];
    private readonly List<ImportedPolyline> _linework = [];

    public ArrayBuilder<Vector3> Positions { get; } = new(1024);
    public ArrayBuilder<int> Indices { get; } = new(3072);
    public ArrayBuilder<int> TriangleMaterial { get; } = new(1024);
    /// <summary>Per-vertex normals, aligned with <see cref="Positions"/>; null until a reader opts in.</summary>
    public ArrayBuilder<Vector3>? Normals { get; set; }
    /// <summary>Per-vertex texture coordinates, aligned with <see cref="Positions"/>; null until a reader opts in.</summary>
    public ArrayBuilder<Vector2>? Uvs { get; set; }
    public int TriangleCount => TriangleMaterial.Count;
    public List<ImportedPolyline> Linework => _linework;
    public IReadOnlyList<string> Warnings => _warnings;
    public IReadOnlyList<RenderMaterial> Materials => _materials;

    public int MaterialIndex(string key, Func<RenderMaterial> create)
    {
        if (_materialByName.TryGetValue(key, out var index)) return index;
        index = _materials.Count;
        _materials.Add(create());
        _materialByName[key] = index;
        return index;
    }

    public void Warn(string message)
    {
        if (_warnings.Count < 40 && !_warnings.Contains(message)) _warnings.Add(message);
    }

    public int AddVertex(Vector3 position, Vector3? normal = null)
    {
        Positions.Add(position);
        Normals?.Add(normal ?? Vector3.Zero);
        return Positions.Count - 1;
    }

    public void AddTriangle(int a, int b, int c, int material)
    {
        Indices.Add(a);
        Indices.Add(b);
        Indices.Add(c);
        TriangleMaterial.Add(material);
    }

    /// <summary>Applies the project frame (axis, units, centering) and returns the finished model.</summary>
    public ImportedModel Build(string path, string format, Vector3 axisHint, double metersPerUnit,
        ModelImportOptions options, string? name = null)
    {
        var positions = Positions.ToArray();
        var normals = Normals?.ToArray();
        var linework = _linework.ToArray();
        var up = ModelFrame.ResolveUpAxis(options.UpAxis, axisHint);
        ModelFrame.Apply(positions, normals, linework, up == UpAxis.Y, (float)metersPerUnit,
            options.CenterHorizontally, options.PlaceOnGround);
        var materials = _materials.Count == 0 ? [MaterialLibrary.Imported] : _materials.ToArray();
        return new ImportedModel
        {
            Name = name ?? Path.GetFileNameWithoutExtension(path),
            SourcePath = path,
            SourceFormat = format,
            Positions = positions,
            Indices = Indices.ToArray(),
            Normals = normals,
            Uvs = Uvs?.ToArray(),
            TriangleMaterial = TriangleMaterial.ToArray(),
            Materials = materials,
            Linework = linework,
            Warnings = _warnings.ToArray()
        };
    }
}

/// <summary>Conversion of source coordinates into the project frame.</summary>
internal static class ModelFrame
{
    /// <summary>
    /// <paramref name="hint"/> is X when the reader knows the file is Z-up (Revit exports, IFC, DXF),
    /// otherwise zero. Auto falls back to the mesh-format default of Y up.
    /// </summary>
    public static UpAxis ResolveUpAxis(UpAxis requested, Vector3 hint) =>
        requested != UpAxis.Auto ? requested : hint.X > 0 ? UpAxis.Z : UpAxis.Y;

    /// <summary>Marker passed by readers whose format is Z-up.</summary>
    public static readonly Vector3 ZUp = Vector3.UnitX;

    /// <summary>Marker passed by readers whose format is Y-up.</summary>
    public static readonly Vector3 YUp = Vector3.Zero;

    /// <summary>Rotates Y-up data to Z-up, scales to meters and optionally recenters, in place.</summary>
    public static void Apply(Vector3[] positions, Vector3[]? normals, ImportedPolyline[] linework, bool yUp, float scale,
        bool center, bool ground)
    {
        for (var i = 0; i < positions.Length; i++) positions[i] = Convert(positions[i], yUp, scale);
        if (normals is not null)
            for (var i = 0; i < normals.Length; i++) normals[i] = Convert(normals[i], yUp, 1);
        foreach (var line in linework)
            for (var i = 0; i < line.Points.Length; i++) line.Points[i] = Convert(line.Points[i], yUp, scale);
        if (!center && !ground) return;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var position in positions)
        {
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }
        if (positions.Length == 0) return;
        var shift = new Vector3(center ? -(min.X + max.X) / 2 : 0, center ? -(min.Y + max.Y) / 2 : 0, ground ? -min.Z : 0);
        for (var i = 0; i < positions.Length; i++) positions[i] += shift;
        foreach (var line in linework)
            for (var i = 0; i < line.Points.Length; i++) line.Points[i] += shift;
    }

    /// <summary>Y-up to Z-up is (x, y, z) → (x, −z, y): a proper rotation, so triangle winding is preserved.</summary>
    private static Vector3 Convert(Vector3 value, bool yUp, float scale) =>
        (yUp ? new Vector3(value.X, -value.Z, value.Y) : value) * scale;
}
