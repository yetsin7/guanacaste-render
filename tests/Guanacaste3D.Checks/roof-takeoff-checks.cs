using Guanacaste3D.Core;

/// <summary>Regression checks for true roof surface area in material takeoffs and budgets.</summary>
static class RoofTakeoffChecks
{
    public static void Run(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Roof quantities"));
        var level = session.Document.Levels[0].Id;
        var preset = ArchitecturalLibrary.SlabPresets.Single(slab => slab.Name == "Gable Roof 30°");
        var id = session.AddSlabFromPreset(level,
            [new(0, 0), new(10, 0), new(10, 6), new(0, 6)], preset, 3.1);
        var roof = session.Document.Floors.Single(floor => floor.Id == id);

        // A surveyed site may use large absolute coordinates. The plan area must
        // remain invariant under translation, including for concave outlines.
        const double survey = 1_000_000_000d;
        var translatedFloor = roof with
        {
            Kind = FloorKind.Floor, Shape = RoofShape.Flat, Pitch = 0, Overhang = 0,
            Boundary =
            [
                new(survey, survey), new(survey + 10, survey),
                new(survey + 10, survey + 6), new(survey, survey + 6)
            ]
        };
        check(Math.Abs(translatedFloor.CalculateArea() - 60) < 1e-8 &&
              Math.Abs(FinishRules.FloorFinishArea(translatedFloor) - 60) < 1e-8,
            "Floor plan and finish quantities retain their area at large survey coordinates");

        var translatedConcave = translatedFloor with
        {
            Boundary =
            [
                new(survey, survey), new(survey + 10, survey),
                new(survey + 10, survey + 6), new(survey + 7, survey + 6),
                new(survey + 7, survey + 2), new(survey + 3, survey + 2),
                new(survey + 3, survey + 6), new(survey, survey + 6)
            ]
        };
        check(Math.Abs(translatedConcave.CalculateArea() - 44) < 1e-8 &&
              Math.Abs((translatedConcave with
              {
                  Boundary = [.. translatedConcave.Boundary.AsEnumerable().Reverse()]
              }).CalculateArea() - 44) < 1e-8,
            "Concave slab takeoff preserves area and winding independence far from the origin");

        // 10 x 6 m plan with 0.6 m eaves on every side: 11.2 x 7.2 m projected.
        var projected = 11.2 * 7.2;
        var expected = projected / Math.Cos(Math.PI / 6);
        check(Math.Abs(RoofGeometry.SurfaceArea(roof) - expected) < 1e-8 &&
              Math.Abs(FinishRules.FloorFinishArea(roof) - expected) < 1e-8,
            "Roof finish quantities include pitched surface and overhanging eaves");

        check(Math.Abs(RoofGeometry.SurfaceArea(roof with { Overhang = 0 }) -
                       60 / Math.Cos(Math.PI / 6)) < 1e-8,
            "A roof without eaves includes its slope but not extra projected area");

        check(new[] { RoofShape.Shed, RoofShape.Gable, RoofShape.Hip }.All(shape =>
                Math.Abs(RoofGeometry.SurfaceArea(roof with { Shape = shape }) - expected) < 1e-8),
            "Shed, gable and hip roof facets have consistent slope-area quantities");

        check(Math.Abs(RoofGeometry.SurfaceArea(roof with { Shape = RoofShape.Flat, Pitch = 0 }) -
                       projected) < 1e-8 &&
              Math.Abs(FinishRules.FloorFinishArea(roof with
              {
                  Kind = FloorKind.Floor, Shape = RoofShape.Flat, Pitch = 0
              }) - 60) < 1e-8,
            "Flat roof eaves are counted, but regular floors keep their plan area");

        check(Math.Abs(RoofGeometry.ProjectedPerimeter(roof) - 36.8) < 1e-8 &&
              Math.Abs(RoofGeometry.ProjectedPerimeter(roof with { Overhang = 0 }) - 32) < 1e-8,
            "Roof projected perimeter includes eaves without confusing it with sloped rake lengths");

        // Unlike the plan perimeter, edge lengths follow the roof's actual 3D geometry.
        var gableEdges = RoofGeometry.EdgeTakeoff(roof);
        var gableRakes = 2 * 7.2 / Math.Cos(Math.PI / 6);
        check(Math.Abs(gableEdges.HorizontalBoundaryLength - 22.4) < 1e-8 &&
              Math.Abs(gableEdges.SlopedBoundaryLength - gableRakes) < 1e-8 &&
              Math.Abs(gableEdges.RidgeLength - 11.2) < 1e-8 &&
              Math.Abs(gableEdges.HipLength) < 1e-8 &&
              Math.Abs(gableEdges.LowEaveBoundaryLength - 22.4) < 1e-8 &&
              Math.Abs(gableEdges.ElevatedHorizontalBoundaryLength) < 1e-8,
            "Gable roof takeoff distinguishes level eaves, 3D rakes and ridge");

        var shedEdges = RoofGeometry.EdgeTakeoff(roof with { Shape = RoofShape.Shed });
        check(Math.Abs(shedEdges.HorizontalBoundaryLength - 22.4) < 1e-8 &&
              Math.Abs(shedEdges.SlopedBoundaryLength - gableRakes) < 1e-8 &&
              shedEdges.RidgeLength == 0 && shedEdges.HipLength == 0 &&
              Math.Abs(shedEdges.LowEaveBoundaryLength - 11.2) < 1e-8 &&
              Math.Abs(shedEdges.ElevatedHorizontalBoundaryLength - 11.2) < 1e-8,
            "Shed roof counts the high level edge without inventing a ridge or hip");

        var hipEdges = RoofGeometry.EdgeTakeoff(roof with { Shape = RoofShape.Hip });
        var hipRise = 3.6 * Math.Tan(Math.PI / 6);
        var expectedHips = 4 * Math.Sqrt(2 * 3.6 * 3.6 + hipRise * hipRise);
        check(Math.Abs(hipEdges.HorizontalBoundaryLength - 36.8) < 1e-8 &&
              Math.Abs(hipEdges.SlopedBoundaryLength) < 1e-8 &&
              Math.Abs(hipEdges.RidgeLength - 4) < 1e-8 &&
              Math.Abs(hipEdges.HipLength - expectedHips) < 1e-8 &&
              Math.Abs(hipEdges.LowEaveBoundaryLength - 36.8) < 1e-8 &&
              Math.Abs(hipEdges.ElevatedHorizontalBoundaryLength) < 1e-8,
            "Hip roof measures four inclined hips and its level central ridge");

        // The generated ridge spans the bounding box, but a U-shaped footprint
        // has an open notch where no physical roof or ridge exists.
        var uBoundary = new Point2[]
        {
            new(0, 0), new(10, 0), new(10, 6), new(7, 6),
            new(7, 2), new(3, 2), new(3, 6), new(0, 6)
        };
        var uRoof = roof with { Boundary = [.. uBoundary], Overhang = 0 };
        var uEdges = RoofGeometry.EdgeTakeoff(uRoof);
        check(Math.Abs(uEdges.RidgeLength - 6) < 1e-8 &&
              Math.Abs(uEdges.HipLength) < 1e-8,
            "Concave U-shaped roof excludes ridge length across its open notch");

        var reversedURoof = uRoof with { Boundary = [.. uBoundary.Reverse()] };
        var reversedEdges = RoofGeometry.EdgeTakeoff(reversedURoof);
        check(Math.Abs(reversedEdges.RidgeLength - uEdges.RidgeLength) < 1e-8 &&
              Math.Abs(reversedEdges.SlopedBoundaryLength - uEdges.SlopedBoundaryLength) < 1e-8,
            "Concave roof edge takeoff is independent of polygon winding");

        var flatEdges = RoofGeometry.EdgeTakeoff(roof with { Shape = RoofShape.Flat, Pitch = 0 });
        check(Math.Abs(flatEdges.HorizontalBoundaryLength - 36.8) < 1e-8 &&
              flatEdges.SlopedBoundaryLength == 0 && flatEdges.RidgeLength == 0 && flatEdges.HipLength == 0 &&
              Math.Abs(flatEdges.LowEaveBoundaryLength - 36.8) < 1e-8 &&
              Math.Abs(flatEdges.ElevatedHorizontalBoundaryLength) < 1e-8,
            "Flat roof has only horizontal perimeter and no artificial crease lengths");

        var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
            var csv = RoofQuantitySchedule.ToCsv(session.Document);
            check(csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 2 &&
                  csv.Contains("CoveredPlanArea_m2", StringComparison.Ordinal) &&
                  csv.Contains(",80.64,", StringComparison.Ordinal) &&
                  csv.Contains(",36.8,\"", StringComparison.Ordinal) &&
                  csv.Contains("ProjectedPerimeter_m,RoofName,HorizontalBoundary_m,SlopedBoundary_m,Ridge_m,Hip_m,LowEaveBoundary_m,ElevatedHorizontalBoundary_m", StringComparison.Ordinal) &&
                  csv.Contains(",11.2,0,22.4,0", StringComparison.Ordinal),
                "Roof quantity CSV includes overhang, coverage and projected perimeter in invariant culture");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previousCulture;
        }

        check(RoofQuantitySchedule.ToCsv(BuildingDocument.Create())
                .Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 1,
            "Roof quantity CSV omits non-roof slabs and retains a header for empty projects");

        // User-controlled level/roof labels must be escaped as CSV and must not execute
        // formulas when an exported estimate is opened in a spreadsheet.
        var exportDocument = session.Document.Clone();
        var levelIndex = exportDocument.Levels.FindIndex(item => item.Id == level);
        exportDocument.Levels[levelIndex] = exportDocument.Levels[levelIndex] with { Name = " =1+1" };
        var roofIndex = exportDocument.Floors.FindIndex(item => item.Id == id);
        exportDocument.Floors[roofIndex] = exportDocument.Floors[roofIndex] with { Name = "@SUM(1,2)" };
        var guardedCsv = RoofQuantitySchedule.ToCsv(exportDocument);
        check(guardedCsv.Contains("\"' =1+1\",", StringComparison.Ordinal) &&
              guardedCsv.Contains(",\"'@SUM(1,2)\",22.4,", StringComparison.Ordinal),
            "Roof CSV protects spreadsheet formulas in level and roof names");

        exportDocument.Levels[levelIndex] = exportDocument.Levels[levelIndex] with
        {
            Name = "Nivel, \"Norte\""
        };
        exportDocument.Floors[roofIndex] = exportDocument.Floors[roofIndex] with
        {
            Name = "Techo, \"Principal\""
        };
        var quotedCsv = RoofQuantitySchedule.ToCsv(exportDocument);
        check(quotedCsv.Contains("\"Nivel, \"\"Norte\"\"\",", StringComparison.Ordinal) &&
              quotedCsv.Contains(",\"Techo, \"\"Principal\"\"\",22.4,", StringComparison.Ordinal),
            "Roof CSV escapes commas and double quotes in user-authored labels");

        var budget = BudgetCalculator.Build(session.Document, UnitPriceList.Default(), 0);
        var roofLine = budget.Lines.Single(line => line.Code == UnitPriceList.RoofArea);
        check(Math.Abs(roofLine.Quantity - Math.Round(expected, 3)) < 1e-9 &&
              roofLine.Total == Math.Round((decimal)roofLine.Quantity * roofLine.UnitPrice, 2),
            "Roof budgets price the true covered surface rather than the smaller plan area");
    }
}
