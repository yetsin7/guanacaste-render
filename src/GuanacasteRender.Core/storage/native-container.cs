using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Guanacaste3D.Core;

/// <summary>
/// The payload read from a sealed native file: the kind signature, the schema
/// version declared in the header, and the JSON document that the checksum covers.
/// </summary>
public readonly record struct SealedPayload(string Signature, int SchemaVersion, string Json);

/// <summary>
/// Shared envelope for native document kinds (families and templates). The first
/// line is a signature such as <c>G3-FAMILY/1 sha256=…</c>; the rest of the file is
/// the JSON payload, and the hash covers exactly those bytes. Identity comes from
/// this signature, so renaming a file to another extension cannot change its kind,
/// and any edit to the payload fails the checksum before it is parsed.
/// </summary>
public static class NativeContainer
{
    /// <summary>Largest accepted file, shared with the project limit.</summary>
    public const long MaximumBytes = 20L * 1024 * 1024;

    /// <summary>Signature of a family document (.g3df).</summary>
    public const string FamilySignature = "G3-FAMILY";

    /// <summary>Signature of a project template (.g3dt).</summary>
    public const string TemplateSignature = "G3-TEMPLATE";

    private static readonly System.Text.RegularExpressions.Regex HeaderPattern =
        new(@"^(G3-[A-Z]+)/([0-9]{1,4}) sha256=([0-9a-f]{64})$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Returns the full file text for a JSON payload under the given signature and schema version.</summary>
    public static string Seal(string signature, int schemaVersion, string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes)
            throw new InvalidDataException("The document exceeds the 20 MB limit.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        return $"{signature}/{schemaVersion} sha256={hash}\n{json}";
    }

    /// <summary>
    /// Parses the signature line and verifies the payload checksum. Structural and
    /// version checks belong to the caller, which knows which kinds it accepts.
    /// </summary>
    public static SealedPayload Open(string content)
    {
        var newline = content.IndexOf('\n');
        if (newline < 0)
            throw new InvalidDataException("The document signature is missing.");
        var match = HeaderPattern.Match(content[..newline]);
        if (!match.Success)
            throw new InvalidDataException("The document signature is missing or unsupported.");
        var json = content[(newline + 1)..];
        var actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        if (!string.Equals(actual, match.Groups[3].Value, StringComparison.Ordinal))
            throw new InvalidDataException("The document checksum does not match its content. The file may be damaged or edited.");
        if (!int.TryParse(match.Groups[2].Value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var version))
            throw new InvalidDataException("The document schema version is invalid.");
        return new SealedPayload(match.Groups[1].Value, version, json);
    }

    /// <summary>
    /// Parses a JSON object and rejects duplicate fields and missing required fields
    /// before any typed deserialization, so a hostile file cannot shadow a value.
    /// </summary>
    public static void RequireObject(string json, IEnumerable<string> requiredFields)
    {
        using var parsed = JsonDocument.Parse(json);
        if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The document must be a JSON object.");
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in parsed.RootElement.EnumerateObject())
            if (!fields.Add(field.Name))
                throw new InvalidDataException("The document contains duplicate fields.");
        foreach (var required in requiredFields)
            if (!fields.Contains(required))
                throw new InvalidDataException($"The required field is missing: {required}.");
    }

    /// <summary>Reads a file as UTF-8 after checking its size against the limit.</summary>
    public static string ReadText(string path)
    {
        if (new FileInfo(path).Length > MaximumBytes)
            throw new InvalidDataException("The file exceeds 20 MB.");
        return File.ReadAllText(path, Encoding.UTF8);
    }

    /// <summary>
    /// Writes content atomically: a temporary file is written in the target folder,
    /// the previous file is kept as a backup, and the new file replaces the target.
    /// </summary>
    public static void WriteAtomically(string path, string content)
    {
        if (Encoding.UTF8.GetByteCount(content) > MaximumBytes)
            throw new InvalidDataException("The file exceeds the 20 MB limit.");
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full)
            ?? throw new InvalidDataException("The file path is invalid.");
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException(directory);
        var temp = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, content, new UTF8Encoding(false));
            if (File.Exists(full))
                File.Copy(full, full + ".bak", overwrite: true);
            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
