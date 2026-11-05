using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Guanacaste3D.Desktop;

/// <summary>A project the person opened or saved, as the Home screen lists it.</summary>
public sealed record RecentDocument(string Path, DateTime LastOpened);

/// <summary>
/// The Home screen's list of recent projects, kept per user under LocalApplicationData with a thumbnail of each one.
/// Reading never fails: a missing or damaged list starts empty.
/// </summary>
public sealed class RecentDocuments
{
    /// <summary>How many projects the list remembers.</summary>
    public const int Capacity = 40;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _folder;
    private readonly string _file;

    public RecentDocuments(string? folder = null)
    {
        _folder = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Guanacaste3D");
        _file = Path.Combine(_folder, "recent-projects.json");
    }

    /// <summary>The folder that holds the cached thumbnails.</summary>
    public string ThumbnailFolder => Path.Combine(_folder, "thumbnails");

    /// <summary>
    /// The list, most recently opened first. Stale entries are pruned automatically so Home never advertises a
    /// project that cannot be opened anymore.
    /// </summary>
    public IReadOnlyList<RecentDocument> Load()
    {
        try
        {
            if (!File.Exists(_file)) return [];

            var loaded = (JsonSerializer.Deserialize<List<RecentDocument>>(File.ReadAllText(_file), Options) ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item.Path))
                .OrderByDescending(item => item.LastOpened)
                .Take(Capacity)
                .ToArray();

            var existing = loaded.Where(item => File.Exists(item.Path)).ToArray();
            if (existing.Length != loaded.Length)
            {
                foreach (var stale in loaded.Where(item => !File.Exists(item.Path)))
                {
                    try { File.Delete(ThumbnailPath(stale.Path)); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
                }
                Save(existing);
            }

            return existing;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    /// <summary>Moves a project to the top of the list.</summary>
    public void Add(string path)
    {
        var full = Path.GetFullPath(path);
        Save(Load().Where(item => !SamePath(item.Path, full)).Prepend(new RecentDocument(full, DateTime.Now)).Take(Capacity));
    }

    /// <summary>Forgets a project (the file itself is untouched) and its thumbnail.</summary>
    public void Remove(string path)
    {
        Save(Load().Where(item => !SamePath(item.Path, path)));
        try { File.Delete(ThumbnailPath(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Where the thumbnail of a project is cached: a hash of its full path, so renaming a file starts afresh.</summary>
    public string ThumbnailPath(string path)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())))[..24];
        return Path.Combine(ThumbnailFolder, hash + "-v4.png");
    }

    private void Save(IEnumerable<RecentDocument> items)
    {
        try
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(_file, JsonSerializer.Serialize(items.ToList(), Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
