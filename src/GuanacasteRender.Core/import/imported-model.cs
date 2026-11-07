using System.Numerics;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Core.Import;

/// <summary>A CAD polyline kept as plan linework; points are in meters.</summary>
public sealed record ImportedPolyline(string Layer, Vector3[] Points, bool Closed);

/// <summary>
/// Geometry read from an external file (OBJ, DXF, IFC…), already converted to the project frame:
/// meters, Z up, +Y north. Triangles index <see cref="Positions"/>; each has one material.
/// </summary>
public sealed class ImportedModel
{
    private (Vector3 Min, Vector3 Max)? _bounds;

    public string Name { get; init; } = "Modelo importado";
    public string SourcePath { get; init; } = "";
    /// <summary>Format label such as "OBJ", "DXF" or "IFC".</summary>
    public string SourceFormat { get; init; } = "";
    public Vector3[] Positions { get; init; } = [];
    /// <summary>Three vertex indices per triangle.</summary>
    public int[] Indices { get; init; } = [];
    /// <summary>Optional per-vertex smooth normals; null renders flat-shaded.</summary>
    public Vector3[]? Normals { get; init; }
    /// <summary>Optional per-vertex texture coordinates (glTF TEXCOORD_0, v down the image), aligned with positions.</summary>
    public Vector2[]? Uvs { get; init; }
    /// <summary>Index into <see cref="Materials"/> for every triangle.</summary>
    public int[] TriangleMaterial { get; init; } = [];
    public IReadOnlyList<RenderMaterial> Materials { get; init; } = [];
    public IReadOnlyList<ImportedPolyline> Linework { get; init; } = [];
    /// <summary>Problems the reader recovered from, in Spanish keys or plain text for the log.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public int TriangleCount => Indices.Length / 3;

    /// <summary>Axis-aligned bounds of the triangles and linework; a unit box when the model is empty.</summary>
    public (Vector3 Min, Vector3 Max) Bounds()
    {
        if (_bounds is { } cached) return cached;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var any = false;
        foreach (var position in Positions)
        {
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
            any = true;
        }
        foreach (var line in Linework)
            foreach (var point in line.Points)
            {
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
                any = true;
            }
        return (_bounds = any ? (min, max) : (Vector3.Zero, Vector3.One)).Value;
    }

    /// <summary>Returns the same geometry attributed to a different source file, as when a converted copy was read.</summary>
    public ImportedModel WithSource(string path, string format, string name) => new()
    {
        Name = name, SourcePath = path, SourceFormat = format, Positions = Positions, Indices = Indices,
        Normals = Normals, Uvs = Uvs, TriangleMaterial = TriangleMaterial, Materials = Materials, Linework = Linework,
        Warnings = Warnings
    };

    /// <summary>Whether the file contained anything to display.</summary>
    public bool HasGeometry => TriangleCount > 0 || Linework.Count > 0;
}

/// <summary>Placement of a linked model relative to the project: uniform scale, rotation about Z, then offset.</summary>
public sealed record ModelPlacement(double OffsetX = 0, double OffsetY = 0, double OffsetZ = 0,
    double RotationDegrees = 0, double Scale = 1)
{
    public static ModelPlacement Identity { get; } = new();

    /// <summary>Builds the matrix for row-vector transforms (System.Numerics convention).</summary>
    public Matrix4x4 ToMatrix() =>
        Matrix4x4.CreateScale((float)Scale) *
        Matrix4x4.CreateRotationZ((float)(RotationDegrees * Math.PI / 180)) *
        Matrix4x4.CreateTranslation((float)OffsetX, (float)OffsetY, (float)OffsetZ);

    /// <summary>Whether every component is finite and the scale is usable.</summary>
    public bool IsValid => double.IsFinite(OffsetX) && double.IsFinite(OffsetY) && double.IsFinite(OffsetZ) &&
                           double.IsFinite(RotationDegrees) && double.IsFinite(Scale) && Scale > 1e-6 && Scale < 1e6;
}
