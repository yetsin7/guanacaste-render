using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>
    /// Builds the selected-wall summary from the same opening-aware layer takeoff used by CSV exports.
    /// This keeps the property palette and exported quantities numerically consistent.
    /// </summary>
    private string BuildWallQuantitySummary(Wall wall)
    {
        var wallType = _session.Document.WallTypes.Single(type => type.Id == wall.WallTypeId);
        var openings = _session.Document.Openings.Where(opening => opening.WallId == wall.Id).ToArray();
        var layers = WallLayerQuantitySchedule.For(wall, wallType, openings)
            .ToDictionary(layer => layer.Layer, StringComparer.Ordinal);
        var core = FindLayer(layers, "Core");
        var leftRepello = FindLayer(layers, "LeftRepello");
        var leftFine = FindLayer(layers, "LeftFine");
        var rightRepello = FindLayer(layers, "RightRepello");
        var rightFine = FindLayer(layers, "RightFine");

        return FormatText(
            "Properties.WallQuantitySummary",
            wallType.Name,
            Units.FormatLength(wall.Length),
            Units.FormatLength(wall.Thickness),
            Units.FormatLength(wall.Height),
            openings.Length,
            Units.FormatArea(core.NetFaceArea),
            Units.FormatLength(core.Thickness),
            Units.FormatVolume(core.NetVolume),
            Units.FormatLength(leftRepello.Thickness),
            Units.FormatVolume(leftRepello.NetVolume),
            Units.FormatLength(leftFine.Thickness),
            Units.FormatVolume(leftFine.NetVolume),
            Units.FormatLength(rightRepello.Thickness),
            Units.FormatVolume(rightRepello.NetVolume),
            Units.FormatLength(rightFine.Thickness),
            Units.FormatVolume(rightFine.NetVolume),
            Units.FormatVolume(leftRepello.NetVolume + rightRepello.NetVolume),
            Units.FormatVolume(leftFine.NetVolume + rightFine.NetVolume),
            Units.FormatVolume(layers.Values.Sum(layer => layer.NetVolume)),
            wall.Id.ToString()[..8]);
    }

    /// <summary>Builds a localized layer overview for a wall type selected in the project browser.</summary>
    private string BuildWallTypeSummary(WallType wallType) => FormatText(
        "Properties.WallTypeSummary",
        wallType.Name,
        GetText("WallFunction." + wallType.Function),
        Units.FormatLength(wallType.Thickness),
        wallType.MaterialName,
        Units.FormatLength(wallType.LeftRepelloThickness),
        Units.FormatLength(wallType.LeftFineThickness),
        Units.FormatLength(wallType.RightRepelloThickness),
        Units.FormatLength(wallType.RightFineThickness));

    /// <summary>Returns an existing quantity layer or a zero-valued placeholder for exposed faces.</summary>
    private static WallLayerQuantity FindLayer(
        IReadOnlyDictionary<string, WallLayerQuantity> layers,
        string layerName) =>
        layers.TryGetValue(layerName, out var layer)
            ? layer
            : new WallLayerQuantity(Guid.Empty, layerName, 0, 0, 0);
}
