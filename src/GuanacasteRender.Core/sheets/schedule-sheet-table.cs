using System.Globalization;
using Guanacaste3D.Core.Interop;

namespace Guanacaste3D.Core;

/// <summary>
/// Draws a saved schedule (doors, windows, rooms...) on a drawing sheet: a title, a header row and the evaluated rows
/// with their group footers and grand total, in paper millimeters so the table stays legible at any sheet size. A
/// viewport shows a schedule when its view identifier is the schedule's.
/// </summary>
public static class ScheduleSheetTable
{
    private static readonly (double, double, double) Ink = (0, 0, 0);

    /// <summary>The heading of a column: the parameter's own name, or the localized name of a built-in field.</summary>
    public static string Header(CategoryScheduleColumn column)
    {
        if (column.Definition is { } definition) return definition.Name;
        var key = $"ScheduleColumn.{column.Key}";
        var label = DefaultLabels.Get(key);
        return label == key ? column.Key : label;
    }

    public static IReadOnlyList<SheetPrimitive> Primitives(BuildingDocument document, ScheduleDefinition definition,
        SheetViewport placement)
    {
        var table = ScheduleEvaluator.Evaluate(document, definition, CultureInfo.CurrentCulture,
            DefaultLabels.Get("ScheduleColumn.Yes"), DefaultLabels.Get("ScheduleColumn.No"));
        var headers = table.Columns.Select(Header).ToList();
        if (table.HasCountColumn) headers.Add(DefaultLabels.Get("ScheduleColumn.Count"));
        var graphics = new List<SheetPrimitive>();
        var left = placement.X;
        var right = placement.X + placement.Width;
        var rowHeight = Math.Clamp((placement.Height - 14) / (table.Rows.Count + 2), 3.2, 5.5);
        var size = rowHeight * 0.55;
        var width = (right - left) / Math.Max(1, headers.Count);
        var y = placement.Y;

        void Rule(double at, double weight) => graphics.Add(new SheetPolyline([new(left, at), new(right, at)], weight, Ink));
        void Cells(IReadOnlyList<string> cells, bool bold)
        {
            for (var index = 0; index < cells.Count && index < headers.Count; index++)
            {
                // Long text is shortened so it never runs into the next column.
                var room = Math.Max(1, (int)(width / (size * 0.55)));
                var text = cells[index].Length > room ? cells[index][..Math.Max(1, room - 1)] + "…" : cells[index];
                graphics.Add(new SheetText(new(left + index * width + 1, y + (rowHeight - size) / 2), text, size, Ink, bold));
            }
        }

        graphics.Add(new SheetText(new(left, y + (rowHeight - size) / 2), definition.Name.ToUpper(CultureInfo.CurrentCulture),
            size * 1.1, Ink, true));
        y += rowHeight;
        Rule(y, 0.35);
        Cells(headers, true);
        y += rowHeight;
        Rule(y, 0.35);
        foreach (var row in table.Rows)
        {
            var emphasis = row.Kind is not ScheduleRowKind.Element;
            if (row.Kind == ScheduleRowKind.GrandTotal) Rule(y, 0.35);
            Cells(row.Cells, emphasis);
            y += rowHeight;
            Rule(y, emphasis ? 0.25 : 0.1);
        }
        var top = placement.Y + rowHeight;
        for (var index = 0; index <= headers.Count; index++)
            graphics.Add(new SheetPolyline([new(left + index * width, top), new(left + index * width, y)], 0.15, Ink));
        return graphics;
    }
}
