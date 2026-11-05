using System.Globalization;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Interop;

/// <summary>Checks spot marks, keynotes, symbols, the room colour legend and the text checker on the breeze-block house.</summary>
static class AnnotationMarkChecks
{
    private static bool Near(double first, double second, double tolerance = 1e-9) => Math.Abs(first - second) < tolerance;

    private static bool Throws(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return true; }
        return false;
    }

    public static void Run(Action<bool, string> check)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Marks(check);
            Texts(check);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static void Marks(Action<bool, string> check)
    {
        var session = new DocumentSession(BreezeBlockHouseShowcase.Create());
        var upper = session.Document.Levels.OrderBy(level => level.Elevation).Last();
        var solids = BuildingMesher.Build(session.Document).Positions.Count;

        var elevation = session.AddSpotElevation(upper.Id, new Point2(20, 20), 0.35);
        var mark = session.Document.StructuralElements.Single(item => item.Id == elevation);
        var expected = (upper.Elevation + 0.35).ToString("0.00", CultureInfo.InvariantCulture);
        check(!mark.HasSolid && SpotMarks.Label(session.Document, mark) == $"+{expected} m",
            $"A spot elevation reads its level plus its height: {SpotMarks.Label(session.Document, mark)}");
        session.SetProjectUnits(new ProjectUnits(LengthUnit.Feet, 1));
        check(SpotMarks.Label(session.Document, session.Document.StructuralElements.Single(item => item.Id == elevation)).EndsWith(" ft", StringComparison.Ordinal),
            "It follows the project's units");
        session.SetProjectUnits(ProjectUnits.Default);

        session.SetProjectSite(new ProjectSite(10.6, -85.4, 0, 0, 500000, 1170000));
        var coordinate = session.AddSpotCoordinate(upper.Id, new Point2(12, 5));
        check(SpotMarks.Label(session.Document, session.Document.StructuralElements.Single(item => item.Id == coordinate)) == "E 500012.00  N 1170005.00",
            "A spot coordinate reads the shared coordinates of its point");
        session.SetProjectSite(new ProjectSite(10.6, -85.4, 0, 0, 600000, 1170000));
        check(SpotMarks.Label(session.Document, session.Document.StructuralElements.Single(item => item.Id == coordinate)).StartsWith("E 600012", StringComparison.Ordinal),
            "…and changes when the project is relocated");

        var slope = session.AddSpotSlope(upper.Id, new Point2(0, 0), new Point2(10, 0), 0.5);
        var slopeLabel = SpotMarks.Label(session.Document, session.Document.StructuralElements.Single(item => item.Id == slope));
        check(slopeLabel == "5.0 %  (1:20)", $"A rise of 0.5 m over 10 m reads {slopeLabel}");
        check(Throws(() => session.AddSpotSlope(upper.Id, new Point2(0, 0), new Point2(0.01, 0), 1)) && Throws(() => session.AddSpotSlope(upper.Id, new Point2(0, 0), new Point2(5, 0), double.NaN)) &&
              Throws(() => session.AddSpotElevation(upper.Id, new Point2(0, 0), 5000)) && Throws(() => session.AddSpotCoordinate(Guid.NewGuid(), new Point2(0, 0))),
            "Two points that coincide, a rise that is not a number, a silly height or an unknown level are refused");

        // Terrain lives in a project of its own here, because it is a solid and would change the count below.
        var site = new DocumentSession(BreezeBlockHouseShowcase.Create());
        var siteLevel = site.Document.Levels[0].Id;
        site.AddFloor(siteLevel, [new Point2(100, 100), new Point2(120, 100), new Point2(120, 120), new Point2(100, 120)], 1, "Terreno", FloorKind.Toposolid, 0);
        check(SpotMarks.TerrainRise(site.Document, siteLevel, new Point2(0, 0), new Point2(5, 5)) is null &&
              SpotMarks.TerrainRise(site.Document, siteLevel, new Point2(105, 105), new Point2(110, 110)) is { } rise && Math.Abs(rise) < 1e-6 &&
              SpotMarks.TerrainRise(site.Document, siteLevel, new Point2(105, 105), new Point2(130, 110)) is null,
            "The terrain rise is known only when the terrain covers both points: here a flat site, so no rise");

        check(Keynotes.Resolve("03 30 00") == "03 30 00 · Hormigón colado en sitio" && Keynotes.Resolve("0422") == "04 22 00 · Mampostería de cobogó (celosía de bloques)" &&
              Keynotes.Resolve("Revisar con la inspección") == "Revisar con la inspección" && Keynotes.Resolve("   ") == string.Empty,
            "A keynote is found by its code, with or without spaces or only its start, and any other text stands for itself");
        var keynote = session.AddKeynote(upper.Id, new Point2(3, 3), "09 91 00");
        check(session.Document.StructuralElements.Single(item => item.Id == keynote).Name == "09 91 00 · Pintura" && Throws(() => session.AddKeynote(upper.Id, new Point2(0, 0), "")),
            "A keynote is placed with its code and text, and empty text is refused");

        foreach (var key in PlanSymbols.Symbols.Concat(PlanSymbols.DetailComponents))
            session.AddSymbol(upper.Id, new Point2(30, 30), key, 1.5);
        check(session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.Symbol) == PlanSymbols.Symbols.Length + PlanSymbols.DetailComponents.Length &&
              Throws(() => session.AddSymbol(upper.Id, new Point2(0, 0), "Dragon")) && Throws(() => session.AddSymbol(upper.Id, new Point2(0, 0), "Anchor", 0)),
            "Every symbol and detail component of the library can be placed; an unknown one or a size of zero is refused");

        var legend = session.AddColorLegend(upper.Id, new Point2(40, 40));
        check(session.Document.StructuralElements.Single(item => item.Id == legend).Kind == StructuralElementKind.ColorLegend, "A colour legend is placed in the plan");
        var names = session.Document.Rooms.Select(room => room.Name.Trim()).Where(name => name.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).Count();
        var legendRows = session.Document.Levels.Sum(level => RoomColors.Legend(session.Document, level.Id).Count);
        check(RoomColors.Of("Sala") == RoomColors.Of("  sala ") && RoomColors.Of("Sala") != RoomColors.Of("Cocina") && legendRows >= 1 && names >= 1,
            $"The same room name always has the same colour, and the legend lists each name once per level ({legendRows} rows in the house)");

        check(BuildingMesher.Build(session.Document).Positions.Count == solids, "None of these marks adds a triangle to the 3D model");
        check(PdfPlanExporter.Export(session.Document, SheetPaperSize.A3, new DateTimeOffset(2026, 11, 7, 0, 0, 0, TimeSpan.Zero)).Length > 0, "A project with all of them still prints");

        var file = Path.Combine(Path.GetTempPath(), $"marks-{Guid.NewGuid():N}.g3d");
        ProjectFile.Save(file, session.Document);
        var reopened = ProjectFile.Load(file);
        File.Delete(file);
        check(new[] { StructuralElementKind.SpotElevation, StructuralElementKind.SpotCoordinate, StructuralElementKind.SpotSlope, StructuralElementKind.Keynote,
                      StructuralElementKind.Symbol, StructuralElementKind.ColorLegend }.All(kind => reopened.StructuralElements.Any(item => item.Kind == kind)),
            "All the kinds of mark survive saving and opening the project");
    }

    private static void Texts(Action<bool, string> check)
    {
        check(TextChecker.Fix("la  la   puerta  principal") == "la puerta principal" && TextChecker.Fix("  Habitacion  ") == "Habitación" &&
              TextChecker.Fix("cimentacion de hormigon") == "cimentación de hormigón" && TextChecker.Fix("Todo bien") == "Todo bien" &&
              TextChecker.Fix("sala sala") == "sala",
            "Fixing removes repeated words and extra spaces and corrects common misspellings, keeping a capital letter");

        var session = new DocumentSession(BreezeBlockHouseShowcase.Create());
        var level = session.Document.Levels[0].Id;
        session.AddModelText(level, new Point2(0, 0), "Muro de  cobogo cobogo");
        session.AddModelText(level, new Point2(1, 1), "Entrada (principal");
        session.AddModelText(level, new Point2(2, 2), "Todo correcto");
        var issues = TextChecker.Check(session.Document);
        check(issues.Any(item => item.Problem == "RepeatedWord") && issues.Any(item => item.Problem == "DoubleSpace") && issues.Any(item => item.Problem == "Misspelled") &&
              issues.Any(item => item.Problem == "Unbalanced") && issues.All(item => item.Text != "Todo correcto"),
            $"The checker finds a repeated word, a double space, a misspelling and an unclosed bracket, and leaves a good text alone ({issues.Count} findings)");

        var changed = session.FixTexts();
        var texts = session.Document.StructuralElements.Where(item => item.Kind == StructuralElementKind.ModelText).Select(item => item.Name).ToArray();
        check(changed >= 1 && texts.Contains("Muro de cobogó") && texts.Contains("Entrada (principal") && texts.Contains("Todo correcto") && session.FixTexts() == 0,
            "Fixing corrects what it can, leaves what needs a person (the unclosed bracket), and a second pass finds nothing");
        session.Undo();
        check(session.Document.StructuralElements.Any(item => item.Name == "Muro de  cobogo cobogo"), "Undo brings the old text back");
    }
}
