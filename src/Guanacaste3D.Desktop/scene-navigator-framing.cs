using System.Numerics;
using System.Windows;

namespace Guanacaste3D.Desktop;

internal sealed partial class SceneNavigator
{
    /// <summary>Fits all visible bounding corners within an off-center crop without changing the perspective lens.</summary>
    public void FrameWithinRegion(Rect region)
    {
        if (IsLocked || region.Width < 6 || region.Height < 6) return;
        _history.Record(Camera);
        var (width, height) = Size();
        var halfVertical = MathF.Tan(Camera.FieldOfViewDegrees * MathF.PI / 360);
        var halfHorizontal = halfVertical * width / height;
        var left = (float)(2 * region.Left / width - 1) * halfHorizontal;
        var right = (float)(2 * region.Right / width - 1) * halfHorizontal;
        var bottom = (float)(1 - 2 * region.Bottom / height) * halfVertical;
        var top = (float)(1 - 2 * region.Top / height) * halfVertical;
        var horizontal = (left + right) / 2;
        var vertical = (bottom + top) / 2;
        var (min, max) = Bounds();
        var center = (min + max) / 2;
        var distance = 0.1f;
        foreach (var x in new[] { min.X, max.X })
        foreach (var y in new[] { min.Y, max.Y })
        foreach (var z in new[] { min.Z, max.Z })
        {
            var offset = new Vector3(x, y, z) - center;
            var depth = Vector3.Dot(offset, Camera.Forward);
            var across = Vector3.Dot(offset, Camera.Right);
            var up = Vector3.Dot(offset, Camera.Up);
            distance = MathF.Max(distance, MathF.Max((across - right * depth) / (right - horizontal),
                (left * depth - across) / (horizontal - left)));
            distance = MathF.Max(distance, MathF.Max((up - top * depth) / (top - vertical),
                (bottom * depth - up) / (vertical - bottom)));
        }
        distance = distance * 1.05f + 0.05f;
        Camera.Position = center - Camera.Forward * distance - Camera.Right * horizontal * distance - Camera.Up * vertical * distance;
        NotifyChanged();
    }
}
