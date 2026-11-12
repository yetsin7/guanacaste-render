using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Produces water-tank and connected supply geometry from the C# application.</summary>
internal static class WaterStorageReference
{
    /// <summary>Writes explicit and household-sized tanks, including separately connected supply systems.</summary>
    internal static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var document = BuildingDocument.Create("Water storage reference");
        var level = document.Levels[0].Id;
        for (var index = 0; index < 3; index++)
            document.Rooms.Add(new Room(new Guid(index + 30, 0, 0, new byte[8]), level, (index + 1).ToString(),
                $"Bedroom {index + 1}", new Point2(index * 3, 6), new Point2(index * 3 + 2, 8)));
        document.Rooms.Add(new Room(new Guid(40, 0, 0, new byte[8]), level, "4", "Office", new Point2(9, 6), new Point2(11, 8)));
        var sources = new[] { PlumbingPointKind.Cistern, PlumbingPointKind.ElevatedTank };
        for (var index = 0; index < sources.Length; index++)
        {
            var source = new Guid(index + 1, 0, 0, new byte[8]);
            var fixture = new Guid(index + 10, 0, 0, new byte[8]);
            var position = new Point2(index * 6, 0);
            document.PlumbingPoints.Add(new PlumbingPoint(source, level, sources[index], sources[index].ToString(), position)
            { CapacityLiters = index == 0 ? 1100 : 2500, TankHeight = 4.5 });
            var fixturePosition = new Point2(index * 6 + 2, 3);
            document.StructuralElements.Add(new StructuralElement(fixture, level, StructuralElementKind.Component, "Basin",
                fixturePosition, fixturePosition, 0.6, 0.9, 0) { Depth = 0.5, ComponentShape = ResidentialComponentShape.PedestalSink });
            document.PlumbingSystems.Add(new PlumbingSystem(new Guid(index + 20, 0, 0, new byte[8]), PlumbingSystemKind.ColdWater,
                source, "Tank supply") { Fixtures = [fixture] });
        }
        document.PlumbingPoints.Add(new PlumbingPoint(Guid.Parse("30000000-0000-0000-0000-000000000001"), level,
            PlumbingPointKind.Cistern, "Bedroom-sized cistern", new Point2(14, 0)));
        document.PlumbingPoints.Add(new PlumbingPoint(Guid.Parse("30000000-0000-0000-0000-000000000002"), level,
            PlumbingPointKind.Cistern, "Occupant-sized cistern", new Point2(18, 0)) { Occupants = 3 });
        var path = Path.Combine(folder, "water-storage.gr");
        NativeDocumentStore.SaveProject(path, document);
        var mesh = BuildingMesher.Build(ProjectFile.Load(path), includeGround: true, plants: PlantDetail.Render);
        MeshReference.WriteMesh(Path.Combine(folder, "water-storage.refmesh"), mesh);
        Console.WriteLine($"Wrote {document.PlumbingPoints.Count} tanks and {mesh.TriangleCount} reference triangles.");
    }
}
