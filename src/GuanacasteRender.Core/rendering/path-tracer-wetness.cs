using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

public sealed partial class PathTracer
{
    // Wet ground: a damp variant and a puddle variant of every material, made once when the wetness is set, so shading picks one
    // by an index and never recomputes a material at a point.
    private SurfaceData[]? _damp;
    private SurfaceData[]? _puddle;
    private bool[]? _wettable;

    /// <summary>
    /// Builds the damp and puddle versions of the scene's materials for the current <see cref="RenderSettings.Wetness"/>: damp is
    /// darker and glossier (wet stone, asphalt and grass reflect a little and look deeper), a puddle is a dark mirror under a thin
    /// film of water. Glass, metal, lights, water itself and the cards of leaves are left as they are.
    /// </summary>
    private void RebuildWetness()
    {
        var wet = Math.Clamp(_settings.Wetness, 0f, 1f);
        if (!(wet > 0.001f))
        {
            _damp = _puddle = null;
            _wettable = null;
            return;
        }
        var damp = Math.Clamp(wet * 1.2f, 0f, 1f);
        var materials = _scene.Materials;
        _wettable = materials.Select(Wettable).ToArray();
        _damp = materials.Select((material, index) => !_wettable[index] ? _surfaces[index] : SurfaceData.From(material with
        {
            BaseColor = material.BaseColor * (1 - 0.38f * damp),
            Roughness = material.Roughness + (MathF.Min(material.Roughness, 0.22f) - material.Roughness) * damp * 0.7f
        })).ToArray();
        _puddle = materials.Select((material, index) => !_wettable[index] ? _surfaces[index] : SurfaceData.From(material with
        {
            BaseColor = material.BaseColor * 0.42f, Roughness = 0.03f, Ior = 1.33f
        })).ToArray();
    }

    private static bool Wettable(RenderMaterial material) =>
        material.Transmission <= 0.001f && material.Metallic < 0.5f && !material.IsEmissive && !material.UvMapped &&
        material.Pattern is not (SurfacePattern.Ripples or SurfacePattern.Waves);

    /// <summary>Whether nothing above the point shuts out the sky: rain only falls where there is no roof, branch or balcony over it.</summary>
    private bool OpenToSky(Vector3 position, Vector3 normal)
    {
        var origin = position + normal * (_rayEpsilon * 2);
        if (_opaque.Occluded(origin, Vector3.UnitZ, _rayEpsilon * 0.1f, float.MaxValue)) return false;
        return _dynamic is null || !_dynamic.Occluded(origin, Vector3.UnitZ, _rayEpsilon * 0.1f, float.MaxValue);
    }

    /// <summary>
    /// Wets a point that faces up and sees the sky: darker, and glossier, with puddles where the ground collects water and rings
    /// on them where rain lands. Returns the new surface, or null when the point stays dry (it faces away, is covered, or its
    /// material cannot get wet). Only a sharply seen point (<paramref name="detailed"/>) gets puddles and rings; the rest of the
    /// light sees the damp surface on average.
    /// </summary>
    private SurfaceData? Wet(int material, Vector3 position, Vector3 geometric, bool detailed, float distance, ref Vector3 diffuse, ref Vector3 normal, Vector3 direction)
    {
        if (_damp is null || _puddle is null || _wettable is null || !_wettable[material] || geometric.Z < 0.5f || !OpenToSky(position, geometric))
            return null;
        var wet = Math.Clamp(_settings.Wetness, 0f, 1f);
        var damp = Math.Clamp(wet * 1.2f, 0f, 1f);
        var puddle = detailed ? SurfacePatterns.Puddles(position, wet) : 0f;
        diffuse *= MathF.Max(0.25f, 1 - 0.38f * damp - 0.3f * puddle);
        if (puddle <= 0.5f) return _damp[material];
        var (du, dv) = SurfacePatterns.RainRings(position, distance);
        var bent = SurfacePatterns.Bend(normal, du, dv);
        if (Vector3.Dot(bent, direction) < -0.05f) normal = bent;
        return _puddle[material];
    }
}
