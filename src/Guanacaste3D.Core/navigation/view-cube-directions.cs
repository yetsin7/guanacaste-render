using System.Numerics;

namespace Guanacaste3D.Core.Navigation;

/// <summary>Orientation targets for the center, edges, and corners of a projected cube face.</summary>
public static class ViewCubeDirections
{
    /// <summary>Maps normalized face coordinates to an axis, two-axis edge, or three-axis corner direction.</summary>
    public static Vector3 FromFace(Vector3 normal, Vector3 across, Vector3 up, Vector2 position) =>
        normal + across * Region(position.X) - up * Region(position.Y);

    /// <summary>Reserves the outer quarter of each face for its adjacent edges and corners.</summary>
    private static float Region(float coordinate) => coordinate < 0.25f ? -1 : coordinate > 0.75f ? 1 : 0;
}
