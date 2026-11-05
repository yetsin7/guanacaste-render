using System.Globalization;
using System.Text;

namespace Guanacaste3D.Core;

/// <summary>
/// Geometric rough-opening returns through each constant-thickness wall layer.
/// These exposed cross-sections are not a specification for plaster wrapping, paint,
/// frames, trim or finished jambs; do not add them to finish takeoffs automatically.
/// </summary>
public sealed record WallOpeningRevealQuantity(
    Guid WallId, Guid OpeningId, OpeningKind OpeningKind, string Layer, double Thickness,
    double JambArea, double HeadArea, double SillArea)
{
    public double TotalArea => JambArea + HeadArea + SillArea;
}

/// <summary>Deterministic geometric takeoff of jambs, heads and window sills by wall layer.</summary>
public static class WallOpeningRevealSchedule
{
    /// <summary>
    /// Computes two vertical jambs and one head per opening. A sill return exists
    /// only when solid wall remains below the opening; a ground-level opening has none.
    /// The same opening validation and layer thicknesses as the wall BIM takeoff apply.
    /// </summary>
    public static IReadOnlyList<WallOpeningRevealQuantity> For(
        Wall wall, WallType type, IEnumerable<WallOpening> openings)
    {
        ArgumentNullException.ThrowIfNull(wall);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(openings);

        var hosted = openings.ToArray();
        var layers = WallLayerQuantitySchedule.For(wall, type, hosted);
        return hosted.OrderBy(opening => opening.Id)
            .SelectMany(opening => layers.Select(layer => new WallOpeningRevealQuantity(
                wall.Id, opening.Id, opening.Kind, layer.Layer, layer.Thickness,
                2 * opening.Height * layer.Thickness,
                opening.Width * layer.Thickness,
                opening.Sill > 0 ? opening.Width * layer.Thickness : 0)))
            .ToArray();
    }

    /// <summary>
    /// One CSV row per opening and nonzero layer. Values are meters and square meters;
    /// opening returns are geometric potential, not quantities of installed finishes.
    /// </summary>
    public static string ToCsv(BuildingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Validate();
        var types = document.WallTypes.ToDictionary(type => type.Id);
        var levels = document.Levels.ToDictionary(level => level.Id);
        var openings = document.Openings.ToLookup(opening => opening.WallId);
        var csv = new StringBuilder(
            "Level,WallId,OpeningId,OpeningKind,Layer,Thickness_m,JambArea_m2,HeadArea_m2,SillArea_m2,TotalRevealArea_m2\n");

        foreach (var wall in document.Walls.OrderBy(wall => wall.LevelId).ThenBy(wall => wall.Id))
        {
            foreach (var reveal in For(wall, types[wall.WallTypeId], openings[wall.Id]))
            {
                csv.Append(SpreadsheetCsv.Quote(levels[wall.LevelId].Name)).Append(',')
                    .Append(reveal.WallId).Append(',')
                    .Append(reveal.OpeningId).Append(',')
                    .Append(reveal.OpeningKind).Append(',')
                    .Append(reveal.Layer).Append(',')
                    .Append(Format(reveal.Thickness)).Append(',')
                    .Append(Format(reveal.JambArea)).Append(',')
                    .Append(Format(reveal.HeadArea)).Append(',')
                    .Append(Format(reveal.SillArea)).Append(',')
                    .Append(Format(reveal.TotalArea)).AppendLine();
            }
        }
        return csv.ToString();
    }


    /// <summary>
    /// Groups rough-opening returns by level, wall type, opening kind, layer and actual
    /// thickness. Counts distinct openings, not wall-layer rows. This is geometric
    /// potential only: installed finishes, wrapping, trim and paint are excluded.
    /// </summary>
    public static string ToSummaryCsv(BuildingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Validate();
        var types = document.WallTypes.ToDictionary(type => type.Id);
        var levels = document.Levels.ToDictionary(level => level.Id);
        var openings = document.Openings.ToLookup(opening => opening.WallId);
        var rows = document.Walls.OrderBy(wall => wall.LevelId).ThenBy(wall => wall.Id)
            .SelectMany(wall => For(wall, types[wall.WallTypeId], openings[wall.Id])
                .Select(reveal => new
                {
                    wall.LevelId,
                    wall.WallTypeId,
                    reveal.OpeningId,
                    reveal.OpeningKind,
                    reveal.Layer,
                    reveal.Thickness,
                    reveal.JambArea,
                    reveal.HeadArea,
                    reveal.SillArea
                }));
        var groups = rows.GroupBy(row => new
            {
                row.LevelId,
                row.WallTypeId,
                row.OpeningKind,
                row.Layer,
                row.Thickness
            })
            .OrderBy(group => group.Key.LevelId)
            .ThenBy(group => group.Key.WallTypeId)
            .ThenBy(group => group.Key.OpeningKind)
            .ThenBy(group => LayerOrder(group.Key.Layer))
            .ThenBy(group => group.Key.Thickness);

        var csv = new StringBuilder(
            "Level,WallTypeId,WallType,OpeningKind,Layer,Thickness_m,OpeningCount,JambArea_m2,HeadArea_m2,SillArea_m2,TotalRevealArea_m2\n");
        foreach (var group in groups)
        {
            var jamb = group.Sum(row => row.JambArea);
            var head = group.Sum(row => row.HeadArea);
            var sill = group.Sum(row => row.SillArea);
            csv.Append(SpreadsheetCsv.Quote(levels[group.Key.LevelId].Name)).Append(',')
                .Append(group.Key.WallTypeId).Append(',')
                .Append(SpreadsheetCsv.Quote(types[group.Key.WallTypeId].Name)).Append(',')
                .Append(group.Key.OpeningKind).Append(',')
                .Append(group.Key.Layer).Append(',')
                .Append(Format(group.Key.Thickness)).Append(',')
                .Append(group.Select(row => row.OpeningId).Distinct().Count().ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Format(jamb)).Append(',')
                .Append(Format(head)).Append(',')
                .Append(Format(sill)).Append(',')
                .Append(Format(jamb + head + sill)).AppendLine();
        }
        return csv.ToString();
    }

    private static int LayerOrder(string layer) => layer switch
    {
        "LeftFine" => 0,
        "LeftRepello" => 1,
        "Core" => 2,
        "RightRepello" => 3,
        "RightFine" => 4,
        _ => 5
    };

    private static string Format(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);
}
