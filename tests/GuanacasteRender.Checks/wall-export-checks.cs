using System.Text.RegularExpressions;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Interop;

/// <summary>Checks that exports and the model audit follow a wall's top profile and its base.</summary>
static class WallExportChecks
{
    private static bool Near(double first, double second, double tolerance = 1e-6) => Math.Abs(first - second) < tolerance;

    public static void Run(Action<bool, string> check)
    {
        var plain = new DocumentSession(BuildingDocument.Create("Plano"));
        var level = plain.Document.Levels[0].Id;
        var wall = plain.AddWall(level, new Point2(0, 0), new Point2(10, 0), 0.2, 3, BuiltInWallTypes.Exterior300Id);
        var ifc = IfcExporter.ToStep(plain.Document, DateTimeOffset.UnixEpoch);
        check(Regex.IsMatch(ifc, @"IFCEXTRUDEDAREASOLID\(#\d+,#\d+,#\d+,3\.\)") && ifc.Contains("IFCRECTANGLEPROFILEDEF") && !ifc.Contains("IFCDIRECTION((0.,-1.,0.))"), "A plain wall is still exported as a box 3 m tall");

        plain.SetWallBaseOffset(wall, -0.5);
        var lowered = IfcExporter.ToStep(plain.Document, DateTimeOffset.UnixEpoch);
        check(lowered.Contains("IFCCARTESIANPOINT((0.,0.,-0.5))") && Regex.IsMatch(lowered, @"IFCEXTRUDEDAREASOLID\(#\d+,#\d+,#\d+,3\.5\)"), "A wall taken 50 cm below its level is exported from there and 3.5 m tall");

        var gable = new DocumentSession(BuildingDocument.Create("Hastial"));
        var gableLevel = gable.Document.Levels[0].Id;
        var gableWall = gable.AddWall(gableLevel, new Point2(0, 0), new Point2(10, 0), 0.2, 4, BuiltInWallTypes.Exterior300Id);
        gable.SetWallProfile(gableWall, [new Point2(0, 2.5), new Point2(5, 4), new Point2(10, 2.5)]);
        var profiled = IfcExporter.ToStep(gable.Document, DateTimeOffset.UnixEpoch);
        check(profiled.Contains("IFCCARTESIANPOINT((5.,4.))") && profiled.Contains("IFCCARTESIANPOINT((10.,2.5))") && profiled.Contains("IFCDIRECTION((0.,-1.,0.))") &&
              Regex.IsMatch(profiled, @"IFCEXTRUDEDAREASOLID\(#\d+,#\d+,#\d+," + Regex.Escape(IfcExporter.Real(gable.Document.Walls.Single().Thickness)) + @"\)"), "A wall with a gable top is exported as its outline in elevation, with the peak at 4 m, extruded across its thickness");
        var peaks = Regex.Matches(profiled, @"IFCARBITRARYCLOSEDPROFILEDEF").Count;
        check(peaks == 1, "Only the wall that has a profile gets an outline");

        var outline = ReadOutline(profiled);
        check(outline.Count == 5 && outline.Take(2).All(point => Near(point.Y, 0)) && Near(Area(outline), gable.Document.Walls.Single().ElevationArea, 1e-6),
            $"The exported outline encloses the same area as the wall in elevation: {Area(outline):0.00} m²");

        // Schedules show the project's units.
        var table = ScheduleEvaluator.Evaluate(plain.Document, ScheduleFor(plain), System.Globalization.CultureInfo.InvariantCulture);
        var metric = table.Rows.First(row => row.Kind == ScheduleRowKind.Element);
        var cm = ScheduleEvaluator.Evaluate(plain.Document, ScheduleFor(plain), System.Globalization.CultureInfo.CurrentCulture, units: new ProjectUnits(LengthUnit.Centimeters, 0));
        var feet = ScheduleEvaluator.Evaluate(plain.Document, ScheduleFor(plain), System.Globalization.CultureInfo.CurrentCulture, units: new ProjectUnits(LengthUnit.Feet, 2));
        var length = plain.Document.Walls.Single().Length;
        var volume = WallGeometry.NetVolume(plain.Document.Walls.Single(), []);
        var here = System.Globalization.CultureInfo.CurrentCulture;
        check(metric.Cells[0] == "10" && cm.Rows.First(row => row.Kind == ScheduleRowKind.Element).Cells[0] == "1000" && feet.Rows.First(row => row.Kind == ScheduleRowKind.Element).Cells[0] == (length / 0.3048).ToString("0.00", here),
            "A schedule shows a 10 m wall as 1000 in centimeters and as feet in imperial");
        check(feet.Rows.First(row => row.Kind == ScheduleRowKind.Element).Cells[1] == (volume / (0.3048 * 0.3048 * 0.3048)).ToString("0.00", here) && feet.Rows.Single(row => row.Kind == ScheduleRowKind.GrandTotal).Cells[0] == (length / 0.3048).ToString("0.00", here),
            "Volumes and the totals row are converted too");
        var imperial = new ProjectUnits(LengthUnit.Feet, 2);
        check(imperial.Symbol("m") == "ft" && imperial.Symbol("m²") == "ft²" && imperial.Symbol("m³") == "ft³" && imperial.Symbol("°") == "°" && imperial.Symbol("") == "" && imperial.NumberFor("°", 5) is null, "Column units become the project's units, and other units are left as they are");

        // Wall schedules carry the net height, the net area and the base.
        var columns = ScheduleQuantities.ColumnsFor(ElementCategory.Walls).Select(column => column.Key).ToList();
        check(columns.Contains(ScheduleQuantities.NetHeight) && columns.Contains(ScheduleQuantities.Area) && columns.Contains(ScheduleQuantities.BaseOffset), "A wall schedule offers net height, area and base offset");
        var quantities = ScheduleQuantities.Compute(plain.Document, ElementCategory.Walls)[wall];
        check(Near((double)quantities[ScheduleQuantities.NetHeight], 3.5) && Near((double)quantities[ScheduleQuantities.BaseOffset], -0.5) && Near((double)quantities[ScheduleQuantities.Area], 35), "A wall taken 50 cm below its level reads 3.5 m tall and 35 m² in the schedule");
        var gableQuantities = ScheduleQuantities.Compute(gable.Document, ElementCategory.Walls)[gableWall];
        check(Near((double)gableQuantities[ScheduleQuantities.Area], gable.Document.Walls.Single().ElevationArea), "A wall with a gable top reads the area under its profile, not its full rectangle");

        // Audit.
        var low = new DocumentSession(BuildingDocument.Create("Dintel"));
        var lowLevel = low.Document.Levels[0].Id;
        var lowWall = low.AddWall(lowLevel, new Point2(0, 0), new Point2(10, 0), 0.2, 3, BuiltInWallTypes.Exterior300Id);
        var window = low.AddOpening(lowWall, OpeningKind.Window, 1.5, 1.0, 0.5, 1.05);
        bool HeadWarning() => ModelAudit.Analyze(low.Document, 0.1, 0.3).Any(issue => issue.Code == ModelAuditCode.LowOpeningHeadClearance && issue.RelatedId == lowWall);
        check(!HeadWarning(), "A window with 1.45 m above it raises no head clearance warning");
        low.SetWallProfile(lowWall, WallProfiles.Shed(10, 3, 0.15));
        check(HeadWarning(), "Under a sloping top the same window is flagged, because the wall is only 10 cm above it there");
        check(window != Guid.Empty, "The window was placed");
    }

    private static ScheduleDefinition ScheduleFor(DocumentSession session)
    {
        var id = session.AddSchedule("Muros " + session.Document.Schedules.Count, ElementCategory.Walls, [ScheduleQuantities.Length, ScheduleQuantities.NetVolume]);
        return session.Document.Schedules.Single(schedule => schedule.Id == id);
    }

    private static List<Point2> ReadOutline(string ifc)
    {
        var polyline = Regex.Matches(ifc, @"IFCPOLYLINE\(\(([^)]*)\)\)").Select(match => match.Groups[1].Value).Last();
        var points = new List<Point2>();
        foreach (var reference in polyline.Split(',').Select(text => text.Trim()).Distinct())
        {
            var match = Regex.Match(ifc, Regex.Escape(reference) + @"=IFCCARTESIANPOINT\(\(([^,)]+),([^,)]+)\)\)");
            if (match.Success) points.Add(new Point2(double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)));
        }
        return points;
    }

    private static double Area(IReadOnlyList<Point2> points)
    {
        var twice = 0.0;
        for (var index = 0; index < points.Count; index++)
        {
            var next = points[(index + 1) % points.Count];
            twice += points[index].X * next.Y - next.X * points[index].Y;
        }
        return Math.Abs(twice) / 2;
    }
}
