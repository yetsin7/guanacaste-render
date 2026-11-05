using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Guanacaste3D.Core;

/// <summary>One file in a transmittal: where it sits inside the package, what it is, how big and its SHA-256.</summary>
public sealed record TransmittalEntry(string Path, string Role, long Bytes, string Sha256);

/// <summary>What was packed: the files and the notes a person receiving the package should read.</summary>
public sealed record TransmittalReport(string ZipPath, IReadOnlyList<TransmittalEntry> Entries, IReadOnlyList<string> Notes);

/// <summary>
/// Packs a model to send it: one zip with the project, the central copy it works from when it is shared, the IFC, DXF and PDF when
/// asked, a manifest with the SHA-256 of every file and a plain-text summary. <see cref="Verify"/> checks a received package against
/// its manifest and proves that the project inside can be opened.
/// </summary>
public static class ModelTransmittal
{
    public const string ManifestName = "manifest.json";
    public const string SummaryName = "TRANSMITTAL.txt";

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    private static string Safe(string name)
    {
        var clean = new string((name ?? string.Empty).Select(letter => Path.GetInvalidFileNameChars().Contains(letter) ? '_' : letter).ToArray()).Trim();
        return clean.Length == 0 ? "proyecto" : clean.Length > 80 ? clean[..80] : clean;
    }

    public static TransmittalReport Create(BuildingDocument document, string zipPath, DateTimeOffset when, bool includeExports)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(zipPath) || Path.GetExtension(zipPath).ToLowerInvariant() != ".zip") throw new InvalidDataException("Choose a .zip file.");
        var name = Safe(document.Name);
        var entries = new List<TransmittalEntry>();
        var notes = new List<string>();
        var files = new List<(string Path, string Role, byte[] Data)>();

        var work = Path.Combine(Path.GetTempPath(), "g3d-transmit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var project = Path.Combine(work, name + ".g3d");
            ProjectFile.Save(project, document);
            files.Add((name + ".g3d", "Project", File.ReadAllBytes(project)));
            if (document.Collaboration is { Enabled: true } collaboration)
            {
                if (File.Exists(collaboration.CentralPath)) files.Add(("central/" + Safe(Path.GetFileName(collaboration.CentralPath)), "Central", File.ReadAllBytes(collaboration.CentralPath)));
                else notes.Add("CentralMissing");
            }
            if (includeExports)
            {
                var exports = Path.Combine(work, "exports");
                foreach (var file in Publishing.CreateSharePackage(document, exports, when).Where(file => !file.EndsWith(".g3d", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
                    files.Add(("exports/" + file, "Export", File.ReadAllBytes(Path.Combine(exports, file))));
            }
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        foreach (var (path, role, data) in files) entries.Add(new TransmittalEntry(path, role, data.LongLength, Hash(data)));
        if (document.References.Count > 0) notes.Add("References:" + document.References.Count);
        if (document.PointClouds.Count > 0) notes.Add("PointClouds:" + document.PointClouds.Count);

        var manifest = JsonSerializer.SerializeToUtf8Bytes(new { Project = document.Name, Created = when, Entries = entries }, new JsonSerializerOptions { WriteIndented = true });
        var summary = new StringBuilder()
            .AppendLine($"{document.Name} — {when:yyyy-MM-dd HH:mm}")
            .AppendLine($"Niveles: {document.Levels.Count}  Muros: {document.Walls.Count}  Aberturas: {document.Openings.Count}  Losas: {document.Floors.Count}  Columnas: {document.Columns.Count}  Hojas: {document.Sheets.Count}")
            .AppendLine($"Imágenes y archivos incrustados en el proyecto: {document.References.Count}  Nubes de puntos: {document.PointClouds.Count}")
            .AppendLine();
        foreach (var entry in entries) summary.AppendLine($"{entry.Path}  {entry.Bytes:N0} bytes  SHA-256 {entry.Sha256[..16]}…");
        if (notes.Contains("CentralMissing")) summary.AppendLine().AppendLine("Aviso: el modelo central no estaba en su ubicación y no se incluyó.");

        var folder = Path.GetDirectoryName(Path.GetFullPath(zipPath));
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using (var stream = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            void Put(string path, byte[] data)
            {
                var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
                using var output = entry.Open();
                output.Write(data);
            }
            foreach (var (path, _, data) in files) Put(path, data);
            Put(ManifestName, manifest);
            Put(SummaryName, new UTF8Encoding(false).GetBytes(summary.ToString()));
        }
        return new TransmittalReport(zipPath, entries, notes);
    }

    /// <summary>The problems found in a package, as short texts; none means that every file matches its manifest and the project opens.</summary>
    public static IReadOnlyList<string> Verify(string zipPath)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath)) return ["NoPackage"];
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var manifestEntry = archive.GetEntry(ManifestName);
            if (manifestEntry is null) return ["NoManifest"];
            using var reader = new StreamReader(manifestEntry.Open());
            using var json = JsonDocument.Parse(reader.ReadToEnd());
            if (!json.RootElement.TryGetProperty("Entries", out var list) || list.ValueKind != JsonValueKind.Array) return ["NoManifest"];
            string? projectPath = null;
            foreach (var item in list.EnumerateArray())
            {
                var path = item.GetProperty("Path").GetString() ?? string.Empty;
                var entry = archive.GetEntry(path);
                if (entry is null) { problems.Add("Missing:" + path); continue; }
                using var memory = new MemoryStream();
                using (var input = entry.Open()) input.CopyTo(memory);
                if (!Hash(memory.ToArray()).Equals(item.GetProperty("Sha256").GetString(), StringComparison.OrdinalIgnoreCase)) problems.Add("Changed:" + path);
                if (item.GetProperty("Role").GetString() == "Project") projectPath = path;
            }
            if (projectPath is null) problems.Add("NoProject");
            else if (!problems.Contains("Missing:" + projectPath) && !problems.Contains("Changed:" + projectPath))
            {
                var work = Path.Combine(Path.GetTempPath(), "g3d-verify-" + Guid.NewGuid().ToString("N") + ".g3d");
                try
                {
                    archive.GetEntry(projectPath)!.ExtractToFile(work);
                    ProjectFile.Load(work);
                }
                catch (Exception exception) when (exception is InvalidDataException or IOException or JsonException) { problems.Add("Unreadable:" + projectPath); }
                finally { try { File.Delete(work); } catch (IOException) { } }
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or KeyNotFoundException or IOException) { problems.Add("Corrupt"); }
        return problems;
    }
}
