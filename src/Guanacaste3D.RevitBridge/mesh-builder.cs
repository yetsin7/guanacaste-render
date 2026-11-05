using System.Runtime.InteropServices;
using System.Text.Json;
using Autodesk.Revit.DB;

namespace Guanacaste3D.RevitBridge;

/// <summary>
/// Triangles of one element in its own frame: model points are moved by −origin, turned by −rotation about Z and
/// converted from feet to meters, so every upright instance of a family yields the same numbers.
/// </summary>
internal sealed class MeshBuilder(XYZ origin, double rotation)
{
    private const float Meters = 0.3048f;
    private readonly List<float> _positions = [];
    private readonly List<int> _indices = [];
    private readonly List<int> _materials = [];
    private readonly double _cos = Math.Cos(-rotation);
    private readonly double _sin = Math.Sin(-rotation);

    public int VertexCount => _positions.Count / 3;
    public int TriangleCount => _materials.Count;

    public void AddVertex(XYZ point)
    {
        var (x, y, z) = (point.X - origin.X, point.Y - origin.Y, point.Z - origin.Z);
        _positions.Add((float)(x * _cos - y * _sin) * Meters);
        _positions.Add((float)(x * _sin + y * _cos) * Meters);
        _positions.Add((float)z * Meters);
    }

    public void AddTriangle(int a, int b, int c, int material)
    {
        _indices.Add(a);
        _indices.Add(b);
        _indices.Add(c);
        _materials.Add(material);
    }

    /// <summary>A fingerprint of the geometry rounded to 0.1 mm, so floating noise between instances is ignored.</summary>
    public ulong Hash()
    {
        var hash = 14695981039346656037UL;
        void Mix(long value)
        {
            hash ^= (ulong)value;
            hash *= 1099511628211UL;
        }
        Mix(_positions.Count);
        Mix(_indices.Count);
        foreach (var value in _positions) Mix((long)Math.Round(value * 10000));
        foreach (var value in _indices) Mix(value);
        foreach (var value in _materials) Mix(value);
        return hash;
    }

    public void WriteTo(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("positions", Convert.ToBase64String(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(_positions))));
        writer.WriteString("indices", Convert.ToBase64String(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(_indices))));
        writer.WriteString("materials", Convert.ToBase64String(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(_materials))));
        writer.WriteEndObject();
    }
}
