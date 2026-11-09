namespace Guanacaste3D.Core;

/// <summary>Surface finishes applied to walls and floors. <see cref="None"/> means the bare assembly.</summary>
public enum FinishKind
{
    None,
    Paint,
    Ceramic,
    Tile,
    ExposedConcrete,
    /// <summary>Asphalt surfacing for roads and driveways.</summary>
    Asphalt,
    /// <summary>Planted lawn over soil, for landscaped site slabs.</summary>
    Grass
}

/// <summary>Computes the finished surface area that takeoffs and estimates use for each finish.</summary>
public static class FinishRules
{
    /// <summary>
    /// Net area of one wall face in square meters: the face rectangle minus the door and
    /// window openings that cut it. Openings are already validated to lie inside the wall.
    /// </summary>
    public static double WallFaceArea(Wall wall, IEnumerable<WallOpening> openings)
    {
        var voids = openings.Where(opening => opening.WallId == wall.Id)
            .Sum(opening => opening.Width * opening.Height);
        return Math.Max(0, wall.ElevationArea - voids);
    }

    /// <summary>Finished area of a wall: both faces, so the finish applies on each side.</summary>
    public static double WallFinishArea(Wall wall, IEnumerable<WallOpening> openings) =>
        2 * WallFaceArea(wall, openings);

    /// <summary>
    /// Finished top-surface area: floor plan area for horizontal slabs, actual pitched
    /// and overhanging roof coverage for roofs. Does not include soffits or fascia.
    /// </summary>
    public static double FloorFinishArea(FloorSlab floor) => RoofGeometry.SurfaceArea(floor);
}
