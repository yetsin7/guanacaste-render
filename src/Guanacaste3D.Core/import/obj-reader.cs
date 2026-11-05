using System.Buffers;
using System.Buffers.Text;
using System.Numerics;
using System.Text;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Core.Import;

/// <summary>
/// Streaming Wavefront OBJ reader built for large exports (hundreds of megabytes from Revit or SketchUp).
/// It parses bytes directly without creating strings per line, merges vertex/normal pairs into single
/// vertices so smooth shading survives, triangulates polygons (including concave ones) and reads the
/// companion MTL file for colors, glass and shininess.
/// </summary>
internal static class ObjReader
{
    private const int ChunkSize = 1 << 20;

    /// <summary>Reads an OBJ file. Unit and axis handling follow <paramref name="options"/>.</summary>
    public static ImportedModel Read(string path, ModelImportOptions options, IProgress<ImportProgress>? progress,
        CancellationToken cancellation)
    {
        var state = new ParseState(options);
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, ChunkSize,
                   FileOptions.SequentialScan))
        {
            var buffer = ArrayPool<byte>.Shared.Rent(ChunkSize * 2);
            try
            {
                var held = 0;
                long consumed = 0;
                var firstChunk = true;
                while (true)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (held == buffer.Length)
                    {
                        // A single line longer than the buffer: grow it instead of failing.
                        var bigger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                        Buffer.BlockCopy(buffer, 0, bigger, 0, held);
                        ArrayPool<byte>.Shared.Return(buffer);
                        buffer = bigger;
                    }
                    var read = stream.Read(buffer, held, buffer.Length - held);
                    held += read;
                    if (firstChunk)
                    {
                        state.DetectHeader(buffer.AsSpan(0, Math.Min(held, 400)));
                        firstChunk = false;
                    }
                    var end = read == 0 ? held : buffer.AsSpan(0, held).LastIndexOf((byte)'\n') + 1;
                    if (end > 0)
                    {
                        ParseLines(buffer.AsSpan(0, end), state, path);
                        consumed += end;
                        held -= end;
                        Buffer.BlockCopy(buffer, end, buffer, 0, held);
                        progress?.Report(new ImportProgress(Math.Min(0.95, (double)consumed / Math.Max(1, stream.Length)),
                            "Import.Reading"));
                    }
                    if (read == 0) break;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        if (state.Mesh.TriangleCount == 0) throw new InvalidDataException("The model file contains no geometry.");
        var scale = options.MetersPerUnit ?? ModelUnits.Detect(state.Bounds, state.AssumedMeters);
        progress?.Report(new ImportProgress(0.97, "Import.Finishing"));
        return state.Mesh.Build(path, "OBJ", state.GeneratedByCad ? ModelFrame.ZUp : ModelFrame.YUp, scale, options);
    }

    /// <summary>All mutable state of a parse, kept in one place so the line parser stays a pure function of it.</summary>
    private sealed class ParseState
    {
        private readonly ModelImportOptions _options;
        public readonly MeshAccumulator Mesh = new();
        public readonly ArrayBuilder<Vector3> RawPositions = new(1 << 16);
        public readonly ArrayBuilder<Vector3> RawNormals = new(1 << 16);
        public VertexMap Vertices = new(1 << 16);
        public Dictionary<string, RenderMaterial>? MaterialLibrary;
        public int CurrentMaterial = -1;
        public Vector3 BoundsMin = new(float.MaxValue);
        public Vector3 BoundsMax = new(float.MinValue);
        public bool GeneratedByCad;
        public readonly List<Vector3> Scratch = [];
        public readonly List<int> ScratchVertex = [];

        public ParseState(ModelImportOptions options)
        {
            _options = options;
            Mesh.Normals = new ArrayBuilder<Vector3>(1 << 16);
        }

        public (Vector3 Min, Vector3 Max) Bounds => (BoundsMin, BoundsMax);

        /// <summary>Whether the file says its units are meters (Autodesk exporters do), so size heuristics are skipped.</summary>
        public bool AssumedMeters { get; private set; }

        public int MaximumTriangles => _options.MaximumTriangles;

        public void DetectHeader(ReadOnlySpan<byte> head)
        {
            var text = Encoding.ASCII.GetString(head);
            // Autodesk's ATF translator (Revit, Inventor, AutoCAD) writes Z-up meters.
            if (text.Contains("Autodesk", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("ATF", StringComparison.Ordinal))
            {
                GeneratedByCad = true;
                AssumedMeters = true;
            }
        }
    }

    /// <summary>Open-addressing map from (vertex, normal) pairs to merged output vertices.</summary>
    private struct VertexMap
    {
        private long[] _keys;
        private int[] _values;
        private int _count;

        public VertexMap(int capacity)
        {
            var size = 1 << 4;
            while (size < capacity * 2) size <<= 1;
            _keys = new long[size];
            _values = new int[size];
            Array.Fill(_keys, -1L);
        }

        public bool TryGetOrAdd(long key, int candidate, out int value)
        {
            if (_count * 2 >= _keys.Length) Grow();
            var mask = _keys.Length - 1;
            var slot = (int)(Mix(key) & (ulong)mask);
            while (true)
            {
                var existing = _keys[slot];
                if (existing == key)
                {
                    value = _values[slot];
                    return true;
                }
                if (existing == -1)
                {
                    _keys[slot] = key;
                    _values[slot] = candidate;
                    _count++;
                    value = candidate;
                    return false;
                }
                slot = (slot + 1) & mask;
            }
        }

        private void Grow()
        {
            var oldKeys = _keys;
            var oldValues = _values;
            _keys = new long[oldKeys.Length * 2];
            _values = new int[oldKeys.Length * 2];
            Array.Fill(_keys, -1L);
            var mask = _keys.Length - 1;
            for (var i = 0; i < oldKeys.Length; i++)
            {
                if (oldKeys[i] == -1) continue;
                var slot = (int)(Mix(oldKeys[i]) & (ulong)mask);
                while (_keys[slot] != -1) slot = (slot + 1) & mask;
                _keys[slot] = oldKeys[i];
                _values[slot] = oldValues[i];
            }
        }

        private static ulong Mix(long key)
        {
            var x = (ulong)key * 0x9E3779B97F4A7C15UL;
            return x ^ (x >> 32);
        }
    }

    private static void ParseLines(ReadOnlySpan<byte> block, ParseState state, string path)
    {
        while (block.Length > 0)
        {
            var newline = block.IndexOf((byte)'\n');
            var line = newline < 0 ? block : block[..newline];
            block = newline < 0 ? [] : block[(newline + 1)..];
            if (line.Length > 0 && line[^1] == (byte)'\r') line = line[..^1];
            ParseLine(line, state, path);
        }
    }

    private static void ParseLine(ReadOnlySpan<byte> line, ParseState state, string path)
    {
        var i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
        if (i >= line.Length || line[i] == '#') return;
        var start = i;
        while (i < line.Length && line[i] != ' ' && line[i] != '\t') i++;
        var keyword = line[start..i];
        var rest = line[i..];
        switch (keyword.Length)
        {
            case 1 when keyword[0] == (byte)'v':
                if (TryReadVector(rest, out var position))
                {
                    state.RawPositions.Add(position);
                    state.BoundsMin = Vector3.Min(state.BoundsMin, position);
                    state.BoundsMax = Vector3.Max(state.BoundsMax, position);
                }
                break;
            case 2 when keyword[0] == (byte)'v' && keyword[1] == (byte)'n':
                if (TryReadVector(rest, out var normal)) state.RawNormals.Add(normal);
                break;
            case 1 when keyword[0] == (byte)'f':
                ParseFace(rest, state);
                break;
            case 6 when keyword.SequenceEqual("usemtl"u8):
                state.CurrentMaterial = ResolveMaterial(Encoding.UTF8.GetString(Trim(rest)), state);
                break;
            case 6 when keyword.SequenceEqual("mtllib"u8):
                LoadMaterialLibrary(Encoding.UTF8.GetString(Trim(rest)), path, state);
                break;
        }
    }

    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && (value[start] == ' ' || value[start] == '\t')) start++;
        while (end > start && (value[end - 1] == ' ' || value[end - 1] == '\t' || value[end - 1] == '\r')) end--;
        return value[start..end];
    }

    private static bool TryReadVector(ReadOnlySpan<byte> text, out Vector3 value)
    {
        value = default;
        Span<float> parts = stackalloc float[3];
        for (var component = 0; component < 3; component++)
        {
            var start = 0;
            while (start < text.Length && (text[start] == ' ' || text[start] == '\t')) start++;
            if (start >= text.Length) return false;
            if (!Utf8Parser.TryParse(text[start..], out float parsed, out var used) || !float.IsFinite(parsed)) return false;
            parts[component] = parsed;
            text = text[(start + used)..];
        }
        value = new Vector3(parts[0], parts[1], parts[2]);
        return true;
    }

    private static int ResolveMaterial(string name, ParseState state)
    {
        return state.Mesh.MaterialIndex(name, () =>
            state.MaterialLibrary is not null && state.MaterialLibrary.TryGetValue(name, out var found)
                ? found with { Name = name }
                : Rendering.MaterialLibrary.Imported with { Name = name });
    }

    private static void ParseFace(ReadOnlySpan<byte> text, ParseState state)
    {
        var corners = state.ScratchVertex;
        var scratch = state.Scratch;
        corners.Clear();
        scratch.Clear();
        var hasNormals = state.RawNormals.Count > 0;
        while (true)
        {
            var start = 0;
            while (start < text.Length && (text[start] == ' ' || text[start] == '\t')) start++;
            if (start >= text.Length) break;
            var end = start;
            while (end < text.Length && text[end] != ' ' && text[end] != '\t') end++;
            if (!ParseCorner(text[start..end], state, hasNormals, out var vertex, out var raw)) return;
            corners.Add(vertex);
            scratch.Add(raw);
            text = text[end..];
        }
        if (corners.Count < 3) return;
        if (state.CurrentMaterial < 0) state.CurrentMaterial = ResolveMaterial("Importado", state);
        var material = state.CurrentMaterial;
        if (state.Mesh.TriangleCount + corners.Count > state.MaximumTriangles)
            throw new InvalidDataException("The model file is too large to import.");

        if (corners.Count == 3)
        {
            state.Mesh.AddTriangle(corners[0], corners[1], corners[2], material);
            return;
        }
        foreach (var (a, b, c) in Triangulator.Triangulate(scratch))
            state.Mesh.AddTriangle(corners[a], corners[b], corners[c], material);
    }

    /// <summary>Parses one "v", "v/t", "v//n" or "v/t/n" corner and returns the merged output vertex.</summary>
    private static bool ParseCorner(ReadOnlySpan<byte> token, ParseState state, bool hasNormals, out int vertex,
        out Vector3 raw)
    {
        vertex = 0;
        raw = default;
        var slash = token.IndexOf((byte)'/');
        var positionToken = slash < 0 ? token : token[..slash];
        if (!Utf8Parser.TryParse(positionToken, out int positionIndex, out _)) return false;
        positionIndex = positionIndex < 0 ? state.RawPositions.Count + positionIndex : positionIndex - 1;
        if ((uint)positionIndex >= (uint)state.RawPositions.Count) return false;
        raw = state.RawPositions[positionIndex];

        var normalIndex = -1;
        if (slash >= 0)
        {
            var afterFirst = token[(slash + 1)..];
            var second = afterFirst.IndexOf((byte)'/');
            if (second >= 0 && Utf8Parser.TryParse(afterFirst[(second + 1)..], out int parsedNormal, out _))
            {
                normalIndex = parsedNormal < 0 ? state.RawNormals.Count + parsedNormal : parsedNormal - 1;
                if ((uint)normalIndex >= (uint)state.RawNormals.Count) normalIndex = -1;
            }
        }

        if (!hasNormals)
        {
            // Without normals a source vertex maps one to one onto an output vertex.
            var key = (long)positionIndex;
            if (state.Vertices.TryGetOrAdd(key, state.Mesh.Positions.Count, out vertex)) return true;
            state.Mesh.AddVertex(raw);
            return true;
        }
        var pair = ((long)positionIndex << 32) | (uint)(normalIndex + 1);
        if (state.Vertices.TryGetOrAdd(pair, state.Mesh.Positions.Count, out vertex)) return true;
        state.Mesh.AddVertex(raw, normalIndex >= 0 ? state.RawNormals[normalIndex] : Vector3.Zero);
        return true;
    }

    // ------------------------------------------------------------------ MTL

    private static void LoadMaterialLibrary(string name, string objPath, ParseState state)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var directory = Path.GetDirectoryName(Path.GetFullPath(objPath)) ?? ".";
        var candidate = Path.Combine(directory, name.Replace('\\', Path.DirectorySeparatorChar));
        if (!File.Exists(candidate))
        {
            var sibling = Path.ChangeExtension(objPath, ".mtl");
            if (!File.Exists(sibling))
            {
                state.Mesh.Warn("Import.Warning.MaterialFileMissing");
                return;
            }
            candidate = sibling;
        }
        try
        {
            state.MaterialLibrary ??= new Dictionary<string, RenderMaterial>(StringComparer.Ordinal);
            foreach (var pair in MtlReader.Read(candidate)) state.MaterialLibrary[pair.Key] = pair.Value;
        }
        catch (IOException)
        {
            state.Mesh.Warn("Import.Warning.MaterialFileMissing");
        }
    }

    /// <summary>Triangulates polygon corners with more than three vertices.</summary>
    private static class Triangulator
    {
        public static IEnumerable<(int A, int B, int C)> Triangulate(List<Vector3> corners)
        {
            if (corners.Count == 4) return Quad(corners);
            return Polygon(corners);
        }

        /// <summary>Splits a quad along the diagonal that keeps both triangles facing the same way.</summary>
        private static IEnumerable<(int, int, int)> Quad(List<Vector3> p)
        {
            var n1 = Vector3.Cross(p[1] - p[0], p[2] - p[0]);
            var n2 = Vector3.Cross(p[2] - p[0], p[3] - p[0]);
            var splitA = Vector3.Dot(n1, n2) > 0;
            var m1 = Vector3.Cross(p[2] - p[1], p[3] - p[1]);
            var m2 = Vector3.Cross(p[3] - p[1], p[0] - p[1]);
            var splitB = Vector3.Dot(m1, m2) > 0;
            if (splitA && (!splitB || Vector3.DistanceSquared(p[0], p[2]) <= Vector3.DistanceSquared(p[1], p[3])))
                return [(0, 1, 2), (0, 2, 3)];
            if (splitB) return [(1, 2, 3), (1, 3, 0)];
            return [(0, 1, 2), (0, 2, 3)];
        }

        /// <summary>Ear-clips a planar polygon after projecting it along its dominant axis; falls back to a fan.</summary>
        private static IEnumerable<(int, int, int)> Polygon(List<Vector3> p)
        {
            var normal = Vector3.Zero;
            for (var i = 0; i < p.Count; i++)
            {
                var current = p[i];
                var next = p[(i + 1) % p.Count];
                normal += new Vector3((current.Y - next.Y) * (current.Z + next.Z),
                    (current.Z - next.Z) * (current.X + next.X), (current.X - next.X) * (current.Y + next.Y));
            }
            var ax = MathF.Abs(normal.X);
            var ay = MathF.Abs(normal.Y);
            var az = MathF.Abs(normal.Z);
            var flip = az >= ax && az >= ay ? normal.Z < 0 : ax >= ay ? normal.X < 0 : normal.Y < 0;
            var projected = new List<Point2>(p.Count);
            foreach (var point in p)
            {
                var (u, v) = az >= ax && az >= ay ? (point.X, point.Y) : ax >= ay ? (point.Y, point.Z) : (point.Z, point.X);
                projected.Add(new Point2(u, v));
            }
            try
            {
                var triangles = PolygonTriangulator.Triangulate(projected);
                // The triangulator emits counter-clockwise triangles in the projected plane; restore the source winding.
                return flip
                    ? triangles.Select(t => (t.A, t.C, t.B)).ToArray()
                    : triangles.ToArray();
            }
            catch (ArgumentException)
            {
                var fan = new List<(int, int, int)>(p.Count - 2);
                for (var i = 1; i < p.Count - 1; i++) fan.Add((0, i, i + 1));
                return fan;
            }
        }
    }
}

/// <summary>Reads Wavefront MTL material libraries into renderer materials.</summary>
internal static class MtlReader
{
    private static readonly string[] GlassWords = ["vidrio", "glass", "cristal", "window", "ventana", "glazing"];

    public static Dictionary<string, RenderMaterial> Read(string path)
    {
        var result = new Dictionary<string, RenderMaterial>(StringComparer.Ordinal);
        string? name = null;
        var diffuse = new Vector3(0.7f);
        var specular = Vector3.Zero;
        var emission = Vector3.Zero;
        var shininess = -1f;
        var dissolve = 1f;
        var illumination = 2;
        var ior = 1.5f;

        void Commit()
        {
            if (name is null) return;
            result[name] = Build(name, diffuse, specular, emission, shininess, dissolve, illumination, ior);
        }

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0])
            {
                case "newmtl":
                    Commit();
                    name = line[6..].Trim();
                    diffuse = new Vector3(0.7f);
                    specular = Vector3.Zero;
                    emission = Vector3.Zero;
                    shininess = -1;
                    dissolve = 1;
                    illumination = 2;
                    ior = 1.5f;
                    break;
                case "Kd" when TryColor(parts, out var kd): diffuse = kd; break;
                case "Ks" when TryColor(parts, out var ks): specular = ks; break;
                case "Ke" when TryColor(parts, out var ke): emission = ke; break;
                case "Ns" when parts.Length > 1 && TryFloat(parts[1], out var ns): shininess = ns; break;
                case "d" when parts.Length > 1 && TryFloat(parts[1], out var d): dissolve = d; break;
                case "Tr" when parts.Length > 1 && TryFloat(parts[1], out var tr): dissolve = 1 - tr; break;
                case "illum" when parts.Length > 1 && int.TryParse(parts[1], out var illum): illumination = illum; break;
                case "Ni" when parts.Length > 1 && TryFloat(parts[1], out var ni): ior = ni; break;
            }
        }
        Commit();
        return result;
    }

    private static RenderMaterial Build(string name, Vector3 diffuse, Vector3 specular, Vector3 emission,
        float shininess, float dissolve, int illumination, float ior)
    {
        // Authoring tools save display colors, so treat Kd as sRGB and convert to the linear values the renderer expects.
        var color = new Vector3(Rendering.MaterialLibrary.SrgbToLinear(Math.Clamp(diffuse.X, 0, 1)),
            Rendering.MaterialLibrary.SrgbToLinear(Math.Clamp(diffuse.Y, 0, 1)),
            Rendering.MaterialLibrary.SrgbToLinear(Math.Clamp(diffuse.Z, 0, 1)));
        // Blinn–Phong exponent to GGX roughness; matte when the file gives no shininess.
        var roughness = shininess > 0 ? MathF.Sqrt(2 / (shininess + 2)) : 0.85f;
        var specularStrength = Sampling.Luminance(specular);
        if (shininess <= 0 || specularStrength < 0.02f) roughness = MathF.Max(roughness, 0.85f);
        roughness = Math.Clamp(roughness, 0.08f, 1f);
        var metallic = illumination == 3 && specularStrength > 0.6f && shininess > 200 ? 0.8f : 0f;

        var lower = name.ToLowerInvariant();
        var glass = dissolve < 0.98f && (GlassWords.Any(word => lower.Contains(word)) || dissolve <= 0.6f);
        if (glass)
        {
            // A pure display color such as Revit's blue glass would tint the whole view, so it is only hinted at.
            var tint = Vector3.Lerp(new Vector3(0.9f, 0.95f, 0.97f), color, 0.25f);
            return new RenderMaterial
            {
                Name = name, BaseColor = Vector3.One, Roughness = 0.04f, Transmission = Math.Clamp(1 - dissolve * 0.2f, 0.7f, 1f),
                Ior = Math.Clamp(ior, 1.2f, 2f), TransmissionTint = tint
            };
        }
        return new RenderMaterial
        {
            Name = name, BaseColor = color, Roughness = roughness, Metallic = metallic,
            Emission = emission, Transmission = dissolve < 0.98f ? Math.Clamp(1 - dissolve, 0, 0.95f) : 0,
            TransmissionTint = Vector3.Lerp(Vector3.One, color, 0.5f), Pattern = SurfacePattern.None
        };
    }

    private static bool TryColor(string[] parts, out Vector3 color)
    {
        color = default;
        if (parts.Length < 4 || !TryFloat(parts[1], out var r) || !TryFloat(parts[2], out var g) || !TryFloat(parts[3], out var b))
            return false;
        color = new Vector3(r, g, b);
        return true;
    }

    private static bool TryFloat(string text, out float value) =>
        float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) &&
        float.IsFinite(value);
}

/// <summary>Chooses a meters-per-unit factor when the file does not state one.</summary>
internal static class ModelUnits
{
    /// <summary>
    /// Guesses millimeters, centimeters or meters from the model's largest extent. Buildings are tens of meters, so an
    /// extent in the thousands means millimeters and one in the hundreds of meters' worth of "units" suggests centimeters.
    /// </summary>
    public static double Detect((Vector3 Min, Vector3 Max) bounds, bool knownMeters)
    {
        if (knownMeters || bounds.Min.X > bounds.Max.X) return 1;
        var size = bounds.Max - bounds.Min;
        var extent = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
        return extent switch
        {
            > 6000 => 0.001,
            > 700 => 0.01,
            _ => 1
        };
    }
}
