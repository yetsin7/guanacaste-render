namespace Guanacaste3D.Core;

/// <summary>
/// Compatibility entry point for architectural room detection. The planar-graph implementation
/// now handles orthogonal, diagonal, and nested wall or separator loops using the same source of truth.
/// </summary>
public static class RoomPolygonDetection
{
    public static IReadOnlyList<RoomRegion> Detect(BuildingDocument document, Guid levelId) =>
        RoomPlanarBoundaryDetection.Detect(document, levelId);
}
