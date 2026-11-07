using System.Numerics;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Core.Navigation;

/// <summary>
/// A free camera in Z-up model space, controlled the way walk-through tools are: look around from where you stand,
/// fly with the keyboard, pan with the middle button, and dolly toward the point under the cursor. The same camera
/// drives the 3D view, the live render preview and the final render, so all three always show the same view.
/// </summary>
public sealed class NavigationCamera
{
    private const float MaximumPitch = 89f;

    public Vector3 Position { get; set; } = new(10, -10, 6);

    /// <summary>Heading in degrees, 0 looking along +X and increasing counter-clockwise (toward +Y).</summary>
    public float YawDegrees { get; set; } = 135;

    /// <summary>Elevation in degrees, positive looking up.</summary>
    public float PitchDegrees { get; set; } = -25;

    public float FieldOfViewDegrees { get; set; } = 45;

    public Vector3 Forward
    {
        get
        {
            var yaw = YawDegrees * MathF.PI / 180;
            var pitch = PitchDegrees * MathF.PI / 180;
            return new Vector3(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch));
        }
    }

    /// <summary>Horizontal right-hand direction; it never tilts, so the horizon stays level.</summary>
    public Vector3 Right
    {
        get
        {
            var yaw = YawDegrees * MathF.PI / 180;
            return new Vector3(MathF.Sin(yaw), -MathF.Cos(yaw), 0);
        }
    }

    public Vector3 Up => Vector3.Cross(Right, Forward);

    /// <summary>Captures an independent camera snapshot for navigation history and render requests.</summary>
    public NavigationCamera Clone() => new()
    {
        Position = Position, YawDegrees = YawDegrees, PitchDegrees = PitchDegrees, FieldOfViewDegrees = FieldOfViewDegrees
    };

    /// <summary>Turns the view in place. Positive <paramref name="deltaX"/> turns right, positive <paramref name="deltaY"/> looks down.</summary>
    public void Look(float deltaX, float deltaY, float degreesPerPixel = 0.25f)
    {
        YawDegrees = Wrap(YawDegrees - deltaX * degreesPerPixel);
        PitchDegrees = Math.Clamp(PitchDegrees - deltaY * degreesPerPixel, -MaximumPitch, MaximumPitch);
    }

    /// <summary>Slides the camera sideways and up/down in the image plane (the "hand" tool).</summary>
    public void Pan(float deltaX, float deltaY, float unitsPerPixel) => Position += PanDelta(deltaX, deltaY, unitsPerPixel);

    /// <summary>The displacement a pan of the given mouse movement produces, so callers can apply collisions to it.</summary>
    public Vector3 PanDelta(float deltaX, float deltaY, float unitsPerPixel) =>
        (-Right * deltaX + Up * deltaY) * unitsPerPixel;

    /// <summary>
    /// Converts a keyboard move into a displacement: X forward along the view (so W flies where you look), Y to the
    /// right, Z straight up, normalized so diagonals are not faster, then scaled by <paramref name="distance"/>.
    /// </summary>
    public Vector3 FlyDelta(Vector3 local, float distance)
    {
        var move = Forward * local.X + Right * local.Y + Vector3.UnitZ * local.Z;
        return move.LengthSquared() < 1e-12f ? Vector3.Zero : Vector3.Normalize(move) * distance;
    }

    /// <summary>Moves along a ray, for wheel zoom toward the cursor.</summary>
    public void DollyToward(Vector3 rayDirection, float distance) => Position += Vector3.Normalize(rayDirection) * distance;

    /// <summary>
    /// Rotates the camera and its position rigidly about a pivot, preserving its distance and screen position.
    /// An off-center selection stays off-center, so switching from pan to orbit never reframes the view.
    /// </summary>
    public void OrbitAround(Vector3 pivot, float deltaX, float deltaY, float degreesPerPixel = 0.4f)
    {
        if (deltaX == 0 && deltaY == 0) return;
        var relative = Position - pivot;
        var horizontal = Vector3.Dot(relative, Right);
        var vertical = Vector3.Dot(relative, Up);
        var depth = Vector3.Dot(relative, Forward);
        YawDegrees = Wrap(YawDegrees - deltaX * degreesPerPixel);
        PitchDegrees = Math.Clamp(PitchDegrees - deltaY * degreesPerPixel, -MaximumPitch, MaximumPitch);
        Position = pivot + Right * horizontal + Up * vertical + Forward * depth;
    }

    /// <summary>Aims the camera at a point without moving it.</summary>
    public void LookAt(Vector3 target)
    {
        var direction = target - Position;
        if (direction.LengthSquared() < 1e-10f) return;
        direction = Vector3.Normalize(direction);
        PitchDegrees = Math.Clamp(MathF.Asin(Math.Clamp(direction.Z, -1f, 1f)) * 180 / MathF.PI, -90, 90);
        if (direction.X * direction.X + direction.Y * direction.Y > 1e-10f)
            YawDegrees = Wrap(MathF.Atan2(direction.Y, direction.X) * 180 / MathF.PI);
    }

    /// <summary>Direction of the ray through a pixel, for picking and cursor-centered zoom.</summary>
    public Vector3 PixelRay(float x, float y, float width, float height)
    {
        var halfHeight = MathF.Tan(Math.Clamp(FieldOfViewDegrees, 5f, 150f) * MathF.PI / 360);
        var halfWidth = halfHeight * width / MathF.Max(1, height);
        var u = (2 * x / MathF.Max(1, width) - 1) * halfWidth;
        var v = (1 - 2 * y / MathF.Max(1, height)) * halfHeight;
        return Vector3.Normalize(Forward + Right * u + Up * v);
    }

    /// <summary>The pixel a model point appears at (the inverse of <see cref="PixelRay"/>), or null when it is behind the camera.</summary>
    public (float X, float Y)? Project(Vector3 point, float width, float height)
    {
        var relative = point - Position;
        var depth = Vector3.Dot(relative, Forward);
        if (depth < 0.05f) return null;
        var halfHeight = MathF.Tan(Math.Clamp(FieldOfViewDegrees, 5f, 150f) * MathF.PI / 360);
        var halfWidth = halfHeight * width / MathF.Max(1, height);
        var u = Vector3.Dot(relative, Right) / depth;
        var v = Vector3.Dot(relative, Up) / depth;
        return ((u / halfWidth + 1) * 0.5f * width, (1 - v / halfHeight) * 0.5f * height);
    }

    /// <summary>Frames a bounding box from a three-quarter view, like the original orbit camera.</summary>
    public static NavigationCamera Frame(Vector3 min, Vector3 max, float azimuthDegrees = -50, float elevationDegrees = 28,
        float distanceScale = 1) =>
        FromRenderCamera(RenderCamera.FrameBounds(min, max, azimuthDegrees, elevationDegrees, distanceScale));

    /// <summary>Fits every corner of a bounding box while preserving the camera orientation and field of view.</summary>
    public void FrameBounds(Vector3 min, Vector3 max, float aspectRatio, float margin = 1.1f)
    {
        var center = (min + max) / 2;
        var halfVertical = MathF.Tan(Math.Clamp(FieldOfViewDegrees, 5, 150) * MathF.PI / 360);
        var halfHorizontal = halfVertical * Math.Clamp(aspectRatio, 0.1f, 10f);
        var distance = 0.1f;
        foreach (var x in new[] { min.X, max.X })
        foreach (var y in new[] { min.Y, max.Y })
        foreach (var z in new[] { min.Z, max.Z })
        {
            var offset = new Vector3(x, y, z) - center;
            var depth = Vector3.Dot(offset, Forward);
            distance = MathF.Max(distance, MathF.Max(MathF.Abs(Vector3.Dot(offset, Right)) / halfHorizontal,
                MathF.Abs(Vector3.Dot(offset, Up)) / halfVertical) * MathF.Max(1, margin) - depth);
        }
        Position = center - Forward * (distance + 0.05f);
    }

    /// <summary>Restores a navigation pose and lens from a renderer camera expressed in the same Z-up space.</summary>
    public static NavigationCamera FromRenderCamera(RenderCamera camera)
    {
        var result = new NavigationCamera { Position = camera.Position, FieldOfViewDegrees = camera.VerticalFieldOfViewDegrees };
        result.LookAt(camera.Target);
        return result;
    }

    /// <summary>Converts to a render camera that looks at the point <paramref name="focusDistance"/> ahead.</summary>
    public RenderCamera ToRenderCamera(float focusDistance = 10) =>
        new(Position, Position + Forward * MathF.Max(0.1f, focusDistance), FieldOfViewDegrees);

    /// <summary>Normalizes heading angles to one positive revolution after incremental rotations.</summary>
    private static float Wrap(float degrees)
    {
        degrees %= 360;
        return degrees < 0 ? degrees + 360 : degrees;
    }
}
