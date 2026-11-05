using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// Turns a low-resolution lighting image into a full-resolution one. It is an interface so a different technique
/// (a vendor temporal upscaler, a learned model) can replace the built-in one without touching the renderer.
/// </summary>
/// <remarks>
/// Implementations must only redistribute the light they are given using the full-resolution geometry buffers;
/// they must never add detail that the model does not contain.
/// </remarks>
public interface IUpscaler
{
    string Name { get; }

    /// <summary>Returns the lighting at the resolution of <paramref name="highFeatures"/>.</summary>
    /// <param name="lowRadiance">Outgoing radiance per low-resolution pixel (includes surface color).</param>
    /// <param name="lowFeatures">Geometry buffers of the low-resolution view.</param>
    /// <param name="highFeatures">Geometry buffers of the output view; the same camera at full resolution.</param>
    Vector3[] Upscale(Vector3[] lowRadiance, FeatureBuffer lowFeatures, FeatureBuffer highFeatures);
}

/// <summary>
/// Joint bilateral upsampling: each output pixel blends its nearest low-resolution neighbors with bilinear weights
/// multiplied by how well their normal and depth agree with the output pixel's own full-resolution geometry. Radiance
/// is divided by surface color before blending and multiplied by the output pixel's color afterwards, so the filter
/// moves illumination, not texture.
/// </summary>
public sealed class GuidedUpsampler : IUpscaler
{
    public string Name => "Guided bilateral (CPU)";

    public Vector3[] Upscale(Vector3[] lowRadiance, FeatureBuffer lowFeatures, FeatureBuffer highFeatures)
    {
        ArgumentNullException.ThrowIfNull(lowRadiance);
        ArgumentNullException.ThrowIfNull(lowFeatures);
        ArgumentNullException.ThrowIfNull(highFeatures);
        var lowWidth = lowFeatures.Width;
        var lowHeight = lowFeatures.Height;
        var width = highFeatures.Width;
        var height = highFeatures.Height;
        if (lowRadiance.Length != lowWidth * lowHeight) throw new ArgumentException("Low-resolution buffer size is wrong.");

        // Irradiance-like values: radiance divided by the surface color of the low-resolution hit.
        var irradiance = new Vector3[lowRadiance.Length];
        for (var i = 0; i < irradiance.Length; i++)
            irradiance[i] = float.IsInfinity(lowFeatures.Depth[i]) ? Vector3.Zero : lowRadiance[i] / Vector3.Max(lowFeatures.Albedo[i], new Vector3(0.02f));

        var output = new Vector3[width * height];
        var scaleX = (float)lowWidth / width;
        var scaleY = (float)lowHeight / height;
        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var p = y * width + x;
                var depthP = highFeatures.Depth[p];
                if (float.IsInfinity(depthP)) continue;       // sky receives no indirect light
                var u = (x + 0.5f) * scaleX - 0.5f;
                var v = (y + 0.5f) * scaleY - 0.5f;
                var x0 = (int)MathF.Floor(u);
                var y0 = (int)MathF.Floor(v);
                var fx = u - x0;
                var fy = v - y0;
                var normalP = highFeatures.Normal[p];
                var sum = Vector3.Zero;
                var weightSum = 0f;
                var radius = 1;
                for (var pass = 0; pass < 2 && weightSum < 1e-4f; pass++, radius = 2)
                {
                    sum = Vector3.Zero;
                    weightSum = 0f;
                    for (var dy = 1 - radius; dy <= radius; dy++)
                    for (var dx = 1 - radius; dx <= radius; dx++)
                    {
                        var qx = x0 + dx;
                        var qy = y0 + dy;
                        if ((uint)qx >= (uint)lowWidth || (uint)qy >= (uint)lowHeight) continue;
                        var q = qy * lowWidth + qx;
                        var depthQ = lowFeatures.Depth[q];
                        if (float.IsInfinity(depthQ)) continue;
                        // Bilinear weight, widened on the second pass so a pixel with no agreeing neighbor still gets light.
                        var wx = radius == 1 ? (dx == 0 ? 1 - fx : fx) : MathF.Max(0.05f, 1 - MathF.Abs(qx - u) / 2);
                        var wy = radius == 1 ? (dy == 0 ? 1 - fy : fy) : MathF.Max(0.05f, 1 - MathF.Abs(qy - v) / 2);
                        var normalWeight = MathF.Pow(MathF.Max(0, Vector3.Dot(normalP, lowFeatures.Normal[q])), 32);
                        var depthWeight = MathF.Exp(-MathF.Abs(depthP - depthQ) / (0.05f * depthP + 1e-3f));
                        var weight = wx * wy * normalWeight * depthWeight;
                        sum += irradiance[q] * weight;
                        weightSum += weight;
                    }
                }
                if (weightSum > 1e-6f) output[p] = sum / weightSum * highFeatures.Albedo[p];
            }
        });
        return output;
    }
}
