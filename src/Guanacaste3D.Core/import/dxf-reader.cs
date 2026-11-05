using System.Globalization;
using System.Numerics;
using System.Text;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Core.Import;

/// <summary>
/// Reads ASCII AutoCAD DXF drawings. Surfaces (3DFACE, SOLID, MESH, polyface and polygon-mesh POLYLINEs) become
/// triangles; lines, arcs, circles and polylines become plan linework. Block references (INSERT) are expanded with
/// their scale, rotation and extrusion, nested blocks included. Hidden layers are skipped and ACIS solids, which
/// cannot be read without the ACIS kernel, are counted and reported instead of silently vanishing.
/// </summary>
internal static class DxfReader
{
    private const int MaximumInsertDepth = 8;
    private const int MaximumExpandedFaces = 20_000_000;

    /// <summary>Whether the file starts with the binary DXF sentinel.</summary>
    public static bool IsBinary(string path)
    {
        Span<byte> head = stackalloc byte[22];
        using var stream = File.OpenRead(path);
        var read = stream.Read(head);
        return read >= 18 && Encoding.ASCII.GetString(head[..18]) == "AutoCAD Binary DXF";
    }

    public static ImportedModel Read(string path, ModelImportOptions options, IProgress<ImportProgress>? progress,
        CancellationToken cancellation)
    {
        if (IsBinary(path)) throw new InvalidDataException("Binary DXF files are not supported; save the drawing as ASCII DXF.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20,
            FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, new UTF8Encoding(false), true, 1 << 20);
        var parser = new Parser(reader, stream, progress, cancellation);
        parser.Run();
        if (!parser.SawEntitiesOrBlocks) throw new InvalidDataException("The DXF file is not valid.");

        var mesh = new MeshAccumulator();
        var emitter = new Emitter(mesh, parser);
        emitter.EmitModelSpace();
        if (parser.AcisSolids > 0) mesh.Warn("Import.Warning.AcisSolids");
        if (parser.SkippedCurves > 0) mesh.Warn("Import.Warning.SkippedCurves");
        if (mesh.TriangleCount == 0 && mesh.Linework.Count == 0) throw new InvalidDataException("The model file contains no geometry.");

        double scale;
        if (options.MetersPerUnit is { } stated) scale = stated;
        else if (parser.UnitsMeters is { } declared) scale = declared;
        else
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (var i = 0; i < mesh.Positions.Count; i++)
            {
                min = Vector3.Min(min, mesh.Positions[i]);
                max = Vector3.Max(max, mesh.Positions[i]);
            }
            foreach (var line in mesh.Linework)
                foreach (var point in line.Points)
                {
                    min = Vector3.Min(min, point);
                    max = Vector3.Max(max, point);
                }
            scale = ModelUnits.Detect((min, max), false);
        }
        return mesh.Build(path, "DXF", ModelFrame.ZUp, scale, options);
    }

    // ------------------------------------------------------------------ parsing

    private readonly record struct Pair(int Code, string Value);

    /// <summary>One triangle in block or model space with its resolved color (−1 = inherit from the insert).</summary>
    private readonly record struct Face(Vector3 A, Vector3 B, Vector3 C, int Color, bool Terrain);

    private sealed record Line3(string Layer, Vector3[] Points, bool Closed, int Color);

    private sealed record Insert(string Block, Matrix4x4 Transform, int Color, string Layer);

    private sealed class Space
    {
        public readonly ArrayBuilder<Face> Faces = new(256);
        public readonly List<Line3> Lines = [];
        public readonly List<Insert> Inserts = [];
        public Vector3 BasePoint;
    }

    private sealed class Parser(StreamReader reader, FileStream stream, IProgress<ImportProgress>? progress,
        CancellationToken cancellation)
    {
        private readonly List<Pair> _pairs = new(64);
        private readonly Dictionary<string, (int Color, bool Off)> _layers = new(StringComparer.OrdinalIgnoreCase);
        private string _section = "";
        private Space _current = new();
        private string? _blockName;
        private long _lastReport;

        // POLYLINE collection
        private bool _inPolyline;
        private List<Pair> _polylineHeader = [];
        private readonly List<List<Pair>> _vertices = [];

        public Space ModelSpace { get; } = new();
        public Dictionary<string, Space> Blocks { get; } = new(StringComparer.OrdinalIgnoreCase);
        public double? UnitsMeters { get; private set; }
        public bool SawEntitiesOrBlocks { get; private set; }
        public int AcisSolids { get; private set; }
        public int SkippedCurves { get; private set; }
        public int ExpandedFaces;

        public (int Color, bool Off) Layer(string name) =>
            _layers.TryGetValue(name, out var layer) ? layer : (7, false);

        public void Run()
        {
            _current = ModelSpace;
            string? headerVariable = null;
            string? pendingType = null;
            var entityLine = 0;
            while (ReadPair(out var pair))
            {
                if (pair.Code != 0)
                {
                    if (pendingType == "SECTION" && pair.Code == 2) _section = pair.Value.Trim();
                    else if (_section == "HEADER")
                    {
                        if (pair.Code == 9) headerVariable = pair.Value;
                        else if (headerVariable == "$INSUNITS" && pair.Code == 70)
                        {
                            UnitsMeters = UnitsToMeters(ParseInt(pair.Value));
                            headerVariable = null;
                        }
                    }
                    else if (pendingType is not null) _pairs.Add(pair);
                    entityLine++;
                    continue;
                }

                if (pendingType is not null) Dispatch(pendingType);
                _pairs.Clear();
                entityLine = 0;
                pendingType = pair.Value.Trim();
                if (pendingType == "SECTION" || pendingType == "ENDSEC" || pendingType == "EOF")
                {
                    if (pendingType == "ENDSEC") _section = "";
                }
            }
            if (pendingType is not null) Dispatch(pendingType);
            FinishPolyline();
            progress?.Report(new ImportProgress(0.9, "Import.Reading"));
        }

        private bool ReadPair(out Pair pair)
        {
            pair = default;
            var codeLine = reader.ReadLine();
            if (codeLine is null) return false;
            var valueLine = reader.ReadLine();
            if (valueLine is null) return false;
            if (!int.TryParse(codeLine.AsSpan().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
                return ReadPair(out pair); // tolerate stray blank lines by resynchronizing on the next pair
            pair = new Pair(code, valueLine.TrimEnd('\r', '\n'));
            if (stream.Position - _lastReport > 4_000_000)
            {
                _lastReport = stream.Position;
                cancellation.ThrowIfCancellationRequested();
                progress?.Report(new ImportProgress(0.9 * stream.Position / Math.Max(1, stream.Length), "Import.Reading"));
            }
            return true;
        }

        private void Dispatch(string type)
        {
            switch (type)
            {
                case "SECTION":
                    return; // the section name was captured as soon as its group arrived
                case "ENDSEC" or "EOF":
                    FinishPolyline();
                    return;
                case "BLOCK":
                    FinishPolyline();
                    _blockName = Text(2) ?? "";
                    _current = new Space { BasePoint = new Vector3((float)D(10), (float)D(20), (float)D(30)) };
                    return;
                case "ENDBLK":
                    FinishPolyline();
                    if (_blockName is not null) Blocks[_blockName] = _current;
                    _blockName = null;
                    _current = ModelSpace;
                    return;
                case "LAYER" when _section == "TABLES":
                {
                    var name = Text(2);
                    if (name is null) return;
                    var layerIndex = (int)D(62, 7);
                    var off = layerIndex < 0 || ((int)D(70) & 1) != 0;
                    _layers[name] = (layerIndex == 0 ? 7 : Math.Abs(layerIndex), off);
                    return;
                }
            }
            if (_section is not ("ENTITIES" or "BLOCKS")) return;
            SawEntitiesOrBlocks = true;

            if (type == "VERTEX" && _inPolyline)
            {
                _vertices.Add([.. _pairs]);
                return;
            }
            if (type == "SEQEND")
            {
                FinishPolyline();
                return;
            }
            FinishPolyline();

            var layerName = Text(8) ?? "0";
            var (layerColor, hidden) = Layer(layerName);
            if (hidden) return;
            var color62 = _pairs.Any(p => p.Code == 62) ? (int)D(62) : 256;
            var color = ResolveColor(color62, layerColor);
            var terrain = IsTerrainLayer(layerName);

            switch (type)
            {
                case "3DFACE":
                {
                    var a = Point(10);
                    var b = Point(11);
                    var c = Point(12);
                    var d = HasPoint(13) ? Point(13) : c;
                    AddFace(a, b, c, color, terrain);
                    if (d != c && d != a) AddFace(a, c, d, color, terrain);
                    break;
                }
                case "SOLID" or "TRACE":
                {
                    var ocs = Ocs(Point(210, defaultZ: 1));
                    var a = Vector3.Transform(Point(10), ocs);
                    var b = Vector3.Transform(Point(11), ocs);
                    var c = Vector3.Transform(Point(12), ocs);
                    var d = HasPoint(13) ? Vector3.Transform(Point(13), ocs) : c;
                    // SOLID lists its corners in a "Z" order, so the outline is 1, 2, 4, 3.
                    AddFace(a, b, d, color, terrain);
                    if (c != d) AddFace(a, d, c, color, terrain);
                    break;
                }
                case "MESH":
                    ReadMesh(color, terrain);
                    break;
                case "POLYLINE":
                    _inPolyline = true;
                    _polylineHeader = [.. _pairs];
                    _vertices.Clear();
                    break;
                case "LWPOLYLINE":
                    ReadLightweightPolyline(layerName, color);
                    break;
                case "LINE":
                    _current.Lines.Add(new Line3(layerName, [Point(10), Point(11)], false, color));
                    break;
                case "CIRCLE":
                    _current.Lines.Add(new Line3(layerName,
                        Arc(Point(10), D(40), 0, 360, Ocs(Point(210, defaultZ: 1))), true, color));
                    break;
                case "ARC":
                    _current.Lines.Add(new Line3(layerName,
                        Arc(Point(10), D(40), D(50), D(51), Ocs(Point(210, defaultZ: 1))), false, color));
                    break;
                case "INSERT":
                    ReadInsert(layerName, color);
                    break;
                case "3DSOLID" or "BODY" or "REGION" or "SURFACE":
                    AcisSolids++;
                    break;
                case "SPLINE" or "ELLIPSE":
                    SkippedCurves++;
                    break;
            }
        }

        private static bool IsTerrainLayer(string layer)
        {
            var lower = layer.ToLowerInvariant();
            return lower.Contains("topo") || lower.Contains("terren") || lower.Contains("terrain") ||
                   lower.Contains("suelo") || lower.Contains("mesh");
        }

        private void AddFace(Vector3 a, Vector3 b, Vector3 c, int color, bool terrain)
        {
            if (!IsFinite(a) || !IsFinite(b) || !IsFinite(c)) return;
            if (++ExpandedFaces > MaximumExpandedFaces) throw new InvalidDataException("The model file is too large to import.");
            _current.Faces.Add(new Face(a, b, c, color, terrain));
        }

        private static bool IsFinite(Vector3 v) => float.IsFinite(v.X + v.Y + v.Z);

        // ---- entities

        private void ReadMesh(int color, bool terrain)
        {
            // MESH stores a vertex list, then a face list as [count, index…] runs.
            var vertices = new List<Vector3>();
            var faceData = new List<int>();
            var x = 0.0;
            var y = 0.0;
            var phase = 0; // 0 none, 1 vertices, 2 faces
            var remaining = 0;
            var haveX = false;
            var haveY = false;
            foreach (var pair in _pairs)
            {
                switch (pair.Code)
                {
                    case 92:
                        remaining = ParseInt(pair.Value);
                        phase = 1;
                        break;
                    case 93:
                        remaining = ParseInt(pair.Value);
                        phase = 2;
                        break;
                    case 10 when phase == 1:
                        x = ParseDouble(pair.Value);
                        haveX = true;
                        break;
                    case 20 when phase == 1 && haveX:
                        y = ParseDouble(pair.Value);
                        haveY = true;
                        break;
                    case 30 when phase == 1 && haveY:
                        vertices.Add(new Vector3((float)x, (float)y, (float)ParseDouble(pair.Value)));
                        haveX = haveY = false;
                        break;
                    case 90 when phase == 2 && remaining > 0:
                        faceData.Add(ParseInt(pair.Value));
                        remaining--;
                        break;
                }
            }
            for (var i = 0; i < faceData.Count;)
            {
                var count = faceData[i++];
                if (count < 3 || i + count > faceData.Count) break;
                for (var k = 1; k < count - 1; k++)
                {
                    var a = faceData[i];
                    var b = faceData[i + k];
                    var c = faceData[i + k + 1];
                    if ((uint)a < (uint)vertices.Count && (uint)b < (uint)vertices.Count && (uint)c < (uint)vertices.Count)
                        AddFace(vertices[a], vertices[b], vertices[c], color, terrain);
                }
                i += count;
            }
        }

        private void FinishPolyline()
        {
            if (!_inPolyline) return;
            _inPolyline = false;
            var header = _polylineHeader;
            var flags = (int)Find(header, 70);
            var layer = Text(header, 8) ?? "0";
            var (layerColor, hidden) = Layer(layer);
            if (hidden) return;
            var color = ResolveColor(header.Any(p => p.Code == 62) ? (int)Find(header, 62) : 256, layerColor);
            var terrain = IsTerrainLayer(layer);
            var points = new List<Vector3>();
            if ((flags & 64) != 0)
            {
                // Polyface mesh: coordinate vertices first, then face records that index them (1-based).
                var coordinates = new List<Vector3>();
                foreach (var vertex in _vertices)
                {
                    var vertexFlags = (int)Find(vertex, 70);
                    if ((vertexFlags & 192) == 192)
                        coordinates.Add(new Vector3((float)Find(vertex, 10), (float)Find(vertex, 20), (float)Find(vertex, 30)));
                }
                foreach (var vertex in _vertices)
                {
                    if (((int)Find(vertex, 70) & 192) != 128) continue;
                    var corners = new List<int>(4);
                    foreach (var code in new[] { 71, 72, 73, 74 })
                    {
                        var index = (int)Find(vertex, code);
                        if (index != 0) corners.Add(Math.Abs(index) - 1);
                    }
                    for (var k = 1; k < corners.Count - 1; k++)
                        if (corners[0] < coordinates.Count && corners[k] < coordinates.Count && corners[k + 1] < coordinates.Count)
                            AddFace(coordinates[corners[0]], coordinates[corners[k]], coordinates[corners[k + 1]], color, terrain);
                }
            }
            else if ((flags & 16) != 0)
            {
                var m = (int)Find(header, 71);
                var n = (int)Find(header, 72);
                var grid = _vertices.Select(v => new Vector3((float)Find(v, 10), (float)Find(v, 20), (float)Find(v, 30))).ToList();
                if (m > 1 && n > 1 && grid.Count >= m * n)
                {
                    var closedM = (flags & 1) != 0;
                    var closedN = (flags & 32) != 0;
                    for (var i = 0; i < (closedM ? m : m - 1); i++)
                    for (var j = 0; j < (closedN ? n : n - 1); j++)
                    {
                        var i2 = (i + 1) % m;
                        var j2 = (j + 1) % n;
                        var p00 = grid[i * n + j];
                        var p01 = grid[i * n + j2];
                        var p10 = grid[i2 * n + j];
                        var p11 = grid[i2 * n + j2];
                        AddFace(p00, p10, p11, color, terrain);
                        AddFace(p00, p11, p01, color, terrain);
                    }
                }
            }
            else
            {
                foreach (var vertex in _vertices)
                    if (((int)Find(vertex, 70) & 16) == 0) // skip spline control frames
                        points.Add(new Vector3((float)Find(vertex, 10), (float)Find(vertex, 20), (float)Find(vertex, 30)));
                if (points.Count >= 2)
                    _current.Lines.Add(new Line3(layer, [.. points], (flags & 1) != 0, color));
            }
            _vertices.Clear();
        }

        private void ReadLightweightPolyline(string layer, int color)
        {
            var points = new List<(double X, double Y, double Bulge)>();
            var flags = (int)D(70);
            var elevation = D(38);
            foreach (var pair in _pairs)
            {
                switch (pair.Code)
                {
                    case 10: points.Add((ParseDouble(pair.Value), 0, 0)); break;
                    case 20 when points.Count > 0:
                        points[^1] = (points[^1].X, ParseDouble(pair.Value), points[^1].Bulge);
                        break;
                    case 42 when points.Count > 0:
                        points[^1] = (points[^1].X, points[^1].Y, ParseDouble(pair.Value));
                        break;
                }
            }
            if (points.Count < 2) return;
            var ocs = Ocs(Point(210, defaultZ: 1));
            var result = new List<Vector3>();
            var closed = (flags & 1) != 0;
            var last = closed ? points.Count : points.Count - 1;
            for (var i = 0; i < points.Count; i++)
            {
                var current = points[i];
                result.Add(Vector3.Transform(new Vector3((float)current.X, (float)current.Y, (float)elevation), ocs));
                if (i < last && Math.Abs(current.Bulge) > 1e-9)
                {
                    var next = points[(i + 1) % points.Count];
                    foreach (var p in Bulge(current.X, current.Y, next.X, next.Y, current.Bulge))
                        result.Add(Vector3.Transform(new Vector3((float)p.X, (float)p.Y, (float)elevation), ocs));
                }
            }
            _current.Lines.Add(new Line3(layer, [.. result], closed, color));
        }

        /// <summary>Interior points of the arc that a polyline bulge (tan of a quarter of the sweep) describes.</summary>
        private static IEnumerable<(double X, double Y)> Bulge(double x1, double y1, double x2, double y2, double bulge)
        {
            var theta = 4 * Math.Atan(bulge);
            var chord = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
            if (chord < 1e-12 || Math.Abs(theta) < 1e-6) yield break;
            var radius = chord / (2 * Math.Sin(Math.Abs(theta) / 2));
            var midX = (x1 + x2) / 2;
            var midY = (y1 + y2) / 2;
            var distance = radius * Math.Cos(theta / 2);
            var nx = -(y2 - y1) / chord;
            var ny = (x2 - x1) / chord;
            var sign = bulge > 0 ? 1 : -1;
            var cx = midX - sign * nx * distance * (Math.Abs(theta) > Math.PI ? -1 : 1);
            var cy = midY - sign * ny * distance * (Math.Abs(theta) > Math.PI ? -1 : 1);
            var start = Math.Atan2(y1 - cy, x1 - cx);
            var steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(theta) / (Math.PI / 18)));
            for (var i = 1; i < steps; i++)
            {
                var angle = start + theta * i / steps;
                yield return (cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle));
            }
        }

        private void ReadInsert(string layer, int color)
        {
            var name = Text(2);
            if (name is null) return;
            var scale = new Vector3((float)D(41, 1), (float)D(42, 1), (float)D(43, 1));
            var rotation = (float)(D(50) * Math.PI / 180);
            var ocs = Ocs(Point(210, defaultZ: 1));
            var insertion = Point(10);
            var columns = Math.Max(1, (int)D(70, 1));
            var rows = Math.Max(1, (int)D(71, 1));
            var columnSpacing = (float)D(44);
            var rowSpacing = (float)D(45);
            if (columns * rows > 10_000) return;
            for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
            {
                // Block points are scaled and rotated in the block's own plane, then carried into the drawing by the OCS.
                var local = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationZ(rotation) *
                            Matrix4x4.CreateTranslation(insertion + new Vector3(column * columnSpacing, row * rowSpacing, 0));
                _current.Inserts.Add(new Insert(name, local * ocs, color, layer));
            }
        }

        // ---- value helpers over the current entity's pairs

        private string? Text(int code) => Text(_pairs, code);

        private static string? Text(List<Pair> pairs, int code)
        {
            foreach (var pair in pairs) if (pair.Code == code) return pair.Value.Trim();
            return null;
        }

        private double D(int code, double fallback = 0) => Find(_pairs, code, fallback);

        private static double Find(List<Pair> pairs, int code, double fallback = 0)
        {
            foreach (var pair in pairs) if (pair.Code == code) return ParseDouble(pair.Value, fallback);
            return fallback;
        }

        private bool HasPoint(int code) => _pairs.Any(p => p.Code == code);

        private Vector3 Point(int code, double defaultZ = 0) =>
            new((float)D(code), (float)D(code + 10), (float)D(code + 20, defaultZ));
    }

    // ------------------------------------------------------------------ expansion

    private sealed class Emitter(MeshAccumulator mesh, Parser parser)
    {
        private readonly Dictionary<(int Color, bool Terrain), int> _materials = [];

        public void EmitModelSpace() => Emit(parser.ModelSpace, Matrix4x4.Identity, -1, 0);

        private void Emit(Space space, Matrix4x4 transform, int inheritedColor, int depth)
        {
            var mirrored = transform.GetDeterminant() < 0;
            var faces = space.Faces.AsSpan();
            for (var i = 0; i < faces.Length; i++)
            {
                var face = faces[i];
                var color = face.Color == -1 ? inheritedColor : face.Color;
                var material = Material(color == -1 ? 7 : color, face.Terrain);
                var a = Vector3.Transform(face.A, transform);
                var b = Vector3.Transform(face.B, transform);
                var c = Vector3.Transform(face.C, transform);
                var index = mesh.Positions.Count;
                mesh.Positions.Add(a);
                mesh.Positions.Add(mirrored ? c : b);
                mesh.Positions.Add(mirrored ? b : c);
                mesh.Normals?.Add(Vector3.Zero);
                mesh.Normals?.Add(Vector3.Zero);
                mesh.Normals?.Add(Vector3.Zero);
                mesh.AddTriangle(index, index + 1, index + 2, material);
            }
            foreach (var line in space.Lines)
                mesh.Linework.Add(new ImportedPolyline(line.Layer,
                    line.Points.Select(p => Vector3.Transform(p, transform)).ToArray(), line.Closed));
            if (depth >= MaximumInsertDepth) return;
            foreach (var insert in space.Inserts)
            {
                if (!parser.Blocks.TryGetValue(insert.Block, out var block)) continue;
                var local = Matrix4x4.CreateTranslation(-block.BasePoint) * insert.Transform * transform;
                Emit(block, local, insert.Color == 0 ? inheritedColor : insert.Color, depth + 1);
            }
        }

        private int Material(int color, bool terrain) =>
            mesh.MaterialIndex($"dxf:{color}:{terrain}", () => terrain
                ? MaterialLibrary.ForKind(SurfaceKind.Ground) with { Name = "Terreno" }
                : new RenderMaterial
                {
                    Name = color == 7 ? "CAD" : $"CAD {color}",
                    BaseColor = AciColor(color),
                    Roughness = 0.8f
                });
    }

    // ------------------------------------------------------------------ small pieces

    /// <summary>Resolves an entity color index: 256 follows the layer; 0 (BYBLOCK) takes the insert's color later.</summary>
    private static int ResolveColor(int entityColor, int layerColor) =>
        entityColor == 256 ? layerColor : entityColor == 0 ? -1 : Math.Abs(entityColor);

    /// <summary>Linear RGB for an AutoCAD color index. 7 (white/black) is rendered as a light neutral.</summary>
    private static Vector3 AciColor(int index)
    {
        if (index == 7) return new Vector3(0.62f);
        Vector3 srgb;
        if (index is >= 1 and <= 9)
        {
            srgb = index switch
            {
                1 => new Vector3(1, 0, 0), 2 => new Vector3(1, 1, 0), 3 => new Vector3(0, 1, 0),
                4 => new Vector3(0, 1, 1), 5 => new Vector3(0, 0, 1), 6 => new Vector3(1, 0, 1),
                8 => new Vector3(0.5f), _ => new Vector3(0.75f)
            };
        }
        else if (index is >= 10 and <= 249)
        {
            // 24 hues in 15° steps, ten saturation/brightness variants each.
            var hue = (index - 10) / 10 * 15f;
            var variant = (index - 10) % 10;
            float[] saturation = [1, 0.5f, 1, 0.5f, 1, 0.5f, 1, 0.5f, 1, 0.5f];
            float[] value = [1, 1, 0.65f, 0.65f, 0.5f, 0.5f, 0.3f, 0.3f, 0.15f, 0.15f];
            srgb = FromHsv(hue, saturation[variant], value[variant]);
        }
        else if (index is >= 250 and <= 255)
        {
            var grey = (index - 250) * 0.1f + 0.2f;
            srgb = new Vector3(grey);
        }
        else
        {
            srgb = new Vector3(0.62f);
        }
        // Pure saturated CAD colors are far from realistic surfaces; blend toward grey so a drawing does not glow.
        srgb = Vector3.Lerp(srgb, new Vector3(Vector3.Dot(srgb, new Vector3(0.3f, 0.59f, 0.11f))), 0.35f);
        return new Vector3(MaterialLibrary.SrgbToLinear(srgb.X), MaterialLibrary.SrgbToLinear(srgb.Y),
            MaterialLibrary.SrgbToLinear(srgb.Z));
    }

    private static Vector3 FromHsv(float hue, float saturation, float value)
    {
        var c = value * saturation;
        var h = hue / 60f;
        var x = c * (1 - MathF.Abs(h % 2 - 1));
        var (r, g, b) = (int)h switch
        {
            0 => (c, x, 0f), 1 => (x, c, 0f), 2 => (0f, c, x), 3 => (0f, x, c), 4 => (x, 0f, c), _ => (c, 0f, x)
        };
        var m = value - c;
        return new Vector3(r + m, g + m, b + m);
    }

    /// <summary>Points of an arc in its own plane (angles in degrees, counter-clockwise), carried into 3D by the OCS matrix.</summary>
    private static Vector3[] Arc(Vector3 center, double radius, double startDegrees, double endDegrees, Matrix4x4 ocs)
    {
        while (endDegrees <= startDegrees) endDegrees += 360;
        var sweep = Math.Min(360, endDegrees - startDegrees);
        var steps = Math.Max(4, (int)Math.Ceiling(sweep / 7.5));
        var points = new Vector3[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            var angle = (startDegrees + sweep * i / steps) * Math.PI / 180;
            var local = new Vector3((float)(radius * Math.Cos(angle)), (float)(radius * Math.Sin(angle)), 0);
            points[i] = Vector3.Transform(center + local, ocs);
        }
        return points;
    }

    /// <summary>
    /// The DXF "arbitrary axis algorithm": the object coordinate system whose Z is the extrusion direction.
    /// Returns identity for the common (0,0,1), and a mirror for (0,0,−1).
    /// </summary>
    private static Matrix4x4 Ocs(Vector3 normal)
    {
        if (normal.LengthSquared() < 1e-12f) return Matrix4x4.Identity;
        var z = Vector3.Normalize(normal);
        if (MathF.Abs(z.X) < 1e-6f && MathF.Abs(z.Y) < 1e-6f && z.Z > 0) return Matrix4x4.Identity;
        var x = MathF.Abs(z.X) < 1f / 64 && MathF.Abs(z.Y) < 1f / 64
            ? Vector3.Normalize(Vector3.Cross(Vector3.UnitY, z))
            : Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, z));
        var y = Vector3.Normalize(Vector3.Cross(z, x));
        return new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
    }

    /// <summary>Meters per drawing unit for a DXF <c>$INSUNITS</c> code; null for unitless or unknown codes.</summary>
    private static double? UnitsToMeters(int code) => code switch
    {
        1 => 0.0254,
        2 => 0.3048,
        3 => 1609.344,
        4 => 0.001,
        5 => 0.01,
        6 => 1,
        7 => 1000,
        10 => 0.9144,
        14 => 0.1,
        15 => 10,
        16 => 100,
        _ => null
    };

    private static double ParseDouble(string text, double fallback = 0) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value
            : fallback;

    private static int ParseInt(string text) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
}
