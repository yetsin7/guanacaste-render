using System.IO;
using System.Text.Json;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Small per-user index of explicitly imported .g3dm manifests, plus packages referenced by the active project.
/// Only validated manifests are offered. It never scans arbitrary directories or copies user assets.
/// </summary>
internal static class PbrGalleryLibrary
{
    internal sealed record Entry(string Key, string Name, string AlbedoPath, double Size,
        double Relief, double Roughness, double Metallic);

    private const int MaxEntries = 64;
    private static readonly object Gate = new();
    private static string IndexPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Guanacaste3D", "imported-pbr-library-v1.json");

    private static string[] ReadIndex()
    {
        try
        {
            if (!File.Exists(IndexPath) || new FileInfo(IndexPath).Length > 32768) return [];
            return (JsonSerializer.Deserialize<string[]>(File.ReadAllText(IndexPath)) ?? [])
                .Where(path => !string.IsNullOrWhiteSpace(path) && path.Length <= 2048)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxEntries).ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                      JsonException or ArgumentException or NotSupportedException)
        {
            return [];
        }
    }

    /// <summary>Remember only an explicitly chosen valid package; no recursive search or bulk import.</summary>
    internal static bool Remember(string manifestPath)
    {
        var imported = PbrMaterialPackages.Read(manifestPath);
        if (imported is null) return false;
        try
        {
            var path = Path.GetFullPath(manifestPath);
            lock (Gate)
            {
                var entries = ReadIndex().Where(existing =>
                    !string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
                    .Prepend(path).Take(MaxEntries).ToArray();
                var folder = Path.GetDirectoryName(IndexPath)!;
                Directory.CreateDirectory(folder);
                var temp = IndexPath + ".tmp";
                try
                {
                    File.WriteAllText(temp, JsonSerializer.Serialize(entries));
                    File.Move(temp, IndexPath, overwrite: true);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                      ArgumentException or NotSupportedException)
        {
            // The session may still use a material even if a locked-down account cannot save a recent list.
            return false;
        }
    }

    /// <summary>Read up to 64 known files. The returned paths refer to the original assets and are not rewritten.</summary>
    internal static IReadOnlyList<Entry> Entries(
        IReadOnlyList<RenderMaterialAssignment>? assignments = null, string? projectFile = null)
    {
        string[] indexed;
        lock (Gate) indexed = ReadIndex();
        var active = (assignments ?? []).Where(a => PbrMaterialPackages.IsPackage(a.TexturePath))
            .Select(a => PbrMaterialPackages.ResolveManifest(a.TexturePath, projectFile))
            .OfType<string>();
        var paths = active.Concat(indexed).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxEntries);
        var found = new List<Entry>();
        foreach (var path in paths)
        {
            var imported = PbrMaterialPackages.Read(path);
            if (imported is not { } item) continue;
            var manifest = item.Manifest;
            found.Add(new Entry(PbrMaterialPackages.Prefix + Path.GetFullPath(path),
                manifest.Name, item.AlbedoPath, manifest.TextureSize, manifest.Relief,
                manifest.Roughness, manifest.Metallic));
        }
        return found;
    }
}
