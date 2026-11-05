using System.Text.Json;
using Guanacaste3D.Core;

/// <summary>
/// Regression checks for the native family (.G3Df) and template (.G3Dt) containers:
/// signatures and checksums, file kind taken from the signature rather than the
/// extension, family import as one undoable transaction, and templates that carry
/// setup only, with projects that stay independent of them.
/// </summary>
static class FamilyTemplateChecks
{
    /// <summary>Runs every family and template check in sequence in a private scratch folder.</summary>
    public static void Run(Action<bool, string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), $"g3-containers-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            CheckFamilyContainer(check, folder);
            CheckFamilyImport(check);
            CheckTemplateContainer(check, folder);
            CheckTemplateProjects(check, folder);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>Returns the message of the invalid-data error an action raises, or null when it succeeds.</summary>
    private static string? ErrorOf(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (InvalidDataException error)
        {
            return error.Message;
        }
    }

    /// <summary>Verifies the family file envelope, checksum, schema version, and kind identity.</summary>
    private static void CheckFamilyContainer(Action<bool, string> check, string folder)
    {
        var family = OpeningFamilyDocument.CreateStandard(OpeningKind.Door, "Puerta de prueba");
        check(family.Types.Count == 1 && family.Types[0].Width == 0.9 && family.Types[0].Depth == 0.04,
            "A new family starts with one standard 900 mm door type that has a leaf");
        var path = Path.Combine(folder, "door.G3Df");
        FamilyFile.Save(path, family);
        var loaded = FamilyFile.Load(path);
        check(loaded.Id == family.Id && loaded.Name == family.Name && loaded.Types.Single().Id == family.Types[0].Id,
            "Family round trip keeps the family and type identifiers");
        check(File.ReadLines(path).First().StartsWith("G3-FAMILY/1 sha256=", StringComparison.Ordinal),
            "Family files open with the family signature and a SHA-256 checksum");

        var text = File.ReadAllText(path);
        var tamperedPath = Path.Combine(folder, "tampered.G3Df");
        var tampered = text.Replace("\"Width\": 0.9", "\"Width\": 0.8", StringComparison.Ordinal);
        File.WriteAllText(tamperedPath, tampered);
        check(tampered != text && (ErrorOf(() => FamilyFile.Load(tamperedPath)) ?? "").Contains("checksum"),
            "A family whose content changed after saving fails its checksum");

        var projectPath = Path.Combine(folder, "identity.g3d");
        ProjectFile.Save(projectPath, BuildingDocument.Create("Identidad"));
        var projectAsFamily = Path.Combine(folder, "project-renamed.G3Df");
        File.Copy(projectPath, projectAsFamily, overwrite: true);
        check((ErrorOf(() => FamilyFile.Load(projectAsFamily)) ?? "").Contains("signature"),
            "A project renamed to .G3Df is rejected because its signature is not a family");

        var templatePath = Path.Combine(folder, "identity.G3Dt");
        TemplateFile.Save(templatePath, BuildingDocument.Create("Identidad"));
        var templateAsFamily = Path.Combine(folder, "template-renamed.G3Df");
        File.Copy(templatePath, templateAsFamily, overwrite: true);
        check((ErrorOf(() => FamilyFile.Load(templateAsFamily)) ?? "").Contains("not a Guanacaste 3D family"),
            "A template renamed to .G3Df is rejected as a family");
        var familyAsTemplate = Path.Combine(folder, "family-renamed.G3Dt");
        File.Copy(path, familyAsTemplate, overwrite: true);
        check((ErrorOf(() => TemplateFile.Load(familyAsTemplate)) ?? "").Contains("not a Guanacaste 3D template"),
            "A family renamed to .G3Dt is rejected as a template");
        var familyAsProject = Path.Combine(folder, "family-renamed.g3d");
        File.Copy(path, familyAsProject, overwrite: true);
        check((ErrorOf(() => ProjectFile.Load(familyAsProject)) ?? "").Contains("signature"),
            "A family renamed to .g3d is rejected as a project because its signature is not the project container's");

        var futurePath = Path.Combine(folder, "future.G3Df");
        var futureJson = JsonSerializer.Serialize(family, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(futurePath, NativeContainer.Seal(NativeContainer.FamilySignature, 2, futureJson));
        check((ErrorOf(() => FamilyFile.Load(futurePath)) ?? "").Contains("Unsupported family schema"),
            "A family from a newer schema is rejected instead of being misread");

        FamilyFile.Save(path, family);
        check(File.Exists(path + ".bak"), "Saving a family over an existing file keeps a backup");
        check(ErrorOf(() => FamilyFile.Save(Path.Combine(folder, "door.txt"), family)) is not null,
            "Families cannot be saved under a non-family extension");
    }

    /// <summary>Verifies building a family from project types and importing it as one undoable transaction.</summary>
    private static void CheckFamilyImport(Action<bool, string> check)
    {
        var source = new DocumentSession(BuildingDocument.Create("Origen"));
        source.AddOpeningType(OpeningKind.Window, "Ventana de taller", "Corrediza 1000", 1.0, 1.1, 0.9, "Aluminio");
        source.AddOpeningType(OpeningKind.Window, "Ventana de taller", "Corrediza 1200", 1.2, 1.1, 0.9);
        var extracted = OpeningFamilyDocument.FromProject(source.Document, "Ventana de taller");
        check(extracted.Category == OpeningKind.Window && extracted.Types.Count == 2 &&
              extracted.Types.All(type => source.Document.OpeningTypes.Any(existing => existing.Id == type.Id)),
            "A family extracted from a project keeps the identifiers of its types");
        check(ErrorOf(() => OpeningFamilyDocument.FromProject(source.Document, "Inexistente")) is not null,
            "Extracting a family that the project does not contain is rejected");

        var target = new DocumentSession(BuildingDocument.Create("Destino"));
        var before = target.Document.OpeningTypes.Count;
        target.ImportOpeningFamily(extracted);
        var imported = target.Document.OpeningTypes.Count(type => type.FamilyName == "Ventana de taller");
        check(target.Document.OpeningTypes.Count == before + 2 && imported == 2,
            "Importing a family adds its types to the project catalog");
        target.ImportOpeningFamily(extracted);
        check(target.Document.OpeningTypes.Count == before + 2,
            "Importing the same family again reuses its types instead of duplicating them");

        var undoable = new DocumentSession(BuildingDocument.Create("Deshacer"));
        var undoBefore = undoable.Document.OpeningTypes.Count;
        undoable.ImportOpeningFamily(extracted);
        undoable.Undo();
        check(undoable.Document.OpeningTypes.Count == undoBefore,
            "Undo removes an imported family as one transaction");
        undoable.Redo();
        check(undoable.Document.OpeningTypes.Count(type => type.FamilyName == "Ventana de taller") == 2,
            "Redo restores the imported family");

        var clash = new DocumentSession(BuildingDocument.Create("Conflicto"));
        clash.AddOpeningType(OpeningKind.Window, "Ventana de taller", "Corrediza 1000", 1.5, 1.5, 0.9);
        clash.ImportOpeningFamily(extracted);
        check(clash.Document.OpeningTypes.Any(type => type.FamilyName == "Ventana de taller" && type.Name == "Corrediza 1000 (2)"),
            "A name clash with a different size receives a numeric suffix");
        check(clash.Document.OpeningTypes.Count(type => type.Name == "Corrediza 1000") == 1,
            "The existing type with the clashing name keeps its name and size");

        var collision = new DocumentSession(BuildingDocument.Create("Identificador"));
        var builtInId = BuiltInOpeningTypes.FixedWindow1200Id;
        var foreignFamily = new OpeningFamilyDocument
        {
            Name = "Familia ajena",
            Category = OpeningKind.Window,
            Types = [new OpeningType(builtInId, OpeningKind.Window, "Familia ajena", "Prueba", 1.0, 1.0, 0.9)]
        };
        collision.ImportOpeningFamily(foreignFamily);
        var foreign = collision.Document.OpeningTypes.Single(type => type.FamilyName == "Familia ajena");
        check(foreign.Id != builtInId && collision.Document.OpeningTypes.Count(type => type.Id == builtInId) == 1,
            "An imported type whose identifier is already used receives a new identifier");

        var countBeforeBad = target.Document.OpeningTypes.Count;
        var badFamily = new OpeningFamilyDocument
        {
            Name = "Mezcla",
            Category = OpeningKind.Window,
            Types = [new OpeningType(Guid.NewGuid(), OpeningKind.Door, "Mezcla", "Puerta", 0.9, 2.1, 0)]
        };
        check(ErrorOf(() => target.ImportOpeningFamily(badFamily)) is not null &&
              target.Document.OpeningTypes.Count == countBeforeBad,
            "A family that mixes a door into a window family is rejected without changing the project");
    }

    /// <summary>Verifies that a template keeps setup, rejects model content, and round-trips with its identity.</summary>
    private static void CheckTemplateContainer(Action<bool, string> check, string folder)
    {
        var session = new DocumentSession(BuildingDocument.Create("Plantilla de prueba"));
        var levelId = session.Document.Levels[0].Id;
        session.AddWall(levelId, new Point2(0, 0), new Point2(5, 0));
        var wallId = session.Document.Walls.Single().Id;
        session.AddOpening(wallId, OpeningKind.Door, offset: 2.0);
        session.AddOpeningType(OpeningKind.Door, "Puerta de taller", "Doble 1500", 1.5, 2.1, 0);

        var path = Path.Combine(folder, "metrica.G3Dt");
        TemplateFile.Save(path, session.Document);
        check(File.ReadLines(path).First().StartsWith($"G3-TEMPLATE/{BuildingDocument.CurrentSchemaVersion} sha256=", StringComparison.Ordinal),
            "Template files open with the template signature and a SHA-256 checksum");
        var template = TemplateFile.Load(path);
        check(template.Walls.Count == 0 && template.Openings.Count == 0 && template.ParameterValues.Count == 0,
            "A template keeps setup but not walls, openings, or parameter values");
        check(template.Levels.Count == session.Document.Levels.Count &&
              template.WallTypes.Count == session.Document.WallTypes.Count &&
              template.OpeningTypes.Any(type => type.Name == "Doble 1500"),
            "A template keeps levels, wall types, and the custom opening catalog");
        check(template.Id == session.Document.Id,
            "A template keeps the identifier of the document it was saved from");

        var contaminated = Path.Combine(folder, "contaminada.G3Dt");
        File.WriteAllText(contaminated, NativeContainer.Seal(NativeContainer.TemplateSignature,
            BuildingDocument.CurrentSchemaVersion, ProjectFile.SerializeDocument(session.Document)));
        check((ErrorOf(() => TemplateFile.Load(contaminated)) ?? "").Contains("cannot contain model elements"),
            "A template file that carries walls or openings is rejected");

        var metric = OfficialTemplates.CreateArchitecturalMetric();
        var metricPath = Path.Combine(folder, "Guanacaste 3D — Arquitectura Métrica.G3Dt");
        TemplateFile.Save(metricPath, metric);
        var loadedMetric = TemplateFile.Load(metricPath);
        check(loadedMetric.Id == OfficialTemplates.ArchitecturalMetricId &&
              loadedMetric.Name == OfficialTemplates.ArchitecturalMetricName &&
              loadedMetric.WallTypes.Count > 0 && loadedMetric.OpeningTypes.Count > 0,
            "The official metric template keeps its identity and built-in catalogs through a round trip");

        var familyCopy = Path.Combine(folder, "familia-como-plantilla.G3Dt");
        FamilyFile.Save(Path.Combine(folder, "familia.G3Df"), OpeningFamilyDocument.CreateStandard(OpeningKind.Door, "Familia"));
        File.Copy(Path.Combine(folder, "familia.G3Df"), familyCopy, overwrite: true);
        check((ErrorOf(() => TemplateFile.Load(familyCopy)) ?? "").Contains("not a Guanacaste 3D template"),
            "A family file renamed to .G3Dt is rejected as a template");
    }

    /// <summary>Verifies that projects created from templates are independent of the template.</summary>
    private static void CheckTemplateProjects(Action<bool, string> check, string folder)
    {
        var path = Path.Combine(folder, "metrica-proyectos.G3Dt");
        TemplateFile.Save(path, OfficialTemplates.CreateArchitecturalMetric());
        var template = TemplateFile.Load(path);
        var project = TemplateRules.CreateProject(template, "  Casa de prueba  ");
        check(project.Name == "Casa de prueba" && project.Id != template.Id && project.Levels.Count == template.Levels.Count,
            "A project created from a template has its own identifier and name and inherits the setup");
        check(project.Walls.Count == 0 && project.OpeningTypes.Count == template.OpeningTypes.Count,
            "A project created from a template starts with no model elements");

        var session = new DocumentSession(project);
        session.AddWall(project.Levels[0].Id, new Point2(0, 0), new Point2(4, 0));
        session.AddOpeningType(OpeningKind.Window, "Ventana propia", "Solo del proyecto", 0.7, 0.9, 1.0);
        check(template.Walls.Count == 0 && template.OpeningTypes.All(type => type.FamilyName != "Ventana propia"),
            "Editing a project does not change the template in memory");

        var reloaded = TemplateFile.Load(path);
        check(reloaded.Walls.Count == 0 && reloaded.OpeningTypes.Count == template.OpeningTypes.Count &&
              reloaded.OpeningTypes.All(type => type.FamilyName != "Ventana propia"),
            "Editing a project does not change the template file");
        check(ErrorOf(() => TemplateRules.CreateProject(template, new string(' ', 3))) is not null,
            "A project cannot be created from a template without a name");
    }
}
