using System.Globalization;
using Guanacaste3D.Core.Interop;

namespace Guanacaste3D.Core;

/// <summary>One line of the area table: a level heading, a room, a level subtotal or the grand total.</summary>
public sealed record AreaScheduleRow(AreaScheduleRowKind Kind, string Number, string Name, double Area);

/// <summary>What an <see cref="AreaScheduleRow"/> represents, so the sheet can style it.</summary>
public enum AreaScheduleRowKind
{
    Level,
    Room,
    Subtotal,
    Total
}

/// <summary>
/// The area table ("cuadro de áreas") of a drawing set: every room grouped by level with a subtotal per level and a
/// grand total, drawn directly in paper millimeters so it stays legible at any sheet size.
/// </summary>
public static class AreaSchedule
{
    private static readonly (double, double, double) Ink = (0, 0, 0);

    /// <summary>Lists the rows in level order; levels without rooms are omitted.</summary>
    public static IReadOnlyList<AreaScheduleRow> Rows(BuildingDocument document)
    {
        var rows = new List<AreaScheduleRow>();
        var total = 0.0;
        foreach (var level in document.Levels.OrderBy(level => level.Elevation))
        {
            var rooms = document.Rooms.Where(room => room.LevelId == level.Id).OrderBy(room => room.Number, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (rooms.Count == 0) continue;
            rows.Add(new(AreaScheduleRowKind.Level, string.Empty, level.Name, 0));
            rows.AddRange(rooms.Select(room => new AreaScheduleRow(AreaScheduleRowKind.Room, room.Number, room.Name, room.Area)));
            var subtotal = rooms.Sum(room => room.Area);
            rows.Add(new(AreaScheduleRowKind.Subtotal, string.Empty, string.Empty, subtotal));
            total += subtotal;
        }
        if (rows.Count > 0) rows.Add(new(AreaScheduleRowKind.Total, string.Empty, string.Empty, total));
        return rows;
    }

    /// <summary>
    /// Draws the table inside a paper rectangle: a header, the rows and thin rules. Row height shrinks when the table
    /// would not fit, never below a legible minimum, and the viewport clips whatever remains.
    /// </summary>
    public static IReadOnlyList<SheetPrimitive> Primitives(BuildingDocument document, SheetViewport placement)
    {
        var (numberHeader, nameHeader, areaHeader) = (DefaultLabels.Get("AreaTable.Number"), DefaultLabels.Get("AreaTable.Room"),
            DefaultLabels.Get("AreaTable.Area"));
        var (levelTotalLabel, totalLabel) = (DefaultLabels.Get("AreaTable.LevelTotal"), DefaultLabels.Get("AreaTable.Total"));
        var rows = Rows(document);
        var graphics = new List<SheetPrimitive>();
        var left = placement.X;
        var right = placement.X + placement.Width;
        var available = placement.Height - 14;
        var rowHeight = Math.Clamp(available / (rows.Count + 1), 3.2, 5.5);
        var size = rowHeight * 0.55;
        var numberEnd = left + Math.Min(14, placement.Width * 0.2);
        var areaStart = right - Math.Min(26, placement.Width * 0.3);
        var y = placement.Y;

        void Rule(double at, double weight = 0.15) => graphics.Add(new SheetPolyline([new(left, at), new(right, at)], weight, Ink));
        void Cells(string number, string name, string area, bool bold)
        {
            graphics.Add(new SheetText(new(left + 1, y + (rowHeight - size) / 2), number, size, Ink, bold));
            graphics.Add(new SheetText(new(numberEnd + 1, y + (rowHeight - size) / 2), name, size, Ink, bold));
            graphics.Add(new SheetText(new(areaStart + 1, y + (rowHeight - size) / 2), area, size, Ink, bold));
        }

        Rule(y, 0.35);
        Cells(numberHeader, nameHeader, areaHeader, true);
        y += rowHeight;
        Rule(y, 0.35);
        foreach (var row in rows)
        {
            switch (row.Kind)
            {
                case AreaScheduleRowKind.Level:
                    Cells(string.Empty, row.Name, string.Empty, true);
                    break;
                case AreaScheduleRowKind.Room:
                    Cells(row.Number, row.Name, Format(row.Area), false);
                    break;
                case AreaScheduleRowKind.Subtotal:
                    Cells(string.Empty, levelTotalLabel, Format(row.Area), true);
                    break;
                default:
                    Rule(y, 0.35);
                    Cells(string.Empty, totalLabel, Format(row.Area), true);
                    break;
            }
            y += rowHeight;
            Rule(y, row.Kind is AreaScheduleRowKind.Subtotal or AreaScheduleRowKind.Total ? 0.3 : 0.1);
        }
        var bottom = Math.Max(y, placement.Y + rowHeight * 2);
        foreach (var x in new[] { left, numberEnd, areaStart, right })
            graphics.Add(new SheetPolyline([new(x, placement.Y), new(x, bottom)], 0.15, Ink));
        return graphics;
    }

    /// <summary>Formats an area with two decimals and the square meter unit.</summary>
    public static string Format(double area) => area.ToString("0.00", CultureInfo.CurrentCulture) + " m²";
}
