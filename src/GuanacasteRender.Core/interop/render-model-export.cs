namespace Guanacaste3D.Core.Interop;

/// <summary>What writing a render export did with a file that was already there.</summary>
public enum RenderExportOutcome
{
    /// <summary>A new export was written, or an unrelated file was replaced.</summary>
    Written,

    /// <summary>The export already held render work, which was kept while the model was brought up to date.</summary>
    MergedWithRenderWork
}

/// <summary>
/// Writes the project snapshot the modeling application hands to the render application, together with the model
/// link that lets the render application fetch later model changes without losing its own work.
/// </summary>
public static class RenderModelExport
{
    /// <summary>
    /// Saves <paramref name="snapshot"/> at <paramref name="exportPath"/> with its model link. When the file is an
    /// earlier export of the same project that the render application has worked on, the new model is merged into
    /// it instead of replacing it. <paramref name="modelPath"/> is the saved model the snapshot was taken from.
    /// </summary>
    public static RenderExportOutcome Write(string exportPath, BuildingDocument snapshot, string? modelPath)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var outcome = RenderExportOutcome.Written;
        var document = snapshot;
        if (ExistingRenderWork(exportPath, snapshot.Id) is var (existing, baseline))
        {
            document = RenderModelUpdate.Merge(baseline, existing, snapshot).Document;
            outcome = RenderExportOutcome.MergedWithRenderWork;
        }
        var token = Guid.NewGuid().ToString("N");
        document.Extensions[RenderModelUpdate.SyncTokenKey] = token;
        ProjectFile.Save(exportPath, document);
        var link = new RenderModelLink
        {
            SourcePath = modelPath is null ? null : Path.GetFullPath(modelPath),
            SyncToken = token,
            Baseline = snapshot
        };
        RenderModelLinkFile.Save(exportPath, modelPath is null ? link : link.WithStampOf(modelPath));
        return outcome;
    }

    /// <summary>
    /// The earlier export at a path and the model it was written from, when both belong to this project and still
    /// match each other; null when there is nothing safe to merge with.
    /// </summary>
    private static (BuildingDocument Existing, BuildingDocument Baseline)? ExistingRenderWork(string exportPath, Guid projectId)
    {
        if (!File.Exists(exportPath) || RenderModelLinkFile.Load(exportPath) is not { Baseline: { } baseline } link) return null;
        try
        {
            var existing = ProjectFile.Load(exportPath);
            return existing.Id == projectId && RenderModelUpdate.SyncTokenOf(existing) == link.SyncToken ? (existing, baseline) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}

/// <summary>Locates the installed render application so the modeling application can hand a project to it.</summary>
public static class RenderApplication
{
    /// <summary>File name of the render application's executable.</summary>
    public const string ExecutableName = "GuanacasteRender.exe";

    private const string InstanceName = @"Local\GuanacasteRender";

    /// <summary>
    /// The render application's executable, or null when it is not installed: the path it last recorded for itself
    /// when that is still the real application, otherwise the newest version in the per-user install folder.
    /// </summary>
    public static string? FindExecutable()
    {
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var record = Path.Combine(local, "GuanacasteRender", "application-path.txt");
            if (File.Exists(record))
            {
                var recorded = File.ReadAllText(record).Trim();
                if (string.Equals(Path.GetFileName(recorded), ExecutableName, StringComparison.OrdinalIgnoreCase) && File.Exists(recorded))
                    return recorded;
            }
            var install = Path.Combine(local, "Programs", "Guanacaste Render");
            if (!Directory.Exists(install)) return null;
            return Directory.GetDirectories(install)
                .Select(folder => (Version: Version.TryParse(Path.GetFileName(folder), out var version) ? version : null,
                    Executable: Path.Combine(folder, ExecutableName)))
                .Where(candidate => candidate.Version is not null && File.Exists(candidate.Executable))
                .OrderByDescending(candidate => candidate.Version)
                .Select(candidate => candidate.Executable)
                .FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>True while a render application window is open; a second launch would exit without opening a file.</summary>
    public static bool IsRunning()
    {
        if (!Mutex.TryOpenExisting(InstanceName, out var instance)) return false;
        instance.Dispose();
        return true;
    }
}
