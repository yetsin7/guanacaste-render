using System.Text;
using System.Text.RegularExpressions;

namespace Guanacaste3D.Core.Import;

/// <summary>What a Revit file reveals without Revit: its version, where it was last saved, and its embedded preview.</summary>
public sealed record RevitFileDetails(string? FormatYear, string? Build, string? LastSavePath, string? Worksharing,
    string? Locale, string Kind, byte[]? PreviewPng);

/// <summary>
/// Reads the readable parts of Revit files (.rvt project, .rfa family, .rte/.rft templates). Revit stores model
/// geometry in compressed, proprietary partitions that only Autodesk software can interpret, so geometry is never
/// extracted here; the supported route is Revit's own IFC export, which this application imports.
/// </summary>
public static partial class RevitFileInfo
{
    /// <summary>Reads version, build, path, worksharing state, locale and the preview image.</summary>
    public static RevitFileDetails Read(string path)
    {
        using var file = CompoundFile.Open(path);
        string? format = null, build = null, savePath = null, worksharing = null, locale = null;
        if (file.ReadStream("BasicFileInfo") is { } info)
        {
            // The block is UTF-16 text with a binary prefix of unknown parity, so decode both alignments.
            foreach (var alignment in new[] { 0, 1 })
            {
                if (info.Length <= alignment) continue;
                var text = Encoding.Unicode.GetString(info, alignment, (info.Length - alignment) & ~1);
                if (!text.Contains("Build:", StringComparison.Ordinal) && !text.Contains("Format:", StringComparison.Ordinal))
                    continue;
                format = Capture(FormatPattern(), text);
                build = Capture(BuildPattern(), text);
                savePath = Capture(SavePathPattern(), text);
                worksharing = Capture(WorksharingPattern(), text);
                locale = Capture(LocalePattern(), text);
                break;
            }
            // Older files keep the version as a length-prefixed string near the start.
            format ??= Capture(YearPattern(), Encoding.Unicode.GetString(info, 0, info.Length & ~1));
        }

        byte[]? preview = null;
        if (file.ReadStream("RevitPreview4.0") is { } raw) preview = ExtractPng(raw);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var kind = extension switch { ".rfa" => "Family", ".rte" or ".rft" => "Template", _ => "Project" };
        return new RevitFileDetails(format, build, savePath, worksharing, locale, kind, preview);
    }

    /// <summary>Describes a Revit file for the import dialog; it is never importable directly.</summary>
    internal static ImportInspection Inspect(string path, long bytes)
    {
        var notes = new List<string>();
        RevitFileDetails? details = null;
        try
        {
            details = Read(path);
        }
        catch (InvalidDataException)
        {
            notes.Add("Import.Note.RevitUnreadable");
        }
        catch (IOException)
        {
            notes.Add("Import.Note.RevitUnreadable");
        }
        if (details is not null)
        {
            if (details.FormatYear is not null) notes.Add("Import.Note.RevitVersion|" + details.FormatYear);
            if (details.Build is not null) notes.Add("Import.Note.RevitBuild|" + details.Build);
            if (details.LastSavePath is not null) notes.Add("Import.Note.RevitPath|" + details.LastSavePath);
            if (details.Worksharing is not null) notes.Add("Import.Note.RevitWorksharing|" + details.Worksharing);
        }
        return new ImportInspection(ImportFormat.Revit, "Revit", bytes, false, "Import.Summary.Revit", notes,
            details?.PreviewPng);
    }

    /// <summary>Cuts the PNG image out of the preview stream, ending at its IEND chunk.</summary>
    private static byte[]? ExtractPng(byte[] data)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var start = data.AsSpan().IndexOf(signature);
        if (start < 0) return null;
        var end = data.AsSpan(start).IndexOf("IEND"u8);
        if (end < 0) return null;
        var length = end + 8; // the chunk name plus its four-byte checksum
        return start + length <= data.Length ? data.AsSpan(start, length).ToArray() : null;
    }

    private static string? Capture(Regex pattern, string text)
    {
        var match = pattern.Match(text);
        var value = match.Success ? match.Groups[1].Value.Trim('\0', ' ', '\r', '\n') : null;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    [GeneratedRegex(@"Format:\s*([^\r\n\0]+)")]
    private static partial Regex FormatPattern();

    [GeneratedRegex(@"Build:\s*([^\r\n\0]+)")]
    private static partial Regex BuildPattern();

    [GeneratedRegex(@"Last Save Path:\s*([^\r\n\0]+)")]
    private static partial Regex SavePathPattern();

    [GeneratedRegex(@"Worksharing:\s*([^\r\n\0]+)")]
    private static partial Regex WorksharingPattern();

    [GeneratedRegex(@"Locale when saved:\s*([^\r\n\0]+)")]
    private static partial Regex LocalePattern();

    [GeneratedRegex(@"\b(20\d{2})\b")]
    private static partial Regex YearPattern();
}
