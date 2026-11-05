using System.Globalization;
using System.Text.Json;

namespace Guanacaste3D.Core;

/// <summary>
/// A reusable door or window family saved as a .g3df file. It groups the types of
/// one category under one name, so the same family can be loaded into any project.
/// Every type obeys the project catalog rules, because the family is validated with
/// <see cref="OpeningTypeRules"/> rather than a second set of limits.
/// </summary>
public sealed class OpeningFamilyDocument
{
    /// <summary>Current family payload schema.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Largest number of types one family may hold.</summary>
    public const int MaximumTypes = 500;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public OpeningKind Category { get; set; }
    public List<OpeningType> Types { get; set; } = [];

    /// <summary>Creates a family with one standard type of the category, ready to be edited.</summary>
    public static OpeningFamilyDocument CreateStandard(OpeningKind category, string name)
    {
        var trimmed = name.Trim();
        var type = category == OpeningKind.Door
            ? new OpeningType(Guid.NewGuid(), category, trimmed, SizeName(0.9, 2.1), 0.9, 2.1, 0) { Depth = 0.04 }
            : new OpeningType(Guid.NewGuid(), category, trimmed, SizeName(1.2, 1.2), 1.2, 1.2, 0.9);
        return new OpeningFamilyDocument { Name = trimmed, Category = category, Types = [type] };
    }

    /// <summary>
    /// Extracts the project types of one family into a new family document. The
    /// types keep their identifiers, so importing the file back into the same
    /// project recognizes them instead of creating duplicates.
    /// </summary>
    public static OpeningFamilyDocument FromProject(BuildingDocument document, string familyName)
    {
        var types = document.OpeningTypes.Where(type => type.FamilyName == familyName).ToList();
        if (types.Count == 0)
            throw new InvalidDataException("The family has no types in this project.");
        if (types.Select(type => type.Kind).Distinct().Count() != 1)
            throw new InvalidDataException("A family cannot mix doors and windows.");
        return new OpeningFamilyDocument
        {
            Name = familyName,
            Category = types[0].Kind,
            Types = types
        };
    }

    /// <summary>Rejects an invalid family before it is saved, imported, or shown.</summary>
    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException("Unsupported family schema version.");
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 120 || !Enum.IsDefined(Category))
            throw new InvalidDataException("The family identity, name, or category is invalid.");
        if (Types is null || Types.Count is < 1 or > MaximumTypes || Types.Any(type =>
                type is null || type.Kind != Category || type.FamilyName != Name))
            throw new InvalidDataException("A family must contain at least one type of its own category and name.");
        OpeningTypeRules.Validate(new BuildingDocument { OpeningTypes = Types });
    }

    /// <summary>Formats a size as the catalog does, for example "900 × 2100 mm".</summary>
    private static string SizeName(double width, double height) =>
        string.Create(CultureInfo.InvariantCulture, $"{width * 1000:0} × {height * 1000:0} mm");
}

/// <summary>Reads and writes .g3df family files. Identity comes from the signature line.</summary>
public static class FamilyFile
{
    /// <summary>File extension of family documents.</summary>
    public const string Extension = ".g3df";

    /// <summary>Returns whether a path uses the family extension. The file signature is still checked on load.</summary>
    public static bool IsFamilyPath(string path) =>
        string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase);

    private static readonly string[] RequiredFields = ["SchemaVersion", "Id", "Name", "Category", "Types"];
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = false };

    /// <summary>Loads and validates a family, rejecting files of any other kind or a damaged payload.</summary>
    public static OpeningFamilyDocument Load(string path)
    {
        if (!IsFamilyPath(path))
            throw new InvalidDataException("Guanacaste 3D families must use the .g3df extension.");
        var payload = NativeContainer.Open(NativeContainer.ReadText(path));
        if (payload.Signature != NativeContainer.FamilySignature)
            throw new InvalidDataException("The file is not a Guanacaste 3D family.");
        if (payload.SchemaVersion != OpeningFamilyDocument.CurrentSchemaVersion)
            throw new InvalidDataException("Unsupported family schema version.");
        NativeContainer.RequireObject(payload.Json, RequiredFields);
        var family = JsonSerializer.Deserialize<OpeningFamilyDocument>(payload.Json, Options)
            ?? throw new InvalidDataException("The family document is empty.");
        family.Validate();
        return family;
    }

    /// <summary>Validates and atomically writes a family, keeping the previous file as a backup.</summary>
    public static void Save(string path, OpeningFamilyDocument family)
    {
        if (!IsFamilyPath(path))
            throw new InvalidDataException("Guanacaste 3D families must use the .g3df extension.");
        family.Validate();
        var json = JsonSerializer.Serialize(family, Options);
        NativeContainer.WriteAtomically(path,
            NativeContainer.Seal(NativeContainer.FamilySignature, OpeningFamilyDocument.CurrentSchemaVersion, json));
    }
}

/// <summary>Adds a family's types to a project catalog. The result is validated as a whole.</summary>
public static class OpeningFamilyImport
{
    /// <summary>
    /// Merges a family into a project catalog. A type with the same family, name, kind,
    /// and size as one already present is reused. A new type keeps its identifier when
    /// that identifier is free; otherwise it receives a new one. A name clash with a
    /// different size is resolved by suffixing the name, following the catalog's rule.
    /// </summary>
    public static void Apply(BuildingDocument document, OpeningFamilyDocument family)
    {
        family.Validate();
        foreach (var incoming in family.Types)
        {
            var existing = document.OpeningTypes.FirstOrDefault(type =>
                type.Kind == incoming.Kind && type.FamilyName == family.Name &&
                MatchesImportedName(type.Name, incoming.Name) &&
                Math.Abs(type.Width - incoming.Width) <= OpeningTypeRules.SizeTolerance &&
                Math.Abs(type.Height - incoming.Height) <= OpeningTypeRules.SizeTolerance &&
                Math.Abs(type.DefaultSill - incoming.DefaultSill) <= OpeningTypeRules.SizeTolerance &&
                Math.Abs(type.Depth - incoming.Depth) <= OpeningTypeRules.SizeTolerance &&
                Math.Abs(type.FrameWidth - incoming.FrameWidth) <= OpeningTypeRules.SizeTolerance &&
                type.GlazingLightCount == incoming.GlazingLightCount &&
                type.MaterialName == incoming.MaterialName);
            if (existing is not null) continue;
            var id = document.OpeningTypes.Any(type => type.Id == incoming.Id) ? Guid.NewGuid() : incoming.Id;
            var name = UniqueName(document.OpeningTypes, family.Name, incoming.Name);
            document.OpeningTypes.Add(incoming with { Id = id, Name = name });
        }
    }

    /// <summary>Recognizes an earlier collision suffix so importing the same family is idempotent.</summary>
    private static bool MatchesImportedName(string existingName, string incomingName)
    {
        if (existingName == incomingName) return true;
        var prefix = incomingName + " (";
        return existingName.StartsWith(prefix, StringComparison.Ordinal) &&
            existingName.EndsWith(')') &&
            int.TryParse(existingName[prefix.Length..^1], out var suffix) && suffix >= 2;
    }

    /// <summary>Returns the type name, or the name with a numeric suffix when the family already uses it.</summary>
    private static string UniqueName(IEnumerable<OpeningType> types, string familyName, string name)
    {
        var taken = types.Where(type => type.FamilyName == familyName)
            .Select(type => type.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name)) return name;
        var suffix = 2;
        while (taken.Contains($"{name} ({suffix})")) suffix++;
        return $"{name} ({suffix})";
    }
}
