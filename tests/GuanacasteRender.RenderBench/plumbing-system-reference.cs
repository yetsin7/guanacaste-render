using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Produces a compact routed plumbing reference from the architectural showcase.</summary>
internal static class PlumbingSystemReference
{
    /// <summary>Retains the connected fixtures, water source, sanitary outlet, and vents from the example house.</summary>
    internal static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var showcase = BreezeBlockHouseShowcase.Create();
        var connected = showcase.PlumbingSystems.SelectMany(system => system.Fixtures.Append(system.SourceId)).ToHashSet();
        var document = BuildingDocument.Create("Plumbing system reference");
        document.Levels = showcase.Levels;
        document.Views = BuiltInProjectViews.CreateDefaults(document.Levels);
        document.StructuralElements = showcase.StructuralElements.Where(element => connected.Contains(element.Id)).ToList();
        document.PlumbingPoints = showcase.PlumbingPoints;
        document.PlumbingSystems = showcase.PlumbingSystems;
        var path = Path.Combine(folder, "plumbing-systems.gr");
        NativeDocumentStore.SaveProject(path, document);
        var mesh = BuildingMesher.Build(ProjectFile.Load(path), includeGround: true, plants: PlantDetail.Render);
        MeshReference.WriteMesh(Path.Combine(folder, "plumbing-systems.refmesh"), mesh);
        Console.WriteLine($"Wrote {document.PlumbingSystems.Count} systems and {mesh.TriangleCount} reference triangles.");
    }
}
