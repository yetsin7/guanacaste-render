using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Produces a routed storm-drain reference for the Rust mesher.</summary>
internal static class StormReference
{
    /// <summary>Writes a roof, two downspouts, and their shared outlet using the source application's analysis.</summary>
    internal static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var document = BuildingDocument.Create("Storm drainage reference");
        var level = document.Levels[0].Id;
        document.Floors.Add(new FloorSlab(Guid.Parse("20000000-0000-0000-0000-000000000001"), level, "Roof",
            [new(0, 0), new(8, 0), new(8, 6), new(0, 6)], 0.15)
        { Kind = FloorKind.Roof, Shape = RoofShape.Gable, Pitch = 20, ElevationOffset = 3, Overhang = 0.3 });
        var first = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var second = Guid.Parse("20000000-0000-0000-0000-000000000003");
        document.StructuralElements.Add(new StructuralElement(first, level, StructuralElementKind.Downspout, "Leader A",
            new Point2(0, 0), new Point2(0, 0), 0.10, 3, 0) { Style = ElementStyle.Round });
        document.StructuralElements.Add(new StructuralElement(second, level, StructuralElementKind.Downspout, "Leader B",
            new Point2(8, 6), new Point2(8, 6), 0.10, 3, 0) { Style = ElementStyle.Round });
        var outlet = Guid.Parse("20000000-0000-0000-0000-000000000004");
        document.PlumbingPoints.Add(new PlumbingPoint(outlet, level, PlumbingPointKind.StormOutlet, "Storm outlet", new Point2(10, -2))
        { RainfallMmPerHour = 180, InvertDepth = 0.8 });
        document.PlumbingSystems.Add(new PlumbingSystem(Guid.Parse("20000000-0000-0000-0000-000000000005"),
            PlumbingSystemKind.StormWater, outlet, "Storm system") { Fixtures = [first, second] });
        var path = Path.Combine(folder, "storm.gr");
        NativeDocumentStore.SaveProject(path, document);
        var mesh = BuildingMesher.Build(ProjectFile.Load(path), includeGround: true, plants: PlantDetail.Render);
        MeshReference.WriteMesh(Path.Combine(folder, "storm.refmesh"), mesh);
        Console.WriteLine($"Wrote storm geometry with {mesh.TriangleCount} reference triangles.");
    }
}
