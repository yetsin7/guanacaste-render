using System.Numerics;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Import;

namespace Guanacaste3D.Core;

/// <summary>What converting one model into a family produced.</summary>
public sealed record FamilyConversion(string Source, string Family, string Name, int SourceTriangles, int Triangles, double Width, double Depth, double Height, long Bytes);

/// <summary>A model that could not be converted, and why.</summary>
public sealed record FamilyConversionFailure(string Source, string Reason);

/// <summary>
/// Turns 3D models made elsewhere (OBJ, glTF, STL…) into mesh families (.g3df) that can be loaded and placed: the model is reduced to
/// a triangle budget, its materials are matched to render surfaces by name, it is moved so the middle of its base is the origin, and its
/// normals are smoothed. It is the in-application form of the command-line converter.
/// </summary>
public static class FamilyConverter
{
    public const int MinimumTriangles = 1000;
    public const int MaximumTriangles = 400_000;

    private static readonly (string[] Words, SurfaceKind Kind)[] Surfaces =
    [
        (["glass", "vidrio", "cristal", "window", "ventana"], SurfaceKind.Glass),
        (["leaf", "leaves", "hoja", "foliage", "follaje"], SurfaceKind.Foliage),
        (["bark", "trunk", "tronco", "corteza"], SurfaceKind.Bark),
        (["chrome", "metal", "steel", "acero", "hierro", "aluminium", "aluminum", "brass"], SurfaceKind.Metal),
        (["ceramic", "porcelain", "ceramica", "cerámica", "porcelana"], SurfaceKind.Ceramic),
        (["fabric", "cloth", "tela", "upholster", "cushion", "cojin", "cojín", "leather", "cuero"], SurfaceKind.Upholstery),
        (["lamp", "emissive", "emisivo", "light", "luz"], SurfaceKind.LightEmitter),
        (["grass", "pasto", "soil", "tierra", "ground", "suelo"], SurfaceKind.Ground),
    ];

    /// <summary>The surface a material of this name is drawn with; anything unknown is plain structure.</summary>
    public static SurfaceKind SurfaceFor(string? materialName)
    {
        if (string.IsNullOrWhiteSpace(materialName)) return SurfaceKind.Structure;
        foreach (var (words, kind) in Surfaces)
            if (words.Any(word => materialName.Contains(word, StringComparison.OrdinalIgnoreCase))) return kind;
        return SurfaceKind.Structure;
    }

    /// <summary>The file types that can be converted.</summary>
    public static bool CanConvert(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".obj" or ".gltf" or ".glb" or ".stl";

    /// <summary>
    /// Converts one model into a family file. <paramref name="metersPerUnit"/> scales a model drawn in other units; <paramref name="yawDegrees"/>
    /// turns it about the vertical so its front faces -Y. The family is loaded back to prove that it can be read.
    /// </summary>
    public static FamilyConversion Convert(string modelPath, string familyPath, string? name, FamilyCategory category, int triangleBudget = 60_000,
        double metersPerUnit = 1, double yawDegrees = 0)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath)) throw new InvalidDataException("The model file does not exist.");
        if (string.IsNullOrWhiteSpace(familyPath) || Path.GetExtension(familyPath).ToLowerInvariant() != FamilyFile.Extension) throw new InvalidDataException("The family file must be a .g3df.");
        if (!Enum.IsDefined(category) || category == FamilyCategory.TitleBlocks) throw new InvalidDataException("The family category is invalid.");
        if (!double.IsFinite(metersPerUnit) || metersPerUnit <= 1e-6 || metersPerUnit > 1000 || !double.IsFinite(yawDegrees)) throw new InvalidDataException("The scale or the turn is invalid.");
        triangleBudget = Math.Clamp(triangleBudget, MinimumTriangles, MaximumTriangles);
        var clean = (name ?? Path.GetFileNameWithoutExtension(modelPath)).Trim();
        if (clean.Length == 0 || clean.Length > 120) throw new InvalidDataException("The family name is invalid.");

        ImportedModel source;
        try { source = ModelImporter.Import(modelPath, new ModelImportOptions { MetersPerUnit = metersPerUnit, MaximumTriangles = 40_000_000 }).Model; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new InvalidDataException("The model cannot be read.", exception);
        }
        if (source.TriangleCount == 0) throw new InvalidDataException("The model has no triangles.");

        var proxy = MeshProxy.Build(source, triangleBudget);
        var yaw = yawDegrees * Math.PI / 180;
        var (cos, sin) = ((float)Math.Cos(yaw), (float)Math.Sin(yaw));
        var turned = proxy.Positions.Select(p => new Vector3(p.X * cos - p.Y * sin, p.X * sin + p.Y * cos, p.Z)).ToArray();
        var min = turned.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
        var max = turned.Aggregate(new Vector3(float.MinValue), Vector3.Max);
        var centre = new Vector3((min.X + max.X) / 2, (min.Y + max.Y) / 2, min.Z);
        var positions = turned.Select(p => p - centre).ToArray();

        var indices = new List<int>();
        var kinds = new List<SurfaceKind>();
        for (var triangle = 0; triangle < proxy.TriangleCount; triangle++)
        {
            var (a, b, c) = (proxy.Indices[triangle * 3], proxy.Indices[triangle * 3 + 1], proxy.Indices[triangle * 3 + 2]);
            if (a == b || b == c || a == c) continue;
            if (Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]).LengthSquared() < 1e-14f) continue;
            indices.AddRange([a, b, c]);
            var material = triangle < proxy.TriangleMaterial.Length ? proxy.TriangleMaterial[triangle] : -1;
            kinds.Add(SurfaceFor(material >= 0 && material < source.Materials.Count ? source.Materials[material].Name : null));
        }
        if (kinds.Count == 0) throw new InvalidDataException("The model has no triangles.");

        var family = new FamilyDocument
        {
            Name = clean,
            Category = category,
            Types = [new FamilyTypeDefinition(Guid.NewGuid(), "Estándar", [])],
            Mesh = FamilyMesh.Create(positions, indices, kinds, SmoothNormals(positions, indices))
        };
        family.Id = ArchitecturalLibrary.StableId("mesh-family", clean);
        var folder = Path.GetDirectoryName(Path.GetFullPath(familyPath));
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        NativeDocumentStore.SaveFamily(familyPath, family);
        var size = NativeDocumentStore.LoadFamily(familyPath).Mesh!.Decode().Size;
        return new FamilyConversion(modelPath, familyPath, clean, source.TriangleCount, kinds.Count, size.X, size.Y, size.Z, new FileInfo(familyPath).Length);
    }

    /// <summary>Converts each model into a family beside the others in a folder; the ones that fail are listed with the reason and the rest go on.</summary>
    public static (IReadOnlyList<FamilyConversion> Converted, IReadOnlyList<FamilyConversionFailure> Failed) ConvertMany(
        IReadOnlyList<string> modelPaths, string folder, FamilyCategory category, int triangleBudget = 60_000, double metersPerUnit = 1)
    {
        ArgumentNullException.ThrowIfNull(modelPaths);
        if (modelPaths.Count == 0 || modelPaths.Count > 500) throw new InvalidDataException("Choose between one and 500 models.");
        if (string.IsNullOrWhiteSpace(folder)) throw new InvalidDataException("Choose a folder.");
        var converted = new List<FamilyConversion>();
        var failed = new List<FamilyConversionFailure>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in modelPaths)
        {
            try
            {
                if (!CanConvert(path)) throw new InvalidDataException("This kind of file cannot be converted.");
                var stem = new string(Path.GetFileNameWithoutExtension(path).Select(letter => Path.GetInvalidFileNameChars().Contains(letter) ? '_' : letter).ToArray()).Trim();
                if (stem.Length == 0) stem = "familia";
                var unique = stem;
                for (var number = 2; !taken.Add(unique); number++) unique = $"{stem} {number}";
                converted.Add(Convert(path, Path.Combine(folder, unique + FamilyFile.Extension), unique, category, triangleBudget, metersPerUnit));
            }
            catch (InvalidDataException exception)
            {
                failed.Add(new FamilyConversionFailure(path, exception.Message));
            }
        }
        return (converted, failed);
    }

    /// <summary>Area-weighted vertex normals; vertices at one position are smoothed together so the model shades round, not faceted.</summary>
    private static Vector3[] SmoothNormals(Vector3[] positions, List<int> indices)
    {
        var weld = new Dictionary<Vector3, int>();
        var group = positions.Select(position => weld.TryGetValue(position, out var index) ? index : weld[position] = weld.Count).ToArray();
        var sums = new Vector3[weld.Count];
        for (var index = 0; index < indices.Count; index += 3)
        {
            var (a, b, c) = (indices[index], indices[index + 1], indices[index + 2]);
            var face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            sums[group[a]] += face;
            sums[group[b]] += face;
            sums[group[c]] += face;
        }
        return group.Select(index => sums[index].LengthSquared() > 0 ? Vector3.Normalize(sums[index]) : Vector3.UnitZ).ToArray();
    }
}
