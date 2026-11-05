using System.Text.Json;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// Portable, headless inspection of Guanacaste material packages (.g3dm).
/// This only validates metadata, paths and file limits; raster decoding belongs to the renderer.
/// </summary>
public static class PbrMaterialAudit
{
    private const long MaxManifestBytes = 64 * 1024;
    private const long MaxTextureBytes = 128L * 1024 * 1024;
    private static readonly HashSet<string> SupportedImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" };

    public sealed record MaterialInfo(string Path, string Name, string Category, string Description,
        string Author, string License, IReadOnlyList<string> Tags,
        IReadOnlyDictionary<string, string> Maps, double Roughness, double Metallic,
        double Ior, double TextureSize, double Relief);

    public sealed record Inspection(string Path, MaterialInfo? Material, IReadOnlyList<string> Errors,
        IReadOnlyList<string> Warnings)
    {
        public bool IsValid => Material is not null && Errors.Count == 0;
    }

    /// <summary>Inspects every .g3dm in a folder without reading any texture bytes into memory.</summary>
    public static IReadOnlyList<Inspection> Scan(string directory, int maxMaterials = 1000)
    {
        if (maxMaterials is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(maxMaterials));
        if (!Directory.Exists(directory)) return [];
        try
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => string.Equals(System.IO.Path.GetExtension(path), ".g3dm", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Take(maxMaterials)
                .Select(Inspect)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [new Inspection(directory, null, ["Unable to access the material library: " + ex.Message], [])];
        }
    }

    public static Inspection Inspect(string path)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var fullPath = path;
        try
        {
            fullPath = System.IO.Path.GetFullPath(path);
            var file = new FileInfo(fullPath);
            if (!string.Equals(file.Extension, ".g3dm", StringComparison.OrdinalIgnoreCase))
                return new Inspection(fullPath, null, ["The manifest extension must be .g3dm."], []);
            if (!file.Exists)
                return new Inspection(fullPath, null, ["The material manifest is missing."], []);
            if (file.Length > MaxManifestBytes)
                return new Inspection(fullPath, null, ["The manifest is larger than 64 KiB."], []);
            if (file.LinkTarget is not null)
                return new Inspection(fullPath, null, ["Symbolic-link manifests are not allowed."], []);

            using var source = File.OpenRead(fullPath);
            using var json = JsonDocument.Parse(source, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 12
            });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new Inspection(fullPath, null, ["The manifest must be a JSON object."], []);
            // Duplicate property names with different casing are ambiguous between old and new
            // JSON serializers. Reject them so auditing and subsequent rendering agree exactly.
            if (root.EnumerateObject().Select(property => property.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != root.EnumerateObject().Count())
                errors.Add("Duplicate material properties are not allowed.");

            var version = GetNumber(root, "version", 1);
            if (version != 1) errors.Add("Unsupported .g3dm version.");
            var name = GetString(root, "name") ?? "";
            if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
                errors.Add("Material name must contain 1–120 characters.");
            var category = GetString(root, "category") ?? "General";
            var description = GetString(root, "description") ?? "";
            var author = GetString(root, "author") ?? "";
            var license = GetString(root, "license") ?? "";
            if (category.Length > 80 || description.Length > 1000 ||
                author.Length > 120 || license.Length > 120)
                errors.Add("Material metadata exceeds the supported length.");
            var tags = ReadTags(root, errors);
            var roughness = GetNumber(root, "roughness", .7);
            var metallic = GetNumber(root, "metallic", 0);
            var ior = GetNumber(root, "ior", 1.5);
            var size = GetNumber(root, "textureSize", 1);
            var relief = GetNumber(root, "relief", .2);
            if (!ValidRange(roughness, .02, 1) || !ValidRange(metallic, 0, 1) ||
                !ValidRange(ior, 1, 3) || !ValidRange(size, .01, 1000) ||
                !ValidRange(relief, 0, 1))
                errors.Add("PBR material properties are outside the supported physical ranges.");

            var maps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var parent = System.IO.Path.GetDirectoryName(fullPath)!;
            foreach (var (channel, required) in new[]
                { ("albedo", true), ("normal", false), ("roughnessMap", false), ("metallicMap", false), ("heightMap", false) })
            {
                var mapName = GetString(root, channel);
                if (string.IsNullOrWhiteSpace(mapName))
                {
                    if (required) errors.Add("Missing required albedo image.");
                    continue;
                }
                if (!TrySecureMapPath(parent, mapName, out var resolved, out var reason))
                {
                    errors.Add(channel + ": " + reason);
                    continue;
                }
                maps[channel] = resolved!;
            }
            if (maps.Count == 1)
                warnings.Add("Only albedo supplied: normal, roughness, metallic and height values are uniform or procedural.");
            foreach (var field in new[] { "ao", "displacementMap", "opacityMap", "emissionMap" })
                if (TryGet(root, field, out var unsupported) &&
                    unsupported.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
                    warnings.Add(field + " is reserved metadata and is not connected to the renderer.");

            var material = errors.Count == 0 ? new MaterialInfo(fullPath, name.Trim(), category.Trim(),
                description, author, license, tags, maps, roughness, metallic, ior, size, relief) : null;
            return new Inspection(fullPath, material, errors, warnings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
                                    ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return new Inspection(fullPath, null, ["Material cannot be read: " + ex.Message], []);
        }
    }

    private static IReadOnlyList<string> ReadTags(JsonElement root, List<string> errors)
    {
        if (!TryGet(root, "tags", out var raw) || raw.ValueKind == JsonValueKind.Null) return [];
        if (raw.ValueKind != JsonValueKind.Array || raw.GetArrayLength() > 24)
        {
            errors.Add("Tags must be an array containing at most 24 strings.");
            return [];
        }
        var result = new List<string>();
        foreach (var item in raw.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String ||
                item.GetString() is not string tag || tag.Length is < 1 or > 48)
                errors.Add("Each material tag must be a string of 1–48 characters.");
            else if (!result.Contains(tag, StringComparer.OrdinalIgnoreCase))
                result.Add(tag);
        }
        return result;
    }

    // Version 1 packages created by the original WPF wizard use PascalCase JSON properties.
    // Hand-authored .g3dm manifests normally use camelCase. Both are native v1.
    private static bool TryGet(JsonElement root, string key, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase)) continue;
            value = property.Value;
            return true;
        }
        value = default;
        return false;
    }

    private static string? GetString(JsonElement root, string key)
    {
        if (!TryGet(root, key, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new JsonException("Expected a string for " + key);
        return value.GetString();
    }

    private static double GetNumber(JsonElement root, string key, double fallback)
    {
        if (!TryGet(root, key, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number))
            throw new JsonException("Expected a number for " + key);
        return number;
    }

    private static bool ValidRange(double number, double low, double high) =>
        double.IsFinite(number) && number >= low && number <= high;

    private static bool TrySecureMapPath(string directory, string relativeName, out string? result,
        out string error)
    {
        result = null;
        error = "Invalid map path.";
        if (string.IsNullOrWhiteSpace(relativeName) ||
            relativeName.Contains('\\') || relativeName.Contains(':') ||
            System.IO.Path.IsPathRooted(relativeName))
            return false;
        var segments = relativeName.Split('/');
        if (segments.Any(segment => segment is "" or "." or "..")) return false;
        try
        {
            var current = directory;
            foreach (var segment in segments)
            {
                current = System.IO.Path.Combine(current, segment);
                if (Directory.Exists(current) && new DirectoryInfo(current).LinkTarget is not null)
                {
                    error = "A symbolic-link directory is not allowed.";
                    return false;
                }
                if (File.Exists(current) && new FileInfo(current).LinkTarget is not null)
                {
                    error = "A symbolic-link texture is not allowed.";
                    return false;
                }
            }
            var path = System.IO.Path.GetFullPath(current);
            var relative = System.IO.Path.GetRelativePath(directory, path);
            if (relative == ".." || relative.StartsWith(".." + System.IO.Path.DirectorySeparatorChar,
                    StringComparison.Ordinal) || System.IO.Path.IsPathRooted(relative))
                return false;
            if (!SupportedImageExtensions.Contains(System.IO.Path.GetExtension(path)))
            {
                error = "Unsupported image extension.";
                return false;
            }
            var texture = new FileInfo(path);
            if (!texture.Exists)
            {
                error = "Referenced image is missing.";
                return false;
            }
            if (texture.Length == 0 || texture.Length > MaxTextureBytes)
            {
                error = "Image is empty or exceeds the 128 MiB import limit.";
                return false;
            }
            result = path;
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            error = ex.Message;
            return false;
        }
    }
}
