using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Produces reference geometry for water meters and sewer and storm outlets.</summary>
internal static class PlumbingPointReference
{
    /// <summary>Writes the original mesh and ground plane for isolated service points without routed systems.</summary>
    internal static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var document = BuildingDocument.Create("Plumbing point reference");
        var level = document.Levels[0].Id;
        var kinds = new[] { PlumbingPointKind.WaterMeter, PlumbingPointKind.SewerOutlet, PlumbingPointKind.StormOutlet };
        for (var index = 0; index < kinds.Length; index++)
            document.PlumbingPoints.Add(new PlumbingPoint(new Guid(index + 1, 0, 0, new byte[8]), level, kinds[index],
                kinds[index].ToString(), new Point2(index * 2, 0)));
        var path = Path.Combine(folder, "plumbing-points.gr");
        NativeDocumentStore.SaveProject(path, document);
        var mesh = BuildingMesher.Build(ProjectFile.Load(path), includeGround: true, plants: PlantDetail.Render);
        MeshReference.WriteMesh(Path.Combine(folder, "plumbing-points.refmesh"), mesh);
        Console.WriteLine($"Wrote {document.PlumbingPoints.Count} points and {mesh.TriangleCount} reference triangles.");
    }
}
