using System.Numerics;

namespace Guanacaste3D.Core.Navigation;

/// <summary>A fixed world-space construction plane used throughout one pointer-driven model edit.</summary>
public readonly record struct ModelDragPlane(Vector3 Origin, Vector3 Normal)
{
    /// <summary>Intersects a forward ray; parallel, nonfinite, backward, and excessively distant intersections are rejected.</summary>
    public Vector3? Intersect(Vector3 rayOrigin, Vector3 rayDirection)
    {
        var denominator = Vector3.Dot(rayDirection, Normal);
        if (!float.IsFinite(denominator) || Math.Abs(denominator) < 1e-4f) return null;
        var distance = Vector3.Dot(Origin - rayOrigin, Normal) / denominator;
        if (!float.IsFinite(distance) || distance < 0 || distance > 10000) return null;
        var point = rayOrigin + rayDirection * distance;
        return float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z) ? point : null;
    }
}
