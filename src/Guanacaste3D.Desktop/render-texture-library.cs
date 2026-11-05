using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop;

/// <summary>
/// The render textures the application ships, including original procedural and licensed photographic images, and
/// the loading of any image the user picks. Decoded textures are cached by file and modification time, so rebuilding
/// the scene after an edit does not decode them again.
/// </summary>
internal static class RenderTextureLibrary
{
    /// <summary>A shipped texture: its file key, label resource, real size of one tile in meters and suggested relief.</summary>
    public sealed record BuiltInTexture(string Key, string LabelKey, double Size, double Relief, string Extension = ".png");

    /// <summary>Paths of shipped textures are stored as this prefix plus the key, so projects stay portable.</summary>
    public const string BuiltInPrefix = "builtin:";

    public static IReadOnlyList<BuiltInTexture> BuiltIn { get; } =
    (new BuiltInTexture[]
    {
        new("brick-red", "Texture.Brick", 1.0, 0.6),
        new("wood-floor", "Texture.WoodFloor", 1.0, 0.25),
        new("stone-paving", "Texture.StonePaving", 2.0, 0.6),
        new("concrete", "Texture.Concrete", 1.2, 0.15),
        new("stucco-white", "Texture.Stucco", 1.0, 0.35),
        new("gravel", "Texture.Gravel", 1.0, 0.8),
        new("ambientcg-wood096", "Texture.AmbientWood096", 0.5, 0.15, ".jpg"),
        new("ambientcg-wood092", "Texture.AmbientWood092", 0.8, 0.15, ".jpg"),
        new("ambientcg-wood051", "Texture.AmbientWood051", 0.8, 0.15, ".jpg"),
        new("ambientcg-bricks105", "Texture.AmbientBricks105", 2.4, 0.3, ".jpg"),
        new("ambientcg-bricks113a", "Texture.AmbientBricks113A", 1.0, 0.3, ".jpg"),
        new("ambientcg-concrete046", "Texture.AmbientConcrete046", 2.4, 0.12, ".jpg"),
        new("ambientcg-concrete034", "Texture.AmbientConcrete034", 1.1, 0.12, ".jpg"),
        new("ambientcg-tiles141", "Texture.AmbientTiles141", 2.0, 0.2, ".jpg"),
        new("ambientcg-tiles107", "Texture.AmbientTiles107", 1.0, 0.2, ".jpg"),
        new("ambientcg-marble012", "Texture.AmbientMarble012", 1.0, 0.05, ".jpg"),
        new("ambientcg-plaster001", "Texture.AmbientPlaster001", 1.0, 0.1, ".jpg"),
        new("ambientcg-ground108", "Texture.AmbientGround108", 1.5, 0.25, ".jpg"),
        new("ambientcg-fabric085", "Texture.AmbientFabric085", 0.3, 0.1, ".jpg")
    }).Concat(TreeTextureGenerator.Catalog.Select(species =>
        new BuiltInTexture(species.Key, "Texture." + species.Key, species.Bark ? 1.2 : 0.8,
            species.Bark ? 0.75 : 0.22))).ToArray();

    /// <summary>The categories whose render material a project may replace, with their label resources.</summary>
    public static IReadOnlyList<(SurfaceKind Category, string LabelKey)> Categories { get; } =
    [
        (SurfaceKind.Wall, "Material.Walls"),
        (SurfaceKind.InteriorWall, "Material.InteriorWalls"),
        (SurfaceKind.Floor, "Material.Floors"),
        (SurfaceKind.Roof, "Material.Roof"),
        (SurfaceKind.Ground, "Material.Ground"),
        (SurfaceKind.Structure, "Material.Structure"),
        (SurfaceKind.DoorLeaf, "Material.Wood"),
        (SurfaceKind.Upholstery, "Material.Upholstery"),
        (SurfaceKind.Earth, "Material.Earth"),
        (SurfaceKind.Rock, "Material.Rock"),
        (SurfaceKind.WallConcrete, "Material.WallConcrete"),
        (SurfaceKind.WallWood, "Material.WallWood"),
        (SurfaceKind.WallBrick, "Material.WallBrick"),
        (SurfaceKind.WallStone, "Material.WallStone"),
        (SurfaceKind.Bark, "Material.Bark"),
        (SurfaceKind.Foliage, "Material.Foliage"),
        (SurfaceKind.Leaf, "Material.Leaf"),
        (SurfaceKind.Glass, "Material.Glass"),
        (SurfaceKind.OpeningFrame, "Material.Frames"),
        (SurfaceKind.Metal, "Material.Metal"),
        (SurfaceKind.Water, "Material.Water"),
        (SurfaceKind.Asphalt, "Material.Asphalt"),
        (SurfaceKind.Bedding, "Material.Bedding"),
        (SurfaceKind.Ceramic, "Material.Ceramic"),
        (SurfaceKind.CarPaint, "Material.CarPaint")
    ];

    private static readonly ConcurrentDictionary<string, (DateTime Stamp, RenderTexture Texture)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolves a preset or shipped texture, an absolute path, or a path relative to the project.</summary>
    public static string? Resolve(string texturePath, string? projectFile)
    {
        // Library identifiers are not file paths. Presets resolve to their shipped texture key when available.
        if (PbrMaterialPackages.IsPackage(texturePath))
        {
            var imported = PbrMaterialPackages.Read(PbrMaterialPackages.ResolveManifest(texturePath, projectFile));
            return imported?.AlbedoPath;
        }
        if (texturePath.StartsWith(MaterialPresets.Prefix, StringComparison.Ordinal))
        {
            var preset = MaterialPresets.FromPath(texturePath);
            return preset?.TextureKey is { } key ? Resolve(BuiltInPrefix + key, projectFile) : null;
        }
        if (texturePath.StartsWith(BuiltInPrefix, StringComparison.Ordinal))
        {
            var key = texturePath[BuiltInPrefix.Length..];
            if (TreeTextureGenerator.Contains(key)) return TreeTextureGenerator.Resolve(key);
            var builtIn = BuiltIn.FirstOrDefault(texture => texture.Key == key);
            return builtIn is null ? null : Path.Combine(AppContext.BaseDirectory, "resources", "textures", key + builtIn.Extension);
        }
        if (Path.IsPathRooted(texturePath)) return texturePath;
        var folder = projectFile is null ? null : Path.GetDirectoryName(Path.GetFullPath(projectFile));
        return folder is null ? null : Path.GetFullPath(Path.Combine(folder, texturePath));
    }

    /// <summary>
    /// Decodes an image (PNG, JPEG, BMP, TIFF, GIF) into a render texture, or returns null when it is missing or
    /// unreadable; the scene then keeps the stock material rather than failing. Safe on worker threads.
    /// </summary>
    public static RenderTexture? Load(string? path)
    {
        if (path is null || !File.Exists(path)) return null;
        try
        {
            var stamp = File.GetLastWriteTimeUtc(path);
            if (Cache.TryGetValue(path, out var cached) && cached.Stamp == stamp) return cached.Texture;
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource frame = decoder.Frames[0];
            // Very large photographs are reduced while decoding; the renderer keeps at most 2048 texels a side anyway.
            if (frame.PixelWidth > 4096 || frame.PixelHeight > 4096)
            {
                var factor = 4096.0 / Math.Max(frame.PixelWidth, frame.PixelHeight);
                frame = new TransformedBitmap(frame, new ScaleTransform(factor, factor));
            }
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            var texture = RenderTexture.FromBgra32(converted.PixelWidth, converted.PixelHeight, pixels);
            Cache[path] = (stamp, texture);
            return texture;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException
                                              or FileFormatException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static readonly ConcurrentDictionary<string, (DateTime Stamp, NormalMap Map)> NormalMaps = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, (DateTime Stamp, RenderScalarMap Map)> ScalarMaps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Decode linear RGB/gray map pixels, limited in size to protect CPU-only machines.</summary>
    private static (int Width, int Height, byte[] Bgra)? DecodePbrMap(string? path)
    {
        if (path is null || !File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource frame = decoder.Frames[0];
            if (frame.PixelWidth > 2048 || frame.PixelHeight > 2048)
            {
                var factor = 2048.0 / Math.Max(frame.PixelWidth, frame.PixelHeight);
                frame = new TransformedBitmap(frame, new ScaleTransform(factor, factor));
            }
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
            converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
            return (converted.PixelWidth, converted.PixelHeight, pixels);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException
                                              or FileFormatException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    public static NormalMap? LoadNormal(string? path)
    {
        if (path is null || !File.Exists(path)) return null;
        var stamp = File.GetLastWriteTimeUtc(path);
        if (NormalMaps.TryGetValue(path, out var cached) && cached.Stamp == stamp) return cached.Map;
        if (DecodePbrMap(path) is not { } image) return null;
        var rgb = new byte[image.Width * image.Height * 3];
        for (var index = 0; index < image.Width * image.Height; index++)
        {
            rgb[index * 3] = image.Bgra[index * 4 + 2];
            rgb[index * 3 + 1] = image.Bgra[index * 4 + 1];
            rgb[index * 3 + 2] = image.Bgra[index * 4];
        }
        var map = NormalMap.FromRgb(image.Width, image.Height, rgb);
        NormalMaps[path] = (stamp, map);
        return map;
    }

    /// <summary>Reads the red channel as raw linear grayscale, without converting sRGB; suitable for separate R/M maps.</summary>
    public static RenderScalarMap? LoadScalar(string? path)
    {
        if (path is null || !File.Exists(path)) return null;
        var stamp = File.GetLastWriteTimeUtc(path);
        if (ScalarMaps.TryGetValue(path, out var cached) && cached.Stamp == stamp) return cached.Map;
        if (DecodePbrMap(path) is not { } image) return null;
        var gray = new byte[image.Width * image.Height];
        for (var index = 0; index < gray.Length; index++) gray[index] = image.Bgra[index * 4 + 2];
        var map = RenderScalarMap.FromGray(image.Width, image.Height, gray);
        ScalarMaps[path] = (stamp, map);
        return map;
    }

    /// <summary>Optional OpenGL tangent normal beside an albedo image. Missing sidecars are free to ignore.</summary>
    private static NormalMap? LoadSidecarNormal(string? albedoPath)
    {
        if (string.IsNullOrWhiteSpace(albedoPath) || !File.Exists(albedoPath)) return null;
        var folder = Path.GetDirectoryName(albedoPath);
        var name = Path.GetFileNameWithoutExtension(albedoPath);
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(name)) return null;
        foreach (var suffix in new[] { "_NormalGL.png", "_normal.png", "_Normal.png", "_nrm.png" })
        {
            var sidecar = Path.Combine(folder, name + suffix);
            if (File.Exists(sidecar) && LoadNormal(sidecar) is { } result) return result;
        }
        return null;
    }

    private static readonly ConcurrentDictionary<string, (DateTime Stamp, BitmapSource Image)> Previews = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The images of the project's assignments, reduced to 1024 pixels, for the interactive 3D view. Missing or
    /// unreadable images are left out, so those surfaces keep their plain color.
    /// </summary>
    public static IReadOnlyDictionary<SurfaceKind, (ImageSource Image, double Size)> Previews3D(
        IReadOnlyList<RenderMaterialAssignment> assignments, string? projectFile)
    {
        var result = new Dictionary<SurfaceKind, (ImageSource, double)>();
        foreach (var assignment in assignments)
        {
            if (assignment.ElementId is not null) continue;
            // Presets store a material: identifier, not a filesystem path. Resolve the
            // preset's bundled texture so the interactive 3D viewport matches the render.
            var preset = MaterialPresets.FromPath(assignment.TexturePath);
            var path = PbrMaterialPackages.IsPackage(assignment.TexturePath)
                ? Resolve(assignment.TexturePath, projectFile)
                : preset is not null
                ? preset.TextureKey is { } key ? Resolve(BuiltInPrefix + key, projectFile) : null
                : assignment.TexturePath.StartsWith(MaterialPresets.Prefix, StringComparison.Ordinal)
                    ? null : Resolve(assignment.TexturePath, projectFile);
            if (path is null || !File.Exists(path)) continue;
            try
            {
                var stamp = File.GetLastWriteTimeUtc(path);
                if (!Previews.TryGetValue(path, out var cached) || cached.Stamp != stamp)
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = 1024;
                    image.UriSource = new Uri(path);
                    image.EndInit();
                    image.Freeze();
                    cached = (stamp, image);
                    Previews[path] = cached;
                }
                result[assignment.Category] = (cached.Image, assignment.TextureSize);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException
                                                  or FileFormatException or ArgumentException or InvalidOperationException)
            {
                // A broken image only loses its preview; the render reports the same file by falling back as well.
            }
        }
        return result;
    }

    /// <summary>
    /// Resolves per-element overrides while retaining project-wide category fallbacks.
    /// A scoped assignment affects only triangles owned by its BIM element.
    /// </summary>
    public static Func<Guid, SurfaceKind, RenderMaterial> ElementResolver(
        IReadOnlyList<RenderMaterialAssignment> assignments, string? projectFile, bool lightsOn = true)
    {
        var global = Resolver(assignments, projectFile, lightsOn);
        var scoped = new Dictionary<(Guid, SurfaceKind), RenderMaterial>();
        foreach (var assignment in assignments)
        {
            if (assignment.ElementId is not Guid id) continue;
            // Reuse the exact same loader for stock materials, library presets and local images.
            var only = Resolver([assignment with { ElementId = null }], projectFile, lightsOn);
            scoped[(id, assignment.Category)] = only(assignment.Category);
        }
        return (id, category) => scoped.TryGetValue((id, category), out var material) ? material : global(category);
    }

    /// <summary>
    /// The material of each surface category for the render: the project's texture where one is assigned and readable,
    /// the stock material everywhere else.
    /// </summary>
    public static Func<SurfaceKind, RenderMaterial> Resolver(IReadOnlyList<RenderMaterialAssignment> assignments, string? projectFile,
        bool lightsOn = true)
    {
        var textured = new Dictionary<SurfaceKind, RenderMaterial>();
        foreach (var assignment in assignments)
        {
            if (assignment.ElementId is not null) continue;
            RenderMaterial? material = null;
            if (PbrMaterialPackages.IsPackage(assignment.TexturePath))
                material = PbrMaterialPackages.Build(assignment.TexturePath, projectFile,
                    assignment.TextureSize, assignment.Relief);
            else if (MaterialPresets.FromPath(assignment.TexturePath) is { } preset)
            {
                var albedoPath = preset.TextureKey is { } key ? Resolve(BuiltInPrefix + key, projectFile) : null;
                var image = Load(albedoPath);
                material = MaterialPresets.Build(preset, image) with
                {
                    TextureSize = (float)assignment.TextureSize,
                    TextureRelief = (float)assignment.Relief,
                    NormalMap = LoadSidecarNormal(albedoPath) ?? preset.Material.NormalMap
                };
            }
            else if (assignment.TexturePath == "stock:")
            {
                material = MaterialLibrary.ForKind(assignment.Category) with
                {
                    TextureSize = (float)assignment.TextureSize,
                    TextureRelief = (float)assignment.Relief
                };
            }
            else if (assignment.Category != SurfaceKind.Glass &&
                     Load(Resolve(assignment.TexturePath, projectFile)) is { } texture)
            {
                material = MaterialLibrary.Textured(assignment.Category, texture, assignment.TextureSize, assignment.Relief)
                    with { NormalMap = LoadSidecarNormal(Resolve(assignment.TexturePath, projectFile)) };
            }
            if (material is null) continue;
            textured[assignment.Category] = MaterialTint.Apply(material, assignment.TintHex) with
            {
                Roughness = assignment.RoughnessOverride is { } roughness ? (float)roughness : material.Roughness,
                Metallic = assignment.MetallicOverride is { } metallic ? (float)metallic : material.Metallic,
                NormalMap = assignment.NormalPath is null ? material.NormalMap :
                    LoadNormal(Resolve(assignment.NormalPath, projectFile)),
                RoughnessMap = assignment.RoughnessPath is null ? material.RoughnessMap :
                    LoadScalar(Resolve(assignment.RoughnessPath, projectFile)),
                MetallicMap = assignment.MetallicPath is null ? material.MetallicMap :
                    LoadScalar(Resolve(assignment.MetallicPath, projectFile))
            };
        }
        // With the lights off a fixture is just its white diffuser.
        if (!lightsOn)
        {
            textured[SurfaceKind.LightEmitter] = MaterialLibrary.ForKind(SurfaceKind.LightEmitter) with { Emission = default };
            textured[SurfaceKind.SpotEmitter] = MaterialLibrary.ForKind(SurfaceKind.SpotEmitter) with { Emission = default };
        }
        return kind => textured.TryGetValue(kind, out var material) ? material : MaterialLibrary.ForKind(kind);
    }
}
