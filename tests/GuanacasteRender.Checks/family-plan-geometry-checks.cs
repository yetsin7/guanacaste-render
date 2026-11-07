using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Checks that in-place family solids remain coordinated across plans, render meshes, and persistence.</summary>
internal static class FamilyPlanGeometryChecks
{
    /// <summary>Exercises a cut solid, a low solid, story filtering, and reopening the native project.</summary>
    public static void Run(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Family plan geometry"));
        var familyId = session.CreateInPlaceFamily(BimCategoryCatalog.GenericModelsId, "Plan block");
        var profile = new[] { new Point2(0, 0), new Point2(2, 0), new Point2(2, 1), new Point2(0, 1) };
        session.AddInPlaceExtrusion(familyId, InPlaceFamilyRules.HorizontalPlane(0), profile, 0, 2);
        var family = session.Document.InPlaceFamilies.Single();
        var plan = FamilyPlanGeometry.ForLevel(family, 0, 3);
        check(plan.Count > 0 && plan.All(segment => segment.Start.X is >= -0.001 and <= 2.001 &&
                                         segment.Start.Y is >= -0.001 and <= 1.001),
            "A cut family solid appears with its model coordinates in the ground plan");
        check(FamilyPlanGeometry.ForLevel(family, 3, 6).Count == 0,
            "A family solid is absent from stories above its own geometry");
        check(FamilyGeometry.Tessellate(family).TriangleCount > 0 &&
              BuildingMesher.Build(session.Document).TriangleCount >= FamilyGeometry.Tessellate(family).TriangleCount,
            "The same family contributes triangles to the shared 3D and render mesh");

        var lowId = session.CreateInPlaceFamily(BimCategoryCatalog.GenericModelsId, "Low block");
        session.AddInPlaceExtrusion(lowId, InPlaceFamilyRules.HorizontalPlane(0), profile, 0, 0.5);
        check(FamilyPlanGeometry.ForLevel(session.Document.InPlaceFamilies.Single(item => item.Id == lowId), 0, 3).Count == 4,
            "Low furniture retains its perimeter below the plan cut plane");

        var path = Path.Combine(Path.GetTempPath(), $"family-plan-{Guid.NewGuid():N}.g3d");
        try
        {
            ProjectFile.Save(path, session.Document);
            var reopened = ProjectFile.Load(path).InPlaceFamilies.Single(item => item.Id == familyId);
            check(FamilyPlanGeometry.ForLevel(reopened, 0, 3).Count == plan.Count,
                "A reopened native project regenerates the same family plan geometry");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
        }
        CheckTemplateExcludesModeledFamilies(session.Document, check);
        CheckCalculatedExtrusion(check);
    }

    /// <summary>Checks chained formulas drive the same solid and plan geometry after a type value changes.</summary>
    private static void CheckCalculatedExtrusion(Action<bool, string> check)
    {
        var baseLength = new FamilyParameterDefinition(Guid.NewGuid(), "BaseLength", FamilyParameterKind.Length,
            FamilyParameterScope.Type) { DefaultNumber = 1 };
        var doubled = new FamilyParameterDefinition(Guid.NewGuid(), "Doubled", FamilyParameterKind.Length,
            FamilyParameterScope.Calculated) { Formula = "BaseLength * 2", DefaultNumber = 99 };
        var depth = new FamilyParameterDefinition(Guid.NewGuid(), "Depth", FamilyParameterKind.Length,
            FamilyParameterScope.Calculated) { Formula = "Doubled + 0.5 m" };
        var plane = InPlaceFamilyRules.HorizontalPlane(0);
        var operation = new FamilyOperation(Guid.NewGuid(), "Body", FamilyOperationKind.SolidExtrusion)
        {
            WorkPlaneId = plane.Id,
            Profile = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)],
            Expressions = new() { [FamilyGeometry.DepthKey] = "Depth" }
        };
        var family = new FamilyDocument
        {
            Parameters = [baseLength, doubled, depth], WorkPlanes = [plane], Operations = [operation],
            Types = [new(Guid.NewGuid(), "Standard", [new(baseLength.Id, 0.5, null)])]
        };
        family.Validate();
        check(Math.Abs(FamilyGeometry.SignedVolume(FamilyGeometry.Tessellate(family)) - 1.5) < 1e-6,
            "A calculated family parameter overrides its stored default and drives extrusion volume");
        family.Types[0].Values[0] = new(baseLength.Id, 0.2, null);
        family.Validate();
        check(Math.Abs(FamilyGeometry.SignedVolume(FamilyGeometry.Tessellate(family)) - 0.9) < 1e-6 &&
              FamilyPlanGeometry.ForLevel(family, 0, 3).Count == 4,
            "Changing a type value regenerates chained formulas in 3D and the low-solid plan");
        var tallerType = new FamilyTypeDefinition(Guid.NewGuid(), "Tall", [new(baseLength.Id, 1, null)]);
        family.Types.Add(tallerType);
        family.Validate();
        check(Math.Abs(FamilyGeometry.SignedVolume(FamilyGeometry.Tessellate(family, tallerType.Id)) - 2.5) < 1e-6 &&
              Math.Abs(FamilyGeometry.SignedVolume(FamilyGeometry.Tessellate(family)) - 0.9) < 1e-6 &&
              FamilyPlanGeometry.ForLevel(family, 0, 3, tallerType.Id).Count > 4,
            "Each selected family type drives its own coordinated 3D and plan geometry");
        var path = Path.Combine(Path.GetTempPath(), $"family-types-{Guid.NewGuid():N}.g3df");
        try
        {
            NativeDocumentStore.SaveFamily(path, family);
            var reopened = NativeDocumentStore.LoadFamily(path);
            check(Math.Abs(FamilyGeometry.SignedVolume(FamilyGeometry.Tessellate(reopened, tallerType.Id)) - 2.5) < 1e-6,
                "Native family persistence preserves type identity and calculated geometry");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
        tallerType.Values[0] = new(baseLength.Id, -1, null);
        var invalidTypeRejected = false;
        try { family.Validate(); }
        catch (InvalidDataException) { invalidTypeRejected = true; }
        check(invalidTypeRejected, "Invalid geometry in a non-default family type is rejected");
        family.Types.Remove(tallerType);
        family.Parameters[2] = depth with { Formula = "Doubled / 0" };
        var rejected = false;
        try { family.Validate(); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "An undefined calculated extrusion is rejected during family validation");
    }

    /// <summary>Ensures native templates keep setup while excluding family instances and their geometry.</summary>
    private static void CheckTemplateExcludesModeledFamilies(BuildingDocument document, Action<bool, string> check)
    {
        var path = Path.Combine(Path.GetTempPath(), $"family-template-{Guid.NewGuid():N}.g3dt");
        try
        {
            TemplateFile.Save(path, document);
            var template = TemplateFile.Load(path);
            check(template.InPlaceFamilies.Count == 0 && template.Levels.Count == document.Levels.Count,
                "A native template retains levels without copying modeled in-place families");
            check(TemplateRules.CreateProject(template, "New project").InPlaceFamilies.Count == 0,
                "A project from a template starts without the source project's family solids");
            File.WriteAllText(path, NativeContainer.Seal(NativeContainer.TemplateSignature,
                BuildingDocument.CurrentSchemaVersion, ProjectFile.SerializeDocument(document)));
            var rejected = false;
            try { TemplateFile.Load(path); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "A template containing modeled families is rejected on open");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
        }
    }
}
