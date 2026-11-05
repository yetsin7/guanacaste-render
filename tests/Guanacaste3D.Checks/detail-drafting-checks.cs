using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Interop;

/// <summary>Checks the plan-only drafting elements (detail line, filled region, revision cloud, insulation) on the breeze-block house.</summary>
static class DetailDraftingChecks
{
    private static readonly StructuralElementKind[] Kinds =
    [
        StructuralElementKind.DetailLine, StructuralElementKind.FilledRegion,
        StructuralElementKind.RevisionCloud, StructuralElementKind.Insulation
    ];

    public static void Run(Action<bool, string> check)
    {
        var session = new DocumentSession(BreezeBlockHouseShowcase.Create());
        var level = session.Document.Levels[0].Id;
        var before = BuildingMesher.Build(session.Document);
        var solids = session.Document.StructuralElements.Count(element => element.HasSolid);
        var steps = session.UndoNames.Count;

        var ids = Kinds.Select((kind, index) =>
            session.AddStructuralElement(level, kind, kind.ToString(), new Point2(600 + index * 10, 600), new Point2(606 + index * 10, 603))).ToArray();
        var added = session.Document.StructuralElements.Where(element => ids.Contains(element.Id)).ToArray();
        check(added.Length == 4 && session.UndoNames.Count == steps + 4 && added.All(element => !element.HasSolid),
            "A detail line, a filled region, a revision cloud and insulation are added, one undoable step each, and none has a solid");
        var after = BuildingMesher.Build(session.Document);
        check(after.Positions.Count == before.Positions.Count && session.Document.StructuralElements.Count(element => element.HasSolid) == solids,
            "None of the drafting elements adds a triangle to the 3D model");

        var region = added.Single(element => element.Kind == StructuralElementKind.FilledRegion);
        var cloud = added.Single(element => element.Kind == StructuralElementKind.RevisionCloud);
        var area = Math.Abs(region.End.X - region.Start.X) * Math.Abs(region.End.Y - region.Start.Y);
        check(region.Footprint.Count == 4 && Math.Abs(Polygon(region.Footprint) - area) < 1e-9 && Polygon(cloud.Footprint) > 0,
            $"A filled region and a revision cloud cover the rectangle between their corners ({area:0.0} m²)");
        check(added.Single(element => element.Kind == StructuralElementKind.DetailLine).IsDatumLine &&
              !added.Single(element => element.Kind == StructuralElementKind.Insulation).IsDatumLine,
            "A detail line is drafted as a line; insulation is drafted as batting of its own thickness");

        var file = Path.Combine(Path.GetTempPath(), $"drafting-{Guid.NewGuid():N}.g3d");
        ProjectFile.Save(file, session.Document);
        var reopened = ProjectFile.Load(file);
        File.Delete(file);
        check(Kinds.All(kind => reopened.StructuralElements.Count(element => element.Kind == kind) == 1),
            "The four kinds survive saving and opening the project");

        check(DxfExporter.ToAscii(session.Document).Length > 0 &&
              PdfPlanExporter.Export(session.Document, SheetPaperSize.A3, new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero)).Length > 0,
            "The project with drafting elements still exports to DXF and PDF");

        session.RemoveStructuralElement(region.Id);
        check(session.Document.StructuralElements.All(element => element.Id != region.Id), "A drafting element is deleted like any other");
        session.Undo();
        check(session.Document.StructuralElements.Any(element => element.Id == region.Id), "Undo brings it back");
    }

    private static double Polygon(IReadOnlyList<Point2> points)
    {
        var sum = 0.0;
        for (var index = 0; index < points.Count; index++)
        {
            var next = points[(index + 1) % points.Count];
            sum += points[index].X * next.Y - next.X * points[index].Y;
        }
        return Math.Abs(sum) / 2;
    }
}
