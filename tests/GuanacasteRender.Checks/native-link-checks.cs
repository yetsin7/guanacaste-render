using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Import;

/// <summary>Checks that another Guanacaste 3D project can be linked as a model: the cobogó house read back as geometry.</summary>
static class NativeLinkChecks
{
    public static void Run(Action<bool, string> check)
    {
        var house = BreezeBlockHouseShowcase.Create();
        var path = Path.Combine(Path.GetTempPath(), $"vinculo-{Guid.NewGuid():N}.g3d");
        try
        {
            ProjectFile.Save(path, house);
            var inspection = ModelImporter.Inspect(path);
            check(ModelImporter.Detect(path) == ImportFormat.Native && inspection.CanImport && inspection.FormatName == "Guanacaste 3D",
                "A saved project is recognised as a Guanacaste 3D model that can be linked");
            var expected = BuildingMesher.Build(house);
            var result = ModelImporter.Import(path);
            check(result.Format == ImportFormat.Native && result.Model.TriangleCount == expected.TriangleCount && result.Model.Positions.Length == expected.Positions.Count,
                $"Linking the project brings in the same {result.Model.TriangleCount} triangles the project shows");
            var (min, max) = result.Model.Bounds();
            var (expectedMin, expectedMax) = expected.Bounds();
            check(Math.Abs(max.X - min.X - (expectedMax.X - expectedMin.X)) < 0.01 && Math.Abs(max.Z - min.Z - (expectedMax.Z - expectedMin.Z)) < 0.01,
                "Its size in meters is the size of the house");
            check(ModelImporter.DialogFilter.Contains("*.g3d", StringComparison.Ordinal), "The file dialog offers Guanacaste 3D projects");
        }
        finally { File.Delete(path); }
    }
}
