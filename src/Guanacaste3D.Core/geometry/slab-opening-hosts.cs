namespace Guanacaste3D.Core.Geometry;

/// <summary>Resolves the real slab hosts of floor openings and vertical shafts.</summary>
public static class SlabOpeningHosts
{
    /// <summary>Returns slabs fully containing a void at its applicable levels; unrelated structural kinds have no hosts.</summary>
    public static IReadOnlyList<FloorSlab> Resolve(BuildingDocument document, StructuralElement opening)
    {
        if (opening.Kind is not (StructuralElementKind.Opening or StructuralElementKind.Shaft)) return [];
        var elevation = document.Levels.Single(level => level.Id == opening.LevelId).Elevation;
        var extent = PlanRectangle.Of(opening.Footprint);
        return document.Floors.Where(floor =>
            (opening.Kind == StructuralElementKind.Shaft
                ? document.Levels.Single(level => level.Id == floor.LevelId).Elevation >= elevation
                : floor.Kind == FloorKind.Floor && floor.LevelId == opening.LevelId) &&
            SlabOpenings.ContainsRectangle(floor.Boundary, extent)).ToArray();
    }
}
