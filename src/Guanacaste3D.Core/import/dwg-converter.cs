using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Guanacaste3D.Core.Import;

/// <summary>
/// Imports DWG drawings by converting them to ASCII DXF with software already installed on the computer: the
/// AutoCAD Core Console (<c>accoreconsole.exe</c>, any AutoCAD 2013 or later) or the free ODA File Converter. DWG is a
/// proprietary, frequently changing format, so reading it through the vendor's own engine is the dependable option.
/// The original file is only read; the conversion happens in a private temporary folder that is removed afterwards.
/// </summary>
internal static partial class DwgConverter
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(180);

    private enum Engine { None, AutoCad, Oda }

    private sealed record Found(Engine Engine, string Path, string Description);

    // ------------------------------------------------------------------ inspection

    public static ImportInspection Inspect(string path, long bytes)
    {
        var notes = new List<string>();
        var release = ReleaseName(ReadMagic(path));
        if (release is not null) notes.Add("Import.Note.DwgVersion|" + release);
        var engine = FindEngine();
        if (engine.Engine == Engine.None)
        {
            notes.Add("Import.Note.DwgConverterMissing");
            return new ImportInspection(ImportFormat.Dwg, "AutoCAD DWG", bytes, false, "Import.Summary.DwgMissing", notes);
        }
        notes.Add("Import.Note.DwgConverter|" + engine.Description);
        return new ImportInspection(ImportFormat.Dwg, "AutoCAD DWG", bytes, true, "Import.Summary.Ready", notes);
    }

    private static string ReadMagic(string path)
    {
        var head = new byte[6];
        using var stream = File.OpenRead(path);
        var read = stream.Read(head, 0, head.Length);
        return Encoding.ASCII.GetString(head, 0, read);
    }

    /// <summary>Maps the six-character DWG signature to the AutoCAD release family that writes it.</summary>
    internal static string? ReleaseName(string magic) => magic switch
    {
        "AC1032" => "AutoCAD 2018–2027",
        "AC1027" => "AutoCAD 2013–2017",
        "AC1024" => "AutoCAD 2010–2012",
        "AC1021" => "AutoCAD 2007–2009",
        "AC1018" => "AutoCAD 2004–2006",
        "AC1015" => "AutoCAD 2000–2002",
        "AC1014" => "AutoCAD R14",
        "AC1012" => "AutoCAD R13",
        "AC1009" => "AutoCAD R11/R12",
        _ => null
    };

    // ------------------------------------------------------------------ discovery

    private static Found FindEngine()
    {
        if (Environment.GetEnvironmentVariable("GUANACASTE_ACCORECONSOLE") is { Length: > 0 } custom && File.Exists(custom))
            return new Found(Engine.AutoCad, custom, "AutoCAD (" + Path.GetFileName(Path.GetDirectoryName(custom)) + ")");
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var autodesk = Path.Combine(programFiles, "Autodesk");
        if (Directory.Exists(autodesk))
        {
            var console = Directory.EnumerateDirectories(autodesk, "AutoCAD *")
                .Where(directory => !directory.Contains("LT", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(directory => directory, StringComparer.OrdinalIgnoreCase)
                .Select(directory => Path.Combine(directory, "accoreconsole.exe"))
                .FirstOrDefault(File.Exists);
            if (console is not null)
                return new Found(Engine.AutoCad, console, Path.GetFileName(Path.GetDirectoryName(console)!));
        }
        var oda = Path.Combine(programFiles, "ODA");
        if (Directory.Exists(oda))
        {
            var converter = Directory.EnumerateFiles(oda, "ODAFileConverter.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (converter is not null) return new Found(Engine.Oda, converter, "ODA File Converter");
        }
        return new Found(Engine.None, "", "");
    }

    // ------------------------------------------------------------------ conversion

    public static ImportedModel Read(string path, ModelImportOptions options, IProgress<ImportProgress>? progress,
        CancellationToken cancellation)
    {
        var engine = FindEngine();
        if (engine.Engine == Engine.None)
            throw new InvalidDataException("AutoCAD DWG files need AutoCAD or the ODA File Converter, which were not found on this computer.");

        var work = Path.Combine(Path.GetTempPath(), "guanacaste3d-dwg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            progress?.Report(new ImportProgress(0.1, "Import.ConvertingDwg"));
            var dxf = engine.Engine == Engine.AutoCad
                ? ConvertWithAutoCad(engine.Path, path, work, cancellation)
                : ConvertWithOda(engine.Path, path, work, cancellation);
            if (!File.Exists(dxf) || new FileInfo(dxf).Length < 100)
                throw new InvalidDataException("The DWG conversion did not produce a drawing.");
            var inner = new Progress<ImportProgress>(p => progress?.Report(p with { Fraction = 0.3 + 0.65 * p.Fraction }));
            var model = DxfReader.Read(dxf, options, inner, cancellation);
            return model.WithSource(path, "DWG", Path.GetFileNameWithoutExtension(path));
        }
        finally
        {
            TryDelete(work);
        }
    }

    private static string ConvertWithAutoCad(string console, string dwg, string work, CancellationToken cancellation)
    {
        var output = Path.Combine(work, "converted.dxf");
        var script = Path.Combine(work, "convert.scr");
        // FILEDIA 0 turns the file dialog into a command-line prompt; DXFOUT's last answer is the decimal precision.
        // No QUIT: the console exits when the script ends, and a localized "discard changes?" prompt would hang it.
        File.WriteAllText(script, $"FILEDIA\r\n0\r\n_DXFOUT\r\n{output}\r\n16\r\n", new UTF8Encoding(false));
        Run(console, $"/i \"{dwg}\" /s \"{script}\" /l en-US", work, cancellation);
        return output;
    }

    private static string ConvertWithOda(string converter, string dwg, string work, CancellationToken cancellation)
    {
        var input = Path.Combine(work, "in");
        var output = Path.Combine(work, "out");
        Directory.CreateDirectory(input);
        Directory.CreateDirectory(output);
        var name = Path.GetFileName(dwg);
        File.Copy(dwg, Path.Combine(input, name));
        Run(converter, $"\"{input}\" \"{output}\" ACAD2018 DXF 0 1 \"{name}\"", work, cancellation);
        return Path.Combine(output, Path.ChangeExtension(name, ".dxf"));
    }

    private static void Run(string executable, string arguments, string workingDirectory, CancellationToken cancellation)
    {
        var info = new ProcessStartInfo(executable, arguments)
        {
            // AutoCAD writes crash reports and logs to its working directory; keep them in the temporary folder.
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };
        using var process = Process.Start(info) ?? throw new InvalidDataException("The DWG conversion did not produce a drawing.");
        // Draining the pipes keeps a chatty console from blocking on a full buffer.
        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        using var registration = cancellation.Register(() => TryKill(process));
        if (!process.WaitForExit(Timeout))
        {
            TryKill(process);
            throw new InvalidDataException("The DWG conversion timed out.");
        }
        cancellation.ThrowIfCancellationRequested();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
