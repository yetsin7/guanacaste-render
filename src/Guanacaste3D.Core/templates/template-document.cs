namespace Guanacaste3D.Core;

/// <summary>
/// Reads and writes .g3dt project templates. A template is a project document that
/// holds only setup: levels, wall and opening types, parameter definitions, views,
/// schedules, and sheets. It never holds model elements or parameter values, so a
/// project created from it cannot inherit geometry, and editing that project cannot
/// change the template file.
/// </summary>
public static class TemplateFile
{
    /// <summary>File extension of project templates.</summary>
    public const string Extension = ".g3dt";

    /// <summary>Returns whether a path uses the template extension. The file signature is still checked on load.</summary>
    public static bool IsTemplatePath(string path) =>
        string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Loads, migrates, and validates a template. Files of any other kind are rejected.</summary>
    public static BuildingDocument Load(string path)
    {
        if (!IsTemplatePath(path))
            throw new InvalidDataException("Guanacaste 3D templates must use the .g3dt extension.");
        var payload = NativeContainer.Open(NativeContainer.ReadText(path));
        if (payload.Signature != NativeContainer.TemplateSignature)
            throw new InvalidDataException("The file is not a Guanacaste 3D template.");
        if (payload.SchemaVersion is < 1 or > BuildingDocument.CurrentSchemaVersion)
            throw new InvalidDataException("Unsupported template schema version.");
        var template = ProjectFile.DeserializeDocument(payload.Json);
        RejectModelContent(template);
        return template;
    }

    /// <summary>Validates and atomically writes a template built from a project's setup.</summary>
    public static void Save(string path, BuildingDocument document)
    {
        if (!IsTemplatePath(path))
            throw new InvalidDataException("Guanacaste 3D templates must use the .g3dt extension.");
        document.Validate();
        var template = TemplateRules.StripModelContent(document);
        var json = ProjectFile.SerializeDocument(template);
        NativeContainer.WriteAtomically(path,
            NativeContainer.Seal(NativeContainer.TemplateSignature, BuildingDocument.CurrentSchemaVersion, json));
    }

    /// <summary>Rejects a template that carries geometry or values, which only a project may hold.</summary>
    private static void RejectModelContent(BuildingDocument template)
    {
        if (template.Walls.Count + template.Openings.Count + template.Rooms.Count + template.Floors.Count +
            template.Columns.Count + template.StructuralElements.Count + template.ParameterValues.Count +
            template.InPlaceFamilies.Count + template.Dimensions.Count > 0)
            throw new InvalidDataException("A template cannot contain model elements or parameter values.");
    }
}

/// <summary>Rules that turn a template into a new project and that keep templates free of model content.</summary>
public static class TemplateRules
{
    /// <summary>
    /// Returns a copy of a project that keeps its setup and removes its model content,
    /// so the result can be saved as a template.
    /// </summary>
    public static BuildingDocument StripModelContent(BuildingDocument document)
    {
        var template = document.Clone();
        template.Dimensions = [];
        template.Walls = [];
        template.Openings = [];
        template.Rooms = [];
        template.Floors = [];
        template.Columns = [];
        template.StructuralElements = [];
        template.ParameterValues = [];
        template.InPlaceFamilies = [];
        return template;
    }

    /// <summary>
    /// Creates a new, independent project from a template. The copy has its own identifier
    /// and name; changing it later leaves the template untouched because it shares no state.
    /// </summary>
    public static BuildingDocument CreateProject(BuildingDocument template, string name)
    {
        template.Validate();
        var project = template.Clone();
        project.Id = Guid.NewGuid();
        project.Name = name.Trim();
        project.Validate();
        return project;
    }
}

/// <summary>Templates that ship with Guanacaste 3D and are defined in code, not in a file.</summary>
public static class OfficialTemplates
{
    /// <summary>Stable identifier of the metric architecture template.</summary>
    public static readonly Guid ArchitecturalMetricId = Guid.Parse("c2f6a1d8-7e3b-4b9a-8d52-5a1e0f6b9c71");

    /// <summary>Display name of the metric architecture template.</summary>
    public const string ArchitecturalMetricName = "Guanacaste 3D — Arquitectura Métrica";

    /// <summary>
    /// Builds the metric architecture template: protected floor/roof datums, built-in wall and opening
    /// catalogs, and the default views. Model lengths are meters; the template has no unit
    /// setting, so every project it creates stays metric.
    /// </summary>
    public static BuildingDocument CreateArchitecturalMetric()
    {
        var document = BuildingDocument.Create(ArchitecturalMetricName);
        document.Id = ArchitecturalMetricId;
        return document;
    }
}
