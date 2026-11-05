using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Guanacaste3D.Core.Import.Revit;

/// <summary>Stage of a Revit export, for the progress window: "starting", "opening", "exporting", "converting".</summary>
public readonly record struct RevitExportProgress(string Stage, double Fraction);

/// <summary>
/// Reads a .rvt project through the user's own Revit: installs the Guanacaste add-in for that Revit version, starts
/// Revit with an export job, waits for the exchange file and closes Revit. Revit's geometry database is proprietary,
/// so a licensed Revit is the only reliable reader; the exchange file is cached per source file and save time, so
/// opening the same project again is immediate.
/// </summary>
public static class RevitBridgeRunner
{
    private const string AddinFileName = "Guanacaste3D.RevitBridge.addin";
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(40);

    /// <summary>A Revit installation that has a matching bridge add-in shipped with Guanacaste.</summary>
    public sealed record RevitInstallation(int Year, string Executable, string BridgeAssembly);

    /// <summary>Installed Revit versions that can run the bridge, newest first.</summary>
    public static IReadOnlyList<RevitInstallation> Installations()
    {
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var result = new List<RevitInstallation>();
        for (var year = 2035; year >= 2025; year--)
        {
            var executable = Path.Combine(programs, "Autodesk", $"Revit {year}", "Revit.exe");
            var bridge = Path.Combine(AppContext.BaseDirectory, "revit-bridge", $"Revit{year}", "Guanacaste3D.RevitBridge.dll");
            if (File.Exists(executable) && File.Exists(bridge)) result.Add(new RevitInstallation(year, executable, bridge));
        }
        return result;
    }

    /// <summary>The newest installation able to open a file saved by <paramref name="fileYear"/> (Revit only opens older files).</summary>
    public static RevitInstallation? InstallationFor(int? fileYear) =>
        Installations().FirstOrDefault(installation => fileYear is null || installation.Year >= fileYear);

    /// <summary>Where the exchange file of a project is cached; it changes whenever the project is saved again.</summary>
    public static string CachePath(string revitPath)
    {
        var info = new FileInfo(revitPath);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            info.FullName.ToUpperInvariant() + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks)))[..20];
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Guanacaste3D", "revit-cache");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, Path.GetFileNameWithoutExtension(revitPath) + "-" + key + ExchangeFile.Extension);
    }

    /// <summary>
    /// Installs the bridge for one Revit version the way the downloadable installer does: the assembly is copied into
    /// its own folder in Revit's add-ins folder and the manifest points there, so rebuilding or moving Guanacaste never
    /// touches the copy a running Revit has loaded. A copy locked by a running Revit is kept.
    /// </summary>
    public static void InstallAddin(RevitInstallation installation)
    {
        var addins = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Autodesk", "Revit", "Addins", installation.Year.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var folder = Path.Combine(addins, "Guanacaste3D");
        Directory.CreateDirectory(folder);
        var assembly = Path.Combine(folder, "Guanacaste3D.RevitBridge.dll");
        try
        {
            if (!File.Exists(assembly) || !File.ReadAllBytes(assembly).AsSpan().SequenceEqual(File.ReadAllBytes(installation.BridgeAssembly)))
                File.Copy(installation.BridgeAssembly, assembly, overwrite: true);
        }
        catch (IOException) when (File.Exists(assembly))
        {
            // Revit has the installed copy loaded; it is replaced the next time Revit is closed.
        }
        var manifest = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <RevitAddIns>
              <AddIn Type="Application">
                <Name>Guanacaste 3D</Name>
                <Assembly>{System.Security.SecurityElement.Escape(assembly)}</Assembly>
                <AddInId>5B0C7F3E-2D4A-4E61-9C83-6A1F0B2E7D45</AddInId>
                <FullClassName>Guanacaste3D.RevitBridge.BridgeApplication</FullClassName>
                <VendorId>GUANACASTE3D</VendorId>
                <VendorDescription>Guanacaste 3D</VendorDescription>
              </AddIn>
            </RevitAddIns>
            """;
        var path = Path.Combine(addins, AddinFileName);
        if (!File.Exists(path) || File.ReadAllText(path) != manifest) File.WriteAllText(path, manifest);
    }

    /// <summary>
    /// Remembers where Guanacaste 3D runs from, so the Revit add-in's "Abrir en Guanacaste 3D" button can start it.
    /// </summary>
    public static void RecordApplicationPath(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable)) return;
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Guanacaste3D");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, "application-path.txt");
            if (!File.Exists(file) || File.ReadAllText(file) != executable) File.WriteAllText(file, executable);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only the Revit button's convenience depends on it.
        }
    }

    /// <summary>
    /// Exports a Revit project to an exchange file and returns its path, reusing the cached export when the project
    /// has not changed since. Revit runs in its own window; the first time, Revit asks the user to trust the add-in.
    /// </summary>
    public static async Task<string> ExportAsync(string revitPath, IProgress<RevitExportProgress>? progress = null,
        CancellationToken cancellation = default)
    {
        var output = CachePath(revitPath);
        if (File.Exists(output)) return output;
        int? year = null;
        try
        {
            if (RevitFileInfo.Read(revitPath).FormatYear is { } format && int.TryParse(format, out var parsed)) year = parsed;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            // The version is only a hint for choosing a Revit installation.
        }
        var installation = InstallationFor(year) ??
                           throw new InvalidDataException(Installations().Count == 0
                               ? "Revit is not installed on this computer; export the model to IFC or .g3dx from Revit and import that file."
                               : "The project was saved by a newer Revit than the one installed on this computer.");
        InstallAddin(installation);

        var work = Path.Combine(Path.GetTempPath(), "Guanacaste3D", "revit-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        var job = Path.Combine(work, "job.json");
        var status = Path.Combine(work, "status.json");
        await File.WriteAllTextAsync(job, JsonSerializer.Serialize(new { Input = Path.GetFullPath(revitPath), Output = output, Status = status }),
            cancellation);
        var start = new ProcessStartInfo(installation.Executable, "/nosplash") { UseShellExecute = false };
        start.Environment["G3D_REVIT_JOB"] = job;
        progress?.Report(new RevitExportProgress("starting", 0));
        using var revit = Process.Start(start) ?? throw new InvalidDataException("Revit could not be started.");
        var clock = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                if (clock.Elapsed > Timeout) throw new InvalidDataException("Revit did not finish exporting the project in time.");
                if (ReadStatus(status) is { } state)
                {
                    var stage = state.TryGetProperty("state", out var value) ? value.GetString() ?? "" : "";
                    if (stage == "done")
                    {
                        if (state.TryGetProperty("ok", out var ok) && ok.GetBoolean() && File.Exists(output)) break;
                        var error = state.TryGetProperty("error", out var message) ? message.GetString() : null;
                        throw new InvalidDataException("Revit could not export the project: " + (error ?? "unknown error"));
                    }
                    progress?.Report(new RevitExportProgress(stage,
                        state.TryGetProperty("progress", out var fraction) ? fraction.GetDouble() : 0));
                }
                if (revit.HasExited) throw new InvalidDataException("Revit closed before exporting the project.");
                await Task.Delay(500, cancellation);
            }
        }
        finally
        {
            // The add-in asks Revit to exit; give it a moment, then make sure no hidden Revit stays behind.
            try
            {
                if (!revit.HasExited && !revit.WaitForExit(TimeSpan.FromSeconds(20))) revit.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { /* already gone */ }
            try { Directory.Delete(work, recursive: true); }
            catch (IOException) { /* temporary folder, cleaned by Windows */ }
        }
        return output;
    }

    private static JsonElement? ReadStatus(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.Clone();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
