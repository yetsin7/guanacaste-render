using System.Globalization;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Interop;

/// <summary>Checks project units (formatting and reading) and the shared parameter file, on the breeze-block house.</summary>
static class UnitsAndSharedParameterChecks
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
            Units(check);
            SharedParameters(check);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static void Units(Action<bool, string> check)
    {
        var meters = new ProjectUnits(LengthUnit.Meters, 2);
        var feet = new ProjectUnits(LengthUnit.Feet, 3);
        check(meters.FormatLength(1.2) == "1.20 m" && new ProjectUnits(LengthUnit.Centimeters, 0).FormatLength(1.2) == "120 cm" &&
              new ProjectUnits(LengthUnit.Millimeters, 0).FormatLength(0.0254) == "25 mm" && feet.FormatLength(0.3048) == "1.000 ft" &&
              new ProjectUnits(LengthUnit.Inches, 1).FormatLength(0.0254) == "1.0 in",
            "Lengths are shown in the project's unit and decimals: 1.20 m, 120 cm, 25 mm, 1.000 ft, 1.0 in");
        check(meters.FormatArea(12.5) == "12.50 m²" && feet.FormatArea(0.09290304) == "1.000 ft²" && feet.FormatVolume(0.028316846592) == "1.000 ft³" &&
              meters.FormatVolume(3) == "3.00 m³", "Areas and volumes follow the length unit: metric in m² and m³, imperial in ft² and ft³");

        check(meters.TryParseLength("2.5", out var a) && Near(a, 2.5) && meters.TryParseLength(" 30 cm ", out var b) && Near(b, 0.3) &&
              meters.TryParseLength("10ft", out var c) && Near(c, 3.048) && meters.TryParseLength("4\"", out var d) && Near(d, 0.1016) &&
              meters.TryParseLength("5'", out var e) && Near(e, 1.524) && meters.TryParseLength("120 mm", out var f) && Near(f, 0.12) &&
              feet.TryParseLength("10", out var g) && Near(g, 3.048) && feet.TryParseLength("2 m", out var h) && Near(h, 2),
            "A typed length is in the project's unit, or in the unit written after it (m, cm, mm, ft, in, ' and \")");
        check(!meters.TryParseLength("", out _) && !meters.TryParseLength("abc", out _) && !meters.TryParseLength("NaN", out _) &&
              !meters.TryParseLength("1e999", out _) && !meters.TryParseLength("5 yards", out _) && !meters.TryParseLength(null, out _),
            "Text that is not a length is not read");

        // Whatever the unit, writing a value and reading it back gives the same length, to the precision shown.
        var random = new Random(20261105);
        var worst = 0.0;
        foreach (var unit in Enum.GetValues<LengthUnit>())
            for (var decimals = 0; decimals <= 4; decimals++)
                for (var round = 0; round < 25; round++)
                {
                    var units = new ProjectUnits(unit, decimals);
                    var value = random.NextDouble() * 50;
                    if (!units.TryParseLength(units.LengthNumber(value), out var back)) { worst = double.MaxValue; continue; }
                    worst = Math.Max(worst, Math.Abs(back - value) - 0.5 * Math.Pow(10, -decimals) * units.MetersPerUnit);
                }
        check(worst <= 1e-9, "Showing a length and reading it back stays within the precision shown, in every unit and decimals (500 random values)");
        check(new ProjectUnits(LengthUnit.Meters, 7).IsValid == false && new ProjectUnits((LengthUnit)99, 2).IsValid == false && ProjectUnits.Default.IsValid,
            "Units outside the supported range are invalid");

        var session = new DocumentSession(BreezeBlockHouseShowcase.Create());
        var level = session.Document.Levels[0].Id;
        var wallA = session.AddWall(level, new Point2(800, 800), new Point2(804, 800));
        var wallB = session.AddWall(level, new Point2(808, 800), new Point2(812, 800));
        var dimension = session.AddDimensionBetween(wallA, wallB);
        var steps = session.UndoNames.Count;
        var walls = session.Document.Walls.Select(wall => (wall.Id, wall.Start, wall.End, wall.Thickness, wall.Height)).ToArray();
        session.SetProjectUnits(new ProjectUnits(LengthUnit.Feet, 1));
        var after = session.Document.Walls.Select(wall => (wall.Id, wall.Start, wall.End, wall.Thickness, wall.Height)).ToArray();
        check(session.UndoNames.Count == steps + 1 && walls.SequenceEqual(after) && session.Document.Units.Length == LengthUnit.Feet,
            "Changing the units is one undoable step and does not touch a single wall");
        var pdf = PdfPlanExporter.Export(session.Document, SheetPaperSize.A3, new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero));
        var dxf = DxfExporter.ToAscii(session.Document);
        check(dxf.Contains("13.1 ft") && pdf.Length > 0, "A dimension is written in the project's unit in the DXF (4 m = 13.1 ft)");
        _ = dimension;

        var file = Path.Combine(Path.GetTempPath(), $"units-{Guid.NewGuid():N}.g3d");
        ProjectFile.Save(file, session.Document);
        var reopened = ProjectFile.Load(file);
        File.Delete(file);
        check(reopened.Units == new ProjectUnits(LengthUnit.Feet, 1), "The units survive saving and opening the project");
        session.Undo();
        check(session.Document.Units == ProjectUnits.Default && Throws(() => session.SetProjectUnits(new ProjectUnits(LengthUnit.Meters, 9))),
            "Undo brings the old units back, and invalid units are refused");
    }

    private static void SharedParameters(Action<bool, string> check)
    {
        var session = new DocumentSession(BreezeBlockHouseShowcase.Create());
        var first = session.AddParameter("Resistencia al fuego", ParameterSpec.Text, ParameterBinding.Type, [ElementCategory.Walls], "Seguridad", "Clasificación en horas");
        var second = session.AddParameter("Espesor mínimo", ParameterSpec.Length, ParameterBinding.Instance, [ElementCategory.Walls, ElementCategory.Floors], "Dimensiones");
        var third = session.AddParameter("Aprobado", ParameterSpec.YesNo, ParameterBinding.Instance, [ElementCategory.Doors]);
        var calculated = session.AddParameter("Doble espesor", ParameterSpec.Length, ParameterBinding.Instance, [ElementCategory.Walls], "Dimensiones", "", "[Espesor mínimo] * 2");

        var text = SharedParameterFile.Write(session.Document.ParameterDefinitions.Where(definition => definition.Formula.Length == 0));
        var read = SharedParameterFile.Read(text);
        check(text.Contains("*PARAM\tGUID\tNAME\tDATATYPE") && text.Contains("PARAM\t" + first.ToString("D") + "\tResistencia al fuego\tTEXT") &&
              text.Contains("\tLENGTH\t") && text.Contains("\tYESNO\t") && !text.Contains("Doble espesor"),
            "The file is written in Revit's shared parameter format, with each parameter's GUID and type, and without calculated ones");
        check(read.Parameters.Count == 3 && read.Skipped == 0 &&
              read.Parameters.Single(item => item.Id == first) is { Name: "Resistencia al fuego", Spec: ParameterSpec.Text, Group: "Seguridad", Description: "Clasificación en horas" } &&
              read.Parameters.Single(item => item.Id == second).Spec == ParameterSpec.Length && read.Parameters.Single(item => item.Id == third).Group == "General",
            "Reading the file gives back the same parameters: identifiers, names, types, groups and descriptions");

        var revit = "# This is a Revit shared parameter file.\r\n*META\tVERSION\tMINVERSION\r\nMETA\t2\t1\r\n*GROUP\tID\tNAME\r\nGROUP\t1\tIdentidad\r\n" +
                    "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE\r\n" +
                    "PARAM\t6b7e2c1a-0d8e-4f35-9a40-3c1d5e7f9a11\tCódigo\tTEXT\t\t1\t1\tCódigo de obra\t1\t0\r\n" +
                    "PARAM\t6b7e2c1a-0d8e-4f35-9a40-3c1d5e7f9a12\tFoto\tIMAGE\t\t1\t1\t\t1\t0\r\n" +
                    "PARAM\tnot-a-guid\tRoto\tTEXT\t\t1\t1\t\t1\t0\r\n" +
                    "PARAM\t6b7e2c1a-0d8e-4f35-9a40-3c1d5e7f9a13\tcódigo\tTEXT\t\t1\t1\t\t1\t0\r\n" +
                    "PARAM\t6b7e2c1a-0d8e-4f35-9a40-3c1d5e7f9a14\tSuperficie\tAREA\t\t1\t1\t\t1\t0\r\n";
        var fromRevit = SharedParameterFile.Read(revit);
        check(fromRevit.Parameters.Select(item => item.Name).SequenceEqual(["Código", "Superficie"]) && fromRevit.Skipped == 3 &&
              fromRevit.Parameters[0].Group == "Identidad" && fromRevit.Parameters[1].Spec == ParameterSpec.Area,
            "A file from Revit is read: the group is looked up by its number, and an unknown type, a bad GUID and a repeated name are skipped (3)");
        check(Throws(() => SharedParameterFile.Read("esto no es un archivo")) && Throws(() => SharedParameterFile.Read("")),
            "Text that is not a shared parameter file is refused");

        var other = new DocumentSession(BreezeBlockHouseShowcase.Create());
        var added = other.ImportSharedParameters(read.Parameters, [ElementCategory.Walls], ParameterBinding.Instance);
        check(added == 3 && other.Document.ParameterDefinitions.Select(item => item.Id).Order().SequenceEqual(new[] { first, second, third }.Order()) &&
              other.Document.ParameterDefinitions.All(item => item.Categories.SequenceEqual([ElementCategory.Walls])),
            "Importing adds the parameters to another project with the same GUIDs, in the chosen category");
        check(other.ImportSharedParameters(read.Parameters, [ElementCategory.Walls], ParameterBinding.Instance) == 0 &&
              Throws(() => other.ImportSharedParameters(fromRevit.Parameters, [], ParameterBinding.Instance)),
            "Importing them again adds nothing, and importing with no category is refused");
        var wall = other.Document.Walls[0].Id;
        other.SetParameterValue(wall, second, 0.4, null);
        check(other.Document.ParameterValues.Any(value => value.OwnerId == wall && value.DefinitionId == second),
            "An imported parameter takes values like one made in the project");
        _ = calculated;
    }
}
