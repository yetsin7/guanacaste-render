using System.Text.Json;

namespace Guanacaste3D.Core;

/// <summary>One automatic copy of a project that was being edited, waiting to be recovered or discarded.</summary>
public sealed record RecoveryCopy(Guid DocumentId, string Name, string? OriginalPath, DateTime SavedAtUtc, string DocumentPath);

/// <summary>
/// Keeps an automatic copy of the project being edited, so a crash, a power cut or a killed process loses seconds of
/// work, not hours. A copy is a normal checksummed .g3d replaced atomically beside a small description, one pair per
/// project, so a half-written copy never hides the previous complete one. Saving the project or closing it on purpose
/// discards its copy.
/// </summary>
public static class AutoRecovery
{
    /// <summary>The folder the tests and a portable install can redirect with this environment variable.</summary>
    public const string DirectoryVariable = "G3D_RECOVERY_DIR";

    private sealed record Description(Guid DocumentId, string Name, string? OriginalPath, DateTime SavedAtUtc);

    /// <summary>Where copies live: the redirected folder, or the user's local application data.</summary>
    public static string DefaultDirectory =>
        Environment.GetEnvironmentVariable(DirectoryVariable) is { Length: > 0 } redirected
            ? redirected
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Guanacaste 3D", "Recovery");

    /// <summary>Writes the copy of a project, replacing its previous copy only once the new one is complete.</summary>
    public static void Save(BuildingDocument document, string? originalPath, string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        directory ??= DefaultDirectory;
        Directory.CreateDirectory(directory);
        ProjectFile.Save(DocumentFile(directory, document.Id), document);

        var description = JsonSerializer.Serialize(new Description(document.Id, document.Name, originalPath, DateTime.UtcNow));
        var target = DescriptionFile(directory, document.Id);
        var temporary = target + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(temporary, description);
        File.Move(temporary, target, overwrite: true);
    }

    /// <summary>Lists the copies in a folder, newest first. Descriptions that cannot be read are skipped.</summary>
    public static IReadOnlyList<RecoveryCopy> Find(string? directory = null)
    {
        directory ??= DefaultDirectory;
        if (!Directory.Exists(directory)) return [];
        var copies = new List<RecoveryCopy>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.recovery.json"))
        {
            try
            {
                var description = JsonSerializer.Deserialize<Description>(File.ReadAllText(file));
                if (description is null || description.DocumentId == Guid.Empty) continue;
                var documentPath = DocumentFile(directory, description.DocumentId);
                if (!File.Exists(documentPath)) continue;
                copies.Add(new RecoveryCopy(description.DocumentId, description.Name, description.OriginalPath,
                    description.SavedAtUtc, documentPath));
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                // A damaged description only hides its own copy.
            }
        }
        return copies.OrderByDescending(copy => copy.SavedAtUtc).ToArray();
    }

    /// <summary>Opens a copy through the normal project loader, so it is validated and migrated like any project.</summary>
    public static BuildingDocument Load(RecoveryCopy copy)
    {
        ArgumentNullException.ThrowIfNull(copy);
        return ProjectFile.Load(copy.DocumentPath);
    }

    /// <summary>Removes the copy of one project, including the loader's backup beside it.</summary>
    public static void Discard(Guid documentId, string? directory = null)
    {
        directory ??= DefaultDirectory;
        var document = DocumentFile(directory, documentId);
        foreach (var path in new[] { document, document + ".bak", DescriptionFile(directory, documentId) })
        {
            try { File.Delete(path); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A copy that cannot be removed now is offered again, which is safer than losing it.
            }
        }
    }

    private static string DocumentFile(string directory, Guid id) => Path.Combine(directory, $"{id:N}.recovery.g3d");

    private static string DescriptionFile(string directory, Guid id) => Path.Combine(directory, $"{id:N}.recovery.json");
}
