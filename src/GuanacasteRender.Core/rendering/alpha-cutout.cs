using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// Which triangles of a scene are cut-out cards and where: each such triangle points to its texture's mask and cutoff,
/// and a hit is a hole when the mask, read at the hit's texture coordinates, falls below the cutoff. Tested inside
/// the BVH, so a leaf card's transparent margin neither blocks the view nor casts a square shadow.
/// </summary>
public sealed class AlphaCutout
{
    private readonly short[] _maskOfTriangle;
    private readonly RenderTexture[] _masks;
    private readonly float[] _cutoffs;
    private readonly Vector2[] _uvs;

    private AlphaCutout(short[] maskOfTriangle, RenderTexture[] masks, float[] cutoffs, Vector2[] uvs)
    {
        _maskOfTriangle = maskOfTriangle;
        _masks = masks;
        _cutoffs = cutoffs;
        _uvs = uvs;
    }

    /// <summary>The cut-outs of a scene, or null when no material has one.</summary>
    public static AlphaCutout? For(RenderScene scene)
    {
        if (scene.TriangleUvs is not { } uvs) return null;
        var maskOfMaterial = new short[scene.Materials.Length];
        var masks = new List<RenderTexture>();
        var cutoffs = new List<float>();
        for (var material = 0; material < scene.Materials.Length; material++)
        {
            var m = scene.Materials[material];
            maskOfMaterial[material] = -1;
            if (!m.UvMapped || m.AlphaCutoff <= 0 || m.Texture is not { HasAlpha: true } texture || masks.Count >= short.MaxValue) continue;
            maskOfMaterial[material] = (short)masks.Count;
            masks.Add(texture);
            cutoffs.Add(m.AlphaCutoff);
        }
        if (masks.Count == 0) return null;
        var maskOfTriangle = new short[scene.TriangleCount];
        for (var triangle = 0; triangle < scene.TriangleCount; triangle++)
            maskOfTriangle[triangle] = maskOfMaterial[scene.TriangleMaterial[triangle]];
        return new AlphaCutout(maskOfTriangle, [.. masks], [.. cutoffs], uvs);
    }

    /// <summary>Whether the hit at barycentric (u, v) of a triangle falls in a transparent part of its texture.</summary>
    public bool IsHole(int triangle, float u, float v)
    {
        var mask = _maskOfTriangle[triangle];
        if (mask < 0) return false;
        var uv = _uvs[triangle * 3] * (1 - u - v) + _uvs[triangle * 3 + 1] * u + _uvs[triangle * 3 + 2] * v;
        // Texture coordinates run down the image; the texture reads v up, as on a wall.
        return _masks[mask].Alpha(uv.X, 1 - uv.Y) < _cutoffs[mask];
    }
}
