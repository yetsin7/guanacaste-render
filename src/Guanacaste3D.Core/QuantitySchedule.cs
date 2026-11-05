using System.Globalization;
using System.Text;

namespace Guanacaste3D.Core;

/// <summary>Deterministic, culture-invariant quantity takeoff for wall assemblies.</summary>
public static class QuantitySchedule
{
    /// <summary>Exports one invariant-culture quantity row per wall as CSV.</summary>
    public static string ToCsv(BuildingDocument document)
    {
        document.Validate();
        var csv = new StringBuilder();
        csv.AppendLine("Level,WallId,Length_m,Thickness_m,Height_m,GrossVolume_m3,NetVolume_m3,DoorCount,WindowCount," +
            "Finish,FinishArea_m2");
        foreach (var wall in document.Walls.OrderBy(w => w.LevelId).ThenBy(w => w.Id))
        {
            var level = document.Levels.Single(l => l.Id == wall.LevelId);
            var openings = document.Openings.Where(o => o.WallId == wall.Id).ToArray();
            csv.Append(EscapeCsv(level.Name)).Append(',')
                .Append(wall.Id).Append(',')
                .Append(FormatNumber(wall.Length)).Append(',')
                .Append(FormatNumber(wall.Thickness)).Append(',')
                .Append(FormatNumber(wall.Height)).Append(',')
                .Append(FormatNumber(wall.ElevationArea * wall.Thickness)).Append(',')
                .Append(FormatNumber(WallGeometry.NetVolume(wall, openings))).Append(',')
                .Append(openings.Count(o => o.Kind == OpeningKind.Door)).Append(',')
                .Append(openings.Count(o => o.Kind == OpeningKind.Window)).Append(',')
                .Append(wall.Finish).Append(',')
                .Append(FormatNumber(FinishRules.WallFinishArea(wall, openings))).AppendLine();
        }
        return csv.ToString();
    }

    /// <summary>Formats a metric value with deterministic decimal punctuation.</summary>
    private static string FormatNumber(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>Escapes a text value according to CSV quoting rules.</summary>
    private static string EscapeCsv(string value) => SpreadsheetCsv.Quote(value);
}
