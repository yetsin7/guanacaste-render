namespace Guanacaste3D.Core.Navigation;

/// <summary>Converts wheel deltas into bounded multiplicative movement toward a picked surface.</summary>
public static class NavigationZoom
{
    private const float MinimumClearance = 0.05f;
    private const float WheelDeltaPerNotch = 120;

    /// <summary>
    /// Moves one fifth of the remaining focus distance per standard inward notch.
    /// Fractional notches compose to the same magnification, and reversing the wheel restores the original
    /// distance unless scene-scale or surface-clearance bounds intervene. Invalid inputs produce no movement.
    /// </summary>
    public static float DollyStep(float focusDistance, int wheelDelta, float sceneRadius, bool accelerated = false)
    {
        if (!float.IsFinite(focusDistance) || !float.IsFinite(sceneRadius) ||
            focusDistance <= 0 || sceneRadius <= 0 || wheelDelta == 0) return 0;
        var notches = Math.Clamp(wheelDelta / WheelDeltaPerNotch, -8, 8) * (accelerated ? 2 : 1);
        var step = focusDistance * (1 - MathF.Pow(0.8f, notches));
        return Math.Clamp(step, -sceneRadius * 0.6f, MathF.Max(0, focusDistance - MinimumClearance));
    }
}
