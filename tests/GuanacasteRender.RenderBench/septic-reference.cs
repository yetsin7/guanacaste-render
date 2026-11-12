using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Produces a septic system and drain-field reference from the original application.</summary>
internal static class SepticReference
{
    /// <summary>Writes a connected toilet, vent, septic tank, and multi-trench field beside a wall.</summary>
    internal static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var document = BuildingDocument.Create("Septic reference");
        var level = document.Levels[0].Id;
        document.Walls.Add(new Wall(Guid.Parse("40000000-0000-0000-0000-000000000001"), level,
            new Point2(0, 0), new Point2(6, 0), 0.2, 3));
        var fixture = Guid.Parse("40000000-0000-0000-0000-000000000002");
        var location = new Point2(2, 2);
        document.StructuralElements.Add(new StructuralElement(fixture, level, StructuralElementKind.Component, "Toilet",
            location, location, 0.4, 0.8, 0) { Depth = 0.7, ComponentShape = ResidentialComponentShape.Toilet });
        var septic = Guid.Parse("40000000-0000-0000-0000-000000000003");
        document.PlumbingPoints.Add(new PlumbingPoint(septic, level, PlumbingPointKind.SepticTank, "Septic tank", new Point2(8, -3))
        { Percolation = 35 });
        document.PlumbingPoints.Add(new PlumbingPoint(Guid.Parse("40000000-0000-0000-0000-000000000004"), level,
            PlumbingPointKind.VentStack, "Vent", new Point2(2, 1)));
        document.PlumbingSystems.Add(new PlumbingSystem(Guid.Parse("40000000-0000-0000-0000-000000000005"),
            PlumbingSystemKind.Sanitary, septic, "Sanitary") { Fixtures = [fixture] });
        var path = Path.Combine(folder, "septic.gr");
        NativeDocumentStore.SaveProject(path, document);
        var mesh = BuildingMesher.Build(ProjectFile.Load(path), includeGround: true, plants: PlantDetail.Render);
        MeshReference.WriteMesh(Path.Combine(folder, "septic.refmesh"), mesh);
        Console.WriteLine($"Wrote septic geometry with {mesh.TriangleCount} reference triangles.");
    }
}
