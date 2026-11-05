using System.Globalization;

namespace Guanacaste3D.Core;

/// <summary>What importing a table of values did.</summary>
public sealed record TableImportReport(int Applied, int Rejected, int UnknownElements, IReadOnlyList<string> UnknownColumns);

public sealed partial class DocumentSession
{
    private static char Separator(string header) => header.Contains('\t') ? '\t' : header.Contains(';') ? ';' : ',';

    private static bool TryYesNo(string text, out double value)
    {
        var lower = text.Trim().ToLowerInvariant();
        value = 0;
        if (lower is "1" or "si" or "sí" or "true" or "yes" or "x" or "verdadero") { value = 1; return true; }
        return lower is "0" or "no" or "false" or "falso";
    }

    /// <summary>
    /// Fills parameter values from a table: a header row with <c>Id</c> and the names of project parameters, then one row
    /// per element with its identifier and its values. Empty cells are left alone; a value the parameter cannot take, or
    /// an element it is not bound to, is counted as rejected and the rest still go in. One undoable step.
    /// </summary>
    public TableImportReport ImportParameterValues(string? table)
    {
        var lines = (table ?? string.Empty).Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Trim().Length > 0).ToList();
        if (lines.Count < 2) throw new InvalidDataException("The table has no rows.");
        var separator = Separator(lines[0]);
        var header = lines[0].Split(separator).Select(cell => cell.Trim().Trim('"')).ToArray();
        if (!header[0].Equals("Id", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The first column of the table must be Id.");
        var columns = new List<(int Index, ParameterDefinition Definition)>();
        var unknown = new List<string>();
        for (var index = 1; index < header.Length; index++)
        {
            var definition = Document.ParameterDefinitions.FirstOrDefault(item => item.Name.Equals(header[index], StringComparison.CurrentCultureIgnoreCase));
            if (definition is null) { if (header[index].Length > 0) unknown.Add(header[index]); }
            else columns.Add((index, definition));
        }
        if (columns.Count == 0) throw new InvalidDataException("None of the columns is a project parameter.");
        var elements = ElementCatalog.Index(Document);
        var edits = new List<ParameterEdit>();
        int rejected = 0, unknownElements = 0;
        foreach (var line in lines.Skip(1))
        {
            var cells = line.Split(separator).Select(cell => cell.Trim().Trim('"')).ToArray();
            if (cells.Length == 0 || !Guid.TryParse(cells[0], out var id) || !elements.ContainsKey(id)) { unknownElements++; continue; }
            foreach (var (index, definition) in columns)
            {
                if (index >= cells.Length || cells[index].Length == 0) continue;
                var cell = cells[index];
                if (definition.Spec == ParameterSpec.Text) { edits.Add(new ParameterEdit(id, definition.Id, null, cell)); continue; }
                double number;
                var ok = definition.Spec switch
                {
                    ParameterSpec.YesNo => TryYesNo(cell, out number),
                    ParameterSpec.Length => Document.Units.TryParseLength(cell, out number),
                    ParameterSpec.Area => Document.Units.TryParseArea(cell, out number),
                    _ => double.TryParse(cell.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number)
                };
                if (!ok) { rejected++; continue; }
                edits.Add(new ParameterEdit(id, definition.Id, number, null));
            }
        }
        // Each edit is tried alone, so the ones the document refuses do not stop the others.
        var applied = 0;
        using var group = BeginGroup("Transaction.ImportParameterValues");
        foreach (var edit in edits)
        {
            try { SetParameterValues([edit]); applied++; }
            catch (InvalidDataException) { rejected++; }
        }
        if (applied > 0) group.Assimilate();
        return new TableImportReport(applied, rejected, unknownElements, unknown);
    }

    /// <summary>
    /// Brings review marks into a level from a table of "x;y;comment" lines (a comma or a tab also separates them): each becomes
    /// a note with a revision cloud round it. Returns how many were made and how many lines were left out.
    /// </summary>
    public (int Made, int Skipped) ImportReviewMarks(Guid levelId, string? table)
    {
        if (Document.Levels.All(level => level.Id != levelId)) throw new InvalidDataException("The level does not exist.");
        var marks = new List<(Point2 At, string Text)>();
        var skipped = 0;
        foreach (var raw in (table ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var separator = Separator(line);
            var parts = line.Split(separator, 3);
            if (parts.Length < 3 || !double.TryParse(parts[0].Replace(',', '.').Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(parts[1].Replace(',', '.').Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                !double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 1e6 || Math.Abs(y) > 1e6 || parts[2].Trim().Length == 0)
            {
                if (marks.Count > 0 || parts.Length >= 3) skipped++;
                continue;
            }
            var text = parts[2].Trim().Trim('"');
            marks.Add((new Point2(x, y), text.Length > 120 ? text[..120] : text));
        }
        if (marks.Count == 0) throw new InvalidDataException("The table has no review marks.");
        if (marks.Count > 2000) throw new InvalidDataException("The table has too many review marks.");
        Commit("Transaction.ImportReviewMarks", document =>
        {
            foreach (var (at, text) in marks)
            {
                document.StructuralElements.Add(new StructuralElement(Guid.NewGuid(), levelId, StructuralElementKind.RevisionCloud, "Marca de revisión",
                    new Point2(at.X - 1.2, at.Y - 0.7), new Point2(at.X + 1.2, at.Y + 0.7), 0, 0, 0));
                document.StructuralElements.Add(new StructuralElement(Guid.NewGuid(), levelId, StructuralElementKind.ModelText, text,
                    new Point2(at.X + 1.4, at.Y), new Point2(at.X + 1.4, at.Y), 0, 0, 0));
            }
        });
        return (marks.Count, skipped);
    }
}
