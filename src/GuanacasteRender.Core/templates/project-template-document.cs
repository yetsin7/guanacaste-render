namespace Guanacaste3D.Core;

public enum TemplateLengthUnit { Millimeter, Centimeter, Meter, Inch, Foot }

public sealed record TemplateLevel(string Name, double Elevation);
public sealed record TemplateMaterial(string Name, string Category);
public sealed record TemplateFamilyReference(Guid FamilyId, string Name, string? SourcePath);

public sealed class ProjectTemplateDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled Template";
    public TemplateLengthUnit LengthUnit { get; set; } = TemplateLengthUnit.Millimeter;
    public int DisplayPrecision { get; set; } = 1;
    public List<TemplateLevel> Levels { get; set; } = [];
    public List<TemplateMaterial> Materials { get; set; } = [];
    public List<TemplateFamilyReference> PreloadedFamilies { get; set; } = [];
    public List<string> ViewTemplates { get; set; } = [];
    public List<string> SheetBases { get; set; } = [];
    public Dictionary<string, string> Settings { get; set; } = [];

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException("Unsupported template schema version.");
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 160 ||
            !Enum.IsDefined(LengthUnit) || DisplayPrecision is < 0 or > 8 ||
            Levels is null || Materials is null || PreloadedFamilies is null ||
            ViewTemplates is null || SheetBases is null || Settings is null ||
            Levels.Count is < 1 or > 200 || Materials.Count > 10000 || PreloadedFamilies.Count > 20000)
            throw new InvalidDataException("The template identity or settings are invalid.");

        if (Levels.Any(level => string.IsNullOrWhiteSpace(level.Name) || level.Name.Length > 100 ||
                                !double.IsFinite(level.Elevation)) ||
            Levels.Select(level => level.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Levels.Count)
            throw new InvalidDataException("The template level collection is invalid.");

        if (Materials.Any(material => string.IsNullOrWhiteSpace(material.Name) || material.Name.Length > 120 ||
                                     material.Category is null || material.Category.Length > 120) ||
            Materials.Select(material => material.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Materials.Count)
            throw new InvalidDataException("The template material collection is invalid.");

        if (PreloadedFamilies.Any(family => family.FamilyId == Guid.Empty || string.IsNullOrWhiteSpace(family.Name) ||
                                           family.Name.Length > 160 || (family.SourcePath?.Length ?? 0) > 1024) ||
            PreloadedFamilies.Select(family => family.FamilyId).Distinct().Count() != PreloadedFamilies.Count)
            throw new InvalidDataException("The template family references are invalid.");

        if (ViewTemplates.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > 120) ||
            SheetBases.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > 120) ||
            Settings.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 120 ||
                                 pair.Value is null || pair.Value.Length > 4000))
            throw new InvalidDataException("The template contains an invalid named setting.");
    }

    /// <summary>Creates an independent project and restores missing protected base datums from older templates.</summary>
    public BuildingDocument CreateProject(string projectName)
    {
        Validate();
        if (string.IsNullOrWhiteSpace(projectName) || projectName.Length > 120)
            throw new ArgumentException("A project name is required.", nameof(projectName));

        var document = BuildingDocument.Create(projectName);
        document.Levels = Levels.Select(level => new Level(Guid.NewGuid(), level.Name, level.Elevation)).ToList();
        document.Views = BuiltInProjectViews.CreateDefaults(document.Levels);
        BaseLevels.Ensure(document);
        document.Validate();
        return document;
    }

    public static ProjectTemplateDocument CreateMetricArchitecture()
    {
        var template = new ProjectTemplateDocument
        {
            Name = "Guanacaste 3D — Arquitectura Métrica",
            LengthUnit = TemplateLengthUnit.Millimeter,
            DisplayPrecision = 1,
            Levels = [new("Nivel 1", 0), new("Nivel 2", 3.0)],
            Materials =
            [
                new("Concreto", "Estructura"),
                new("Madera", "Arquitectura"),
                new("Vidrio", "Acristalamiento")
            ],
            PreloadedFamilies =
            [
                new(BuiltInFamilyLibrary.ArchitecturalDoorId, "Puerta Arquitectónica Paramétrica — Guanacaste 3D", null),
                new(BuiltInFamilyLibrary.ArchitecturalWindowId, "Ventana Arquitectónica Paramétrica — Guanacaste 3D", null)
            ],
            ViewTemplates = ["Arquitectura — Planta", "Arquitectura — Coordinación 3D"],
            SheetBases = ["A1 — Arquitectura"],
            Settings = new()
            {
                ["Units.System"] = "Metric",
                ["Naming.LevelPrefix"] = "Nivel",
                ["BIM.Discipline"] = "Architecture"
            }
        };
        template.Validate();
        return template;
    }
}
