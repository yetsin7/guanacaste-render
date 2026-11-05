using System.Globalization;
using System.Numerics;
using System.Xml;
using System.Xml.Linq;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Core.Import;

/// <summary>
/// Reads COLLADA (.dae) scenes, the interchange format SketchUp writes. Triangles, polylists and polygons are
/// triangulated, node transforms and instanced geometry are expanded, and effect colors become materials. The file's
/// declared unit (meters per unit) and up axis are honored.
/// </summary>
internal static class ColladaReader
{
    public static ImportedModel Read(string path, ModelImportOptions options, IProgress<ImportProgress>? progress,
        CancellationToken cancellation)
    {
        XDocument document;
        try
        {
            // DTD processing stays off: collada files never need it and external entities must not be fetched.
            using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            throw new InvalidDataException("The COLLADA file is not valid.");
        }
        var root = document.Root;
        if (root is null || root.Name.LocalName != "COLLADA") throw new InvalidDataException("The COLLADA file is not valid.");

        var unit = root.Elements().FirstOrDefault(e => e.Name.LocalName == "asset")?.Elements().FirstOrDefault(e => e.Name.LocalName == "unit")
            ?.Attribute("meter") is { } meter && double.TryParse(meter.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var declared)
            ? declared : 1.0;
        var upAxis = root.Elements().FirstOrDefault(e => e.Name.LocalName == "asset")?.Elements().FirstOrDefault(e => e.Name.LocalName == "up_axis")?.Value.Trim();

        var context = new Context(root, options);
        var mesh = new MeshAccumulator { Normals = new ArrayBuilder<Vector3>(1 << 14) };
        context.Build(mesh, cancellation);
        progress?.Report(new ImportProgress(0.95, "Import.Finishing"));
        if (mesh.TriangleCount == 0) throw new InvalidDataException("The model file contains no geometry.");
        return mesh.Build(path, "DAE", upAxis == "Z_UP" ? ModelFrame.ZUp : ModelFrame.YUp, options.MetersPerUnit ?? unit, options);
    }

    private sealed class Context
    {
        private readonly XElement _root;
        private readonly ModelImportOptions _options;
        private readonly Dictionary<string, XElement> _byId = [];
        private readonly Dictionary<string, RenderMaterial> _materials = [];

        public Context(XElement root, ModelImportOptions options)
        {
            _root = root;
            _options = options;
            foreach (var element in root.Descendants())
                if (element.Attribute("id") is { } id) _byId[id.Value] = element;
        }

        public void Build(MeshAccumulator mesh, CancellationToken cancellation)
        {
            var scene = _root.Elements().FirstOrDefault(e => e.Name.LocalName == "scene");
            var visualUrl = scene?.Elements().FirstOrDefault(e => e.Name.LocalName == "instance_visual_scene")?.Attribute("url")?.Value;
            var visual = visualUrl is not null && _byId.TryGetValue(visualUrl.TrimStart('#'), out var found)
                ? found
                : _root.Descendants().FirstOrDefault(e => e.Name.LocalName == "visual_scene");
            if (visual is null)
            {
                // No scene graph: draw every geometry at the origin.
                foreach (var geometry in _root.Descendants().Where(e => e.Name.LocalName == "geometry"))
                    AddGeometry(mesh, geometry, Matrix4x4.Identity, [], cancellation);
                return;
            }
            foreach (var node in visual.Elements().Where(e => e.Name.LocalName == "node"))
                Visit(mesh, node, Matrix4x4.Identity, 0, cancellation);
        }

        private void Visit(MeshAccumulator mesh, XElement node, Matrix4x4 parent, int depth, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (depth > 64) return;
            var world = LocalTransform(node) * parent;
            foreach (var child in node.Elements())
            {
                switch (child.Name.LocalName)
                {
                    case "instance_geometry" or "instance_controller":
                        if (child.Attribute("url")?.Value.TrimStart('#') is { } id && _byId.TryGetValue(id, out var geometry))
                            AddGeometry(mesh, geometry, world, MaterialBindings(child), cancellation);
                        break;
                    case "instance_node":
                        if (child.Attribute("url")?.Value.TrimStart('#') is { } nodeId && _byId.TryGetValue(nodeId, out var instance))
                            Visit(mesh, instance, world, depth + 1, cancellation);
                        break;
                    case "node":
                        Visit(mesh, child, world, depth + 1, cancellation);
                        break;
                }
            }
        }

        /// <summary>Composes a node's transform elements in document order, in row-vector form.</summary>
        private static Matrix4x4 LocalTransform(XElement node)
        {
            var result = Matrix4x4.Identity;
            foreach (var element in node.Elements())
            {
                var values = element.Value.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0).ToArray();
                Matrix4x4 transform;
                switch (element.Name.LocalName)
                {
                    case "matrix" when values.Length == 16:
                        // Row-major listing of a column-vector matrix: reading it as rows of the row-vector matrix needs a transpose.
                        transform = new Matrix4x4(values[0], values[4], values[8], values[12], values[1], values[5], values[9], values[13],
                            values[2], values[6], values[10], values[14], values[3], values[7], values[11], values[15]);
                        break;
                    case "translate" when values.Length >= 3:
                        transform = Matrix4x4.CreateTranslation(values[0], values[1], values[2]);
                        break;
                    case "scale" when values.Length >= 3:
                        transform = Matrix4x4.CreateScale(values[0], values[1], values[2]);
                        break;
                    case "rotate" when values.Length >= 4:
                    {
                        var axis = new Vector3(values[0], values[1], values[2]);
                        transform = axis.LengthSquared() > 1e-12f
                            ? Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(axis), values[3] * MathF.PI / 180)
                            : Matrix4x4.Identity;
                        break;
                    }
                    default:
                        continue;
                }
                // Column-vector product T1·T2·T3 becomes T3ᵀ·T2ᵀ·T1ᵀ when written for row vectors.
                result = transform * result;
            }
            return result;
        }

        private static List<(string Symbol, string Target)> MaterialBindings(XElement instance) =>
            instance.Descendants().Where(e => e.Name.LocalName == "instance_material")
                .Select(e => (e.Attribute("symbol")?.Value ?? "", e.Attribute("target")?.Value.TrimStart('#') ?? "")).ToList();

        // ---- geometry

        private void AddGeometry(MeshAccumulator mesh, XElement geometry, Matrix4x4 world, List<(string Symbol, string Target)> bindings,
            CancellationToken cancellation)
        {
            if (geometry.Elements().FirstOrDefault(e => e.Name.LocalName == "mesh") is not { } meshElement) return;
            var sources = meshElement.Elements().Where(e => e.Name.LocalName == "source").ToDictionary(
                e => e.Attribute("id")?.Value ?? "", e => ReadFloats(e));
            var vertexInputs = meshElement.Elements().FirstOrDefault(e => e.Name.LocalName == "vertices") is { } vertices
                ? (Id: vertices.Attribute("id")?.Value ?? "", Position: Inputs(vertices).FirstOrDefault(i => i.Semantic == "POSITION").Source)
                : default;
            var mirrored = world.GetDeterminant() < 0;

            foreach (var primitive in meshElement.Elements().Where(e => e.Name.LocalName is "triangles" or "polylist" or "polygons"))
            {
                cancellation.ThrowIfCancellationRequested();
                var inputs = Inputs(primitive);
                var vertexInput = inputs.FirstOrDefault(i => i.Semantic == "VERTEX");
                if (vertexInput.Source is null) continue;
                var positionSource = vertexInput.Source == vertexInputs.Id ? vertexInputs.Position : vertexInput.Source;
                if (positionSource is null || !sources.TryGetValue(positionSource, out var positions) || positions.Stride < 3) continue;
                var normalInput = inputs.FirstOrDefault(i => i.Semantic == "NORMAL");
                var normals = normalInput.Source is not null && sources.TryGetValue(normalInput.Source, out var n) ? n : null;
                var stride = inputs.Max(i => i.Offset) + 1;

                var material = Material(mesh, primitive.Attribute("material")?.Value, bindings);
                var counts = primitive.Name.LocalName == "polylist"
                    ? Ints(primitive.Elements().FirstOrDefault(e => e.Name.LocalName == "vcount")?.Value)
                    : null;
                var polygonData = primitive.Name.LocalName == "polygons"
                    ? primitive.Elements().Where(e => e.Name.LocalName == "p").Select(e => Ints(e.Value)).ToList()
                    : [Ints(primitive.Elements().FirstOrDefault(e => e.Name.LocalName == "p")?.Value)];

                var cache = new Dictionary<long, int>();
                int Vertex(int[] data, int corner)
                {
                    var positionIndex = data[corner * stride + vertexInput.Offset];
                    var normalIndex = normals is not null ? data[corner * stride + normalInput.Offset] : -1;
                    var key = ((long)positionIndex << 32) | (uint)(normalIndex + 1);
                    if (cache.TryGetValue(key, out var existing)) return existing;
                    var p = new Vector3(positions.Values[positionIndex * positions.Stride], positions.Values[positionIndex * positions.Stride + 1],
                        positions.Values[positionIndex * positions.Stride + 2]);
                    var normal = Vector3.Zero;
                    if (normals is not null && normalIndex >= 0 && (normalIndex + 1) * normals.Stride <= normals.Values.Length)
                    {
                        normal = Vector3.TransformNormal(new Vector3(normals.Values[normalIndex * normals.Stride],
                            normals.Values[normalIndex * normals.Stride + 1], normals.Values[normalIndex * normals.Stride + 2]), world);
                        normal = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.Zero;
                    }
                    return cache[key] = mesh.AddVertex(Vector3.Transform(p, world), normal);
                }

                foreach (var data in polygonData)
                {
                    var corner = 0;
                    var polygons = counts ?? Enumerable.Repeat(3, data.Length / Math.Max(1, stride * 3)).ToArray();
                    if (primitive.Name.LocalName == "polygons") polygons = [data.Length / Math.Max(1, stride)];
                    foreach (var count in polygons)
                    {
                        if (count < 3 || (corner + count) * stride > data.Length) break;
                        if (mesh.TriangleCount + count > _options.MaximumTriangles)
                            throw new InvalidDataException("The model file is too large to import.");
                        var first = Vertex(data, corner);
                        for (var k = 1; k < count - 1; k++)
                        {
                            var b = Vertex(data, corner + k);
                            var c = Vertex(data, corner + k + 1);
                            mesh.AddTriangle(first, mirrored ? c : b, mirrored ? b : c, material);
                        }
                        corner += count;
                    }
                }
            }
        }

        private readonly record struct Input(string Semantic, string? Source, int Offset);

        private static List<Input> Inputs(XElement element) => element.Elements().Where(e => e.Name.LocalName == "input")
            .Select(e => new Input(e.Attribute("semantic")?.Value ?? "", e.Attribute("source")?.Value.TrimStart('#'),
                int.TryParse(e.Attribute("offset")?.Value, out var offset) ? offset : 0)).ToList();

        private sealed record Floats(float[] Values, int Stride);

        private static Floats ReadFloats(XElement source)
        {
            var array = source.Elements().FirstOrDefault(e => e.Name.LocalName == "float_array");
            var values = (array?.Value ?? "").Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .Select(v => float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0).ToArray();
            var accessor = source.Descendants().FirstOrDefault(e => e.Name.LocalName == "accessor");
            return new Floats(values, int.TryParse(accessor?.Attribute("stride")?.Value, out var stride) ? stride : 3);
        }

        private static int[] Ints(string? text) => (text ?? "").Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(v => int.TryParse(v, out var i) ? i : 0).ToArray();

        // ---- materials

        private int Material(MeshAccumulator mesh, string? symbol, List<(string Symbol, string Target)> bindings)
        {
            var target = bindings.FirstOrDefault(b => b.Symbol == symbol).Target ?? symbol;
            var key = "dae:" + (target ?? "");
            return mesh.MaterialIndex(key, () => target is not null ? BuildMaterial(target) : MaterialLibrary.Imported);
        }

        private RenderMaterial BuildMaterial(string materialId)
        {
            if (_materials.TryGetValue(materialId, out var cached)) return cached;
            RenderMaterial result = MaterialLibrary.Imported;
            if (_byId.TryGetValue(materialId, out var material))
            {
                var effectUrl = material.Elements().FirstOrDefault(e => e.Name.LocalName == "instance_effect")?.Attribute("url")?.Value.TrimStart('#');
                if (effectUrl is not null && _byId.TryGetValue(effectUrl, out var effect))
                {
                    var diffuse = effect.Descendants().FirstOrDefault(e => e.Name.LocalName == "diffuse")?
                        .Elements().FirstOrDefault(e => e.Name.LocalName == "color")?.Value;
                    var values = (diffuse ?? "0.7 0.7 0.7 1").Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
                        .Select(v => float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0.7f).ToArray();
                    var color = new Vector3(values.ElementAtOrDefault(0), values.ElementAtOrDefault(1), values.ElementAtOrDefault(2));
                    var alpha = values.Length > 3 ? values[3] : 1f;
                    // Collada colors are display values; convert them to linear for lighting.
                    var linear = new Vector3(MaterialLibrary.SrgbToLinear(Math.Clamp(color.X, 0, 1)),
                        MaterialLibrary.SrgbToLinear(Math.Clamp(color.Y, 0, 1)), MaterialLibrary.SrgbToLinear(Math.Clamp(color.Z, 0, 1)));
                    result = alpha < 0.98f
                        ? MaterialLibrary.ClearGlass with { Name = materialId, Transmission = Math.Clamp(1 - alpha, 0.2f, 0.97f), TransmissionTint = Vector3.Lerp(new Vector3(0.9f, 0.95f, 0.97f), linear, 0.3f) }
                        : new RenderMaterial { Name = materialId, BaseColor = linear, Roughness = 0.8f };
                }
            }
            return _materials[materialId] = result;
        }
    }
}
