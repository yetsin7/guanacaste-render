using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core;

/// <summary>
/// The triangles of a mesh family: a detailed model (a tree, a car) converted from glTF or OBJ into a loadable .g3df
/// component. Coordinates are meters, Z up, with the origin at the middle of the base and the front toward -Y, like
/// every other component family. Each triangle carries the render surface it is made of, so it takes part in the
/// project's materials (foliage, bark, car paint, glass...). Arrays are stored as base64 little-endian binary.
/// </summary>
public sealed class FamilyMesh
{
    /// <summary>Largest mesh a family may carry; enough for a scanned tree once reduced.</summary>
    public const int MaximumTriangles = 1_500_000;

    private Decoded? _decoded;

    /// <summary>Vertex positions, three float32 each.</summary>
    public string Positions { get; set; } = string.Empty;

    /// <summary>Three int32 vertex indices per triangle.</summary>
    public string Indices { get; set; } = string.Empty;

    /// <summary>One <see cref="SurfaceKind"/> byte per triangle.</summary>
    public string Kinds { get; set; } = string.Empty;

    /// <summary>Optional smooth shading normals, three float32 per vertex.</summary>
    public string? Normals { get; set; }

    /// <summary>
    /// Optional light version for interactive views (a few tens of thousands of triangles); the renderer always uses
    /// the full mesh. It is scaled with the full mesh's bounds, so both versions fill the same box.
    /// </summary>
    public FamilyMesh? Preview { get; set; }

    /// <summary>Optional texture coordinates, two float32 per vertex (v runs down the image, as in glTF).</summary>
    public string? Uvs { get; set; }

    /// <summary>The family's own images (leaf, bark, car paint...), mapped with <see cref="Uvs"/>.</summary>
    public List<FamilyMeshTexture> Textures { get; set; } = [];

    /// <summary>One byte per triangle: its image in <see cref="Textures"/>, or 255 for an untextured triangle.</summary>
    public string? TriangleTextures { get; set; }

    /// <summary>Most images a family may carry.</summary>
    public const int MaximumTextures = 16;

    /// <summary>Marks an untextured triangle in <see cref="TriangleTextures"/>.</summary>
    public const byte NoTexture = 255;

    /// <summary>The decoded arrays of a mesh, ready to transform into a scene.</summary>
    public sealed record Decoded(Vector3[] Positions, int[] Indices, SurfaceKind[] Kinds, Vector3[]? Normals, Vector3 Min, Vector3 Max)
    {
        public Vector2[]? Uvs { get; init; }
        public byte[]? TriangleTextures { get; init; }
        public int TriangleCount => Kinds.Length;
        public Vector3 Size => Max - Min;
    }

    /// <summary>Packs arrays into a family mesh.</summary>
    public static FamilyMesh Create(IReadOnlyList<Vector3> positions, IReadOnlyList<int> indices, IReadOnlyList<SurfaceKind> kinds,
        IReadOnlyList<Vector3>? normals = null)
    {
        var mesh = new FamilyMesh
        {
            Positions = Convert.ToBase64String(MemoryMarshal.AsBytes(positions.ToArray().AsSpan())),
            Indices = Convert.ToBase64String(MemoryMarshal.AsBytes(indices.ToArray().AsSpan())),
            Kinds = Convert.ToBase64String(kinds.Select(kind => (byte)kind).ToArray()),
            Normals = normals is null ? null : Convert.ToBase64String(MemoryMarshal.AsBytes(normals.ToArray().AsSpan()))
        };
        mesh.Validate();
        return mesh;
    }

    /// <summary>Packs a textured mesh: texture coordinates per vertex and an image (or none) per triangle.</summary>
    public static FamilyMesh Create(IReadOnlyList<Vector3> positions, IReadOnlyList<int> indices, IReadOnlyList<SurfaceKind> kinds,
        IReadOnlyList<Vector3>? normals, IReadOnlyList<Vector2> uvs, IReadOnlyList<byte> triangleTextures, List<FamilyMeshTexture> textures)
    {
        var mesh = new FamilyMesh
        {
            Positions = Convert.ToBase64String(MemoryMarshal.AsBytes(positions.ToArray().AsSpan())),
            Indices = Convert.ToBase64String(MemoryMarshal.AsBytes(indices.ToArray().AsSpan())),
            Kinds = Convert.ToBase64String(kinds.Select(kind => (byte)kind).ToArray()),
            Normals = normals is null ? null : Convert.ToBase64String(MemoryMarshal.AsBytes(normals.ToArray().AsSpan())),
            Uvs = Convert.ToBase64String(MemoryMarshal.AsBytes(uvs.ToArray().AsSpan())),
            TriangleTextures = Convert.ToBase64String(triangleTextures.ToArray()),
            Textures = textures
        };
        mesh.Validate();
        return mesh;
    }

    /// <summary>The decoded arrays, computed once and then cached; the stored strings are treated as immutable.</summary>
    public Decoded Decode()
    {
        if (_decoded is { } cached) return cached;
        try
        {
            var positions = MemoryMarshal.Cast<byte, Vector3>(Convert.FromBase64String(Positions)).ToArray();
            var indices = MemoryMarshal.Cast<byte, int>(Convert.FromBase64String(Indices)).ToArray();
            var kinds = Convert.FromBase64String(Kinds).Select(value => (SurfaceKind)value).ToArray();
            var normals = Normals is null ? null : MemoryMarshal.Cast<byte, Vector3>(Convert.FromBase64String(Normals)).ToArray();
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var position in positions)
            {
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }
            return _decoded = new Decoded(positions, indices, kinds, normals, min, max)
            {
                Uvs = Uvs is null ? null : MemoryMarshal.Cast<byte, Vector2>(Convert.FromBase64String(Uvs)).ToArray(),
                TriangleTextures = TriangleTextures is null ? null : Convert.FromBase64String(TriangleTextures)
            };
        }
        catch (FormatException)
        {
            throw new InvalidDataException("The family mesh is not valid.");
        }
    }

    /// <summary>Rejects truncated arrays, indices out of range, unknown surfaces and non-finite or empty geometry.</summary>
    public void Validate()
    {
        if (Positions is null || Indices is null || Kinds is null || Preview?.Preview is not null)
            throw new InvalidDataException("The family mesh is not valid.");
        // Every edit of the project validates the whole document, and a loaded tree is hundreds of thousands of triangles and
        // images of megabytes: a mesh that was checked and has not changed since (the same arrays, the same images) is not checked again.
        if (_validatedStamp is { } stamp && StampMatches(stamp)) return;
        Preview?.Validate();
        var mesh = Decode();
        if (mesh.Kinds.Length is < 1 or > MaximumTriangles || mesh.Indices.Length != mesh.Kinds.Length * 3 ||
            (mesh.Normals is not null && mesh.Normals.Length != mesh.Positions.Length) ||
            mesh.Positions.Any(point => !float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z)) ||
            mesh.Indices.Any(index => (uint)index >= (uint)mesh.Positions.Length) ||
            mesh.Kinds.Any(kind => !Enum.IsDefined(kind)) ||
            mesh.Size.X <= 0.001f || mesh.Size.Y <= 0.001f || mesh.Size.Z <= 0.001f ||
            Textures is null || Textures.Count > MaximumTextures || Textures.Any(texture => texture is null || !texture.IsValid) ||
            (mesh.Uvs is null) != (mesh.TriangleTextures is null) ||
            (mesh.Uvs is not null && (mesh.Uvs.Length != mesh.Positions.Length || mesh.Uvs.Any(uv => !float.IsFinite(uv.X + uv.Y)))) ||
            (mesh.TriangleTextures is not null && (mesh.TriangleTextures.Length != mesh.Kinds.Length ||
                mesh.TriangleTextures.Any(texture => texture != NoTexture && texture >= Textures.Count))))
            throw new InvalidDataException("The family mesh is not valid.");
        _validatedStamp = Stamp();
    }

    private object?[]? _validatedStamp;

    /// <summary>What a validation depends on, by identity: the strings are immutable, so the same instance is the same content.</summary>
    private object?[] Stamp()
    {
        var stamp = new List<object?> { Positions, Indices, Kinds, Normals, Uvs, TriangleTextures, Preview, Textures, Textures?.Count };
        if (Textures is not null)
            foreach (var texture in Textures)
                stamp.AddRange([texture, texture?.Rgba, texture?.NormalRgb, texture?.Width, texture?.Height, texture?.NormalWidth, texture?.NormalHeight, texture?.AlphaCutoff, texture?.Name]);
        return [.. stamp];
    }

    private bool StampMatches(object?[] before)
    {
        var now = Stamp();
        if (now.Length != before.Length) return false;
        for (var index = 0; index < now.Length; index++)
            if (!ReferenceEquals(now[index], before[index]) && !(now[index] is ValueType && now[index]!.Equals(before[index]))) return false;
        return true;
    }

    /// <summary>
    /// Appends the mesh to a scene, scaled so its bounds match the instance's width, depth and height, turned by its
    /// rotation about the insertion point and standing at <paramref name="baseElevation"/>. With
    /// <paramref name="preview"/> it appends the light version when the family has one.
    /// </summary>
    public void AppendTo(SceneMesh scene, Point2 at, double rotation, double width, double depth, double height, double baseElevation,
        bool preview = false)
    {
        var bounds = Decode();
        var mesh = preview && Preview is { } light ? light.Decode() : bounds;
        var size = bounds.Size;
        var scale = new Vector3((float)(width / size.X), (float)(depth / size.Y), (float)(height / size.Z));
        var center = new Vector3((bounds.Min.X + bounds.Max.X) / 2, (bounds.Min.Y + bounds.Max.Y) / 2, bounds.Min.Z);
        var (cos, sin) = ((float)Math.Cos(rotation), (float)Math.Sin(rotation));
        var origin = new Vector3((float)at.X, (float)at.Y, (float)baseElevation);
        Vector3 Place(Vector3 point)
        {
            var local = (point - center) * scale;
            return origin + new Vector3(local.X * cos - local.Y * sin, local.X * sin + local.Y * cos, local.Z);
        }
        Vector3 Turn(Vector3 normal)
        {
            // Normals transform by the inverse scale so they stay perpendicular to a stretched surface.
            var local = new Vector3(normal.X / scale.X, normal.Y / scale.Y, normal.Z / scale.Z);
            var turned = new Vector3(local.X * cos - local.Y * sin, local.X * sin + local.Y * cos, local.Z);
            return turned.LengthSquared() > 0 ? Vector3.Normalize(turned) : turned;
        }
        // The images of the full mesh go to the scene once, shared by every instance of the family.
        var sceneTexture = new int[Textures.Count];
        if (!preview && mesh.Uvs is not null)
            for (var index = 0; index < Textures.Count; index++)
            {
                var image = Textures[index].Texture();
                sceneTexture[index] = scene.Textures.FindIndex(existing => ReferenceEquals(existing.Texture, image));
                if (sceneTexture[index] < 0)
                {
                    sceneTexture[index] = scene.Textures.Count;
                    scene.Textures.Add(new SceneTexture(image, Textures[index].AlphaCutoff, Textures[index].Normal()));
                }
            }
        for (var triangle = 0; triangle < mesh.TriangleCount; triangle++)
        {
            var (a, b, c) = (mesh.Indices[triangle * 3], mesh.Indices[triangle * 3 + 1], mesh.Indices[triangle * 3 + 2]);
            if (!preview && mesh.Uvs is { } uvs && mesh.TriangleTextures![triangle] is var texture && texture != NoTexture)
                scene.TexturedTriangles[scene.TriangleCount] = new TexturedTriangle(uvs[a], uvs[b], uvs[c], sceneTexture[texture]);
            if (mesh.Normals is { } normals)
                scene.AddSmoothTriangle(Place(mesh.Positions[a]), Place(mesh.Positions[b]), Place(mesh.Positions[c]),
                    Turn(normals[a]), Turn(normals[b]), Turn(normals[c]), mesh.Kinds[triangle]);
            else
                scene.AddTriangle(Place(mesh.Positions[a]), Place(mesh.Positions[b]), Place(mesh.Positions[c]), mesh.Kinds[triangle]);
        }
    }

    [JsonIgnore]
    public int TriangleCount => Decode().TriangleCount;
}

/// <summary>
/// An image of a mesh family: RGBA pixels (rows top to bottom, base64) and the coverage below which the surface is
/// cut away, 0 for an opaque image. Kept small: 512 texels a side is plenty for a leaf card.
/// </summary>
public sealed class FamilyMeshTexture
{
    /// <summary>Largest image side a family may store.</summary>
    public const int MaximumSize = 2048;

    private Rendering.RenderTexture? _texture;
    private Rendering.NormalMap? _normal;

    public string Name { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public string Rgba { get; set; } = string.Empty;
    public float AlphaCutoff { get; set; }
    /// <summary>Optional normal map, RGB (OpenGL convention), base64; relief needs less detail than colour.</summary>
    public string? NormalRgb { get; set; }
    /// <summary>Size of <see cref="NormalRgb"/>; zero means the size of the colour image.</summary>
    public int NormalWidth { get; set; }
    public int NormalHeight { get; set; }

    private (int Width, int Height) NormalSize => NormalWidth > 0 ? (NormalWidth, NormalHeight) : (Width, Height);

    [JsonIgnore]
    public bool IsValid
    {
        get
        {
            if (Width is < 1 or > MaximumSize || Height is < 1 or > MaximumSize || !float.IsFinite(AlphaCutoff) ||
                AlphaCutoff is < 0 or > 1 || Rgba is null || Name is null || Name.Length > 120) return false;
            try
            {
                var (normalWidth, normalHeight) = NormalSize;
                return Convert.FromBase64String(Rgba).Length == Width * Height * 4 && NormalWidth is >= 0 and <= MaximumSize &&
                       NormalHeight is >= 0 and <= MaximumSize && (NormalWidth == 0) == (NormalHeight == 0) &&
                       (NormalRgb is null || Convert.FromBase64String(NormalRgb).Length == normalWidth * normalHeight * 3);
            }
            catch (FormatException) { return false; }
        }
    }

    /// <summary>Packs RGBA pixels.</summary>
    public static FamilyMeshTexture Create(string name, int width, int height, byte[] rgba, float alphaCutoff, byte[]? normalRgb = null,
        int normalWidth = 0, int normalHeight = 0) =>
        new()
        {
            Name = name, Width = width, Height = height, Rgba = Convert.ToBase64String(rgba), AlphaCutoff = alphaCutoff,
            NormalRgb = normalRgb is null ? null : Convert.ToBase64String(normalRgb), NormalWidth = normalWidth, NormalHeight = normalHeight
        };

    /// <summary>The normal map, decoded once, or null when the image has none.</summary>
    public Rendering.NormalMap? Normal() => NormalRgb is null ? null
        : _normal ??= Rendering.NormalMap.FromRgb(NormalSize.Width, NormalSize.Height, Convert.FromBase64String(NormalRgb));

    /// <summary>The render texture, decoded once.</summary>
    public Rendering.RenderTexture Texture() =>
        _texture ??= Rendering.RenderTexture.FromRgba(Width, Height, Convert.FromBase64String(Rgba), MaximumSize);
}
