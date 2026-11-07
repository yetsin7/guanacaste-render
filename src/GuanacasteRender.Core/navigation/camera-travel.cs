using System.Numerics;

namespace Guanacaste3D.Core.Navigation;

/// <summary>Metric navigation independent of project extents, suitable for interiors and large sites.</summary>
public static class CameraTravel
{
    /// <summary>Converts local input into bounded travel; horizontal mode preserves altitude unless explicitly raised.</summary>
    public static Vector3 Displacement(NavigationCamera camera, Vector3 input, float speed, float seconds,
        bool horizontal, bool fast = false, bool precise = false)
    {
        if (!float.IsFinite(speed) || !float.IsFinite(seconds) || !float.IsFinite(input.LengthSquared()))
            return Vector3.Zero;
        var forward = horizontal ? Vector3.Cross(Vector3.UnitZ, camera.Right) : camera.Forward;
        var direction = forward * input.X + camera.Right * input.Y + Vector3.UnitZ * input.Z;
        if (direction.LengthSquared() < 1e-12f) return Vector3.Zero;
        var multiplier = precise ? 0.2f : fast ? 4f : 1f;
        return Vector3.Normalize(direction) * Math.Clamp(speed, 0.1f, 50f) * multiplier * Math.Clamp(seconds, 0, 0.1f);
    }

    /// <summary>Same travel with an explicit speed multiplier (Lumion-style slow, fast and very fast modes).</summary>
    public static Vector3 Displacement(NavigationCamera camera, Vector3 input, float speed, float seconds,
        bool horizontal, float multiplier)
    {
        if (!float.IsFinite(multiplier)) return Vector3.Zero;
        return Displacement(camera, input, speed * Math.Clamp(multiplier, 0.05f, 20f), seconds, horizontal);
    }
}
