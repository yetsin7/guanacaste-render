using System.Numerics;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Import;

namespace Guanacaste3D.Core.Rendering;

/// <summary>A point or spot light. <see cref="Intensity"/> is radiant intensity per steradian (color already multiplied in).</summary>
public sealed record RenderLight(Vector3 Position, Vector3 Intensity, float Radius = 0.05f,
    Vector3? SpotDirection = null, float SpotInnerDegrees = 25, float SpotOuterDegrees = 40)
{
    /// <summary>
    /// Measured light distribution (IES). When set, <see cref="Intensity"/> is a color and scale factor applied to the
    /// profile's candela values (white, 1 for the file as measured) and <see cref="SpotDirection"/> is the luminaire's
    /// downward axis (straight down by default).
    /// </summary>
    public IesProfile? Ies { get; init; }
}

/// <summary>
/// Everything the path tracer needs: indexed triangles, one material per triangle, optional smooth
/// normals and explicit lights. Build it with <see cref="RenderSceneBuilder"/> so the BIM model and any
/// linked models share a single acceleration structure.
/// </summary>
public sealed class RenderScene
{
    internal RenderScene(Vector3[] positions, int[] indices, int[] triangleMaterial, Vector3[]? normals,
        RenderMaterial[] materials, RenderLight[] lights, int droppedTriangles, Vector2[]? triangleUvs = null,
        int dynamicTriangleStart = 0, int dynamicTriangleCount = 0, int dynamicVertexStart = 0, int dynamicVertexCount = 0)
    {
        TriangleUvs = triangleUvs;
        DynamicTriangleStart = dynamicTriangleStart;
        DynamicTriangleCount = dynamicTriangleCount;
        DynamicVertexStart = dynamicVertexStart;
        DynamicVertexCount = dynamicVertexCount;
        Positions = positions;
        Indices = indices;
        TriangleMaterial = triangleMaterial;
        Normals = normals;
        Materials = materials;
        Lights = lights;
        DroppedTriangles = droppedTriangles;
    }

    public Vector3[] Positions { get; }
    /// <summary>
    /// The last triangles and vertices of the scene (people who walk) may move from frame to frame; a tracer keeps its own copy
    /// of them and rebuilds only their small hierarchy. Zero when nothing in the scene moves.
    /// </summary>
    public int DynamicTriangleStart { get; }
    public int DynamicTriangleCount { get; }
    public int DynamicVertexStart { get; }
    public int DynamicVertexCount { get; }
    public int[] Indices { get; }
    public int[] TriangleMaterial { get; }
    public Vector3[]? Normals { get; }
    /// <summary>
    /// Texture coordinates of each triangle's three corners (three per triangle), for materials that are
    /// <see cref="RenderMaterial.UvMapped"/>; null when no triangle has any.
    /// </summary>
    public Vector2[]? TriangleUvs { get; }
    public RenderMaterial[] Materials { get; }
    public RenderLight[] Lights { get; }
    /// <summary>Triangles discarded for non-finite coordinates, bad indices or zero area.</summary>
    public int DroppedTriangles { get; }
    public int TriangleCount => Indices.Length / 3;

    /// <summary>Axis-aligned bounds of all vertices used by triangles; a unit box when empty.</summary>
    public (Vector3 Min, Vector3 Max) Bounds()
    {
        if (Indices.Length == 0) return (Vector3.Zero, Vector3.One);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var index in Indices)
        {
            min = Vector3.Min(min, Positions[index]);
            max = Vector3.Max(max, Positions[index]);
        }
        return (min, max);
    }

    /// <summary>Approximate heap size of the geometry, used to warn before very large renders.</summary>
    public long EstimatedBytes =>
        Positions.Length * 12L + Indices.Length * 4L + TriangleMaterial.Length * 4L +
        (Normals?.Length ?? 0) * 12L + (TriangleUvs?.Length ?? 0) * 8L + TriangleCount * 40L;
}

/// <summary>Combines the BIM mesh and linked models into one <see cref="RenderScene"/>.</summary>
public sealed class RenderSceneBuilder
{
    private readonly ArrayBuilderProxy<Vector3> _positions = new();
    private readonly ArrayBuilderProxy<int> _indices = new();
    private readonly ArrayBuilderProxy<int> _triangleMaterial = new();
    private readonly ArrayBuilderProxy<Vector3> _normals = new();
    private readonly List<RenderMaterial> _materials = [];
    private readonly Dictionary<RenderMaterial, int> _materialIndex = [];
    private readonly List<RenderLight> _lights = [];
    private readonly List<(int Triangle, TexturedTriangle Corners)> _uvs = [];
    private bool _anyNormals;
    private int _dropped;
    private int _dynamicTriangleStart, _dynamicTriangleCount, _dynamicVertexStart, _dynamicVertexCount;

    /// <summary>Adds the BIM scene mesh, mapping each surface category to its stock material.</summary>
    public RenderSceneBuilder AddBuilding(SceneMesh mesh,
        Func<SurfaceKind, RenderMaterial>? materialFor = null,
        Func<Guid, SurfaceKind, RenderMaterial>? materialForElement = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (_dynamicTriangleCount > 0) throw new InvalidOperationException("Moving geometry must be added last.");
        materialFor ??= MaterialLibrary.ForKind;
        var kinds = new Dictionary<(SurfaceKind, Guid?), int>();
        var dimmed = new Dictionary<(SurfaceKind, int, Guid?), int>();
        var textured = new Dictionary<(SurfaceKind, int, Guid?), int>();
        var rangeCursor = 0;
        var ranges = mesh.ElementRanges;
        var start = _positions.Count;
        var meshPositions = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(mesh.Positions);
        _positions.AddRange(meshPositions);
        if (mesh.Normals is { } meshNormals && meshNormals.Count == mesh.Positions.Count)
        {
            // Smooth-shaded parts of the building (foliage, trunks); flat vertices carry a zero normal.
            _anyNormals = true;
            PadNormals(start);
            foreach (var normal in meshNormals)
                _normals.Add(normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.Zero);
        }
        for (var triangle = 0; triangle < mesh.TriangleCount; triangle++)
        {
            var a = mesh.Indices[triangle * 3];
            var b = mesh.Indices[triangle * 3 + 1];
            var c = mesh.Indices[triangle * 3 + 2];
            if (!ValidTriangle(meshPositions, a, b, c))
            {
                _dropped++;
                continue;
            }
            var kind = mesh.TriangleKinds[triangle];
            while (rangeCursor < ranges.Count && ranges[rangeCursor].End <= triangle) rangeCursor++;
            Guid? owner = rangeCursor < ranges.Count && ranges[rangeCursor].First <= triangle
                ? ranges[rangeCursor].ElementId : null;
            RenderMaterial Resolve() => owner is Guid id && materialForElement is not null
                ? materialForElement(id, kind) : materialFor(kind);
            int material;
            var output = mesh.EmissionOf(triangle);
            if (output < 1)
            {
                // A light on a dimmer: its own copy of the material with the emission scaled, one per dimmer step.
                var step = (int)MathF.Round(output * 64);
                if (!dimmed.TryGetValue((kind, step, owner), out material))
                {
                    var full = Resolve();
                    dimmed[(kind, step, owner)] = material = MaterialIndex(full with { Emission = full.Emission * (step / 64f) });
                }
            }
            else if (mesh.TexturedTriangles.TryGetValue(triangle, out var corners) && (uint)corners.Texture < (uint)mesh.Textures.Count)
            {
                // A detailed family's own image: the category keeps its roughness and translucency, the image its colour.
                if (!textured.TryGetValue((kind, corners.Texture, owner), out material))
                {
                    var image = mesh.Textures[corners.Texture];
                    textured[(kind, corners.Texture, owner)] = material = MaterialIndex(Resolve() with
                    {
                        BaseColor = Vector3.One,
                        Pattern = kind == SurfaceKind.Bark && image.Normal is null ? SurfacePattern.Bark : SurfacePattern.None,
                        Texture = image.Texture, TextureSize = 1,
                        TextureRelief = 0, UvMapped = true, AlphaCutoff = image.AlphaCutoff, NormalMap = image.Normal
                    });
                }
                _uvs.Add((_triangleMaterial.Count, corners));
            }
            else if (!kinds.TryGetValue((kind, owner), out material)) kinds[(kind, owner)] = material = MaterialIndex(Resolve());
            _indices.Add(start + a);
            _indices.Add(start + b);
            _indices.Add(start + c);
            _triangleMaterial.Add(material);
        }
        PadNormals();
        return this;
    }

    /// <summary>Adds a linked model after applying <paramref name="placement"/>.</summary>
    public RenderSceneBuilder AddModel(ImportedModel model, ModelPlacement? placement = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (_dynamicTriangleCount > 0) throw new InvalidOperationException("Moving geometry must be added last.");
        var matrix = (placement ?? ModelPlacement.Identity).ToMatrix();
        var start = _positions.Count;
        foreach (var position in model.Positions) _positions.Add(Vector3.Transform(position, matrix));
        if (model.Normals is { } normals && normals.Length == model.Positions.Length)
        {
            _anyNormals = true;
            PadNormals(start);
            foreach (var normal in normals)
            {
                var rotated = Vector3.TransformNormal(normal, matrix);
                _normals.Add(rotated.LengthSquared() > 1e-12f ? Vector3.Normalize(rotated) : Vector3.Zero);
            }
        }
        var remap = new int[Math.Max(1, model.Materials.Count)];
        for (var i = 0; i < model.Materials.Count; i++) remap[i] = MaterialIndex(model.Materials[i]);
        if (model.Materials.Count == 0) remap[0] = MaterialIndex(MaterialLibrary.Imported);
        var localPositions = model.Positions;
        for (var triangle = 0; triangle < model.TriangleCount; triangle++)
        {
            var a = model.Indices[triangle * 3];
            var b = model.Indices[triangle * 3 + 1];
            var c = model.Indices[triangle * 3 + 2];
            if (!ValidTriangle(localPositions, a, b, c))
            {
                _dropped++;
                continue;
            }
            // Tolerate files whose triangle material list is shorter than the triangle list.
            var material = triangle < model.TriangleMaterial.Length ? model.TriangleMaterial[triangle] : 0;
            var mappedMaterial = remap[(uint)material < (uint)remap.Length ? material : 0];
            _indices.Add(start + a);
            _indices.Add(start + b);
            _indices.Add(start + c);
            _triangleMaterial.Add(mappedMaterial);
            // Imported glTF-style UVs are per vertex; keep them in triangle order
            // after invalid faces have been discarded. Non-textured meshes stay compact.
            if (model.Uvs is { } uvs && uvs.Length == localPositions.Length &&
                _materials[mappedMaterial].UvMapped && _materials[mappedMaterial].Texture is not null)
                _uvs.Add((_triangleMaterial.Count - 1,
                    new TexturedTriangle(uvs[a], uvs[b], uvs[c], 0)));
        }
        PadNormals();
        return this;
    }

    /// <summary>
    /// Adds geometry whose vertices a tracer can move between frames (people walking), at their starting places. It must be
    /// the last geometry added, and can be added once: the scene remembers where it is, and a tracer is given new vertex
    /// positions for it with <see cref="PathTracer.MoveActors"/>. Triangle numbers are relative to the block's own vertices.
    /// </summary>
    public RenderSceneBuilder AddDynamic(Vector3[] positions, int[] indices, int[] triangleMaterial, RenderMaterial[] materials,
        Vector3[]? normals = null)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(triangleMaterial);
        ArgumentNullException.ThrowIfNull(materials);
        if (_dynamicTriangleCount > 0) throw new InvalidOperationException("Moving geometry can be added only once.");
        if (normals is not null && normals.Length != positions.Length) throw new ArgumentException("There must be one normal for each position.", nameof(normals));
        if (indices.Length % 3 != 0 || triangleMaterial.Length != indices.Length / 3 || indices.Any(index => (uint)index >= (uint)positions.Length) ||
            triangleMaterial.Any(material => (uint)material >= (uint)materials.Length))
            throw new ArgumentException("The moving geometry is not consistent.");
        PadNormals();
        var start = _positions.Count;
        _dynamicVertexStart = start;
        _dynamicTriangleStart = _triangleMaterial.Count;
        _positions.AddRange(positions);
        if (normals is not null)
        {
            // Smooth normals turn with a moving car, so the tracer keeps its own copy and rotates it per frame; the scene
            // holds the starting ones.
            _anyNormals = true;
            PadNormals(start);
            foreach (var normal in normals) _normals.Add(normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.Zero);
        }
        var remap = materials.Select(MaterialIndex).ToArray();
        for (var triangle = 0; triangle < triangleMaterial.Length; triangle++)
        {
            _indices.Add(start + indices[triangle * 3]);
            _indices.Add(start + indices[triangle * 3 + 1]);
            _indices.Add(start + indices[triangle * 3 + 2]);
            _triangleMaterial.Add(remap[triangleMaterial[triangle]]);
        }
        _dynamicVertexCount = positions.Length;
        _dynamicTriangleCount = triangleMaterial.Length;
        PadNormals();
        return this;
    }

    /// <summary>Adds an explicit light.</summary>
    public RenderSceneBuilder AddLight(RenderLight light)
    {
        ArgumentNullException.ThrowIfNull(light);
        if (float.IsFinite(light.Position.X + light.Position.Y + light.Position.Z) &&
            float.IsFinite(light.Intensity.X + light.Intensity.Y + light.Intensity.Z))
            _lights.Add(light);
        return this;
    }

    /// <summary>Freezes the accumulated geometry into an immutable scene.</summary>
    public RenderScene Build()
    {
        PadNormals();
        var materials = _materials.Count == 0 ? [MaterialLibrary.Imported.Sanitized()] : _materials.ToArray();
        Vector2[]? uvs = null;
        if (_uvs.Count > 0)
        {
            uvs = new Vector2[_triangleMaterial.Count * 3];
            foreach (var (triangle, corners) in _uvs)
            {
                uvs[triangle * 3] = corners.A;
                uvs[triangle * 3 + 1] = corners.B;
                uvs[triangle * 3 + 2] = corners.C;
            }
        }
        return new RenderScene(_positions.ToArray(), _indices.ToArray(), _triangleMaterial.ToArray(),
            _anyNormals ? _normals.ToArray() : null, materials, _lights.ToArray(), _dropped, uvs,
            _dynamicTriangleStart, _dynamicTriangleCount, _dynamicVertexStart, _dynamicVertexCount);
    }

    private int MaterialIndex(RenderMaterial material)
    {
        var clean = material.Sanitized();
        if (_materialIndex.TryGetValue(clean, out var index)) return index;
        index = _materials.Count;
        _materials.Add(clean);
        _materialIndex[clean] = index;
        return index;
    }

    /// <summary>Keeps the normal array aligned with positions once any model supplies normals.</summary>
    private void PadNormals(int? upTo = null)
    {
        if (!_anyNormals) return;
        var target = upTo ?? _positions.Count;
        while (_normals.Count < target) _normals.Add(Vector3.Zero);
    }

    private static bool ValidTriangle(ReadOnlySpan<Vector3> positions, int a, int b, int c)
    {
        var count = positions.Length;
        if ((uint)a >= (uint)count || (uint)b >= (uint)count || (uint)c >= (uint)count) return false;
        var p = positions[a];
        var q = positions[b];
        var r = positions[c];
        if (!float.IsFinite(p.X + p.Y + p.Z + q.X + q.Y + q.Z + r.X + r.Y + r.Z)) return false;
        return Vector3.Cross(q - p, r - p).LengthSquared() > 1e-18f;
    }

    /// <summary>Small wrapper that adds a count and bulk copy on top of the generic builder.</summary>
    private sealed class ArrayBuilderProxy<T>
    {
        private readonly ArrayBuilder<T> _inner = new(1024);
        public int Count => _inner.Count;
        public void Add(T item) => _inner.Add(item);
        public void AddRange(ReadOnlySpan<T> items) => _inner.AddRange(items);
        public T[] ToArray() => _inner.ToArray();
    }
}
