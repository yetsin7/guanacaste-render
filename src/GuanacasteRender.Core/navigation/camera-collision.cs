using System.Numerics;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Core.Navigation;

/// <summary>
/// Keeps a spherical camera from entering geometry while walking or flying through a model. A move is cast as a ray
/// against the scene hierarchy; if it would come within the camera radius of a surface, the camera stops there and
/// slides along the surface with whatever movement is left, which feels like walking along a wall instead of stopping dead.
/// </summary>
public sealed class CameraCollision(Bvh scene, float radius = 0.3f)
{
    private const int SlideIterations = 3;

    public float Radius { get; } = radius;

    /// <summary>Returns the position reached by moving from <paramref name="from"/> by <paramref name="delta"/>.</summary>
    public Vector3 Move(Vector3 from, Vector3 delta)
    {
        var position = from;
        var remaining = delta;
        for (var iteration = 0; iteration < SlideIterations && remaining.LengthSquared() > 1e-10f; iteration++)
        {
            var length = remaining.Length();
            var direction = remaining / length;
            var hit = new RayHit { Distance = length + Radius };
            if (!scene.Intersect(position, direction, 1e-4f, ref hit))
            {
                position += remaining;
                break;
            }
            var allowed = MathF.Max(0, hit.Distance - Radius);
            position += direction * allowed;
            var normal = scene.Normal(in hit);
            if (Vector3.Dot(normal, direction) > 0) normal = -normal;
            // Whatever distance is left slides along the surface instead of pushing into it.
            var left = remaining - direction * allowed;
            remaining = left - normal * Vector3.Dot(left, normal);
        }
        return position;
    }

    /// <summary>True when a surface lies within half the radius of the position along any of the six axis directions.</summary>
    public bool IsBlocked(Vector3 position)
    {
        foreach (var axis in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ })
        {
            var hit = new RayHit { Distance = Radius * 0.5f };
            if (scene.Intersect(position, axis, 1e-4f, ref hit)) return true;
        }
        return false;
    }
}
