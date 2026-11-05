using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Interop;

/// <summary>Checks the matchline, the sheet guide grid and the view reference on the breeze-block house and its sheets.</summary>
static class SheetAidChecks
{
    private static bool Throws(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return true; }
        return false;
    }

    public static void Run(Action<bool, string> check)
    {
        var session = new DocumentSession(BreezeBlockHouseShowcase.Create());
        var level = session.Document.Levels[0].Id;
        var sheet = session.Document.Sheets[0];
        var solids = BuildingMesher.Build(session.Document).Positions.Count;

        var matchline = session.AddStructuralElement(level, StructuralElementKind.Matchline, "Línea de cortes", new Point2(10, -5), new Point2(10, 25));
        var line = session.Document.StructuralElements.Single(item => item.Id == matchline);
        check(!line.HasSolid && line.IsDatumLine && BuildingMesher.Build(session.Document).Positions.Count == solids,
            "A matchline is a line in the plan with no solid, and adds nothing to the 3D model");
        check(PdfPlanExporter.Export(session.Document, SheetPaperSize.A3, new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero)).Length > 0,
            "A project with a matchline still prints its plans");

        // Guide grid: stored on the sheet, never part of the printed composition.
        var before = SheetComposition.Build(session.Document, sheet, DateTimeOffset.UnixEpoch).Graphics.Count;
        session.SetSheetGuideGrid(sheet.Id, 25);
        var after = SheetComposition.Build(session.Document, session.Document.Sheets[0], DateTimeOffset.UnixEpoch).Graphics.Count;
        check(session.Document.Sheets[0].GuideGridMm == 25 && after == before, "The guide grid is saved with the sheet and adds nothing to what is printed");
        check(Throws(() => session.SetSheetGuideGrid(sheet.Id, 2)) && Throws(() => session.SetSheetGuideGrid(sheet.Id, 500)) &&
              Throws(() => session.SetSheetGuideGrid(sheet.Id, double.NaN)) && Throws(() => session.SetSheetGuideGrid(Guid.NewGuid(), 25)),
            "A spacing outside 5–100 mm, not a number, or a sheet that does not exist is refused");
        session.SetSheetGuideGrid(sheet.Id, 0);
        check(session.Document.Sheets[0].GuideGridMm == 0, "Zero takes the guide grid off");

        // View reference: finds the view by the start of its name and tells the sheet it is on.
        var placed = session.Document.Sheets.SelectMany(item => item.Viewports.Select(viewport => (item.Number, viewport.ViewId))).First();
        var target = session.Document.Views.First(view => view.Id == placed.ViewId);
        check(ViewReferences.Find(session.Document, target.Name.ToUpperInvariant()) is { } exact && exact.Id == target.Id &&
              ViewReferences.Find(session.Document, target.Name[..Math.Min(4, target.Name.Length)]) is not null &&
              ViewReferences.Find(session.Document, "no existe esta vista") is null && ViewReferences.Find(session.Document, "  ") is null,
            $"A view is found by its name, in any case, or by the start of it («{target.Name}»)");
        check(ViewReferences.Label(session.Document, target).EndsWith(placed.Number, StringComparison.Ordinal), $"Its label carries the sheet it is on: {ViewReferences.Label(session.Document, target)}");
        var unplaced = session.Document.Views.FirstOrDefault(view => !session.Document.Sheets.Any(item => item.Viewports.Any(viewport => viewport.ViewId == view.Id)));
        if (unplaced is not null) check(ViewReferences.Label(session.Document, unplaced) == unplaced.Name, "A view that is on no sheet is named alone");

        var steps = session.UndoNames.Count;
        var reference = session.AddViewReference(level, new Point2(3, 4), target.Id);
        var mark = session.Document.StructuralElements.Single(item => item.Id == reference);
        check(session.UndoNames.Count == steps + 1 && mark.Kind == StructuralElementKind.ViewReference && !mark.HasSolid && mark.Name == ViewReferences.Label(session.Document, target),
            "A view reference is one undoable step and carries the view and sheet as its text");
        check(Throws(() => session.AddViewReference(level, new Point2(0, 0), Guid.NewGuid())) && Throws(() => session.AddViewReference(Guid.NewGuid(), new Point2(0, 0), target.Id)),
            "A view or a level that does not exist is refused");
        check(PdfPlanExporter.Export(session.Document, SheetPaperSize.A3, new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero)).Length > 0, "The reference prints with the plan");

        var file = Path.Combine(Path.GetTempPath(), $"aids-{Guid.NewGuid():N}.g3d");
        session.SetSheetGuideGrid(sheet.Id, 40);
        ProjectFile.Save(file, session.Document);
        var reopened = ProjectFile.Load(file);
        File.Delete(file);
        check(reopened.Sheets[0].GuideGridMm == 40 && reopened.StructuralElements.Any(item => item.Kind == StructuralElementKind.Matchline) &&
              reopened.StructuralElements.Any(item => item.Kind == StructuralElementKind.ViewReference), "Guide grid, matchline and view reference survive saving and opening");
    }
}
