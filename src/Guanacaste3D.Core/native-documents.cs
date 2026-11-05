using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Guanacaste3D.Core;

public enum NativeDocumentKind : byte { Project = 1, Family = 2, Template = 3 }

public sealed record NativeDocumentInfo(
    NativeDocumentKind Kind,
    int ContainerVersion,
    int SchemaVersion,
    Guid DocumentId,
    long UncompressedBytes,
    long CompressedBytes,
    string Sha256);

public static class NativeDocumentStore
{
    private const string Signature = "G3DNATV1";
    private const int ContainerVersion = 1;
    private const long MaximumPayloadBytes = 256L * 1024 * 1024;
    private const int HeaderBytes = 8 + 4 + 1 + 4 + 16 + 8 + 8 + 32;

    public const string ProjectExtension = ".g3d";
    public const string FamilyExtension = ".g3df";
    public const string TemplateExtension = ".g3dt";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = false
    };

    public static string ExtensionFor(NativeDocumentKind kind) => kind switch
    {
        NativeDocumentKind.Project => ProjectExtension,
        NativeDocumentKind.Family => FamilyExtension,
        NativeDocumentKind.Template => TemplateExtension,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static void SaveProject(string path, BuildingDocument document)
    {
        document.Validate();
        Save(path, NativeDocumentKind.Project, document.SchemaVersion, document.Id, document);
    }

    /// <summary>Checks the stored header before migrating the verified payload to the current project schema.</summary>
    public static BuildingDocument LoadProject(string path)
    {
        var (info, json) = Read(path);
        RequireKind(info, NativeDocumentKind.Project);
        var document = Deserialize<BuildingDocument>(json);
        if (document.Id != info.DocumentId || document.SchemaVersion != info.SchemaVersion)
            throw new InvalidDataException("The project header and payload identity do not match.");
        return ProjectFile.DeserializeDocument(Encoding.UTF8.GetString(json));
    }

    public static void SaveFamily(string path, FamilyDocument document)
    {
        document.Validate();
        Save(path, NativeDocumentKind.Family, document.SchemaVersion, document.Id, document);
    }

    public static FamilyDocument LoadFamily(string path)
    {
        var (info, json) = Read(path);
        RequireKind(info, NativeDocumentKind.Family);
        var document = Deserialize<FamilyDocument>(json);
        if (document.Id != info.DocumentId || document.SchemaVersion != info.SchemaVersion)
            throw new InvalidDataException("The family header and payload identity do not match.");
        document.Validate();
        return document;
    }

    public static void SaveTemplate(string path, ProjectTemplateDocument document)
    {
        document.Validate();
        Save(path, NativeDocumentKind.Template, document.SchemaVersion, document.Id, document);
    }

    public static ProjectTemplateDocument LoadTemplate(string path)
    {
        var (info, json) = Read(path);
        RequireKind(info, NativeDocumentKind.Template);
        var document = Deserialize<ProjectTemplateDocument>(json);
        if (document.Id != info.DocumentId || document.SchemaVersion != info.SchemaVersion)
            throw new InvalidDataException("The template header and payload identity do not match.");
        document.Validate();
        return document;
    }

    public static NativeDocumentInfo Inspect(string path) => Read(path).Info;

    public static bool TryInspect(string path, out NativeDocumentInfo? info)
    {
        try
        {
            info = Inspect(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            info = null;
            return false;
        }
    }

    private static void Save<T>(string path, NativeDocumentKind kind, int schemaVersion, Guid documentId, T document)
    {
        if (!string.Equals(Path.GetExtension(path), ExtensionFor(kind), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The selected path does not use the required Guanacaste 3D document extension.");
        if (schemaVersion < 1 || documentId == Guid.Empty)
            throw new InvalidDataException("The document identity or schema version is invalid.");

        var json = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        if (json.LongLength > MaximumPayloadBytes)
            throw new InvalidDataException("The native document payload exceeds the supported size.");

        var compressed = Compress(json);
        var hash = SHA256.HashData(json);

        using var container = new MemoryStream(HeaderBytes + compressed.Length);
        using (var writer = new BinaryWriter(container, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes(Signature));
            writer.Write(ContainerVersion);
            writer.Write((byte)kind);
            writer.Write(schemaVersion);
            writer.Write(documentId.ToByteArray());
            writer.Write((long)json.Length);
            writer.Write((long)compressed.Length);
            writer.Write(hash);
            writer.Write(compressed);
        }

        AtomicWrite(path, container.ToArray());
    }

    private static (NativeDocumentInfo Info, byte[] Json) Read(string path)
    {
        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("The Guanacaste 3D document was not found.", path);
        if (fileInfo.Length < HeaderBytes || fileInfo.Length > MaximumPayloadBytes + HeaderBytes)
            throw new InvalidDataException("The native document container size is invalid.");

        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ReadPayload(stream);
    }

    /// <summary>
    /// Reads and verifies a native container from a seekable stream without opening paths or resolving references.
    /// Leaves the stream open. Shell thumbnail providers use this entry point without requiring a file path.
    /// </summary>
    public static (NativeDocumentInfo Info, byte[] Json) ReadPayload(Stream stream)
    {
        if (!stream.CanSeek || stream.Length < HeaderBytes || stream.Length > MaximumPayloadBytes + HeaderBytes)
            throw new InvalidDataException("The native document container size is invalid.");
        stream.Position = 0;
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        if (!reader.ReadBytes(8).SequenceEqual(Encoding.ASCII.GetBytes(Signature)))
            throw new InvalidDataException("The Guanacaste 3D native file signature is missing or unsupported.");

        var containerVersion = reader.ReadInt32();
        if (containerVersion != ContainerVersion)
            throw new InvalidDataException("Unsupported Guanacaste 3D container version.");

        var kindValue = reader.ReadByte();
        if (!Enum.IsDefined(typeof(NativeDocumentKind), kindValue))
            throw new InvalidDataException("The native document kind is invalid.");
        var kind = (NativeDocumentKind)kindValue;

        var schemaVersion = reader.ReadInt32();
        var documentIdBytes = reader.ReadBytes(16);
        if (documentIdBytes.Length != 16 || schemaVersion < 1)
            throw new InvalidDataException("The native document header is truncated or invalid.");
        var documentId = new Guid(documentIdBytes);

        var uncompressedLength = reader.ReadInt64();
        var compressedLength = reader.ReadInt64();
        var expectedHash = reader.ReadBytes(32);
        if (documentId == Guid.Empty || expectedHash.Length != 32 ||
            uncompressedLength is <= 0 or > MaximumPayloadBytes ||
            compressedLength is <= 0 or > MaximumPayloadBytes ||
            stream.Length - stream.Position != compressedLength)
            throw new InvalidDataException("The native document header lengths are invalid.");

        var compressed = reader.ReadBytes(checked((int)compressedLength));
        if (compressed.LongLength != compressedLength)
            throw new InvalidDataException("The native document payload is truncated.");

        var json = Decompress(compressed, uncompressedLength);
        var actualHash = SHA256.HashData(json);
        if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
            throw new InvalidDataException("The native document integrity checksum does not match.");

        var info = new NativeDocumentInfo(
            kind,
            containerVersion,
            schemaVersion,
            documentId,
            uncompressedLength,
            compressedLength,
            Convert.ToHexString(actualHash).ToLowerInvariant());
        return (info, json);
    }

    private static byte[] Compress(byte[] json)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(json);
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] compressed, long expectedLength)
    {
        try
        {
            using var input = new MemoryStream(compressed, writable: false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(checked((int)expectedLength));
            var buffer = new byte[81920];
            int count;
            while ((count = gzip.Read(buffer)) > 0)
            {
                if (output.Length + count > expectedLength)
                    throw new InvalidDataException("The native document decompressed length does not match its header.");
                output.Write(buffer, 0, count);
            }
            if (output.Length != expectedLength || output.Length > MaximumPayloadBytes)
                throw new InvalidDataException("The native document decompressed length does not match its header.");
            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            throw new InvalidDataException("The native document compressed payload is invalid.", ex);
        }
    }

    private static T Deserialize<T>(byte[] json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new InvalidDataException("The native document payload is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The native document JSON payload is invalid.", ex);
        }
    }

    private static void RequireKind(NativeDocumentInfo info, NativeDocumentKind expected)
    {
        if (info.Kind != expected)
            throw new InvalidDataException("The selected Guanacaste 3D file is a different document type.");
    }

    private static void AtomicWrite(string path, byte[] bytes)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full)
            ?? throw new InvalidDataException("The file path is invalid.");
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException(directory);

        var temporary = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       64 * 1024, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(full))
                File.Copy(full, full + ".bak", overwrite: true);
            File.Move(temporary, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
