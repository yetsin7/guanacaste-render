using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Interop;
using System.Text;

/// <summary>Exercises the coordinated residential study and the stair opening's effect on its roof.</summary>
static class TwoStoreyHouseChecks
{
    /// <summary>Checks geometry, sheet view references, export and native document persistence.</summary>
    public static void Run(Action<bool, string> check)
    {
        var house = TwoStoreyHouse.Create();
        var levels = house.Levels.OrderBy(level => level.Elevation).ToArray();
        var stair = house.StructuralElements.Single(element => element.Kind == StructuralElementKind.Stair);
        check(levels.Length == 2 && Math.Abs(levels[1].Elevation - levels[0].Elevation - stair.Height) < 1e-9 &&
              house.Floors.Count(floor => floor.Kind == FloorKind.Floor) == 2 &&
              house.Floors.Count(floor => floor.Kind == FloorKind.Roof) == 1,
            "The house connects two floor levels with a stair and a roof");
        check(house.Sheets.Count == 4 && house.Sheets.Select(sheet => sheet.Viewports.Single().ViewId).Distinct().Count() == 4 &&
              levels.All(level => house.Sheets.Any(sheet => house.Views.Any(view => view.Id == sheet.Viewports.Single().ViewId && view.LevelId == level.Id))),
            "Each house plan and coordinated section and elevation occupy separate sheets");

        var withoutOpening = house.Clone();
        withoutOpening.StructuralElements.RemoveAll(element => element.Kind == StructuralElementKind.Opening);
        var roofOnly = house.Clone();
        roofOnly.Walls.Clear(); roofOnly.Openings.Clear(); roofOnly.Columns.Clear();
        roofOnly.Floors.RemoveAll(floor => floor.Kind != FloorKind.Roof);
        var roofWithOpening = BuildingMesher.Build(roofOnly);
        roofOnly.StructuralElements.RemoveAll(element => element.Kind == StructuralElementKind.Opening);
        var roofWithoutOpening = BuildingMesher.Build(roofOnly);
        check(roofWithOpening.TriangleCount == roofWithoutOpening.TriangleCount &&
              roofWithOpening.Positions.SequenceEqual(roofWithoutOpening.Positions),
            "A stair opening on the upper floor leaves its roof intact");
        check(BuildingMesher.Build(house).TriangleCount != BuildingMesher.Build(withoutOpening).TriangleCount,
            "The stair opening changes the floor geometry");
        var opening = house.StructuralElements.Single(element => element.Kind == StructuralElementKind.Opening);
        check(PdfPlanExporter.PlanPrimitives(house, levels[1].Id).OfType<SheetPolygon>()
                .Any(polygon => polygon.Points.SequenceEqual(opening.Footprint)),
            "The upper plan shows the stair opening outline");
        var roofSession = new DocumentSession(roofOnly);
        check(roofSession.AddOpening(levels[1].Id, StructuralElementKind.Opening, "Floor opening", new(1, 1), new(2, 2)) is null,
            "A floor opening cannot be created when only a roof is present");

        var path = Path.Combine(Path.GetTempPath(), $"house-{Guid.NewGuid():N}.g3d");
        try
        {
            ProjectFile.Save(path, house);
            var reopened = ProjectFile.Load(path);
            var pdf = Encoding.Latin1.GetString(PdfPlanExporter.ExportSheets(reopened, DateTimeOffset.UnixEpoch));
            check(reopened.Levels.Count == 2 && reopened.Sheets.Count == 4 && pdf.Contains("/Count 4", StringComparison.Ordinal),
                "The two-storey project and four PDF sheets survive a native round trip");
        }
        finally { File.Delete(path); }
    }
}
