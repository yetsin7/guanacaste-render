using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Produces pitched-roof meshes for comparisons with the Rust geometry implementation.</summary>
internal static class RoofReference
{
    /// <summary>Writes each supported roof shape with and without gable walls over rectangular and concave plans.</summary>
    internal static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var document = BuildingDocument.Create("Pitched roof reference");
        var level = document.Levels[0].Id;
        foreach (var shape in new[] { RoofShape.Shed, RoofShape.Gable, RoofShape.Hip })
        foreach (var walls in new[] { false, true })
        foreach (var concave in new[] { false, true })
        {
            var x = (int)shape * 20 + (walls ? 10 : 0);
            var y = concave ? 15 : 0;
            var boundary = concave
                ? new List<Point2> { new(x, y), new(x + 8, y), new(x + 8, y + 3), new(x + 5, y + 3), new(x + 5, y + 7), new(x, y + 7) }
                : new List<Point2> { new(x, y), new(x + 8, y), new(x + 8, y + 6), new(x, y + 6) };
            if (concave) boundary.Reverse();
            var number = (int)shape * 100 + (walls ? 10 : 0) + (concave ? 1 : 0);
            document.Floors.Add(new FloorSlab(new Guid(number, 0, 0, new byte[8]), level, shape.ToString(), boundary, 0.12)
            {
                Kind = FloorKind.Roof, Shape = shape, Pitch = 25, Overhang = 0.35, ElevationOffset = 3.2, GableWalls = walls
            });
        }
        var path = Path.Combine(folder, "pitched-roofs.gr");
        NativeDocumentStore.SaveProject(path, document);
        var mesh = BuildingMesher.Build(ProjectFile.Load(path), includeGround: false, plants: PlantDetail.Render);
        MeshReference.WriteMesh(Path.Combine(folder, "pitched-roofs.refmesh"), mesh);
        Console.WriteLine($"Wrote {document.Floors.Count} roofs and {mesh.TriangleCount} reference triangles.");
    }
}
