namespace Guanacaste3D.Core;

/// <summary>A family file found in a shared library folder.</summary>
public sealed record LibraryFamily(string Path, string Name, string Kind, long Bytes, DateTime ModifiedUtc);

/// <summary>
/// A shared library of families kept in a folder, which can be one that a cloud service, a server or a team keeps in step on every
/// computer. It lists the .g3df files in it that can be read and adds new ones to it.
/// </summary>
public static class FamilyLibrary
{
    public const int MaximumFiles = 2000;
    private const int MaximumDepth = 3;

    /// <summary>Whether the file is a family this version can read: a native family, or a door or window family.</summary>
    public static string? KindOf(string path)
    {
        try
        {
            if (NativeDocumentStore.TryInspect(path, out var info) && info?.Kind == NativeDocumentKind.Family) return "Native";
            if (FamilyFile.IsFamilyPath(path)) { FamilyFile.Load(path); return "Opening"; }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException) { }
        return null;
    }

    /// <summary>The readable families in a folder and the folders under it (three levels), by name; files that cannot be read are left out.</summary>
    public static IReadOnlyList<LibraryFamily> Scan(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) throw new InvalidDataException("Choose a folder.");
        if (!Directory.Exists(folder)) throw new InvalidDataException("The folder does not exist.");
        var found = new List<LibraryFamily>();
        void Visit(string directory, int depth)
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(directory, "*" + FamilyFile.Extension); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return; }
            foreach (var path in files)
            {
                if (found.Count >= MaximumFiles) return;
                if (KindOf(path) is not { } kind) continue;
                var info = new FileInfo(path);
                found.Add(new LibraryFamily(path, Path.GetFileNameWithoutExtension(path), kind, info.Length, info.LastWriteTimeUtc));
            }
            if (depth >= MaximumDepth) return;
            try
            {
                foreach (var child in Directory.EnumerateDirectories(directory).Order(StringComparer.OrdinalIgnoreCase)) Visit(child, depth + 1);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        Visit(folder, 0);
        return found.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Copies a family into the library under its own name (or the next free one) after checking that it can be read; returns where it went.</summary>
    public static string Publish(string source, string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) throw new InvalidDataException("Choose a folder.");
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source) || KindOf(source) is null) throw new InvalidDataException("The family file is not valid.");
        Directory.CreateDirectory(folder);
        var stem = Path.GetFileNameWithoutExtension(source);
        var target = Path.Combine(folder, stem + FamilyFile.Extension);
        for (var number = 2; File.Exists(target); number++) target = Path.Combine(folder, $"{stem} {number}{FamilyFile.Extension}");
        File.Copy(source, target);
        return target;
    }
}
