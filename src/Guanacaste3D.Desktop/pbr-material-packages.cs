using System.IO;
using System.Text.Json;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Portable PBR package manifest for base colour and optional independently sampled normal,
/// roughness, metallic and grayscale height channels.
/// </summary>
internal sealed record PbrMaterialManifest
{
    public int Version { get; init; } = 1;
    public string Name { get; init; } = "Material PBR";
    public string Category { get; init; } = "General";
    public string Description { get; init; } = "";
    public string Author { get; init; } = "";
    public string License { get; init; } = "";
    public string[] Tags { get; init; } = [];
    public string Albedo { get; init; } = "";
    public string? Normal { get; init; }
    public string? RoughnessMap { get; init; }
    public string? MetallicMap { get; init; }
    public string? HeightMap { get; init; }
    public double Roughness { get; init; } = 0.7;
    public double Metallic { get; init; }
    public double Ior { get; init; } = 1.5;
    public double TextureSize { get; init; } = 1;
    public double Relief { get; init; } = 0.2;
}

/// <summary>
/// Safe local importer for .g3dm JSON sidecars. Rejects paths escaping the manifest directory;
/// projects retain the manifest path so a material can be changed without embedding large images.
/// </summary>
internal static class PbrMaterialPackages
{
    public const string Prefix = "pbr:";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static bool IsPackage(string? path) =>
        path?.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) == true;

    public static string? ResolveManifest(string assignment, string? projectFile)
    {
        if (!IsPackage(assignment)) return null;
        var raw = assignment[Prefix.Length..];
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            if (Path.IsPathRooted(raw)) return Path.GetFullPath(raw);
            var projectFolder = projectFile is null ? null : Path.GetDirectoryName(Path.GetFullPath(projectFile));
            return projectFolder is null ? null : Path.GetFullPath(Path.Combine(projectFolder, raw));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    public static (PbrMaterialManifest Manifest, string AlbedoPath)? Read(string? path)
    {
        if (path is null || !File.Exists(path) ||
            !string.Equals(Path.GetExtension(path), ".g3dm", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            // Portable validation also blocks unsafe subdirectories, oversized assets and symlink escapes.
            // Keep the same contract on Windows, Linux and macOS before invoking any image decoder.
            if (!PbrMaterialAudit.Inspect(path).IsValid) return null;
            if (new FileInfo(path).Length > 65536) return null;
            var data = File.ReadAllText(path);
            var manifest = JsonSerializer.Deserialize<PbrMaterialManifest>(data, JsonOptions);
            if (manifest is null || manifest.Version != 1 ||
                string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 120 ||
                string.IsNullOrWhiteSpace(manifest.Albedo) || Path.IsPathRooted(manifest.Albedo) ||
                !double.IsFinite(manifest.Roughness) || manifest.Roughness is < 0.02 or > 1 ||
                !double.IsFinite(manifest.Metallic) || manifest.Metallic is < 0 or > 1 ||
                !double.IsFinite(manifest.Ior) || manifest.Ior is < 1 or > 3 ||
                !double.IsFinite(manifest.TextureSize) || manifest.TextureSize is < 0.01 or > 1000 ||
                !double.IsFinite(manifest.Relief) || manifest.Relief is < 0 or > 1)
                return null;
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            var image = SafeImagePath(directory, manifest.Albedo);
            if (image is null ||
                (manifest.Normal is not null && SafeImagePath(directory, manifest.Normal) is null) ||
                (manifest.RoughnessMap is not null && SafeImagePath(directory, manifest.RoughnessMap) is null) ||
                (manifest.MetallicMap is not null && SafeImagePath(directory, manifest.MetallicMap) is null) ||
                (manifest.HeightMap is not null && SafeImagePath(directory, manifest.HeightMap) is null))
                return null;
            return (manifest, image);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? SafeImagePath(string directory, string relativeName)
    {
        if (string.IsNullOrWhiteSpace(relativeName) || Path.IsPathRooted(relativeName)) return null;
        var image = Path.GetFullPath(Path.Combine(directory, relativeName));
        var relative = Path.GetRelativePath(directory, image);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relative) ||
            !new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" }
                .Contains(Path.GetExtension(image), StringComparer.OrdinalIgnoreCase) ||
            !File.Exists(image)) return null;
        return image;
    }

    /// <summary>
    /// Detects a conventional PBR texture set next to a selected base-color image.
    /// Only exact stem + known channel suffixes count: unrelated images are never guessed.
    /// Results are keyed by normal, roughnessMap, metallicMap and heightMap.
    /// </summary>
    public static IReadOnlyDictionary<string, string> DiscoverCompanionMaps(string albedoPath)
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(albedoPath) || !File.Exists(albedoPath))
            return found;
        var directory = Path.GetDirectoryName(Path.GetFullPath(albedoPath))!;
        var stem = Path.GetFileNameWithoutExtension(albedoPath);
        var colorSuffixes = new[] { "_basecolor", "_base_color", "_albedo", "_diffuse", "_color", "_colour", "-basecolor", "-albedo", "-diffuse" };
        foreach (var suffix in colorSuffixes.OrderByDescending(item => item.Length))
            if (stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                stem = stem[..^suffix.Length];
                break;
            }
        var channelSuffixes = new (string Channel, string[] Suffixes)[]
        {
            ("normal", ["_normal", "_normalgl", "_normal_gl", "_nor", "_nrm", "-normal"]),
            ("roughnessMap", ["_roughness", "_rough", "-roughness"]),
            ("metallicMap", ["_metallic", "_metalness", "_metal", "-metallic"]),
            ("heightMap", ["_height", "_displacement", "_disp", "_bump", "-height", "-displacement"])
        };
        var candidates = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" }
                .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Take(500).ToArray();
        foreach (var (channel, suffixes) in channelSuffixes)
            foreach (var suffix in suffixes)
            {
                var match = candidates.FirstOrDefault(path =>
                    string.Equals(Path.GetFileNameWithoutExtension(path), stem + suffix,
                        StringComparison.OrdinalIgnoreCase));
                if (match is null) continue;
                found[channel] = match;
                break;
            }
        return found;
    }

    /// <summary>
    /// Creates a self-contained folder material from one raster photograph. Both files
    /// use the same basename and existing packages are never overwritten silently.
    /// </summary>
    public static string CreateFromImage(string sourceImage, string destinationManifest,
        string name, double roughness, double metallic, double size, double relief)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120 ||
            !double.IsFinite(roughness) || roughness is < 0.02 or > 1 ||
            !double.IsFinite(metallic) || metallic is < 0 or > 1 ||
            !double.IsFinite(size) || size is < 0.01 or > 1000 ||
            !double.IsFinite(relief) || relief is < 0 or > 1)
            throw new ArgumentException("Invalid PBR material parameters.");
        if (!File.Exists(sourceImage) ||
            !new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" }
                .Contains(Path.GetExtension(sourceImage), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("The selected PBR albedo image is invalid.");
        if (!string.Equals(Path.GetExtension(destinationManifest), ".g3dm",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Material packages must have a .g3dm extension.");
        var destination = Path.GetFullPath(destinationManifest);
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        if (File.Exists(destination))
            throw new IOException("The material manifest already exists.");
        var imageName = Path.GetFileNameWithoutExtension(destination) + "-albedo" +
            Path.GetExtension(sourceImage).ToLowerInvariant();
        var targetImage = Path.Combine(directory, imageName);
        if (File.Exists(targetImage))
            throw new IOException("The material albedo already exists.");
        // Validate that source is decodable before copying. The renderer will use its
        // existing WPF decoder and downsampling limits after the import.
        if (RenderTextureLibrary.Load(sourceImage) is null)
            throw new InvalidDataException("The selected PBR image cannot be decoded.");
        var companions = DiscoverCompanionMaps(sourceImage);
        var copied = new List<string>();
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (channel, path) in companions)
        {
            if ((channel == "normal" && RenderTextureLibrary.LoadNormal(path) is null) ||
                (channel != "normal" && RenderTextureLibrary.LoadScalar(path) is null))
                throw new InvalidDataException("One of the PBR companion maps cannot be decoded.");
            var suffix = channel switch
            {
                "normal" => "-normal",
                "roughnessMap" => "-roughness",
                "heightMap" => "-height",
                _ => "-metallic"
            };
            var filename = Path.GetFileNameWithoutExtension(destination) + suffix +
                Path.GetExtension(path).ToLowerInvariant();
            if (File.Exists(Path.Combine(directory, filename)))
                throw new IOException("A destination PBR companion map already exists.");
            names[channel] = filename;
        }
        var manifest = new PbrMaterialManifest
        {
            Name = name.Trim(), Albedo = imageName,
            Normal = names.GetValueOrDefault("normal"),
            RoughnessMap = names.GetValueOrDefault("roughnessMap"),
            MetallicMap = names.GetValueOrDefault("metallicMap"),
            HeightMap = names.GetValueOrDefault("heightMap"),
            Roughness = roughness, Metallic = metallic,
            TextureSize = size, Relief = relief
        };
        try
        {
            File.Copy(sourceImage, targetImage);
            copied.Add(targetImage);
            foreach (var (channel, path) in companions)
            {
                var file = Path.Combine(directory, names[channel]);
                File.Copy(path, file);
                copied.Add(file);
            }
            var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
            using var writer = new StreamWriter(stream);
            writer.Write(json);
            return destination;
        }
        catch
        {
            foreach (var copy in copied)
                File.Delete(copy);
            // Never delete the target manifest: another process may have created it.
            throw;
        }
    }

    public static RenderMaterial? Build(string assignment, string? projectFile, double size, double relief)
    {
        var imported = Read(ResolveManifest(assignment, projectFile));
        if (imported is not { } package) return null;
        var texture = RenderTextureLibrary.Load(package.AlbedoPath);
        if (texture is null) return null;
        var manifest = package.Manifest;
        var folder = Path.GetDirectoryName(ResolveManifest(assignment, projectFile)!)!;
        return new RenderMaterial
        {
            Name = manifest.Name, Texture = texture,
            TextureSize = (float)size, TextureRelief = (float)relief,
            Roughness = (float)manifest.Roughness, Metallic = (float)manifest.Metallic,
            Ior = (float)manifest.Ior, BaseColor = System.Numerics.Vector3.One,
            NormalMap = manifest.Normal is null ? null : RenderTextureLibrary.LoadNormal(
                SafeImagePath(folder, manifest.Normal)),
            RoughnessMap = manifest.RoughnessMap is null ? null : RenderTextureLibrary.LoadScalar(
                SafeImagePath(folder, manifest.RoughnessMap)),
            MetallicMap = manifest.MetallicMap is null ? null : RenderTextureLibrary.LoadScalar(
                SafeImagePath(folder, manifest.MetallicMap)),
            HeightMap = manifest.HeightMap is null ? null : RenderTextureLibrary.LoadScalar(
                SafeImagePath(folder, manifest.HeightMap))
        }.Sanitized();
    }
}
