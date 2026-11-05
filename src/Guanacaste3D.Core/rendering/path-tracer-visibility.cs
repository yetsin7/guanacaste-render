using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

public sealed partial class PathTracer
{
    /// <summary>Transmits direct light through glass or bypasses occluders for an unshadowed material study.</summary>
    private Vector3 Visibility(Vector3 origin, Vector3 direction, float maxDistance)
    {
        if (!_settings.CastShadows) return Vector3.One;
        _threadRays++;
        if (_opaque.Occluded(origin, direction, _rayEpsilon * 0.1f, maxDistance)) return Vector3.Zero;
        if (_dynamic is not null && _dynamic.Occluded(origin, direction, _rayEpsilon * 0.1f, maxDistance)) return Vector3.Zero;
        if (_transmissive is null) return Vector3.One;
        var transmittance = Vector3.One;
        var remaining = maxDistance;
        for (var crossing = 0; crossing < MaxGlassCrossings; crossing++)
        {
            var hit = new RayHit { Distance = remaining };
            if (!_transmissive.Intersect(origin, direction, _rayEpsilon * 0.1f, ref hit)) break;
            ref readonly var surface = ref _surfaces[_scene.TriangleMaterial[hit.Triangle]];
            var cosine = MathF.Abs(Vector3.Dot(direction, _transmissive.Normal(in hit)));
            transmittance *= surface.Tint * (surface.Transmission * (1 - Microfacet.Fresnel(surface.DielectricF0, cosine)));
            if (transmittance.X + transmittance.Y + transmittance.Z < 1e-3f) return Vector3.Zero;
            origin += direction * (hit.Distance + _rayEpsilon);
            if (remaining != float.MaxValue) remaining -= hit.Distance + _rayEpsilon;
        }
        return transmittance;
    }

}
