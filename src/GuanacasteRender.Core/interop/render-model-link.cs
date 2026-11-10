using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Guanacaste3D.Core.Interop;

/// <summary>
/// The connection between a render project and the model it came from: where the model lives, what that file looked
/// like at the last exchange, and the model both applications last agreed on.
/// </summary>
public sealed record RenderModelLink
{
    /// <summary>Absolute path of the model file, or null when the model was never saved.</summary>
    public string? SourcePath { get; init; }

    /// <summary>Path of the model relative to the project folder, so both can move together.</summary>
    public string? RelativeSourcePath { get; init; }

    /// <summary>Last write time (UTC ticks) of the model file at the last exchange; 0 when unknown.</summary>
    public long SourceWriteTicksUtc { get; init; }

    /// <summary>Length in bytes of the model file at the last exchange.</summary>
    public long SourceLength { get; init; }

    /// <summary>Token of the document state <see cref="Baseline"/> belongs to (see <see cref="RenderModelUpdate.SyncTokenKey"/>).</summary>
    public string SyncToken { get; init; } = string.Empty;

    /// <summary>The model as both applications last saw it; null when that state is not known.</summary>
    public BuildingDocument? Baseline { get; init; }

    /// <summary>Returns the link with the model file's current time and size recorded as seen.</summary>
    public RenderModelLink WithStampOf(string path)
    {
        var (ticks, length) = RenderModelLinkFile.Stamp(path);
        return this with { SourceWriteTicksUtc = ticks, SourceLength = length };
    }

    /// <summary>True when the model file exists and differs from what the last exchange recorded.</summary>
    public bool SourceChanged(string? resolvedSource)
    {
        if (resolvedSource is null) return false;
        var (ticks, length) = RenderModelLinkFile.Stamp(resolvedSource);
        return ticks != 0 && (ticks != SourceWriteTicksUtc || length != SourceLength);
    }
}

/// <summary>Reads and writes a project's model link in a compressed file beside the project.</summary>
public static class RenderModelLinkFile
{
    private const string Suffix = ".model-link";
    private const int FormatVersion = 1;
    private const long MaximumFileBytes = 128L * 1024 * 1024;
    private const long MaximumPayloadBytes = 512L * 1024 * 1024;

    /// <summary>Serialized shape of the sidecar; the baseline travels as the project's own JSON.</summary>
    private sealed record Payload(int FormatVersion, string? SourcePath, string? RelativeSourcePath,
        long SourceWriteTicksUtc, long SourceLength, string SyncToken, string? Baseline);

    /// <summary>The file that holds the model link of the project saved at <paramref name="projectPath"/>.</summary>
    public static string SidecarPath(string projectPath) => projectPath + Suffix;

    /// <summary>Last write time in UTC ticks and length of a file; zeros when it cannot be read.</summary>
    public static (long Ticks, long Length) Stamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? (info.LastWriteTimeUtc.Ticks, info.Length) : (0, 0);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (0, 0);
        }
    }

    /// <summary>
    /// Loads the link of a project, or null when it has none. A damaged link is treated as missing: it must never
    /// stop the project itself from opening, and the next update then falls back to the rules without history.
    /// </summary>
    public static RenderModelLink? Load(string projectPath)
    {
        var path = SidecarPath(projectPath);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaximumFileBytes) return null;
            using var file = File.OpenRead(path);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = gzip.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + read > MaximumPayloadBytes) return null;
                buffer.Write(chunk, 0, read);
            }
            var payload = JsonSerializer.Deserialize<Payload>(Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length));
            if (payload is null || payload.FormatVersion != FormatVersion) return null;
            return new RenderModelLink
            {
                SourcePath = payload.SourcePath,
                RelativeSourcePath = payload.RelativeSourcePath,
                SourceWriteTicksUtc = payload.SourceWriteTicksUtc,
                SourceLength = payload.SourceLength,
                SyncToken = payload.SyncToken ?? string.Empty,
                Baseline = payload.Baseline is null ? null : ProjectFile.DeserializeDocument(payload.Baseline)
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes the link beside the project, replacing the previous one atomically.</summary>
    public static void Save(string projectPath, RenderModelLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        var full = Path.GetFullPath(SidecarPath(projectPath));
        var directory = Path.GetDirectoryName(full) ?? throw new InvalidDataException("The file path is invalid.");
        var relative = link.SourcePath is null ? link.RelativeSourcePath : Path.GetRelativePath(directory, link.SourcePath);
        var payload = new Payload(FormatVersion, link.SourcePath, relative, link.SourceWriteTicksUtc, link.SourceLength,
            link.SyncToken, link.Baseline is null ? null : ProjectFile.SerializeDocument(link.Baseline));
        var temp = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = File.Create(temp))
            using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
                gzip.Write(JsonSerializer.SerializeToUtf8Bytes(payload));
            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>
    /// The model file a link points at: its absolute path when that still exists, otherwise the path relative to
    /// the project folder. Null when neither exists.
    /// </summary>
    public static string? ResolveSource(RenderModelLink link, string? projectPath)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (!string.IsNullOrWhiteSpace(link.SourcePath) && File.Exists(link.SourcePath)) return link.SourcePath;
        if (projectPath is null || string.IsNullOrWhiteSpace(link.RelativeSourcePath)) return null;
        var directory = Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? ".";
        var candidate = Path.GetFullPath(Path.Combine(directory, link.RelativeSourcePath));
        return File.Exists(candidate) ? candidate : null;
    }
}
