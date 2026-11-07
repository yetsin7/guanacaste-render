using Guanacaste3D.Core;

/// <summary>Checks importing parameter values and review marks from tables, on the breeze-block house.</summary>
static class TableImportChecks
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
        var text = session.AddParameter("Código", ParameterSpec.Text, ParameterBinding.Instance, [ElementCategory.Walls]);
        var cost = session.AddParameter("Costo", ParameterSpec.Number, ParameterBinding.Instance, [ElementCategory.Walls]);
        var thick = session.AddParameter("Espesor extra", ParameterSpec.Length, ParameterBinding.Instance, [ElementCategory.Walls]);
        var fire = session.AddParameter("Cortafuego", ParameterSpec.YesNo, ParameterBinding.Instance, [ElementCategory.Walls]);
        var doorsOnly = session.AddParameter("Solo puertas", ParameterSpec.Text, ParameterBinding.Instance, [ElementCategory.Doors]);
        var walls = session.Document.Walls.Take(6).Select(wall => wall.Id).ToArray();

        var table = string.Join("\n", new[]
        {
            "Id;Código;Costo;Espesor extra;Cortafuego;Inventada",
            $"{walls[0]};M-01;12,5;30 cm;sí;x",
            $"{walls[1]};M-02;7.25;0.1;no;",
            $"{walls[2]};;;;;",
            $"{walls[3]};M-04;caro;5 yd;quizás;",
            $"{Guid.NewGuid()};ZZ;1;1;1;",
            "no-es-un-guid;ZZ;1;1;1;"
        });
        var before = session.UndoNames.Count;
        var report = session.ImportParameterValues(table);
        string? Text(Guid id, Guid parameter) => session.Document.ParameterValues.FirstOrDefault(value => value.OwnerId == id && value.DefinitionId == parameter)?.Text;
        double? Number(Guid id, Guid parameter) => session.Document.ParameterValues.FirstOrDefault(value => value.OwnerId == id && value.DefinitionId == parameter)?.Number;
        check(Text(walls[0], text) == "M-01" && Number(walls[0], cost) == 12.5 && Math.Abs(Number(walls[0], thick)!.Value - 0.3) < 1e-9 && Number(walls[0], fire) == 1 &&
              Text(walls[1], text) == "M-02" && Number(walls[1], cost) == 7.25 && Number(walls[1], fire) == 0,
            "Values are read from the table: text, decimals with a comma or a point, a length with its unit, yes and no");
        check(Text(walls[2], text) is null && Number(walls[3], cost) is null && Number(walls[3], thick) is null && Number(walls[3], fire) is null && Text(walls[3], text) == "M-04",
            "Empty cells are left alone, and a value the parameter cannot take is refused without stopping the rest of its row");
        check(report.Applied == 9 && report.Rejected == 3 && report.UnknownElements == 2 && report.UnknownColumns.SequenceEqual(["Inventada"]) && session.UndoNames.Count == before + 1,
            $"The report counts {report.Applied} applied, {report.Rejected} refused, {report.UnknownElements} rows with no known element and the unknown column, in one undoable step");
        check(Throws(() => session.ImportParameterValues("")) && Throws(() => session.ImportParameterValues("Nombre;Costo\nx;1")) && Throws(() => session.ImportParameterValues("Id;Nada\n" + walls[0] + ";1")),
            "An empty table, one without an Id column and one with no parameter columns are refused");
        var doors = session.Document.Openings.First(opening => opening.Kind == OpeningKind.Door).Id;
        var bound = session.ImportParameterValues($"Id,Solo puertas,Código\n{walls[4]},x,M-05\n{doors},puerta-1,");
        check(bound.Applied == 2 && Text(doors, doorsOnly) == "puerta-1" && Text(walls[4], doorsOnly) is null && Text(walls[4], text) == "M-05",
            "A parameter only takes values on the kinds of element it is bound to (a comma works as separator, too)");
        session.Undo();
        check(Text(doors, doorsOnly) is null, "Undo takes the whole import back");

        // Review marks.
        var rooms = session.Document.StructuralElements.Count;
        var marks = session.ImportReviewMarks(level, "# revisión\n10;5;Falta cota\n12,5;7,25;Revisar el cobogó\nmala línea\n20\t3\t\"Con tab\"\n5;5;\n");
        check(marks.Made == 3 && marks.Skipped >= 1 && session.Document.StructuralElements.Count == rooms + 6 &&
              session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.RevisionCloud) >= 3 &&
              session.Document.StructuralElements.Any(item => item.Kind == StructuralElementKind.ModelText && item.Name == "Revisar el cobogó"),
            "Each review mark becomes a note with a revision cloud round it; lines that cannot be read are counted");
        check(Throws(() => session.ImportReviewMarks(level, "nada que leer")) && Throws(() => session.ImportReviewMarks(Guid.NewGuid(), "1;1;x")),
            "A table with no marks, or an unknown level, is refused");
    }
}
