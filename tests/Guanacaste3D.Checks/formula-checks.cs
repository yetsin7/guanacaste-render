using Guanacaste3D.Core;

/// <summary>
/// Regression checks for calculated parameters (schema 6): parsing, unit
/// analysis, functions, dependency order, cycle rejection, schedule
/// evaluation, read-only enforcement, and persistence.
/// </summary>
static class FormulaChecks
{
    /// <summary>Runs every formula check in sequence.</summary>
    public static void Run(Action<bool, string> check)
    {
        CheckParsingAndUnits(check);
        CheckEvaluation(check);
        CheckDocumentFormulas(check);
        CheckPersistence(check);
    }

    /// <summary>Type-checks a formula against a fixed symbol table and returns null when it is rejected.</summary>
    private static FormulaType? TypeOf(string formula)
    {
        var symbols = new Dictionary<string, FormulaType>(StringComparer.OrdinalIgnoreCase)
        {
            ["Width"] = new(FormulaKind.Number, 1), ["Height"] = new(FormulaKind.Number, 1),
            ["Area"] = new(FormulaKind.Number, 2), ["Count"] = FormulaType.Scalar,
            ["Finish"] = FormulaType.Text, ["Exterior"] = FormulaType.Boolean
        };
        try { return FormulaTypeChecker.Check(FormulaParser.Parse(formula), name => symbols.TryGetValue(name, out var type) ? type : null); }
        catch (InvalidDataException) { return null; }
    }

    /// <summary>Verifies syntax errors and dimensional analysis.</summary>
    private static void CheckParsingAndUnits(Action<bool, string> check)
    {
        check(TypeOf("Width * Height") == new FormulaType(FormulaKind.Number, 2) &&
              TypeOf("Area / Width") == new FormulaType(FormulaKind.Number, 1) &&
              TypeOf("sqrt(Area)") == new FormulaType(FormulaKind.Number, 1) &&
              TypeOf("Width ^ 3") == new FormulaType(FormulaKind.Number, 3),
            "Multiplication, division, square root, and powers combine units of length");
        check(TypeOf("Width + Area") is null && TypeOf("Width > 2") is null && TypeOf("sqrt(Width)") is null,
            "Inconsistent units are rejected (length + area, length vs number, sqrt of a length)");
        check(TypeOf("Width > 0") == FormulaType.Boolean && TypeOf("0 - Area") == new FormulaType(FormulaKind.Number, 2),
            "A bare zero adopts the unit of the other operand");
        check(TypeOf("Width > 2 * Height") == FormulaType.Boolean &&
              TypeOf("if(Exterior and Count >= 2, \"Ext\", Finish)") == FormulaType.Text &&
              TypeOf("if(Exterior, Width, Area)") is null,
            "Comparisons yield yes/no and if() branches must share one type");
        var syntaxError = "";
        try { FormulaParser.Parse("(Width + 2"); } catch (InvalidDataException exception) { syntaxError = exception.Message; }
        check(syntaxError.Contains("position", StringComparison.Ordinal) && TypeOf("Unknown * 2") is null,
            "Syntax errors report a position and unknown names are rejected");
        check(TypeOf("Width + 50 cm") == new FormulaType(FormulaKind.Number, 1) && TypeOf("Area > 2 m²") == FormulaType.Boolean &&
              TypeOf("Width + 2") is null && TypeOf("Width * 10mm") == new FormulaType(FormulaKind.Number, 2),
            "Number literals accept metric unit suffixes that take part in unit analysis");
        check(TypeOf("[Fire rating] + 1") is null && TypeOf("round(Width, 0.05)") == new FormulaType(FormulaKind.Number, 1),
            "Bracketed names resolve like bare names and round() accepts a step");
    }

    /// <summary>Verifies arithmetic, functions, rounding, and undefined results.</summary>
    private static void CheckEvaluation(Action<bool, string> check)
    {
        FormulaValue? Eval(string formula, double width = 1.234) => FormulaEvaluator.Evaluate(
            FormulaParser.Parse(formula), name => name.Equals("Width", StringComparison.OrdinalIgnoreCase)
                ? FormulaValue.Of(width) : name == "Missing" ? null : FormulaValue.Of(2));
        check(Eval("round(Width, 0.05)")?.Number is double r && Math.Abs(r - 1.25) < 1e-12 &&
              Eval("roundup(Width, 0.1)")?.Number is double up && Math.Abs(up - 1.3) < 1e-12 &&
              Eval("rounddown(Width)")?.Number == 1,
            "Rounding functions honor an optional step");
        check(Eval("if(Width > 1, 10, 20)")?.Number == 10 && Eval("2 ^ 3 + max(1, 4, 2)")?.Number == 12 &&
              Math.Abs(Eval("sin(30)")!.Value.Number - 0.5) < 1e-12 && Eval("0.1 + 0.2 = 0.3")?.IsTrue == true,
            "Conditionals, precedence, degree trigonometry, and tolerant equality");
        check(Math.Abs(Eval("Width + 50 cm")!.Value.Number - 1.734) < 1e-12 && Eval("2 mm * 1000")?.Number is double mm &&
              Math.Abs(mm - 2) < 1e-12, "Unit literals convert to meters before arithmetic");
        check(Eval("Width / 0") is null && Eval("sqrt(-1)") is null && Eval("Missing + 1") is null &&
              Eval("if(Width > 1, 5, Missing)")?.Number == 5,
            "Undefined results are empty, and if() evaluates only the chosen branch");
    }

    /// <summary>Verifies calculated parameters inside a document and its schedules.</summary>
    private static void CheckDocumentFormulas(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Formulas"));
        var levelId = session.Document.Levels[0].Id;
        var wallId = session.AddWall(levelId, new Point2(0, 0), new Point2(6, 0));
        var doorId = session.AddOpening(wallId, OpeningKind.Door, offset: 1.5);
        session.AddOpening(wallId, OpeningKind.Window, offset: 4.0);
        var cost = session.AddParameter("Unit cost", ParameterSpec.Currency, ParameterBinding.Instance,
            [ElementCategory.Doors, ElementCategory.Windows]);
        var area = session.AddParameter("Leaf area", ParameterSpec.Area, ParameterBinding.Instance,
            [ElementCategory.Doors, ElementCategory.Windows], formula: "Width * Alto");
        var total = session.AddParameter("Total cost", ParameterSpec.Currency, ParameterBinding.Instance,
            [ElementCategory.Doors, ElementCategory.Windows], formula: "[Unit cost] * [Leaf area] / 1 m²");
        session.SetParameterValue(doorId, cost, 100, null);
        var doors = CategoryScheduleService.Build(session.Document, ElementCategory.Doors);
        var doorRow = doors.Rows.Single();
        check(Math.Abs(doorRow.Values[area].Number!.Value - 0.9 * 2.1) < 1e-12 &&
              Math.Abs(doorRow.Values[total].Number!.Value - 100 * 0.9 * 2.1) < 1e-9,
            "Calculated parameters chain in dependency order using quantities and Spanish aliases");
        var windows = CategoryScheduleService.Build(session.Document, ElementCategory.Windows);
        check(windows.Rows.Single().Values.ContainsKey(area) && !windows.Rows.Single().Values.ContainsKey(total) &&
              !doors.Columns.Single(column => column.Definition?.Id == area).IsEditable,
            "Missing inputs leave a calculated cell empty and calculated columns are read-only");
        var rejected = false;
        try { session.SetParameterValue(doorId, area, 5, null); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "Calculated values cannot be edited directly");
        rejected = false;
        try { session.AddParameter("Bad units", ParameterSpec.Length, ParameterBinding.Instance, [ElementCategory.Doors], formula: "Width * Height"); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected && session.Document.ParameterDefinitions.Count == 3,
            "A formula whose units do not match its parameter type is rejected");
        rejected = false;
        try { session.UpdateParameter(cost, "Unit cost", [ElementCategory.Doors, ElementCategory.Windows], formula: "[Total cost] / 2"); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected && session.Document.ParameterDefinitions.Single(d => d.Id == cost).Formula.Length == 0,
            "Circular formulas are rejected without changing the document");
        rejected = false;
        try { session.UpdateParameter(cost, "Price", [ElementCategory.Doors, ElementCategory.Windows]); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "Renaming a parameter that a formula uses is rejected");
        session.UpdateOpeningType(BuiltInOpeningTypes.SingleDoor900Id, "Single Door", "1000 × 2100 mm", 1.0, 2.1, 0);
        var resized = CategoryScheduleService.Build(session.Document, ElementCategory.Doors).Rows.Single();
        check(Math.Abs(resized.Values[area].Number!.Value - 2.1) < 1e-12,
            "Calculated values follow model changes such as a type resize");
        session.UpdateParameter(cost, "Unit cost", [ElementCategory.Doors, ElementCategory.Windows], formula: "50");
        check(session.Document.ParameterValues.All(value => value.DefinitionId != cost),
            "Turning a stored parameter into a calculated one regenerates away its stored values");
    }

    /// <summary>Verifies that formulas survive a schema-6 round trip.</summary>
    private static void CheckPersistence(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Formula persistence"));
        session.AddParameter("Room perimeter", ParameterSpec.Length, ParameterBinding.Instance,
            [ElementCategory.Rooms], formula: "4 * sqrt(Area)");
        var path = Path.Combine(Path.GetTempPath(), $"guanacaste-formulas-{Guid.NewGuid():N}.g3");
        try
        {
            ProjectFile.Save(path, session.Document);
            var reloaded = ProjectFile.Load(path);
            check(reloaded.SchemaVersion == BuildingDocument.CurrentSchemaVersion && reloaded.ParameterDefinitions.Single().Formula == "4 * sqrt(Area)",
                "Formulas survive a .g3d round trip");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
        }
    }
}
