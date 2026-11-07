using System.Text;
using System.Text.Json;

namespace Guanacaste3D.Core;

/// <summary>
/// Project persistence facade. New projects use the official compressed .g3d
/// container; the earlier .g3 envelope and .g3d.json preview remain readable.
/// </summary>
public static class ProjectFile
{
    private const long MaximumBytes = 20L * 1024 * 1024;
    private const string PreviewHeader = "G3-PROJECT/1\n";

    /// <summary>Official Guanacaste 3D project extension.</summary>
    public const string NativeExtension = NativeDocumentStore.ProjectExtension;

    /// <summary>Previous preview project extension, retained for compatibility.</summary>
    public const string PreviewExtension = ".g3";

    /// <summary>Earlier native extension; same as <see cref="PreviewExtension"/>.</summary>
    public const string PreviousNativeExtension = PreviewExtension;

    /// <summary>Old JSON-only preview extension, import-only.</summary>
    public const string LegacyExtension = ".g3d.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false
    };

    /// <summary>Returns whether a path is a supported native project path.</summary>
    public static bool IsNativePath(string path) =>
        IsOfficialPath(path) || IsPreviewPath(path);

    public static bool IsOfficialPath(string path) =>
        string.Equals(Path.GetExtension(path), NativeExtension, StringComparison.OrdinalIgnoreCase);

    public static bool IsPreviewPath(string path) =>
        string.Equals(Path.GetExtension(path), PreviewExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Recognizes earlier native projects (.g3) for migration to the current extension.</summary>
    public static bool IsPreviousNativePath(string path) => IsPreviewPath(path);

    /// <summary>Returns whether a path uses the import-only JSON preview extension.</summary>
    public static bool IsLegacyPath(string path) =>
        path.EndsWith(LegacyExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Loads and validates a project. Official containers are identified by
    /// signature even when a caller supplies a non-standard extension.
    /// </summary>
    public static BuildingDocument Load(string path)
    {
        if (IsOfficialPath(path))
            return NativeDocumentStore.LoadProject(path);

        if (!IsPreviewPath(path) && !IsLegacyPath(path))
        {
            if (NativeDocumentStore.TryInspect(path, out var info) && info?.Kind == NativeDocumentKind.Project)
                return NativeDocumentStore.LoadProject(path);
            throw new InvalidDataException(
                "Guanacaste 3D projects must be a valid .g3d container, .g3 preview, or .g3d.json legacy document.");
        }

        if (new FileInfo(path).Length > MaximumBytes)
            throw new InvalidDataException("The preview project file exceeds 20 MB.");

        var content = File.ReadAllText(path, Encoding.UTF8);
        if (IsPreviewPath(path) && !content.StartsWith(PreviewHeader, StringComparison.Ordinal))
            throw new InvalidDataException("The .g3 project header is missing or unsupported.");

        var json = IsPreviewPath(path) ? content[PreviewHeader.Length..] : content;
        using (var parsed = JsonDocument.Parse(json))
        {
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("The project document must be a JSON object.");
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in parsed.RootElement.EnumerateObject())
                if (!fields.Add(field.Name))
                    throw new InvalidDataException("The project document contains duplicate fields.");
            foreach (var required in new[] { "SchemaVersion", "Id", "Name", "Levels", "Walls" })
                if (!fields.Contains(required))
                    throw new InvalidDataException("The required field is missing: " + required + ".");
        }

        return DeserializeDocument(json);
    }

    /// <summary>Serializes a project payload with the same options the project file uses.</summary>
    public static string SerializeDocument(BuildingDocument document) => JsonSerializer.Serialize(document, Options);

    /// <summary>
    /// Parses, migrates, and validates a project payload. Templates reuse this path so every accepted
    /// document follows one migration chain.
    /// </summary>
    public static BuildingDocument DeserializeDocument(string json)
    {
        var document = JsonSerializer.Deserialize<BuildingDocument>(json, Options)
            ?? throw new InvalidDataException("The project document is empty.");
        UpgradeLegacyDocument(document);
        document.Validate();
        return document;
    }

    /// <summary>Upgrades earlier schemas to current BIM catalogs and coordinated views.</summary>
    private static void UpgradeLegacyDocument(BuildingDocument document)
    {
        if (document.SchemaVersion == 1)
        {
            document.SchemaVersion = 2;
            document.WallTypes = BuiltInWallTypes.CreateDefaults();
            document.Rooms ??= [];
            document.Floors ??= [];
            document.Sheets ??= [];
            for (var index = 0; index < document.Walls.Count; index++)
            {
                var wall = document.Walls[index];
                var closestType = document.WallTypes.MinBy(type => Math.Abs(type.Thickness - wall.Thickness))!;
                document.Walls[index] = wall with { WallTypeId = closestType.Id };
            }
        }
        if (document.SchemaVersion == 2)
        {
            document.SchemaVersion = 3;
            document.Views = BuiltInProjectViews.CreateDefaults(document.Levels);
        }
        if (document.SchemaVersion == 3)
        {
            document.SchemaVersion = 4;
            document.ParameterDefinitions ??= [];
            document.ParameterValues ??= [];
        }
        if (document.SchemaVersion == 4)
        {
            document.SchemaVersion = 5;
            document.OpeningTypes = BuiltInOpeningTypes.CreateDefaults();
            for (var index = 0; index < document.Openings.Count; index++)
            {
                var opening = document.Openings[index];
                var type = OpeningTypeRules.FindOrCreate(document.OpeningTypes, opening.Kind,
                    opening.Width, opening.Height, opening.Sill);
                document.Openings[index] = opening with { OpeningTypeId = type.Id };
            }
        }
        if (document.SchemaVersion == 5)
            document.SchemaVersion = 6;
        if (document.SchemaVersion == 6)
            document.SchemaVersion = 7;
        if (document.SchemaVersion == 7)
        {
            document.SchemaVersion = 8;
            document.Schedules ??= [];
        }
        if (document.SchemaVersion == 8)
        {
            // Schema 9 adds in-place families; earlier projects have none.
            document.SchemaVersion = 9;
            document.InPlaceFamilies ??= [];
            // Earlier built-in doors had no leaf. Preserve custom sizes, names, and nonzero depths.
            var defaults = BuiltInOpeningTypes.CreateDefaults().Where(type => type.Kind == OpeningKind.Door)
                .ToDictionary(type => type.Id);
            for (var index = 0; index < document.OpeningTypes.Count; index++)
            {
                var type = document.OpeningTypes[index];
                if (type.Kind == OpeningKind.Door && type.Depth == 0 && defaults.TryGetValue(type.Id, out var standard))
                    document.OpeningTypes[index] = type with { Depth = standard.Depth };
            }
        }
        // Schema 10 adds frame width and glazing layout to opening types, plus roof shapes, element styles and column shapes.
        if (document.SchemaVersion == 9) document.SchemaVersion = 10;
        // Schema 11 adds toposolids (a floor kind with elevation points) and render material overrides.
        if (document.SchemaVersion == 10)
        {
            document.SchemaVersion = 11;
            document.RenderMaterials ??= [];
        }
        // Schema 12 adds the electrical system: devices, switch links and circuits.
        if (document.SchemaVersion == 11)
        {
            document.SchemaVersion = 12;
            document.ElectricalDevices ??= [];
            document.ElectricalCircuits ??= [];
        }
        // Schema 13 adds finite section cutting lines and project-wide reference axes.
        if (document.SchemaVersion == 12)
        {
            document.SchemaVersion = 13;
            document.Grids ??= [];
        }
        // Schema 14 adds plumbing: water meters, sewer outlets and the cold, hot and sanitary systems.
        if (document.SchemaVersion == 13)
        {
            document.SchemaVersion = 14;
            document.PlumbingPoints ??= [];
            document.PlumbingSystems ??= [];
        }
        // Schema 15 adds protected base datums and editable drawing-sheet families and compositions.
        if (document.SchemaVersion == 14)
        {
            document.SchemaVersion = 15;
            BaseLevels.Ensure(document);
            document.SheetFamilies ??= [];
            SheetFamilies.Ensure(document);
        }

    }

    /// <summary>
    /// Saves an official .g3d project, or the older .g3 format when an existing
    /// compatibility path is explicitly supplied.
    /// </summary>
    public static void Save(string path, BuildingDocument document)
    {
        if (IsOfficialPath(path))
        {
            NativeDocumentStore.SaveProject(path, document);
            return;
        }
        if (!IsPreviewPath(path))
            throw new InvalidDataException("Native Guanacaste 3D projects must use the .g3d extension.");

        document.Validate();
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full)
            ?? throw new InvalidDataException("The file path is invalid.");
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException(directory);

        var json = JsonSerializer.Serialize(document, Options);
        var content = PreviewHeader + json;
        if (Encoding.UTF8.GetByteCount(content) > MaximumBytes)
            throw new InvalidDataException("The project exceeds the 20 MB preview-format limit.");

        var temp = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temp, content, new UTF8Encoding(false));
            if (File.Exists(full))
                File.Copy(full, full + ".bak", overwrite: true);
            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
