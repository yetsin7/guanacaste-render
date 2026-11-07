using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

internal static partial class SurfacePatterns
{
    /// <summary>Combines broad curing variation with smaller mineral flecks on cast concrete.</summary>
    private static Vector3 PouredConcreteColor(float u, float v)
    {
        var curing = ValueNoise(u * 1.4f + 13, v * 1.4f - 9);
        var mottling = ValueNoise(u * 9f, v * 9f);
        var aggregate = ValueNoise(u * 52f + 4, v * 52f + 8);
        var pores = SmoothStep(0.69f, 0.83f, ValueNoise(u * 65f - 7, v * 65f + 3));
        var brightness = 0.84f + 0.16f * curing + 0.13f * mottling + 0.06f * aggregate - 0.16f * pores;
        return new Vector3(brightness * 1.01f, brightness, brightness * 0.98f);
    }

    /// <summary>Recesses pores and adds shallow mineral relief, fading detail at a distance.</summary>
    private static float PouredConcreteHeight(float u, float v, float fade)
    {
        var aggregate = ValueNoise(u * 52f + 4, v * 52f + 8);
        var pores = SmoothStep(0.69f, 0.83f, ValueNoise(u * 65f - 7, v * 65f + 3));
        return (aggregate * 0.0005f - pores * 0.0012f) * fade;
    }
}
