using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>
/// Regression checks for in-place families, which are <see cref="FamilyDocument"/> instances hosted in place
/// (schema 9): the category catalog, creation and naming, extrusions on horizontal and inclined work planes,
/// parameter regeneration, atomic rejection of invalid profiles, undo and redo, persistence in the .g3d container
/// and as .g3df, formula depths, and migration from schema 8.
/// </summary>
static class InPlaceFamilyChecks
{
    private const double Tolerance = 1e-6;
    private const string GenericModels = BimCategoryCatalog.GenericModelsId;

    /// <summary>A 2 m by 1 m rectangle in counter-clockwise order; its area is 2 m².</summary>
    private static readonly Point2[] TwoByOneRectangle = [new(0, 0), new(2, 0), new(2, 1), new(0, 1)];

    /// <summary>Runs every in-place family check in sequence.</summary>
    public static void Run(Action<bool, string> check)
    {
        CheckCategoryCatalog(check);
        CheckCreationAndNaming(check);
        CheckExtrusionVolumeAndWinding(check);
        CheckWorkPlaneOrientation(check);
        CheckParametricRegeneration(check);
        CheckRejectedChangesLeaveProjectUnchanged(check);
        CheckUndoRedoAndPersistence(check);
        CheckSchemaEightMigration(check);
        CheckSceneIntegration(check);
        CheckOfficialContainerKeepsFamilies(check);
        CheckUnifiedFamilyModel(check);
    }

    /// <summary>Creates a session on a new project so each check starts from an empty document.</summary>
    private static DocumentSession NewSession() => new(BuildingDocument.Create("In-place families"));

    /// <summary>Returns the rejection message when the action throws InvalidDataException, or null when it succeeds.</summary>
    private static string? RejectionMessage(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (InvalidDataException exception)
        {
            return exception.Message;
        }
    }

    /// <summary>A horizontal work plane at the given elevation, facing up.</summary>
    private static FamilyWorkPlane Horizontal(double elevation) => InPlaceFamilyRules.HorizontalPlane(elevation);

    /// <summary>A work plane with the given origin, normal and X axis, for tests that need a specific orientation.</summary>
    private static FamilyWorkPlane TestPlane(FamilyPoint3 origin, FamilyPoint3 normal, FamilyPoint3 xAxis) =>
        new(Guid.NewGuid(), "Plano de prueba", origin, normal, xAxis);

    /// <summary>Returns the single solid extrusion of a family.</summary>
    private static FamilyOperation OperationOf(FamilyDocument family) =>
        family.Operations.Single(operation => operation.Kind == FamilyOperationKind.SolidExtrusion);

    /// <summary>Returns the single length parameter of a family.</summary>
    private static FamilyParameterDefinition LengthOf(FamilyDocument family) =>
        family.Parameters.Single(parameter => parameter.Kind == FamilyParameterKind.Length);

    /// <summary>Returns the material name of an operation, or an empty string when it has none.</summary>
    private static string MaterialOf(FamilyDocument family, FamilyOperation operation) =>
        family.Parameters.FirstOrDefault(parameter => parameter.Id == operation.MaterialParameterId)?.DefaultText
        ?? string.Empty;

    /// <summary>Returns whether two work planes have the same origin, normal and X axis.</summary>
    private static bool SameGeometry(FamilyWorkPlane plane, FamilyWorkPlane other) =>
        plane.Origin == other.Origin && plane.Normal == other.Normal && plane.XAxis == other.XAxis;

    /// <summary>Verifies the registered categories and which ones accept in-place families.</summary>
    private static void CheckCategoryCatalog(Action<bool, string> check)
    {
        var categories = BimCategoryCatalog.All;
        check(categories.Count == 28 && categories.Select(category => category.Id).Distinct().Count() == 28,
            "The category catalog registers all 28 categories with unique stable identifiers");
        var inPlace = categories.Where(category => category.SupportsInPlace).Select(category => category.Id).ToArray();
        check(inPlace.SequenceEqual(new[] { GenericModels }),
            "Only Generic Models accepts in-place families in this increment");
        check(BimCategoryCatalog.Find(GenericModels)?.DefaultName == "Modelos genéricos",
            "Generic Models keeps its localizable display name");
    }

    /// <summary>Verifies family creation as a hosted-in-place FamilyDocument, trimmed storage, and the naming and category rules.</summary>
    private static void CheckCreationAndNaming(Action<bool, string> check)
    {
        var session = NewSession();
        var familyId = session.CreateInPlaceFamily(GenericModels, "  Pedestal  ");
        var family = session.Document.InPlaceFamilies.Single();
        check(family.Id == familyId && family.Name == "Pedestal" && family.Category == FamilyCategory.GenericModels,
            "A family created in Generic Models stores its trimmed name and category");
        check(family.Hosting == FamilyHostingBehavior.InPlace,
            "An in-place family is a FamilyDocument hosted in place, not a separate model");
        check(RejectionMessage(() => session.CreateInPlaceFamily("roofs", "Roof family")) is not null &&
              session.Document.InPlaceFamilies.Count == 1,
            "A category without in-place support is rejected without changing the project");
        check(RejectionMessage(() => session.CreateInPlaceFamily(GenericModels, "PEDESTAL")) is not null &&
              session.Document.InPlaceFamilies.Count == 1,
            "Family names are unique regardless of case");
        check(RejectionMessage(() => session.CreateInPlaceFamily(GenericModels, "   ")) is not null,
            "A blank family name is rejected");
        check(RejectionMessage(() => session.CreateInPlaceFamily("missing-category", "Ghost")) is not null,
            "An unknown category is rejected");
    }

    /// <summary>Verifies solid volume, face orientation, and winding normalization on a horizontal plane.</summary>
    private static void CheckExtrusionVolumeAndWinding(Action<bool, string> check)
    {
        var session = NewSession();
        var blockId = session.CreateInPlaceFamily(GenericModels, "Block");
        session.AddInPlaceExtrusion(blockId, Horizontal(0), TwoByOneRectangle, 0, 1.5,
            name: "Body", materialName: "Concrete");
        var block = FamilyGeometry.Tessellate(session.Document.InPlaceFamilies.Single());
        check(block.TriangleCount == 12, "A rectangular extrusion has eight side triangles and four cap triangles");
        check(Math.Abs(FamilyGeometry.SignedVolume(block) - 3.0) < Tolerance,
            "The signed volume equals profile area times depth (2 m² × 1.5 m), so faces point outward");

        var clockwise = Enumerable.Reverse(TwoByOneRectangle).ToArray();
        var clockwiseId = session.CreateInPlaceFamily(GenericModels, "Clockwise block");
        session.AddInPlaceExtrusion(clockwiseId, Horizontal(0), clockwise, 0, 1.0);
        var clockwiseMesh = FamilyGeometry.Tessellate(
            session.Document.InPlaceFamilies.Single(family => family.Id == clockwiseId));
        check(Math.Abs(FamilyGeometry.SignedVolume(clockwiseMesh) - 2.0) < Tolerance,
            "A clockwise profile is normalized, so it also encloses positive volume");

        const int sides = 32;
        const double radius = 0.5;
        var circle = Enumerable.Range(0, sides).Select(index =>
        {
            var angle = 2 * Math.PI * index / sides;
            return new Point2(radius * Math.Cos(angle), radius * Math.Sin(angle));
        }).ToArray();
        var circleArea = sides / 2.0 * radius * radius * Math.Sin(2 * Math.PI / sides);
        var columnId = session.CreateInPlaceFamily(GenericModels, "Column");
        session.AddInPlaceExtrusion(columnId, Horizontal(0), circle, 0, 2.0);
        var column = FamilyGeometry.Tessellate(session.Document.InPlaceFamilies.Single(family => family.Id == columnId));
        check(Math.Abs(FamilyGeometry.SignedVolume(column) - circleArea * 2.0) < Tolerance,
            "A circular profile extrudes to its polygon area times depth");
    }

    /// <summary>Verifies that solids follow the real work plane, including vertical and inclined planes.</summary>
    private static void CheckWorkPlaneOrientation(Action<bool, string> check)
    {
        var session = NewSession();
        var verticalId = session.CreateInPlaceFamily(GenericModels, "Vertical block");
        var vertical = TestPlane(new(0, 0, 0), new(0, 1, 0), new(1, 0, 0));
        session.AddInPlaceExtrusion(verticalId, vertical, TwoByOneRectangle, 0, 3.0);
        var (min, max) = FamilyGeometry.Tessellate(session.Document.InPlaceFamilies.Single()).Bounds();
        check(Math.Abs(max.Y - min.Y - 3.0) < Tolerance && Math.Abs(max.Z - min.Z - 1.0) < Tolerance &&
              Math.Abs(max.X - min.X - 2.0) < Tolerance,
            "On a vertical plane the solid runs along its normal (+Y), not along the global Z axis");

        var normal = 1 / Math.Sqrt(2);
        var inclined = TestPlane(new(0, 0, 0), new(0, normal, normal), new(1, 0, 0));
        var slantedId = session.CreateInPlaceFamily(GenericModels, "Slanted block");
        session.AddInPlaceExtrusion(slantedId, inclined, TwoByOneRectangle, 1.0, 2.0);
        var slanted = FamilyGeometry.Tessellate(session.Document.InPlaceFamilies.Single(family => family.Id == slantedId));
        var offsets = slanted.Positions.Select(point => (point.Y + point.Z) * normal).ToArray();
        check(Math.Abs(offsets.Min() - 1.0) < 1e-5 && Math.Abs(offsets.Max() - 3.0) < 1e-5 &&
              Math.Abs(FamilyGeometry.SignedVolume(slanted) - 4.0) < 1e-5,
            "On an inclined plane the solid spans offsets 1 to 3 along its normal, with volume 4 m³");
    }

    /// <summary>Verifies that editing a parameter regenerates the same extrusion, keeping its identity and material.</summary>
    private static void CheckParametricRegeneration(Action<bool, string> check)
    {
        var session = NewSession();
        var familyId = session.CreateInPlaceFamily(GenericModels, "Parametric block");
        session.SetInPlaceParameter(familyId, "Profundidad", 1.2);
        session.AddInPlaceExtrusion(familyId, Horizontal(0), TwoByOneRectangle, 0, 1.2,
            depthParameter: "Profundidad", materialName: "Concrete", name: "Body");
        var beforeFamily = session.Document.InPlaceFamilies.Single();
        var before = OperationOf(beforeFamily);
        session.SetInPlaceParameter(familyId, "profundidad", 0.5);
        var family = session.Document.InPlaceFamilies.Single();
        var after = OperationOf(family);
        var volume = FamilyGeometry.SignedVolume(FamilyGeometry.Tessellate(family));
        check(LengthOf(family).Name == "Profundidad" && LengthOf(family).DefaultNumber == 0.5,
            "Changing a parameter keeps its name and updates its value, matched case-insensitively");
        check(after.Id == before.Id && FamilyGeometry.Depth(family, after) == 0.5 &&
              MaterialOf(family, after) == "Concrete" &&
              SameGeometry(FamilyGeometry.PlaneOf(family, after), FamilyGeometry.PlaneOf(beforeFamily, before)),
            "Regeneration keeps the same extrusion with its material and work plane");
        check(Math.Abs(volume - 1.0) < Tolerance,
            "The solid follows the parameter (2 m² × 0.5 m) without creating a new extrusion");
        check(RejectionMessage(() => session.AddInPlaceExtrusion(familyId, Horizontal(0),
                  TwoByOneRectangle, 0, 1, depthParameter: "Missing")) is not null &&
              session.Document.InPlaceFamilies.Single().Operations.Count == 1,
            "An extrusion bound to a missing parameter is rejected without changing the project");
        var boundPath = Path.Combine(Path.GetTempPath(), $"guanacaste-inplace-bound-{Guid.NewGuid():N}.g3");
        try
        {
            ProjectFile.Save(boundPath, session.Document);
            var reopened = ProjectFile.Load(boundPath).InPlaceFamilies.Single();
            var reopenedVolume = FamilyGeometry.SignedVolume(FamilyGeometry.Tessellate(reopened));
            check(OperationOf(reopened).Expressions[FamilyGeometry.DepthKey] == "Profundidad" &&
                  Math.Abs(reopenedVolume - 1.0) < Tolerance,
                "A parameter-bound depth survives save and reopen and still regenerates from its parameter");
        }
        finally
        {
            if (File.Exists(boundPath)) File.Delete(boundPath);
        }
    }

    /// <summary>Verifies that every invalid profile, depth, or parameter is rejected with nothing changed.</summary>
    private static void CheckRejectedChangesLeaveProjectUnchanged(Action<bool, string> check)
    {
        var session = NewSession();
        var familyId = session.CreateInPlaceFamily(GenericModels, "Guarded");
        var plane = Horizontal(0);
        string? Reject(Point2[] profile, double depth = 1) =>
            RejectionMessage(() => session.AddInPlaceExtrusion(familyId, plane, profile, 0, depth));

        check(Reject([new(0, 0), new(1, 0)]) is not null, "An open two-point profile is rejected");
        check(Reject([new(0, 0), new(1, 0), new(1, 0), new(1, 1)]) is not null,
            "A profile with two coincident adjacent vertices is rejected");
        check(Reject([new(0, 0), new(1, 0), new(2, 0)]) is not null,
            "A collinear profile that encloses no area is rejected");
        check(Reject([new(0, 0), new(double.NaN, 0), new(1, 1)]) is not null,
            "A profile with a non-finite coordinate is rejected");
        check(Reject([new(0, 0), new(6, 0), new(0, 3), new(4, 3)]) is { } crossing &&
              crossing.Contains("cross each other"),
            "A self-crossing profile is rejected and the crossing edges are named");
        check(Reject(TwoByOneRectangle, depth: 0) is not null && Reject(TwoByOneRectangle, depth: 2000) is not null,
            "Depths outside 1 mm to 1 km are rejected");
        check(RejectionMessage(() => session.SetInPlaceParameter(familyId, "Ancho", 0)) is not null,
            "A zero length parameter is rejected");
        check(session.Document.InPlaceFamilies.Single().Operations.Count == 0 &&
              session.Document.InPlaceFamilies.Single().Parameters.Count == 0,
            "Every rejected change leaves the family unchanged");
    }

    /// <summary>Verifies undo and redo of an edit, then a save, reopen and regeneration round trip.</summary>
    private static void CheckUndoRedoAndPersistence(Action<bool, string> check)
    {
        var session = NewSession();
        var familyId = session.CreateInPlaceFamily(GenericModels, "Persisted");
        session.SetInPlaceParameter(familyId, "Ancho", 2.0);
        session.AddInPlaceExtrusion(familyId, Horizontal(0), TwoByOneRectangle, 0, 1.0, name: "Base");
        var operationId = OperationOf(session.Document.InPlaceFamilies.Single()).Id;
        var tilted = TestPlane(new(0, 0, 0.5), new(0, 1, 0), new(1, 0, 0));
        session.UpdateInPlaceExtrusion(familyId, operationId, tilted, TwoByOneRectangle, 0.25, 2.0, null, "Steel");
        var edited = session.Document.InPlaceFamilies.Single();
        var editedOperation = OperationOf(edited);
        check(editedOperation.Id == operationId && SameGeometry(FamilyGeometry.PlaneOf(edited, editedOperation), tilted) &&
              FamilyGeometry.Depth(edited, editedOperation) == 2.0 && MaterialOf(edited, editedOperation) == "Steel",
            "Editing an extrusion changes it in place and keeps its identifier");
        session.Undo();
        var undoneFamily = session.Document.InPlaceFamilies.Single();
        var undone = OperationOf(undoneFamily);
        check(FamilyGeometry.Depth(undoneFamily, undone) == 1.0 && MaterialOf(undoneFamily, undone) == string.Empty &&
              SameGeometry(FamilyGeometry.PlaneOf(undoneFamily, undone), Horizontal(0)),
            "Undo restores the previous extrusion definition");
        session.Redo();
        check(FamilyGeometry.Depth(session.Document.InPlaceFamilies.Single(),
                  OperationOf(session.Document.InPlaceFamilies.Single())) == 2.0,
            "Redo reapplies the extrusion edit");

        var filename = Path.Combine(Path.GetTempPath(), $"guanacaste-inplace-{Guid.NewGuid():N}.g3");
        try
        {
            var original = FamilyGeometry.SignedVolume(
                FamilyGeometry.Tessellate(session.Document.InPlaceFamilies.Single()));
            ProjectFile.Save(filename, session.Document);
            var reloaded = ProjectFile.Load(filename);
            var reloadedFamily = reloaded.InPlaceFamilies.Single();
            var reloadedOperation = OperationOf(reloadedFamily);
            check(reloaded.SchemaVersion == BuildingDocument.CurrentSchemaVersion && reloadedFamily.Id == familyId && reloadedFamily.Name == "Persisted" &&
                  reloadedFamily.Category == FamilyCategory.GenericModels && LengthOf(reloadedFamily).DefaultNumber == 2.0 &&
                  reloadedOperation.Id == operationId && SameGeometry(FamilyGeometry.PlaneOf(reloadedFamily, reloadedOperation), tilted) &&
                  reloadedOperation.Profile.SequenceEqual(TwoByOneRectangle) &&
                  MaterialOf(reloadedFamily, reloadedOperation) == "Steel",
                "Saving and reopening keeps the family, parameters, work plane, profile, material and identifiers");
            check(Math.Abs(FamilyGeometry.SignedVolume(FamilyGeometry.Tessellate(reloadedFamily)) - original) < Tolerance,
                "Regeneration after reopening produces the same solid");
        }
        finally
        {
            if (File.Exists(filename)) File.Delete(filename);
        }
    }

    /// <summary>Verifies that families survive the official .g3d container, the format new projects are saved in.</summary>
    private static void CheckOfficialContainerKeepsFamilies(Action<bool, string> check)
    {
        var session = NewSession();
        var familyId = session.CreateInPlaceFamily(GenericModels, "Container block");
        session.SetInPlaceParameter(familyId, "Altura", 0.5);
        session.AddInPlaceExtrusion(familyId, Horizontal(0), TwoByOneRectangle, 0, 0.5,
            depthParameter: "Altura", materialName: "Concrete");
        var path = Path.Combine(Path.GetTempPath(), $"guanacaste-inplace-container-{Guid.NewGuid():N}.g3d");
        try
        {
            ProjectFile.Save(path, session.Document);
            var reopened = ProjectFile.Load(path).InPlaceFamilies.Single();
            var volume = FamilyGeometry.SignedVolume(FamilyGeometry.Tessellate(reopened));
            check(reopened.Name == "Container block" && LengthOf(reopened).DefaultNumber == 0.5 &&
                  OperationOf(reopened).Expressions[FamilyGeometry.DepthKey] == "Altura" &&
                  Math.Abs(volume - 1.0) < Tolerance,
                "Families survive the official .g3d container with their parameters, binding and regenerated volume");
        }
        finally
        {
            foreach (var file in new[] { path, path + ".bak" })
                if (File.Exists(file)) File.Delete(file);
        }
    }

    /// <summary>Verifies that the shared scene mesh includes in-place solids, so 3D views and mesh exports show them.</summary>
    private static void CheckSceneIntegration(Action<bool, string> check)
    {
        var session = NewSession();
        var baseline = BuildingMesher.Build(session.Document).TriangleCount;
        var familyId = session.CreateInPlaceFamily(GenericModels, "Scene block");
        session.AddInPlaceExtrusion(familyId, Horizontal(0), TwoByOneRectangle, 0, 1.5);
        var added = FamilyGeometry.Tessellate(session.Document.InPlaceFamilies.Single()).TriangleCount;
        check(added == 12 && BuildingMesher.Build(session.Document).TriangleCount == baseline + added,
            "The shared scene mesh includes in-place solids, so 3D views and mesh exports show them");
    }

    /// <summary>
    /// Verifies that families are one model: built-in families keep validating with no profiles, an in-place family
    /// reopens from a .g3df file with its geometry, depths accept formulas, and a self-crossing profile is rejected
    /// by the family's own validation.
    /// </summary>
    private static void CheckUnifiedFamilyModel(Action<bool, string> check)
    {
        var door = BuiltInFamilyLibrary.CreateArchitecturalDoor();
        check(FamilyGeometry.Tessellate(door).TriangleCount == 0 && door.Operations.All(operation => operation.Profile.Count == 0),
            "Built-in families keep validating: their operations have no profiles, so they regenerate no solids");

        var session = NewSession();
        var familyId = session.CreateInPlaceFamily(GenericModels, "Standalone");
        session.SetInPlaceParameter(familyId, "Altura", 0.5);
        session.AddInPlaceExtrusion(familyId, Horizontal(0), TwoByOneRectangle, 0, 0.5,
            depthParameter: "Altura", materialName: "Steel", name: "Body");
        var family = session.Document.InPlaceFamilies.Single();
        var path = Path.Combine(Path.GetTempPath(), $"guanacaste-inplace-family-{Guid.NewGuid():N}.g3df");
        try
        {
            NativeDocumentStore.SaveFamily(path, family);
            var reopened = NativeDocumentStore.LoadFamily(path);
            var volume = FamilyGeometry.SignedVolume(FamilyGeometry.Tessellate(reopened));
            check(reopened.Id == family.Id && OperationOf(reopened).Profile.SequenceEqual(TwoByOneRectangle) &&
                  Math.Abs(volume - 1.0) < Tolerance,
                "An in-place family saved as .g3df reopens with its profile and regenerates the same solid");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }

        session.AddInPlaceExtrusion(familyId, Horizontal(0), TwoByOneRectangle, 0, 0, depthParameter: "Altura * 2",
            name: "Doubled");
        var doubled = session.Document.InPlaceFamilies.Single();
        check(FamilyGeometry.Depth(doubled, doubled.Operations.Single(operation => operation.Name == "Doubled")) == 1.0,
            "A depth can be a formula over family parameters");
        check(RejectionMessage(() => session.AddInPlaceExtrusion(familyId, Horizontal(0), TwoByOneRectangle, 0, 1,
                  depthParameter: "Altura * Falta")) is not null,
            "A depth formula that uses a missing parameter is rejected");

        var invalid = session.Document.InPlaceFamilies.Single().Clone();
        var index = invalid.Operations.FindIndex(operation => operation.Profile.Count > 0);
        invalid.Operations[index] = invalid.Operations[index] with
        {
            Profile = [new(0, 0), new(6, 0), new(0, 3), new(4, 3)]
        };
        check(RejectionMessage(() => invalid.Validate()) is { } message && message.Contains("cross each other"),
            "FamilyDocument.Validate rejects a self-crossing extrusion profile and names the edges");
    }

    /// <summary>Verifies that a schema 8 file loads and migrates to schema 9 with no in-place families.</summary>
    private static void CheckSchemaEightMigration(Action<bool, string> check)
    {
        var legacyPath = Path.Combine(Path.GetTempPath(), $"guanacaste-inplace-legacy-{Guid.NewGuid():N}.g3");
        try
        {
            var node = JsonNode.Parse(JsonSerializer.Serialize(BuildingDocument.Create("Legacy")))!.AsObject();
            node["SchemaVersion"] = 8;
            node.Remove("InPlaceFamilies");
            File.WriteAllText(legacyPath, "G3-PROJECT/1\n" + node.ToJsonString(), new UTF8Encoding(false));
            var migrated = ProjectFile.Load(legacyPath);
            check(migrated.SchemaVersion == BuildingDocument.CurrentSchemaVersion && migrated.InPlaceFamilies.Count == 0,
                "A schema 8 project loads, migrates to schema 9 and has no in-place families");
        }
        finally
        {
            if (File.Exists(legacyPath)) File.Delete(legacyPath);
        }
    }
}
