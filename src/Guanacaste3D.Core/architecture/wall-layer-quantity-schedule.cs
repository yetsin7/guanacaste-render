using System.Globalization;
using System.Text;

namespace Guanacaste3D.Core;

/// <summary>
/// Net quantities for a constant-thickness wall layer. Areas exclude hosted door/window openings;
/// returns and reveals at openings, wall joins and paint coverage are not included.
/// </summary>
public sealed record WallLayerQuantity(Guid WallId, string Layer, double Thickness, double NetFaceArea, double NetVolume);

/// <summary>Deterministic per-layer wall takeoff for masonry core, repello and fino.</summary>
public static class WallLayerQuantitySchedule
{
    /// <summary>Calculates volumes from the same opening-aware wall solids used by the BIM model.</summary>
    public static IReadOnlyList<WallLayerQuantity> For(Wall wall, WallType type, IEnumerable<WallOpening> openings)
    {
        ArgumentNullException.ThrowIfNull(wall);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(openings);

        var hosted = openings.ToArray();
        WallGeometry.Validate([wall], hosted);
        var assembly = WallAssemblyPlanRules.For(type, wall.Thickness);
        var netArea = WallGeometry.Solids(wall, hosted)
            .Sum(part => (part.To - part.From) * (part.Top - part.Bottom));
        var layers = new List<WallLayerQuantity>(5);

        void Add(string name, double thickness)
        {
            if (thickness > 0)
                layers.Add(new WallLayerQuantity(wall.Id, name, thickness, netArea, netArea * thickness));
        }

        Add("LeftFine", assembly.LeftFine);
        Add("LeftRepello", assembly.LeftPlaster);
        Add("Core", assembly.CoreThickness);
        Add("RightRepello", assembly.RightPlaster);
        Add("RightFine", assembly.RightFine);
        return layers;
    }

    /// <summary>
    /// Exports one row per nonzero layer, without changing the existing wall quantity CSV contract.
    /// Values are metric and invariant-culture; wall and level ordering is deterministic.
    /// </summary>
    public static string ToCsv(BuildingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Validate();
        var types = document.WallTypes.ToDictionary(type => type.Id);
        var levels = document.Levels.ToDictionary(level => level.Id);
        var openings = document.Openings.ToLookup(opening => opening.WallId);
        var csv = new StringBuilder("Level,WallId,WallTypeId,Layer,Thickness_m,NetFaceArea_m2,NetVolume_m3\n");

        foreach (var wall in document.Walls.OrderBy(wall => wall.LevelId).ThenBy(wall => wall.Id))
        {
            foreach (var layer in For(wall, types[wall.WallTypeId], openings[wall.Id]))
            {
                csv.Append(SpreadsheetCsv.Quote(levels[wall.LevelId].Name)).Append(',')
                    .Append(wall.Id).Append(',')
                    .Append(wall.WallTypeId).Append(',')
                    .Append(layer.Layer).Append(',')
                    .Append(Format(layer.Thickness)).Append(',')
                    .Append(Format(layer.NetFaceArea)).Append(',')
                    .Append(Format(layer.NetVolume)).AppendLine();
            }
        }
        return csv.ToString();
    }

    /// <summary>
    /// Aggregates opening-aware wall layers by level, wall type, layer and actual thickness.
    /// This is a net quantity summary, not a joined-wall, reveal or paint estimate.
    /// Grouping by actual thickness prevents unlike instance overrides from being combined.
    /// </summary>
    public static string ToSummaryCsv(BuildingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Validate();
        var types = document.WallTypes.ToDictionary(type => type.Id);
        var levels = document.Levels.ToDictionary(level => level.Id);
        var openings = document.Openings.ToLookup(opening => opening.WallId);
        var entries = document.Walls.OrderBy(wall => wall.LevelId).ThenBy(wall => wall.Id)
            .SelectMany(wall => For(wall, types[wall.WallTypeId], openings[wall.Id])
                .Select((layer, order) => new
                {
                    wall.LevelId,
                    wall.WallTypeId,
                    layer.Layer,
                    layer.Thickness,
                    layer.NetFaceArea,
                    layer.NetVolume,
                    Order = order
                }));
        var groups = entries.GroupBy(row => new
            {
                row.LevelId,
                row.WallTypeId,
                row.Layer,
                row.Thickness,
                row.Order
            })
            .OrderBy(group => group.Key.LevelId)
            .ThenBy(group => group.Key.WallTypeId)
            .ThenBy(group => group.Key.Order)
            .ThenBy(group => group.Key.Thickness);
        var csv = new StringBuilder("Level,WallTypeId,WallType,Layer,Thickness_m,WallCount,NetFaceArea_m2,NetVolume_m3\n");
        foreach (var group in groups)
        {
            csv.Append(SpreadsheetCsv.Quote(levels[group.Key.LevelId].Name)).Append(',')
                .Append(group.Key.WallTypeId).Append(',')
                .Append(SpreadsheetCsv.Quote(types[group.Key.WallTypeId].Name)).Append(',')
                .Append(group.Key.Layer).Append(',')
                .Append(Format(group.Key.Thickness)).Append(',')
                .Append(group.Count().ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Format(group.Sum(row => row.NetFaceArea))).Append(',')
                .Append(Format(group.Sum(row => row.NetVolume))).AppendLine();
        }
        return csv.ToString();
    }

    private static string Format(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
