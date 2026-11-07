using System.Numerics;
using System.Runtime.CompilerServices;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core.Rendering;

/// <summary>Closest-hit result of a ray query.</summary>
public struct RayHit
{
    public float Distance;
    /// <summary>Triangle index in the caller's numbering (not the internal reordered slot).</summary>
    public int Triangle;
    public float U;
    public float V;
    /// <summary>Internal slot of the hit triangle, used to read its geometry back without a search.</summary>
    internal int Slot;
}

/// <summary>
/// Bounding-volume hierarchy over a triangle set, built with a binned surface-area heuristic.
/// Nodes are stored flat. Triangle data is reordered to follow the leaves, so traversal reads memory
/// sequentially. Large inputs are built on all cores: the top levels are split sequentially and each
/// remaining subtree is built independently, then stitched into the node array.
/// </summary>
public sealed class Bvh
{
    private readonly AlphaCutout? _cutout;

    /// <summary>
    /// Holes in textured cards (leaves, grass): a hit where the texture is transparent is skipped, so closest-hit and
    /// shadow queries both see through it. Null for fully opaque geometry.
    /// </summary>
    public AlphaCutout? Cutout { get => _cutout; init => _cutout = value; }

    private const int BinCount = 12;
    private const int MaxLeafSize = 4;
    private const int MaxDepth = 60;
    private const int ParallelThreshold = 40_000;
    private const int StackSize = 128;

    private struct Node
    {
        public Vector3 Min;
        public int LeftOrFirst; // left child for interior nodes (right = left + 1), first slot for leaves
        public Vector3 Max;
        public int Count;       // zero for interior nodes
    }

    private readonly Vector3[] _vertex0;
    private readonly Vector3[] _edge1;
    private readonly Vector3[] _edge2;
    private readonly int[] _triangleOfSlot;
    private readonly Node[] _nodes;
    private int[]? _slotOfTriangle;

    public int TriangleCount => _triangleOfSlot.Length;
    public int NodeCount => _nodes.Length;

    /// <summary>Bounds of everything in the hierarchy; a degenerate box when it is empty.</summary>
    public (Vector3 Min, Vector3 Max) Bounds => _triangleOfSlot.Length == 0
        ? (Vector3.Zero, Vector3.Zero)
        : (_nodes[0].Min, _nodes[0].Max);

    /// <summary>Builds the hierarchy over every triangle of a scene mesh.</summary>
    public Bvh(SceneMesh mesh) : this(mesh?.Positions.ToArray() ?? throw new ArgumentNullException(nameof(mesh)),
        mesh.Indices.ToArray(), null)
    {
    }

    /// <summary>
    /// Builds the hierarchy over indexed triangles. When <paramref name="triangles"/> is given, only those
    /// triangle numbers are included, and hits report the same numbers.
    /// </summary>
    public Bvh(Vector3[] positions, int[] indices, int[]? triangles, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(indices);
        var count = triangles?.Length ?? indices.Length / 3;
        _triangleOfSlot = new int[count];
        _vertex0 = new Vector3[count];
        _edge1 = new Vector3[count];
        _edge2 = new Vector3[count];
        if (count == 0)
        {
            _nodes = [];
            return;
        }

        var order = new int[count];
        var centroids = new Vector3[count];
        var minimums = new Vector3[count];
        var maximums = new Vector3[count];
        Parallel.For(0, count, new ParallelOptions { CancellationToken = cancellationToken }, slot =>
        {
            var triangle = triangles is null ? slot : triangles[slot];
            var a = positions[indices[triangle * 3]];
            var b = positions[indices[triangle * 3 + 1]];
            var c = positions[indices[triangle * 3 + 2]];
            _triangleOfSlot[slot] = triangle;
            order[slot] = slot;
            minimums[slot] = Vector3.Min(a, Vector3.Min(b, c));
            maximums[slot] = Vector3.Max(a, Vector3.Max(b, c));
            centroids[slot] = (a + b + c) / 3;
        });

        var trace = Environment.GetEnvironmentVariable("G3_BVH_TRACE") is not null;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var builder = new Builder(order, centroids, minimums, maximums, cancellationToken);
        _nodes = builder.Build();
        if (trace) Console.WriteLine($"  [bvh] build {clock.ElapsedMilliseconds} ms, {_nodes.Length} nodes for {count} tris");

        // Pack triangle data in leaf order so each leaf reads contiguous memory.
        var sourceSlots = order;
        var originalTriangles = (int[])_triangleOfSlot.Clone();
        Parallel.For(0, count, slot =>
        {
            var source = sourceSlots[slot];
            var triangle = originalTriangles[source];
            var a = positions[indices[triangle * 3]];
            _triangleOfSlot[slot] = triangle;
            _vertex0[slot] = a;
            _edge1[slot] = positions[indices[triangle * 3 + 1]] - a;
            _edge2[slot] = positions[indices[triangle * 3 + 2]] - a;
        });
    }

    /// <summary>Returns the geometric normal of the triangle behind a hit, unnormalized orientation (not facing the ray).</summary>
    public Vector3 Normal(in RayHit hit) =>
        Vector3.Normalize(Vector3.Cross(_edge1[hit.Slot], _edge2[hit.Slot]));

    /// <summary>Returns the geometric normal of a triangle given its original number.</summary>
    public Vector3 Normal(int triangle)
    {
        if (_slotOfTriangle is null)
        {
            var inverse = new int[_triangleOfSlot.Length == 0 ? 0 : _triangleOfSlot.Max() + 1];
            Array.Fill(inverse, -1);
            for (var slot = 0; slot < _triangleOfSlot.Length; slot++) inverse[_triangleOfSlot[slot]] = slot;
            _slotOfTriangle = inverse;
        }
        var slotOfTriangle = _slotOfTriangle[triangle];
        return slotOfTriangle < 0
            ? throw new ArgumentOutOfRangeException(nameof(triangle))
            : Vector3.Normalize(Vector3.Cross(_edge1[slotOfTriangle], _edge2[slotOfTriangle]));
    }

    /// <summary>Finds the closest intersection within (minDistance, hit.Distance).</summary>
    public bool Intersect(Vector3 origin, Vector3 direction, float minDistance, ref RayHit hit)
    {
        if (_nodes.Length == 0) return false;
        var inverse = new Vector3(1 / direction.X, 1 / direction.Y, 1 / direction.Z);
        Span<int> stack = stackalloc int[StackSize];
        var top = 0;
        var nodeIndex = 0;
        var found = false;
        while (true)
        {
            ref readonly var node = ref _nodes[nodeIndex];
            if (node.Count > 0)
            {
                var end = node.LeftOrFirst + node.Count;
                for (var slot = node.LeftOrFirst; slot < end; slot++)
                    if (IntersectTriangle(slot, origin, direction, minDistance, ref hit)) found = true;
                if (top == 0) break;
                nodeIndex = stack[--top];
                continue;
            }
            var first = node.LeftOrFirst;
            var second = first + 1;
            var firstDistance = SlabTest(in _nodes[first], origin, inverse, hit.Distance);
            var secondDistance = SlabTest(in _nodes[second], origin, inverse, hit.Distance);
            if (firstDistance > secondDistance)
            {
                (first, second) = (second, first);
                (firstDistance, secondDistance) = (secondDistance, firstDistance);
            }
            if (firstDistance == float.MaxValue)
            {
                if (top == 0) break;
                nodeIndex = stack[--top];
                continue;
            }
            nodeIndex = first;
            if (secondDistance != float.MaxValue) stack[top++] = second;
        }
        return found;
    }

    /// <summary>Returns true as soon as any triangle blocks the segment; cheaper than a closest-hit query.</summary>
    public bool Occluded(Vector3 origin, Vector3 direction, float minDistance, float maxDistance)
    {
        if (_nodes.Length == 0) return false;
        var inverse = new Vector3(1 / direction.X, 1 / direction.Y, 1 / direction.Z);
        Span<int> stack = stackalloc int[StackSize];
        var top = 0;
        var nodeIndex = 0;
        var probe = new RayHit { Distance = maxDistance };
        while (true)
        {
            ref readonly var node = ref _nodes[nodeIndex];
            if (node.Count > 0)
            {
                var end = node.LeftOrFirst + node.Count;
                for (var slot = node.LeftOrFirst; slot < end; slot++)
                    if (IntersectTriangle(slot, origin, direction, minDistance, ref probe)) return true;
                if (top == 0) return false;
                nodeIndex = stack[--top];
                continue;
            }
            var first = node.LeftOrFirst;
            var second = first + 1;
            var firstDistance = SlabTest(in _nodes[first], origin, inverse, maxDistance);
            var secondDistance = SlabTest(in _nodes[second], origin, inverse, maxDistance);
            if (firstDistance > secondDistance)
            {
                (first, second) = (second, first);
                (firstDistance, secondDistance) = (secondDistance, firstDistance);
            }
            if (firstDistance == float.MaxValue)
            {
                if (top == 0) return false;
                nodeIndex = stack[--top];
                continue;
            }
            nodeIndex = first;
            if (secondDistance != float.MaxValue) stack[top++] = second;
        }
    }

    /// <summary>Möller–Trumbore ray/triangle test, two-sided.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IntersectTriangle(int slot, Vector3 origin, Vector3 direction, float minDistance, ref RayHit hit)
    {
        var edge1 = _edge1[slot];
        var edge2 = _edge2[slot];
        var p = Vector3.Cross(direction, edge2);
        var determinant = Vector3.Dot(edge1, p);
        if (MathF.Abs(determinant) < 1e-12f) return false;
        var inverseDeterminant = 1 / determinant;
        var t = origin - _vertex0[slot];
        var u = Vector3.Dot(t, p) * inverseDeterminant;
        if (u < 0 || u > 1) return false;
        var q = Vector3.Cross(t, edge1);
        var v = Vector3.Dot(direction, q) * inverseDeterminant;
        if (v < 0 || u + v > 1) return false;
        var distance = Vector3.Dot(edge2, q) * inverseDeterminant;
        if (distance <= minDistance || distance >= hit.Distance) return false;
        if (_cutout is not null && _cutout.IsHole(_triangleOfSlot[slot], u, v)) return false;
        hit.Distance = distance;
        hit.Triangle = _triangleOfSlot[slot];
        hit.Slot = slot;
        hit.U = u;
        hit.V = v;
        return true;
    }

    /// <summary>Returns the entry distance into a node's box, or MaxValue when it is missed.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SlabTest(in Node node, Vector3 origin, Vector3 inverse, float maxDistance)
    {
        var t1 = (node.Min - origin) * inverse;
        var t2 = (node.Max - origin) * inverse;
        var near = Vector3.Min(t1, t2);
        var far = Vector3.Max(t1, t2);
        var entry = MathF.Max(MathF.Max(near.X, near.Y), near.Z);
        var exit = MathF.Min(MathF.Min(far.X, far.Y), far.Z);
        return exit >= MathF.Max(entry, 0) && entry < maxDistance ? entry : float.MaxValue;
    }

    /// <summary>
    /// Top-down binned-SAH construction. Triangle slots are partitioned in place inside
    /// <c>order</c>; subtrees touch disjoint ranges, so they can be built concurrently.
    /// </summary>
    private sealed class Builder(int[] order, Vector3[] centroids, Vector3[] minimums, Vector3[] maximums,
        CancellationToken cancellationToken)
    {
        private readonly List<(int Node, int Depth)> _frontier = [];

        /// <summary>Builds the whole tree and returns the packed node array (root at index 0).</summary>
        public Node[] Build()
        {
            var trace = Environment.GetEnvironmentVariable("G3_BVH_TRACE") is not null;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var top = new Nodes(Math.Max(16, order.Length / 2));
            var root = top.Allocate(1)[0];
            top[root] = new Node { LeftOrFirst = 0, Count = order.Length };
            UpdateBounds(ref top[root]);
            Subdivide(top, root, 0, defer: true);

            if (trace) Console.WriteLine($"  [bvh] top phase {clock.ElapsedMilliseconds} ms, frontier {_frontier.Count}");
            // Each deferred node roots an independent range; build those on all cores.
            var subtrees = new Nodes?[_frontier.Count];
            Parallel.For(0, _frontier.Count, new ParallelOptions { CancellationToken = cancellationToken }, i =>
            {
                var local = new Nodes(Math.Max(16, top[_frontier[i].Node].Count / 2));
                var localRoot = local.Allocate(1)[0];
                local[localRoot] = top[_frontier[i].Node];
                Subdivide(local, localRoot, _frontier[i].Depth, defer: false);
                subtrees[i] = local;
            });

            if (trace) Console.WriteLine($"  [bvh] subtrees {clock.ElapsedMilliseconds} ms");
            for (var i = 0; i < _frontier.Count; i++)
            {
                var local = subtrees[i]!;
                var frontier = _frontier[i].Node;
                if (local.Count == 1)
                {
                    top[frontier] = local[0];
                    continue;
                }
                // Local node k ≥ 1 lands at base + k − 1; the local root replaces the frontier node itself.
                var baseIndex = top.Count;
                var appended = top.Allocate(local.Count - 1);
                for (var k = 1; k < local.Count; k++)
                {
                    var node = local[k];
                    if (node.Count == 0) node.LeftOrFirst += baseIndex - 1;
                    top[appended[k - 1]] = node;
                }
                var rootNode = local[0];
                rootNode.LeftOrFirst += baseIndex - 1;
                top[frontier] = rootNode;
            }
            return top.ToArray();
        }

        private void Subdivide(Nodes nodes, int nodeIndex, int depth, bool defer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = nodes[nodeIndex];
            if (node.Count <= MaxLeafSize || depth >= MaxDepth) return;
            if (defer && node.Count <= ParallelThreshold)
            {
                _frontier.Add((nodeIndex, depth));
                return;
            }

            var (axis, position, cost) = FindBestSplit(node);
            var parentCost = node.Count * SurfaceArea(node.Min, node.Max);
            var leftCount = 0;
            if (axis >= 0 && cost < parentCost)
            {
                var i = node.LeftOrFirst;
                var j = i + node.Count - 1;
                while (i <= j)
                {
                    if (Component(centroids[order[i]], axis) < position)
                    {
                        i++;
                        continue;
                    }
                    (order[i], order[j]) = (order[j], order[i]);
                    j--;
                }
                leftCount = i - node.LeftOrFirst;
            }
            else if (axis < 0 && node.Count > 8 * MaxLeafSize)
            {
                // Every centroid coincides (duplicated geometry); split by count so leaves stay small.
                leftCount = node.Count / 2;
            }
            else if (axis < 0 || node.Count <= 16 * MaxLeafSize)
            {
                return;
            }
            if (leftCount <= 0 || leftCount >= node.Count)
            {
                // SAH preferred a leaf but the leaf would be large; fall back to a median split on the widest axis.
                if (node.Count <= 16 * MaxLeafSize) return;
                leftCount = MedianSplit(node);
                if (leftCount <= 0 || leftCount >= node.Count) return;
            }

            var children = nodes.Allocate(2);
            var left = children[0];
            var right = children[1];
            nodes[left] = new Node { LeftOrFirst = node.LeftOrFirst, Count = leftCount };
            nodes[right] = new Node { LeftOrFirst = node.LeftOrFirst + leftCount, Count = node.Count - leftCount };
            nodes[nodeIndex].LeftOrFirst = left;
            nodes[nodeIndex].Count = 0;
            UpdateBounds(ref nodes[left]);
            UpdateBounds(ref nodes[right]);
            Subdivide(nodes, left, depth + 1, defer);
            Subdivide(nodes, right, depth + 1, defer);
        }

        /// <summary>Partitions the range around the median centroid of its widest axis and returns the left count.</summary>
        private int MedianSplit(Node node)
        {
            var size = node.Max - node.Min;
            var axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
            Array.Sort(order, node.LeftOrFirst, node.Count,
                Comparer<int>.Create((a, b) => Component(centroids[a], axis).CompareTo(Component(centroids[b], axis))));
            return node.Count / 2;
        }

        private void UpdateBounds(ref Node node)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            var end = node.LeftOrFirst + node.Count;
            for (var i = node.LeftOrFirst; i < end; i++)
            {
                var slot = order[i];
                min = Vector3.Min(min, minimums[slot]);
                max = Vector3.Max(max, maximums[slot]);
            }
            node.Min = min;
            node.Max = max;
        }

        /// <summary>Evaluates BinCount−1 candidate planes per axis over the centroid bounds.</summary>
        private (int Axis, float Position, float Cost) FindBestSplit(Node node)
        {
            var bestAxis = -1;
            var bestPosition = 0f;
            var bestCost = float.MaxValue;
            Span<Vector3> binMin = stackalloc Vector3[BinCount];
            Span<Vector3> binMax = stackalloc Vector3[BinCount];
            Span<int> binCount = stackalloc int[BinCount];
            Span<float> leftArea = stackalloc float[BinCount - 1];
            Span<int> leftCount = stackalloc int[BinCount - 1];
            var end = node.LeftOrFirst + node.Count;

            for (var axis = 0; axis < 3; axis++)
            {
                var low = float.MaxValue;
                var high = float.MinValue;
                for (var i = node.LeftOrFirst; i < end; i++)
                {
                    var value = Component(centroids[order[i]], axis);
                    low = MathF.Min(low, value);
                    high = MathF.Max(high, value);
                }
                if (high - low < 1e-7f * MathF.Max(1, MathF.Abs(high))) continue;

                binMin.Fill(new Vector3(float.MaxValue));
                binMax.Fill(new Vector3(float.MinValue));
                binCount.Clear();
                var scale = BinCount / (high - low);
                for (var i = node.LeftOrFirst; i < end; i++)
                {
                    var slot = order[i];
                    var bin = Math.Min(BinCount - 1, (int)((Component(centroids[slot], axis) - low) * scale));
                    binCount[bin]++;
                    binMin[bin] = Vector3.Min(binMin[bin], minimums[slot]);
                    binMax[bin] = Vector3.Max(binMax[bin], maximums[slot]);
                }

                var runningMin = new Vector3(float.MaxValue);
                var runningMax = new Vector3(float.MinValue);
                var runningCount = 0;
                for (var bin = 0; bin < BinCount - 1; bin++)
                {
                    runningCount += binCount[bin];
                    runningMin = Vector3.Min(runningMin, binMin[bin]);
                    runningMax = Vector3.Max(runningMax, binMax[bin]);
                    leftCount[bin] = runningCount;
                    leftArea[bin] = runningCount == 0 ? 0 : SurfaceArea(runningMin, runningMax);
                }
                runningMin = new Vector3(float.MaxValue);
                runningMax = new Vector3(float.MinValue);
                runningCount = 0;
                for (var bin = BinCount - 1; bin > 0; bin--)
                {
                    runningCount += binCount[bin];
                    runningMin = Vector3.Min(runningMin, binMin[bin]);
                    runningMax = Vector3.Max(runningMax, binMax[bin]);
                    var rightArea = runningCount == 0 ? 0 : SurfaceArea(runningMin, runningMax);
                    var cost = leftCount[bin - 1] * leftArea[bin - 1] + runningCount * rightArea;
                    if (cost < bestCost && leftCount[bin - 1] > 0 && runningCount > 0)
                    {
                        bestCost = cost;
                        bestAxis = axis;
                        bestPosition = low + bin / scale;
                    }
                }
            }
            return (bestAxis, bestPosition, bestCost);
        }
    }

    /// <summary>Growable node storage with a stable index space, shared by the sequential and parallel build phases.</summary>
    private sealed class Nodes(int capacity)
    {
        private Node[] _items = new Node[Math.Max(16, capacity)];

        public int Count { get; private set; }

        public ref Node this[int index] => ref _items[index];

        /// <summary>Reserves <paramref name="count"/> consecutive nodes and returns their indices.</summary>
        public int[] Allocate(int count)
        {
            if (Count + count > _items.Length)
                Array.Resize(ref _items, (int)Math.Max(Count + count, Math.Min(int.MaxValue / 2L, _items.Length * 2L)));
            var result = new int[count];
            for (var i = 0; i < count; i++) result[i] = Count + i;
            Count += count;
            return result;
        }

        public Node[] ToArray() => _items.AsSpan(0, Count).ToArray();
    }

    private static float SurfaceArea(Vector3 min, Vector3 max)
    {
        var size = max - min;
        return size.X * size.Y + size.Y * size.Z + size.Z * size.X;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Component(Vector3 vector, int axis) =>
        axis == 0 ? vector.X : axis == 1 ? vector.Y : vector.Z;
}
