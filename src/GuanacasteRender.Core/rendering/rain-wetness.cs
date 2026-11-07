namespace Guanacaste3D.Core.Rendering;

/// <summary>Weather that follows from other weather: rain that falls leaves the ground wet, with no second slider to touch.</summary>
public static class RainWetness
{
    /// <summary>How wet the ground is at full rain (a little short of soaked, so the "Suelo mojado" slider can still go further).</summary>
    public const float AtFullRain = 0.9f;

    /// <summary>The wetness to render: what the "Suelo mojado" slider asks for, or what the rain alone leaves, whichever is more.</summary>
    public static float Effective(float wetness, float rain)
    {
        var asked = float.IsFinite(wetness) ? Math.Clamp(wetness, 0f, 1f) : 0f;
        var fallen = float.IsFinite(rain) ? Math.Clamp(rain, 0f, 1f) * AtFullRain : 0f;
        return MathF.Max(asked, fallen);
    }
}
