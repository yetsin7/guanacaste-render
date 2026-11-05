using System.Text.Json;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// Creates a shareable .g3dm folder package without any WPF, Skia or Windows-only dependency.
/// Every imported channel remains a separate original raster image (no lossy recompression).
/// The renderer is responsible for decoding and displaying the bitmaps.
/// </summary>
public static class PbrMaterialPackageWriter
{
    public sealed record Options(
        string Name,
        string Category = "General",
        string Description = "",
        string Author = "",
        string License = "",
        double Roughness = .7,
        double Metallic = 0,
        double Ior = 1.5,
        double TextureSize = 1,
        double Relief = .2,
        IReadOnlyList<string>? Tags = null);

    /// <summary>
    /// Maps: albedo (required), normal, roughnessMap, metallicMap, heightMap.
    /// Writes all images next to the manifest, then writes the manifest last.
    /// Never overwrites an existing output, and rolls back files created on failure.
    /// </summary>
    public static string Create(string manifestPath, Options options, IReadOnlyDictionary<string, string> maps)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(maps);
        if (!string.Equals(Path.GetExtension(manifestPath), ".g3dm", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The destination must have a .g3dm extension.", nameof(manifestPath));
        if (string.IsNullOrWhiteSpace(options.Name) || options.Name.Length > 120 ||
            options.Category.Length > 80 || options.Description.Length > 1000 ||
            options.Author.Length > 120 || options.License.Length > 120 ||
            !double.IsFinite(options.Roughness) || options.Roughness is < .02 or > 1 ||
            !double.IsFinite(options.Metallic) || options.Metallic is < 0 or > 1 ||
            !double.IsFinite(options.Ior) || options.Ior is < 1 or > 3 ||
            !double.IsFinite(options.TextureSize) || options.TextureSize is < .01 or > 1000 ||
            !double.IsFinite(options.Relief) || options.Relief is < 0 or > 1 ||
            options.Tags is { Count: > 24 } ||
            options.Tags?.Any(tag => string.IsNullOrWhiteSpace(tag) || tag.Length > 48) == true)
            throw new ArgumentException("Invalid material properties.", nameof(options));

        var acceptedChannels = new[] { "albedo", "normal", "roughnessMap", "metallicMap", "heightMap" };
        if (!maps.ContainsKey("albedo") || maps.Keys.Any(key => !acceptedChannels.Contains(key, StringComparer.Ordinal)))
            throw new ArgumentException("Map channels must be albedo, normal, roughnessMap, metallicMap or heightMap.", nameof(maps));

        var destination = Path.GetFullPath(manifestPath);
        var directory = Path.GetDirectoryName(destination)!;
        var stem = Path.GetFileNameWithoutExtension(destination);
        var inputs = new Dictionary<string, (string Source, string FileName)>();
        foreach (var channel in acceptedChannels)
        {
            if (!maps.TryGetValue(channel, out var input)) continue;
            if (!File.Exists(input)) throw new FileNotFoundException("Texture image is missing.", input);
            var file = new FileInfo(input);
            if (file.LinkTarget is not null)
                throw new InvalidDataException("Symbolic-link textures cannot be packed.");
            if (file.Length is < 1 or > 134217728)
                throw new InvalidDataException("Texture size must be between 1 byte and 128 MiB.");
            var extension = file.Extension.ToLowerInvariant();
            if (!new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" }.Contains(extension))
                throw new InvalidDataException("Texture file format is not supported.");
            var suffix = channel switch
            {
                "roughnessMap" => "roughness",
                "metallicMap" => "metallic",
                "heightMap" => "height",
                _ => channel
            };
            inputs[channel] = (file.FullName, stem + "-" + suffix + extension);
        }

        Directory.CreateDirectory(directory);
        var copies = inputs.ToDictionary(item => item.Key,
            item => Path.Combine(directory, item.Value.FileName));
        if (File.Exists(destination) || copies.Values.Any(File.Exists))
            throw new IOException("A .g3dm package or one of its texture files already exists.");

        var manifest = new Dictionary<string, object?>
        {
            ["version"] = 1,
            ["name"] = options.Name.Trim(),
            ["category"] = options.Category,
            ["description"] = options.Description,
            ["author"] = options.Author,
            ["license"] = options.License,
            ["tags"] = options.Tags ?? [],
            ["roughness"] = options.Roughness,
            ["metallic"] = options.Metallic,
            ["ior"] = options.Ior,
            ["textureSize"] = options.TextureSize,
            ["relief"] = options.Relief
        };
        foreach (var (channel, source) in inputs) manifest[channel] = source.FileName;
        var written = new List<string>();
        try
        {
            foreach (var (channel, source) in inputs)
            {
                var target = copies[channel];
                // FileMode.CreateNew avoids silent overwrite even when another process races the validation.
                using var input = File.OpenRead(source.Source);
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                written.Add(target);
                input.CopyTo(output);
            }
            using (var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                written.Add(destination);
                JsonSerializer.Serialize(stream, manifest, new JsonSerializerOptions { WriteIndented = true });
            }
            var inspection = PbrMaterialAudit.Inspect(destination);
            if (!inspection.IsValid)
                throw new InvalidDataException("Created package failed validation: " +
                    string.Join("; ", inspection.Errors));
            return destination;
        }
        catch
        {
            foreach (var path in written)
            {
                try { if (File.Exists(path)) File.Delete(path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            throw;
        }
    }
}
