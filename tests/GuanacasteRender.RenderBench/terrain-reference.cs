using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Produces terrain and plant meshes from the original application for Rust compatibility checks.</summary>
internal static class TerrainReference
{
    /// <summary>Writes flat and surveyed concave sites, including a plant grounded on the surveyed surface.</summary>
    internal static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var document = BuildingDocument.Create("Terrain geometry reference");
        var level = document.Levels[0].Id;
        document.Floors.Add(new FloorSlab(Guid.Parse("10000000-0000-0000-0000-000000000001"), level, "Flat site",
            [new(0, 0), new(1, 0), new(1, 1), new(0, 1)], 0.3)
        { Kind = FloorKind.Toposolid, ElevationOffset = 0.15 });
        document.Floors.Add(new FloorSlab(Guid.Parse("10000000-0000-0000-0000-000000000002"), level, "Surveyed site",
            [new(2, 0), new(3.2, 0), new(3.2, 0.4), new(2.8, 0.4), new(2.8, 1), new(2, 1)], 0.4)
        {
            Kind = FloorKind.Toposolid,
            ElevationOffset = 0.05,
            TopoPoints = [new(2, 0, 0.1), new(3.2, 0, 0.4), new(2.3, 0.3, 0.5), new(2.4, 0.8, -0.15)]
        });
        var plantPosition = new Point2(2.4, 0.4);
        document.StructuralElements.Add(new StructuralElement(Guid.Parse("10000000-0000-0000-0000-000000000003"), level,
            StructuralElementKind.Planting, "Shrub", plantPosition, plantPosition, 0.5, 0.5, 0)
        { Style = ElementStyle.Shrub });
        var path = Path.Combine(folder, "terrain.gr");
        NativeDocumentStore.SaveProject(path, document);
        var mesh = BuildingMesher.Build(ProjectFile.Load(path), includeGround: true, plants: PlantDetail.Render);
        MeshReference.WriteMesh(Path.Combine(folder, "terrain.refmesh"), mesh);
        using var writer = new BinaryWriter(File.Create(Path.Combine(folder, "terrain.normals")));
        foreach (var index in mesh.Indices)
        {
            var normal = mesh.Normals?[index] ?? System.Numerics.Vector3.Zero;
            writer.Write(normal.X);
            writer.Write(normal.Y);
            writer.Write(normal.Z);
        }
        Console.WriteLine($"Wrote {document.Floors.Count} sites and {mesh.TriangleCount} reference triangles.");
    }
}
