using Guanacaste3D.Core;

static class NativeDocumentChecks
{
    public static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "guanacaste-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            CheckProjectContainer(root, check);
            CheckRenderEnvironmentSetup(root, check);
            CheckFamilyContainer(root, check);
            CheckTemplateContainer(root, check);
            CheckDependencyCycles(check);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void CheckProjectContainer(string root, Action<bool, string> check)
    {
        var project = BuildingDocument.Create("Native Project");
        project.RenderSetup = new Guanacaste3D.Core.Rendering.RenderSetup { StoneMicrogeometry = true };
        var path = Path.Combine(root, "native-project.g3d");
        ProjectFile.Save(path, project);

        var info = NativeDocumentStore.Inspect(path);
        check(info.Kind == NativeDocumentKind.Project &&
              info.SchemaVersion == BuildingDocument.CurrentSchemaVersion &&
              info.DocumentId == project.Id &&
              info.UncompressedBytes > 0 && info.CompressedBytes > 0 &&
              info.Sha256.Length == 64,
            ".G3D writes a versioned compressed container with identity and SHA-256 integrity");

        var reopened = ProjectFile.Load(path);
        check(reopened.Id == project.Id && reopened.Name == project.Name,
            ".G3D project survives a validated native round trip");
        check(reopened.RenderSetup?.StoneMicrogeometry == true,
            "Render-only stone detail preference survives the compressed .g3d round trip");
        check(!new Guanacaste3D.Core.Rendering.RenderSetup().StoneMicrogeometry,
            "Legacy render setup defaults to lightweight stone shading");

        var disguised = Path.Combine(root, "native-project.bin");
        File.Copy(path, disguised);
        check(ProjectFile.Load(disguised).Id == project.Id,
            "Native project detection uses the file signature instead of depending only on its extension");

        ProjectFile.Save(path, project);
        check(File.Exists(path + ".bak"), ".G3D atomic overwrite preserves the previous project as a backup");

        var wrongKindRejected = false;
        try { NativeDocumentStore.LoadFamily(path); }
        catch (InvalidDataException) { wrongKindRejected = true; }
        check(wrongKindRejected, "Native containers reject loading a project as a family");
    }

    private static void CheckRenderEnvironmentSetup(string root, Action<bool, string> check)
    {
        var project = BuildingDocument.Create("HDRI portable test");
        project.RenderSetup = new Guanacaste3D.Core.Rendering.RenderSetup
        {
            EnvironmentPath = "assets/softbox.hdr", EnvironmentYawDegrees = 172.5,
            EnvironmentExposureStops = -0.75
        };
        var path = Path.Combine(root, "hdri-preserved.g3d");
        ProjectFile.Save(path, project);
        var saved = ProjectFile.Load(path).RenderSetup;
        check(saved?.EnvironmentPath == "assets/softbox.hdr" &&
              saved.EnvironmentYawDegrees == 172.5 &&
              saved.EnvironmentExposureStops == -0.75,
            "HDRI source, sky rotation and exposure persist through compressed .g3d save/load");

        var rejectsInvalid = false;
        try { (saved! with { EnvironmentYawDegrees = float.PositiveInfinity }).Validate(); }
        catch (InvalidDataException) { rejectsInvalid = true; }
        check(rejectsInvalid, "Corrupted HDRI sky rotation is rejected on project load");

        rejectsInvalid = false;
        try { (saved! with { EnvironmentPath = new string('x', 1025) }).Validate(); }
        catch (InvalidDataException) { rejectsInvalid = true; }
        check(rejectsInvalid, "Oversized external asset references are rejected during validation");
    }

    private static void CheckFamilyContainer(string root, Action<bool, string> check)
    {
        var door = BuiltInFamilyLibrary.CreateArchitecturalDoor();
        var width = door.Parameters.Single(parameter => parameter.Name == "Ancho nominal");
        var handle = door.Parameters.Single(parameter => parameter.Name == "Altura del eje de manija");
        var backset = door.Parameters.Single(parameter => parameter.Name == "Distancia de entrada de cerradura");

        check(Math.Abs(width.DefaultNumber!.Value - 0.9) < 1e-12 &&
              Math.Abs(handle.DefaultNumber!.Value - 1.0) < 1e-12 &&
              Math.Abs(backset.DefaultNumber!.Value - 0.06) < 1e-12,
            "Built-in architectural door starts at 900×2100 mm with 1000 mm handle height and 60 mm lock backset");
        check(door.Operations.Any(operation => operation.Name == "Hueco de rejilla") &&
              door.Operations.Any(operation => operation.Name == "Alojamiento de cerradura") &&
              door.Operations.Any(operation => operation.Name == "Perforación de eje de manija") &&
              door.Operations.Any(operation => operation.Name == "Alojamientos de bisagras") &&
              door.NestedFamilies.Any(family => family.FamilyId == BuiltInFamilyLibrary.MortiseLockId),
            "Door family persists explicit grille, lock perforation, hinge and nested-hardware operations");

        var familyPath = Path.Combine(root, "architectural-door.g3df");
        NativeDocumentStore.SaveFamily(familyPath, door);
        var loaded = NativeDocumentStore.LoadFamily(familyPath);
        check(loaded.Id == door.Id && loaded.Operations.Count == door.Operations.Count &&
              loaded.NestedFamilies.Count == door.NestedFamilies.Count,
            ".G3Df preserves the parametric family operation graph and nested-family references");

        var bytes = File.ReadAllBytes(familyPath);
        bytes[^1] ^= 0x5A;
        var corrupt = Path.Combine(root, "corrupt-family.g3df");
        File.WriteAllBytes(corrupt, bytes);
        var corruptionRejected = false;
        try { NativeDocumentStore.LoadFamily(corrupt); }
        catch (InvalidDataException) { corruptionRejected = true; }
        check(corruptionRejected, ".G3Df rejects a modified or corrupted payload");

        var window = BuiltInFamilyLibrary.CreateArchitecturalWindow();
        check(window.Category == FamilyCategory.Windows &&
              Math.Abs(window.Parameters.Single(parameter => parameter.Name == "Ancho").DefaultNumber!.Value - 1.2) < 1e-12 &&
              window.Operations.Any(operation => operation.Name == "Hojas corredizas"),
            "Built-in window foundation starts at 1200×1200 mm with an extensible sliding-leaf operation");
    }

    private static void CheckTemplateContainer(string root, Action<bool, string> check)
    {
        var template = ProjectTemplateDocument.CreateMetricArchitecture();
        var path = Path.Combine(root, "architecture-metric.g3dt");
        NativeDocumentStore.SaveTemplate(path, template);
        var loaded = NativeDocumentStore.LoadTemplate(path);
        var project = loaded.CreateProject("Project from template");

        check(loaded.LengthUnit == TemplateLengthUnit.Millimeter &&
              loaded.PreloadedFamilies.Any(family => family.FamilyId == BuiltInFamilyLibrary.ArchitecturalDoorId) &&
              project.Levels.Count == loaded.Levels.Count &&
              project.Views.Any(view => view.Kind == ProjectViewKind.Plan),
            ".G3Dt restores metric settings, preloaded-family references and creates an independent project");

        var originalTemplateName = loaded.Levels[0].Name;
        project.Levels[0] = project.Levels[0] with { Name = "Project-only level" };
        check(loaded.Levels[0].Name == originalTemplateName,
            "Editing a project created from .G3Dt does not mutate the source template");
    }

    private static void CheckDependencyCycles(Action<bool, string> check)
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var family = new FamilyDocument
        {
            Name = "Cycle Test",
            Types = [new(Guid.NewGuid(), "Default", [])],
            Operations =
            [
                new(first, "A", FamilyOperationKind.PrimitiveBox) { DependsOn = [second] },
                new(second, "B", FamilyOperationKind.PrimitiveBox) { DependsOn = [first] }
            ]
        };

        var rejected = false;
        try { family.Validate(); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "Family regeneration graph rejects circular operation dependencies");

        var parameterA = new FamilyParameterDefinition(Guid.NewGuid(), "A", FamilyParameterKind.Number,
            FamilyParameterScope.Calculated) { Formula = "B" };
        var parameterB = new FamilyParameterDefinition(Guid.NewGuid(), "B", FamilyParameterKind.Number,
            FamilyParameterScope.Calculated) { Formula = "A" };
        var formulaCycle = new FamilyDocument
        {
            Name = "Formula Cycle",
            Parameters = [parameterA, parameterB],
            Types = [new(Guid.NewGuid(), "Default", [])]
        };
        rejected = false;
        try { formulaCycle.Validate(); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "Family parameter formulas reject circular dependencies");

        var width = new FamilyParameterDefinition(Guid.NewGuid(), "Width", FamilyParameterKind.Length,
            FamilyParameterScope.Type) { DefaultNumber = 1.0 };
        var invalidLength = new FamilyParameterDefinition(Guid.NewGuid(), "InvalidLength", FamilyParameterKind.Length,
            FamilyParameterScope.Calculated) { Formula = "Width * Width" };
        var dimensionalMismatch = new FamilyDocument
        {
            Name = "Dimensional Formula",
            Parameters = [width, invalidLength],
            Types = [new(Guid.NewGuid(), "Default", [])]
        };
        rejected = false;
        try { dimensionalMismatch.Validate(); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "Family formulas enforce dimensional consistency");
    }
}
