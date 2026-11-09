namespace Guanacaste3D.Core.Geometry;

/// <summary>
/// The ground of a project as one surface: the toposolids' terrain, read at any plan point in absolute height. Tools that put
/// things on the site (a crowd, a grove) use it to stand each one on the slope instead of at the level's floor. Building it
/// fits one spline per toposolid, so make it once for a batch.
/// </summary>
public sealed class SiteTerrain
{
    private readonly List<(FloorSlab Slab, TopoSurface Surface, double Datum)> _terrains = [];
    private readonly BuildingDocument _document;

    public SiteTerrain(BuildingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
        foreach (var floor in document.Floors.Where(floor => floor.IsToposolid && floor.Boundary.Count >= 3))
            if (document.Levels.FirstOrDefault(level => level.Id == floor.LevelId) is { } level)
                _terrains.Add((floor, new TopoSurface(floor), level.Elevation));
    }

    /// <summary>Whether the project has any terrain.</summary>
    public bool Any => _terrains.Count > 0;

    /// <summary>The height of the terrain at a plan point, in project metres, or null where no toposolid covers it (the highest wins where they overlap).</summary>
    public double? HeightAt(double x, double y)
    {
        double? best = null;
        var point = new Point2(x, y);
        foreach (var (slab, surface, datum) in _terrains)
        {
            if (!TopographyGeometry.Contains(slab.Boundary, point)) continue;
            var height = datum + surface.ElevationAt(x, y);
            if (best is null || height > best) best = height;
        }
        return best;
    }

    /// <summary>
    /// The level to put a site element on at a plan point: the level of the toposolid under it (a plant or rock stands on that
    /// terrain, whatever its height), otherwise the highest level whose floor is at or below <paramref name="groundHeight"/>, otherwise
    /// the lowest level. Null when the project has no levels.
    /// </summary>
    public Level? LevelFor(double x, double y, double groundHeight)
    {
        var point = new Point2(x, y);
        foreach (var (slab, _, _) in _terrains)
            if (TopographyGeometry.Contains(slab.Boundary, point) && _document.Levels.FirstOrDefault(level => level.Id == slab.LevelId) is { } owner)
                return owner;
        var levels = _document.Levels;
        return levels.Where(level => level.Elevation <= groundHeight + 0.05).OrderByDescending(level => level.Elevation).FirstOrDefault() ??
               levels.OrderBy(level => level.Elevation).FirstOrDefault();
    }
}
