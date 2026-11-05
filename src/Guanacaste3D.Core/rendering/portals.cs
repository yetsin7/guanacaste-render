using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// Windows and other clear openings through which the sky lights a room. Sampling directions toward them
/// (instead of blindly over the hemisphere) is the difference between a clean and a grainy interior: from inside
/// a room almost every cosine-distributed ray hits a wall, while the few that reach the sky carry all the light.
/// </summary>
/// <remarks>
/// Glass triangles that touch and lie in one plane are merged into a single portal (a window), so a window with
/// many panes costs one selection weight, not one per triangle. The open side of each window (the side that sees the
/// sky) is found by probing, so imported glass works as well as BIM windows; glass between two rooms, or on a
/// roofless space, is discarded. Selection weights and <see cref="Pdf"/> share one function, which is what keeps
/// multiple importance sampling unbiased.
/// </remarks>
public sealed class SkyPortals
{
    /// <summary>More windows than this are ignored (largest first): selection costs O(windows) per shading point.</summary>
    public const int MaximumPortals = 128;

    /// <summary>
    /// Most glass triangles considered when merging panes into windows (largest first). Merging compares panes in
    /// pairs, so this bounds the work for models with hundreds of thousands of glass triangles, such as curtain walls.
    /// </summary>
    public const int MaximumCandidates = 3000;

    /// <summary>Windows probed for sky openness before the largest <see cref="MaximumPortals"/> are kept.</summary>
    private const int MaximumProbed = 512;

    private const int ProbeRays = 48;
    private const float MinimumOpenFraction = 0.5f;
    /// <summary>Panes in one plane that are closer than this (a mullion apart) belong to the same window.</summary>
    private const float MergeDistance = 0.3f;
    /// <summary>
    /// Glass is modeled as a thin solid, so its edge faces are slivers a few centimeters wide. A window is at least
    /// this wide in both directions; anything narrower is an edge, not an opening.
    /// </summary>
    private const float MinimumExtent = 0.15f;
    /// <summary>
    /// A pane is a portal only if the space behind it is really closed. Measured one and two meters inside, that space
    /// must see little sky overhead: with a roof, almost all light arrives through the window, and sampling it is
    /// worth the extra shadow ray. In a roofless model or a glazed pergola ordinary BSDF sampling already finds the
    /// sky, so portal rays would only add cost (measured: 2x slower for no gain on the roofless sample house).
    /// </summary>
    private const float MaximumClosedSideOpenFraction = 0.25f;

    // One entry per window.
    private readonly Vector3[] _center;
    private readonly Vector3[] _outward;     // unit normal toward the open (sky-facing) side
    private readonly float[] _area;
    private readonly int[] _first;           // range into the triangle arrays
    private readonly int[] _count;
    // One entry per glass triangle, grouped by window.
    private readonly Vector3[] _a;
    private readonly Vector3[] _b;
    private readonly Vector3[] _c;
    private readonly float[] _cumulativeArea; // running area inside the window, for picking a triangle

    private SkyPortals(List<Window> windows)
    {
        Count = windows.Count;
        _center = new Vector3[Count];
        _outward = new Vector3[Count];
        _area = new float[Count];
        _first = new int[Count];
        _count = new int[Count];
        var triangleCount = windows.Sum(window => window.Triangles.Count);
        _a = new Vector3[triangleCount];
        _b = new Vector3[triangleCount];
        _c = new Vector3[triangleCount];
        _cumulativeArea = new float[triangleCount];
        var slot = 0;
        for (var i = 0; i < Count; i++)
        {
            var window = windows[i];
            _first[i] = slot;
            _count[i] = window.Triangles.Count;
            _outward[i] = window.Outward;
            var weighted = Vector3.Zero;
            var area = 0f;
            foreach (var (a, b, c) in window.Triangles)
            {
                var triangleArea = 0.5f * Vector3.Cross(b - a, c - a).Length();
                (_a[slot], _b[slot], _c[slot]) = (a, b, c);
                weighted += (a + b + c) / 3 * triangleArea;
                area += triangleArea;
                _cumulativeArea[slot++] = area;
            }
            _area[i] = area;
            _center[i] = weighted / area;
        }
    }

    private sealed record Window(List<(Vector3 A, Vector3 B, Vector3 C)> Triangles, Vector3 Outward);

    /// <summary>Number of windows (merged panes) that act as sky portals.</summary>
    public int Count { get; }

    /// <summary>
    /// Finds the usable windows among <paramref name="glassTriangles"/>. Returns null when there are none, so
    /// scenes without windows pay nothing.
    /// </summary>
    /// <param name="opaque">Hierarchy of the opaque geometry; glass must not be in it.</param>
    /// <param name="epsilon">Ray offset in model units.</param>
    public static SkyPortals? TryCreate(RenderScene scene, IReadOnlyList<int> glassTriangles, Bvh opaque, float epsilon)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(glassTriangles);
        ArgumentNullException.ThrowIfNull(opaque);
        var all = new List<(float Area, int Triangle)>(glassTriangles.Count);
        foreach (var triangle in glassTriangles)
        {
            var (a, b, c) = Corners(scene, triangle);
            var area = 0.5f * Vector3.Cross(b - a, c - a).Length();
            if (area > 1e-6f) all.Add((area, triangle));
        }
        if (all.Count == 0) return null;
        // Keep the largest panes only: their count, not the model's glass total, sets the cost below.
        IEnumerable<(float Area, int Triangle)> kept = all.Count <= MaximumCandidates
            ? all
            : all.OrderByDescending(item => item.Area).Take(MaximumCandidates);
        var panes = new List<(Vector3 A, Vector3 B, Vector3 C, Vector3 Normal, float PlaneOffset)>(MaximumCandidates);
        foreach (var (_, triangle) in kept)
        {
            var (a, b, c) = Corners(scene, triangle);
            var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            panes.Add((a, b, c, normal, Vector3.Dot(normal, a)));
        }

        // Merge touching, coplanar, same-facing panes into windows with a small union-find.
        var parent = Enumerable.Range(0, panes.Count).ToArray();
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        var tolerance = MathF.Max(epsilon * 4, 1e-3f);
        for (var i = 0; i < panes.Count; i++)
            for (var j = i + 1; j < panes.Count; j++)
                if (Vector3.Dot(panes[i].Normal, panes[j].Normal) > 0.9995f &&
                    MathF.Abs(panes[i].PlaneOffset - panes[j].PlaneOffset) < tolerance &&
                    Gap(panes[i], panes[j]) <= MergeDistance)
                    parent[Find(i)] = Find(j);

        var windows = new List<Window>();
        var groups = Enumerable.Range(0, panes.Count).GroupBy(Find)
            .OrderByDescending(group => group.Sum(i => 0.5f * Vector3.Cross(panes[i].B - panes[i].A, panes[i].C - panes[i].A).Length()))
            .Take(MaximumProbed);
        foreach (var members in groups)
        {
            var triangles = members.Select(i => (panes[i].A, panes[i].B, panes[i].C)).ToList();
            var normal = Vector3.Normalize(members.Aggregate(Vector3.Zero, (sum, i) => sum + panes[i].Normal));
            var center = triangles.Aggregate(Vector3.Zero, (sum, t) => sum + (t.A + t.B + t.C) / 3) / triangles.Count;
            if (MinimumExtentOf(triangles, normal) < MinimumExtent) continue;
            var front = SideOpenness(opaque, center, normal, epsilon);
            var back = SideOpenness(opaque, center, -normal, epsilon);
            if (MathF.Max(front, back) < MinimumOpenFraction) continue;           // glass between two closed spaces
            if (MathF.Min(front, back) > MaximumClosedSideOpenFraction) continue; // both sides open: no benefit
            windows.Add(new Window(triangles, front >= back ? normal : -normal));
        }
        if (windows.Count == 0) return null;
        if (windows.Count > MaximumPortals)
            windows = [.. windows.OrderByDescending(window => window.Triangles.Sum(t =>
                0.5f * Vector3.Cross(t.B - t.A, t.C - t.A).Length())).Take(MaximumPortals)];
        return new SkyPortals(windows);
    }

    /// <summary>Reports, for each merged window, how open to the sky its two faces are (for tuning and diagnostics).</summary>
    public static IReadOnlyList<(Vector3 Center, Vector3 Normal, float Front, float Back)> Probe(RenderScene scene,
        IReadOnlyList<int> glassTriangles, Bvh opaque, float epsilon)
    {
        var result = new List<(Vector3, Vector3, float, float)>();
        foreach (var triangle in glassTriangles)
        {
            var (a, b, c) = Corners(scene, triangle);
            var cross = Vector3.Cross(b - a, c - a);
            if (cross.Length() < 2e-6f) continue;
            var normal = Vector3.Normalize(cross);
            var center = (a + b + c) / 3;
            result.Add((center, normal, SideOpenness(opaque, center, normal, epsilon), SideOpenness(opaque, center, -normal, epsilon)));
        }
        return result;
    }

    /// <summary>
    /// Picks a window point as seen from <paramref name="position"/> and returns the direction to it with its
    /// solid-angle density. Returns false when no window is usable from here.
    /// </summary>
    public bool TrySample(Vector3 position, Vector3 normal, float u0, float u1, float u2, out Vector3 direction, out float pdf)
    {
        direction = default;
        pdf = 0;
        Span<float> weights = stackalloc float[Count];
        var total = Weights(position, normal, weights);
        if (total <= 0) return false;

        var target = u0 * total;
        var chosen = Count - 1;
        var running = 0f;
        for (var i = 0; i < Count; i++)
        {
            running += weights[i];
            if (running >= target && weights[i] > 0) { chosen = i; break; }
        }
        if (weights[chosen] <= 0) return false;

        // Reuse u0 (rescaled within the chosen window's slice would correlate); a fresh pair picks the point.
        var first = _first[chosen];
        var last = first + _count[chosen] - 1;
        var pick = u1 * _area[chosen];
        var slot = first;
        while (slot < last && _cumulativeArea[slot] < pick) slot++;
        var lower = slot == first ? 0 : _cumulativeArea[slot - 1];
        var span = _cumulativeArea[slot] - lower;
        var v1 = span > 0 ? Math.Clamp((pick - lower) / span, 0f, 0.99999f) : 0.5f; // stratified remainder of u1
        var root = MathF.Sqrt(v1);
        var point = _a[slot] * (1 - root) + _b[slot] * (root * (1 - u2)) + _c[slot] * (root * u2);
        var toPoint = point - position;
        var distanceSquared = toPoint.LengthSquared();
        if (distanceSquared < 1e-10f) return false;
        direction = toPoint / MathF.Sqrt(distanceSquared);
        // The probability of this direction sums every window that the ray also crosses.
        pdf = Pdf(position, direction, weights, total);
        return pdf > 0;
    }

    /// <summary>
    /// Total selection weight of the windows as seen from a point (a solid-angle proxy; zero when the point cannot use
    /// any). It shows where sky light enters a space and is used by diagnostics.
    /// </summary>
    public float Reach(Vector3 position, Vector3 normal)
    {
        Span<float> weights = stackalloc float[Count];
        return Weights(position, normal, weights);
    }

    /// <summary>Solid-angle density with which <see cref="TrySample"/> would generate <paramref name="direction"/>.</summary>
    public float Pdf(Vector3 position, Vector3 normal, Vector3 direction)
    {
        Span<float> weights = stackalloc float[Count];
        var total = Weights(position, normal, weights);
        return total <= 0 ? 0 : Pdf(position, direction, weights, total);
    }

    private float Pdf(Vector3 position, Vector3 direction, ReadOnlySpan<float> weights, float total)
    {
        var pdf = 0f;
        for (var i = 0; i < Count; i++)
        {
            if (weights[i] <= 0) continue;
            var cosine = MathF.Abs(Vector3.Dot(direction, _outward[i]));
            if (cosine < 1e-4f) continue;
            // Inexpensive rejection: the ray must head toward the window's side of the point.
            if (Vector3.Dot(_center[i] - position, direction) <= 0) continue;
            var hit = false;
            var distance = 0f;
            for (var slot = _first[i]; slot < _first[i] + _count[i] && !hit; slot++)
                hit = RayHitsTriangle(position, direction, _a[slot], _b[slot], _c[slot], out distance);
            if (!hit) continue;
            // Uniform over the window's area: density 1/area, converted to solid angle.
            pdf += weights[i] / total * (distance * distance) / (_area[i] * cosine);
        }
        return pdf;
    }

    /// <summary>
    /// Selection weight of each window from a shading point: solid-angle proxy, zero unless the point is on the
    /// closed side of the pane. Shared by sampling and <see cref="Pdf"/> by construction.
    /// </summary>
    private float Weights(Vector3 position, Vector3 normal, Span<float> weights)
    {
        var total = 0f;
        for (var i = 0; i < Count; i++)
        {
            weights[i] = 0;
            var toCenter = _center[i] - position;
            if (Vector3.Dot(position - _center[i], _outward[i]) >= -1e-4f) continue;
            var distanceSquared = toCenter.LengthSquared();
            if (distanceSquared < 1e-8f) continue;
            var inverseDistance = 1 / MathF.Sqrt(distanceSquared);
            var direction = toCenter * inverseDistance;
            var portalCosine = MathF.Abs(Vector3.Dot(direction, _outward[i]));
            if (portalCosine < 1e-3f) continue;
            // The 0.2 floor keeps windows that are only partly above the surface's horizon selectable.
            var surfaceCosine = MathF.Max(0, Vector3.Dot(normal, direction)) + 0.2f;
            var weight = _area[i] * portalCosine * surfaceCosine * (inverseDistance * inverseDistance);
            weights[i] = weight;
            total += weight;
        }
        return total;
    }

    /// <summary>Length of the shorter side of the bounding rectangle of the pane group, measured in its own plane.</summary>
    private static float MinimumExtentOf(List<(Vector3 A, Vector3 B, Vector3 C)> triangles, Vector3 normal)
    {
        var (u, v) = Basis(normal);
        float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
        foreach (var (a, b, c) in triangles)
            foreach (var point in new[] { a, b, c })
            {
                var pu = Vector3.Dot(point, u);
                var pv = Vector3.Dot(point, v);
                minU = MathF.Min(minU, pu); maxU = MathF.Max(maxU, pu);
                minV = MathF.Min(minV, pv); maxV = MathF.Max(maxV, pv);
            }
        return MathF.Min(maxU - minU, maxV - minV);
    }

    /// <summary>Smallest distance between a corner of one pane and a corner of the other.</summary>
    private static float Gap((Vector3 A, Vector3 B, Vector3 C, Vector3 Normal, float PlaneOffset) first,
        (Vector3 A, Vector3 B, Vector3 C, Vector3 Normal, float PlaneOffset) second)
    {
        Span<Vector3> left = [first.A, first.B, first.C];
        Span<Vector3> right = [second.A, second.B, second.C];
        var nearest = float.MaxValue;
        foreach (var p in left)
            foreach (var q in right)
                nearest = MathF.Min(nearest, Vector3.DistanceSquared(p, q));
        return MathF.Sqrt(nearest);
    }

    /// <summary>
    /// How much sky a floor-level point sees on one side of a pane: the larger escape fraction of upward
    /// cosine-distributed rays from points one and two meters away from the glass along <paramref name="away"/>.
    /// </summary>
    private static float SideOpenness(Bvh opaque, Vector3 center, Vector3 away, float epsilon) =>
        MathF.Max(OpenFraction(opaque, center + away * 1f, Vector3.UnitZ, epsilon),
                  OpenFraction(opaque, center + away * 2f, Vector3.UnitZ, epsilon));

    /// <summary>Fraction of cosine-distributed rays from a point that escape the scene without touching opaque geometry.</summary>
    private static float OpenFraction(Bvh opaque, Vector3 origin, Vector3 normal, float epsilon)
    {
        var (tangent, bitangent) = Basis(normal);
        var open = 0;
        // A fixed stratified pattern keeps results deterministic from run to run.
        for (var i = 0; i < ProbeRays; i++)
        {
            var r1 = (i + 0.5f) / ProbeRays;
            var r2 = (i * 0.6180339887f) % 1f;
            var radius = MathF.Sqrt(r1);
            var phi = 2 * MathF.PI * r2;
            var direction = tangent * (radius * MathF.Cos(phi)) + bitangent * (radius * MathF.Sin(phi)) +
                            normal * MathF.Sqrt(MathF.Max(0, 1 - r1));
            var hit = new RayHit { Distance = float.MaxValue };
            if (!opaque.Intersect(origin, direction, epsilon * 0.1f, ref hit)) open++;
        }
        return open / (float)ProbeRays;
    }

    private static (Vector3 A, Vector3 B, Vector3 C) Corners(RenderScene scene, int triangle) =>
        (scene.Positions[scene.Indices[triangle * 3]], scene.Positions[scene.Indices[triangle * 3 + 1]],
         scene.Positions[scene.Indices[triangle * 3 + 2]]);

    private static bool RayHitsTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float distance)
    {
        distance = 0;
        var edge1 = b - a;
        var edge2 = c - a;
        var p = Vector3.Cross(direction, edge2);
        var determinant = Vector3.Dot(edge1, p);
        if (MathF.Abs(determinant) < 1e-12f) return false;
        var inverse = 1 / determinant;
        var t = origin - a;
        var u = Vector3.Dot(t, p) * inverse;
        if (u < 0 || u > 1) return false;
        var q = Vector3.Cross(t, edge1);
        var v = Vector3.Dot(direction, q) * inverse;
        if (v < 0 || u + v > 1) return false;
        distance = Vector3.Dot(edge2, q) * inverse;
        return distance > 1e-5f;
    }

    private static (Vector3 Tangent, Vector3 Bitangent) Basis(Vector3 n)
    {
        var sign = MathF.CopySign(1, n.Z);
        var a = -1 / (sign + n.Z);
        var b = n.X * n.Y * a;
        return (new Vector3(1 + sign * n.X * n.X * a, sign * b, -sign * n.X), new Vector3(b, sign + n.Y * n.Y * a, -n.Y));
    }
}
