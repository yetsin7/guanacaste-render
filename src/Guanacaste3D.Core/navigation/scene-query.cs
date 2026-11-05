using System.Numerics;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Core.Navigation;

/// <summary>
/// Ray queries against a scene for navigation: how far away is the surface under the cursor (cursor-centered zoom and
/// orbit pivots), and a collision helper that keeps the camera out of walls. Building it costs about as much as the
/// renderer's own hierarchy, so it is built once per scene change on a worker thread.
/// </summary>
public sealed class SceneQuery
{
    private readonly Bvh _bvh;

    private SceneQuery(Bvh bvh, (Vector3 Min, Vector3 Max) bounds, int triangles)
    {
        _bvh = bvh;
        Bounds = bounds;
        TriangleCount = triangles;
    }

    public (Vector3 Min, Vector3 Max) Bounds { get; }
    public int TriangleCount { get; }

    public static SceneQuery Build(RenderScene scene, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var query = new SceneQuery(new Bvh(scene.Positions, scene.Indices, null, cancellation), scene.Bounds(), scene.TriangleCount);
        MemoryTrim.AfterHeavyBuild();
        return query;
    }

    /// <summary>Distance to the nearest surface along a ray, or null when nothing is hit within <paramref name="maximum"/>.</summary>
    public float? Pick(Vector3 origin, Vector3 direction, float maximum = 100_000)
    {
        var hit = new RayHit { Distance = maximum };
        return _bvh.Intersect(origin, direction, 1e-4f, ref hit) ? hit.Distance : null;
    }

    public CameraCollision Collision(float radius = 0.3f) => new(_bvh, radius);
}
