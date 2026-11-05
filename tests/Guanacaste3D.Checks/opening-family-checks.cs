using System.Text.Json;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Exercises complete built-in assemblies, legacy repair, and external dimension ingestion.</summary>
internal static class OpeningFamilyChecks
{
    /// <summary>Runs family checks against temporary files and removes only this run's fixtures.</summary>
    public static void Run(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"g3-opening-families-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var session = new DocumentSession(BuildingDocument.Create("Opening family verification"));
            var wallId = session.AddWall(session.Document.Levels[0].Id, new Point2(0, 0), new Point2(8, 0));
            var doorId = session.AddOpening(wallId, OpeningKind.Door, offset: 2);
            var windowId = session.AddOpening(wallId, OpeningKind.Window, offset: 5);
            check(session.Document.Openings.Single(opening => opening.Id == windowId).OpeningTypeId ==
                BuiltInOpeningTypes.FixedWindow1200Id, "Window placement uses the built-in 1200 mm family");
            var mesh = BuildingMesher.Build(session.Document);
            // Each solid is a prism of 12 triangles. The 900 mm door has 3 lining solids and a leaf of 5 solids
            // (two stiles, head and kick rails, panel). The 1200 mm window has 3 lining solids, a sill, and two
            // lights of 4 sash members each; its glazing is one solid per light.
            check(mesh.TriangleKinds.Count(kind => kind == SurfaceKind.DoorLeaf) == 9 * 12 &&
                mesh.TriangleKinds.Count(kind => kind == SurfaceKind.OpeningFrame) == (3 + 3 + 1 + 8) * 12 &&
                mesh.TriangleKinds.Count(kind => kind == SurfaceKind.Glass) == 2 * 12,
                "Default assemblies are layered: a panelled door leaf (stiles, three rails, two raised panels), lining, sill, sash members, and two glazed lights");
            check(mesh.Positions.All(point => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z)),
                "Opening assemblies generate finite geometry");
            var windowTypeId = session.Document.Openings.Single(opening => opening.Id == windowId).OpeningTypeId;
            var customWindowType = session.Document.OpeningTypes.Single(type => type.Id == windowTypeId);
            session.UpdateOpeningType(windowTypeId, customWindowType.FamilyName, customWindowType.Name,
                customWindowType.Width, customWindowType.Height, customWindowType.DefaultSill,
                customWindowType.MaterialName, frameWidth: 0.08, glazingLightCount: 3);
            var customMesh = BuildingMesher.Build(session.Document);
            check(customMesh.TriangleKinds.Count(kind => kind == SurfaceKind.Glass) == 3 * 12 &&
                customMesh.TriangleCount > mesh.TriangleCount,
                "Family light-count parameter regenerates three distinct glass solids");
            var familyPath = Path.Combine(directory, "custom-window.G3Df");
            FamilyFile.Save(familyPath, OpeningFamilyDocument.FromProject(session.Document, customWindowType.FamilyName));
            var reloadedFamily = FamilyFile.Load(familyPath);
            var reloadedType = reloadedFamily.Types.Single(type => type.Id == windowTypeId);
            check(reloadedType.FrameWidth == 0.08 && reloadedType.GlazingLightCount == 3,
                "Native family round trip preserves geometry-driving type parameters");
            var receivingProject = new DocumentSession(BuildingDocument.Create());
            receivingProject.ImportOpeningFamily(reloadedFamily);
            var importedType = receivingProject.Document.OpeningTypes.Single(type =>
                type.FamilyName == customWindowType.FamilyName &&
                type.Name == customWindowType.Name + " (2)");
            check(importedType.FrameWidth == 0.08 && importedType.GlazingLightCount == 3,
                "Import retains custom geometry when a default type has the same name and size");
            receivingProject.ImportOpeningFamily(reloadedFamily);
            check(receivingProject.Document.OpeningTypes.Count(type =>
                type.FamilyName == customWindowType.FamilyName && type.GlazingLightCount == 3) == 1,
                "Repeated family import reuses the matching geometry without extra types");
            session.Undo();
            check(BuildingMesher.Build(session.Document).TriangleCount == mesh.TriangleCount,
                "Undo restores the previous window geometry");
            session.Redo();
            check(BuildingMesher.Build(session.Document).TriangleCount == customMesh.TriangleCount,
                "Redo rebuilds custom family geometry");
            var beforeInvalidType = session.Document;
            try
            {
                session.UpdateOpeningType(windowTypeId, customWindowType.FamilyName, customWindowType.Name,
                    customWindowType.Width, customWindowType.Height, customWindowType.DefaultSill,
                    frameWidth: 0.2, glazingLightCount: 3);
            }
            catch (InvalidDataException) { }
            check(ReferenceEquals(beforeInvalidType, session.Document),
                "Invalid frame width rejects the edit atomically");
            var projectPath = Path.Combine(directory, "families.g3");
            ProjectFile.Save(projectPath, session.Document);
            var loaded = ProjectFile.Load(projectPath);
            check(BuildingMesher.Build(loaded).TriangleCount == customMesh.TriangleCount,
                "Native save and reopen preserves the complete family assemblies");
            session.Undo();
            session.Undo();
            check(session.Document.Openings.Count == 1, "Undo removes a window assembly atomically");
            session.Redo();
            session.Redo();
            check(BuildingMesher.Build(session.Document).TriangleCount == customMesh.TriangleCount,
                "Redo restores frame and glass geometry");
            var legacy = loaded.OpeningTypes.FindIndex(type => type.Id == BuiltInOpeningTypes.SingleDoor900Id);
            loaded.OpeningTypes[legacy] = loaded.OpeningTypes[legacy] with { Depth = 0 };
            loaded.SchemaVersion = 8;
            File.WriteAllText(projectPath, "G3-PROJECT/1\n" + JsonSerializer.Serialize(loaded));
            var migrated = ProjectFile.Load(projectPath);
            check(migrated.OpeningTypes[legacy].Depth == 0.04 && migrated.Openings.Any(opening => opening.Id == doorId),
                "Schema-eight built-in doors gain a leaf without changing placed instance identities");
            var ifcPath = Path.Combine(directory, "window.ifc");
            File.WriteAllText(ifcPath, "#1=IFCSIUNIT($,.LENGTHUNIT.,.CENTI.,.METRE.);#2=IFCUNITASSIGNMENT((#1));" +
                "#3=IFCWINDOW('g',$,'Window',$,$,$,$,$,120.,180.);");
            var window = DoorFamilyImport.Read(ifcPath, kind: OpeningKind.Window);
            check(Math.Abs(window.Width - 1.8) < 1e-9 && Math.Abs(window.Height - 1.2) < 1e-9,
                "IFC windows use declared centimeter units without swapping wide window dimensions");
            var objPath = Path.Combine(directory, "wide-window.obj");
            File.WriteAllText(objPath, "v 0 0 0\nv 1.8 0 0\nv 1.8 1.2 0.06\n");
            var imported = DoorFamilyImport.Read(objPath, kind: OpeningKind.Window);
            check(imported.Width == 1.8 && imported.Height == 1.2 && imported.Depth == 0.06,
                "Y-up meter OBJ windows retain width and height even when wider than tall");
            check(Rejects(() => DoorFamilyImport.Read(ifcPath)), "Import rejects an IFC containing only the other opening category");
            var dxfPath = Path.Combine(directory, "invalid-height.dxf");
            File.WriteAllText(dxfPath, "0\nLINE\n10\n0\n20\n0\n11\n0.9\n21\n0.04\n");
            check(Rejects(() => DoorFamilyImport.Read(dxfPath, double.NaN)), "Non-finite plan import heights are rejected");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Reports whether an invalid external model is rejected before catalog mutation.</summary>
    private static bool Rejects(Action operation)
    {
        try { operation(); }
        catch (InvalidDataException) { return true; }
        return false;
    }
}
