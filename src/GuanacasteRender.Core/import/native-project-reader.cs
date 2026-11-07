using System.Numerics;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Core.Import;

/// <summary>
/// Reads another Guanacaste 3D project as a model to link: its walls, slabs and everything else it builds are meshed as
/// the project itself would show them, in meters with Z up. A linked project is a reference to look at and coordinate
/// with; editing it means opening it.
/// </summary>
internal static class NativeProjectReader
{
    public static ImportedModel Read(string path, ModelImportOptions options, IProgress<ImportProgress>? progress, CancellationToken cancellation)
    {
        var document = ProjectFile.Load(path);
        progress?.Report(new ImportProgress(0.3, "Import.Finishing"));
        cancellation.ThrowIfCancellationRequested();
        var scene = BuildingMesher.Build(document);
        if (scene.TriangleCount == 0) throw new InvalidDataException("The model file contains no geometry.");
        var mesh = new MeshAccumulator { Normals = null };
        var material = mesh.MaterialIndex("g3d", () => MaterialLibrary.Imported with { Name = document.Name });
        for (var index = 0; index < scene.Positions.Count; index++) mesh.AddVertex(scene.Positions[index]);
        for (var triangle = 0; triangle < scene.TriangleCount; triangle++)
        {
            if ((triangle & 0xFFFF) == 0) cancellation.ThrowIfCancellationRequested();
            mesh.AddTriangle(scene.Indices[triangle * 3], scene.Indices[triangle * 3 + 1], scene.Indices[triangle * 3 + 2], material);
        }
        progress?.Report(new ImportProgress(0.95, "Import.Finishing"));
        return mesh.Build(path, "G3D", ModelFrame.ZUp, options.MetersPerUnit ?? 1, options, document.Name);
    }
}
