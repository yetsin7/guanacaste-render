using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

public sealed partial class PathTracer
{
    // Which materials are lawn: only they grow blades. Null while the grass is off, so the picture is then exactly the old one.
    private bool[]? _grassy;

    /// <summary>Marks the lawn materials of the scene for the current <see cref="RenderSettings.GrassHeight"/> (no blades without it).</summary>
    private void RebuildGrass()
    {
        if (!(_settings.GrassHeight > 0.002f))
        {
            _grassy = null;
            return;
        }
        _grassy = _scene.Materials.Select(material => material.Pattern == SurfacePattern.Grass && material.Transmission <= 0.001f && !material.IsEmissive && !material.UvMapped).ToArray();
    }

    /// <summary>
    /// Grows the blades on a lawn point seen sharply: they replace the flat colour and the bump of the pattern. Far away, where a blade
    /// is smaller than a pixel, the blades give way to the average of the lawn, so the field does not turn to noise.
    /// </summary>
    private void ApplyGrass(float patternScale, Vector3 position, Vector3 geometric, Vector3 direction, float distance, ref Vector3 diffuse, ref Vector3 normal)
    {
        var pitch = 0.022f * MathF.Max(0.2f, patternScale);
        var footprint = distance * PixelAngle;
        var resolved = 1 - SmoothUnit(pitch * 0.35f, pitch * 0.9f, footprint);
        var height = _settings.GrassHeight;
        var flat = diffuse * SurfacePatterns.GrassMeanTone;
        if (resolved <= 0.001f)
        {
            diffuse = flat;
            return;
        }
        var sample = SurfacePatterns.GrassBlades(position, geometric, direction, height, pitch, _settings.WindStrength,
            _settings.WindSeconds, _settings.CloudDirectionDegrees * MathF.PI / 180);
        diffuse = Vector3.Lerp(flat, diffuse * sample.Tone, resolved);
        if (sample.Blade)
        {
            var bent = Vector3.Normalize(geometric + sample.Bend * resolved);
            if (Vector3.Dot(bent, direction) < -0.05f) normal = bent;
        }
    }

    private static float SmoothUnit(float low, float high, float value)
    {
        var t = Math.Clamp((value - low) / (high - low), 0f, 1f);
        return t * t * (3 - 2 * t);
    }
}
