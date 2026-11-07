using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>Resolves the same level-based stair height for placement and its transient preview.</summary>
public partial class MainWindow
{
    /// <summary>Reports the actual generated rise and going rather than a generic structural section.</summary>
    private string StairProperties(StructuralElement stair)
    {
        var count = StairGeometry.RiserCount(stair);
        if (stair.Style == ElementStyle.Spiral)
            return FormatText("Properties.SpiralStairSummary", stair.Name, stair.Height, count,
                stair.Height / count, Math.Max(1.2, stair.Width), stair.Offset);
        var flights = StairGeometry.Flights(stair);
        var goings = flights.Select(flight => flight.Start.DistanceTo(flight.End) / flight.Risers).ToArray();
        return FormatText("Properties.StairSummary", stair.Name, stair.Height, count, stair.Height / count,
            stair.Width, flights.Count, goings.DefaultIfEmpty(0).Min(), goings.DefaultIfEmpty(0).Max(), stair.Offset);
    }

    /// <summary>Measures rise from the offset stair base to the nearest higher level, falling back to the preset rise.</summary>
    private double StairPlacementHeight(double defaultHeight, double baseOffset)
    {
        var elevation = _session.Document.Levels.First(level => level.Id == _activeLevelId).Elevation;
        var baseElevation = elevation + baseOffset;
        return _session.Document.Levels.Where(level => level.Elevation > baseElevation + 1e-6)
            .OrderBy(level => level.Elevation).Select(level => level.Elevation - baseElevation)
            .FirstOrDefault(defaultHeight);
    }
}
