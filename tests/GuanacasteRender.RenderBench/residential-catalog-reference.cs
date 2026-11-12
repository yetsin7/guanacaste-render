using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Produces repeatable C# meshes for the Rust residential catalog compatibility checks.</summary>
internal static class ResidentialCatalogReference
{
    /// <summary>Writes each catalog shape twice, once unrotated and once turned, with dimensions from the catalog.</summary>
    internal static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var document = BuildingDocument.Create("Residential geometry reference");
        var level = document.Levels[0].Id;
        foreach (var shape in Enum.GetValues<ResidentialComponentShape>().Where(shape => shape != ResidentialComponentShape.Box))
        {
            var preset = ResidentialComponentCatalog.Presets.First(candidate => candidate.ComponentShape == shape);
            for (var variant = 0; variant < 2; variant++)
            {
                var number = (int)shape;
                var position = new Point2(number % 8 * 5 + variant * 2, number / 8 * 5);
                document.StructuralElements.Add(new StructuralElement(
                    new Guid(number + variant * 1000, 0, 0, new byte[8]), level, StructuralElementKind.Component,
                    shape.ToString(), position, position, preset.Width, preset.Height, preset.Offset)
                {
                    Depth = preset.Depth,
                    ComponentShape = shape,
                    Rotation = variant == 0 ? 0 : 0.61
                });
            }
        }
        var path = Path.Combine(folder, "residential-catalog.gr");
        NativeDocumentStore.SaveProject(path, document);
        var mesh = BuildingMesher.Build(ProjectFile.Load(path), includeGround: false, plants: PlantDetail.Render);
        MeshReference.WriteMesh(Path.Combine(folder, "residential-catalog.refmesh"), mesh);
        Console.WriteLine($"Wrote {document.StructuralElements.Count} components and {mesh.TriangleCount} reference triangles.");
    }
}
