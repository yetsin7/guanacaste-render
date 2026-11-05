using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// EXPERIMENT, not used by the renderer (see docs/render/ADR-002). Four-wide bounding-volume hierarchy. It is built as a binned-SAH binary tree and then collapsed so each
/// node tests four child boxes with one SIMD slab test. Triangle data is stored in leaf order, so a leaf is
/// one contiguous cache-friendly read instead of a gather through an index array.
/// </summary>
public sealed class Bvh4
{
    private const int BinCount = 12;
    private const int MaxLeafSize = 4;
    private const int MaxDepth = 60;
    private const int LeafFlag = int.MinValue;

    private struct Node2
    {
        public Vector3 Min;
        public Vector3 Max;
        public int LeftOrFirst;
        public int Count;
    }

    private struct Node4
    {
        public Vector128<float> MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
        /// <summary>Child codes: ≥ 0 node index, &lt; 0 leaf ((first &lt;&lt; 3 | count) | LeafFlag), −1 empty.</summary>
        public Vector128<int> Child;
    }

    private struct Triangle
    {
        public Vector3 V0, E1, E2;
    }

    private Node2[] _binary = [];
    private int _binaryCount;
    private int[] _order = [];
    private Node4[] _nodes = [];
    private int _nodeCount;
    private Triangle[] _triangles = [];

    public int TriangleCount => _order.Length;
    public int NodeCount => _nodeCount;

    /// <summary>Approximate heap bytes held by the hierarchy and its triangle copy.</summary>
    public long MemoryBytes => (long)_nodes.Length * Unsafe.SizeOf<Node4>() + (long)_triangles.Length * Unsafe.SizeOf<Triangle>() +
                               (long)_order.Length * sizeof(int);

    public Bvh4(SceneMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var count = mesh.TriangleCount;
        var minimums = new Vector3[count];
        var maximums = new Vector3[count];
        var centroids = new Vector3[count];
        var source = new Triangle[count];
        _order = new int[count];
        for (var triangle = 0; triangle < count; triangle++)
        {
            var a = mesh.Positions[mesh.Indices[triangle * 3]];
            var b = mesh.Positions[mesh.Indices[triangle * 3 + 1]];
            var c = mesh.Positions[mesh.Indices[triangle * 3 + 2]];
            source[triangle] = new Triangle { V0 = a, E1 = b - a, E2 = c - a };
            _order[triangle] = triangle;
            minimums[triangle] = Vector3.Min(a, Vector3.Min(b, c));
            maximums[triangle] = Vector3.Max(a, Vector3.Max(b, c));
            centroids[triangle] = (a + b + c) / 3;
        }
        _binary = new Node2[Math.Max(1, count)];
        _binary[0] = new Node2 { LeftOrFirst = 0, Count = count };
        _binaryCount = 1;
        if (count > 0)
        {
            UpdateBounds(0, minimums, maximums);
            Subdivide(0, 0, centroids, minimums, maximums);
        }
        _triangles = new Triangle[count];
        for (var slot = 0; slot < count; slot++) _triangles[slot] = source[_order[slot]];
        _nodes = new Node4[Math.Max(1, _binaryCount / 2 + 1)];
        _nodeCount = 0;
        Collapse(0);
        _binary = [];
    }

    /// <summary>Returns unit geometric normals indexed by the original triangle number (not flipped toward a ray).</summary>
    public Vector3[] BuildNormals()
    {
        var normals = new Vector3[_order.Length];
        for (var slot = 0; slot < _order.Length; slot++)
            normals[_order[slot]] = Vector3.Normalize(Vector3.Cross(_triangles[slot].E1, _triangles[slot].E2));
        return normals;
    }

    private void UpdateBounds(int nodeIndex, Vector3[] minimums, Vector3[] maximums)
    {
        ref var node = ref _binary[nodeIndex];
        node.Min = new Vector3(float.MaxValue);
        node.Max = new Vector3(float.MinValue);
        for (var i = 0; i < node.Count; i++)
        {
            var triangle = _order[node.LeftOrFirst + i];
            node.Min = Vector3.Min(node.Min, minimums[triangle]);
            node.Max = Vector3.Max(node.Max, maximums[triangle]);
        }
    }

    private void Subdivide(int nodeIndex, int depth, Vector3[] centroids, Vector3[] minimums, Vector3[] maximums)
    {
        var node = _binary[nodeIndex];
        if (node.Count <= MaxLeafSize || depth >= MaxDepth) return;
        var (axis, position, cost) = FindBestSplit(node, centroids, minimums, maximums);
        if (axis < 0 || cost >= node.Count * SurfaceArea(node.Min, node.Max)) return;

        var i = node.LeftOrFirst;
        var j = i + node.Count - 1;
        while (i <= j)
        {
            if (Component(centroids[_order[i]], axis) < position) { i++; continue; }
            (_order[i], _order[j]) = (_order[j], _order[i]);
            j--;
        }
        var leftCount = i - node.LeftOrFirst;
        if (leftCount == 0 || leftCount == node.Count) return;

        if (_binaryCount + 2 > _binary.Length) Array.Resize(ref _binary, _binary.Length * 2);
        var left = _binaryCount++;
        var right = _binaryCount++;
        _binary[left] = new Node2 { LeftOrFirst = node.LeftOrFirst, Count = leftCount };
        _binary[right] = new Node2 { LeftOrFirst = i, Count = node.Count - leftCount };
        _binary[nodeIndex].LeftOrFirst = left;
        _binary[nodeIndex].Count = 0;
        UpdateBounds(left, minimums, maximums);
        UpdateBounds(right, minimums, maximums);
        Subdivide(left, depth + 1, centroids, minimums, maximums);
        Subdivide(right, depth + 1, centroids, minimums, maximums);
    }

    private (int Axis, float Position, float Cost) FindBestSplit(Node2 node, Vector3[] centroids, Vector3[] minimums,
        Vector3[] maximums)
    {
        var bestAxis = -1;
        var bestPosition = 0f;
        var bestCost = float.MaxValue;
        Span<Vector3> binMin = stackalloc Vector3[BinCount];
        Span<Vector3> binMax = stackalloc Vector3[BinCount];
        Span<int> binCount = stackalloc int[BinCount];
        Span<float> leftArea = stackalloc float[BinCount - 1];
        Span<int> leftCount = stackalloc int[BinCount - 1];
        for (var axis = 0; axis < 3; axis++)
        {
            var low = float.MaxValue;
            var high = float.MinValue;
            for (var i = 0; i < node.Count; i++)
            {
                var value = Component(centroids[_order[node.LeftOrFirst + i]], axis);
                low = MathF.Min(low, value);
                high = MathF.Max(high, value);
            }
            if (high - low < 1e-6f) continue;
            binMin.Fill(new Vector3(float.MaxValue));
            binMax.Fill(new Vector3(float.MinValue));
            binCount.Clear();
            var scale = BinCount / (high - low);
            for (var i = 0; i < node.Count; i++)
            {
                var triangle = _order[node.LeftOrFirst + i];
                var bin = Math.Min(BinCount - 1, (int)((Component(centroids[triangle], axis) - low) * scale));
                binCount[bin]++;
                binMin[bin] = Vector3.Min(binMin[bin], minimums[triangle]);
                binMax[bin] = Vector3.Max(binMax[bin], maximums[triangle]);
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
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestAxis = axis;
                    bestPosition = low + bin / scale;
                }
            }
        }
        return (bestAxis, bestPosition, bestCost);
    }

    /// <summary>Turns a binary subtree into a four-wide node by absorbing the largest children into it.</summary>
    private int Collapse(int binaryIndex)
    {
        var self = _nodeCount++;
        if (self >= _nodes.Length) Array.Resize(ref _nodes, _nodes.Length * 2);
        Span<int> slots = stackalloc int[4];
        var slotCount = 0;
        var root = _binary[binaryIndex];
        if (root.Count > 0)
        {
            slots[slotCount++] = binaryIndex;
        }
        else
        {
            slots[slotCount++] = root.LeftOrFirst;
            slots[slotCount++] = root.LeftOrFirst + 1;
            while (slotCount < 4)
            {
                var best = -1;
                var bestArea = -1f;
                for (var i = 0; i < slotCount; i++)
                {
                    var candidate = _binary[slots[i]];
                    if (candidate.Count > 0) continue;
                    var area = SurfaceArea(candidate.Min, candidate.Max);
                    if (area > bestArea) { bestArea = area; best = i; }
                }
                if (best < 0) break;
                var split = _binary[slots[best]];
                slots[best] = split.LeftOrFirst;
                if (slotCount < 4) slots[slotCount++] = split.LeftOrFirst + 1;
            }
        }

        Span<float> minX = stackalloc float[4], minY = stackalloc float[4], minZ = stackalloc float[4];
        Span<float> maxX = stackalloc float[4], maxY = stackalloc float[4], maxZ = stackalloc float[4];
        Span<int> child = stackalloc int[4];
        for (var i = 0; i < 4; i++)
        {
            if (i >= slotCount)
            {
                (minX[i], minY[i], minZ[i]) = (float.MaxValue, float.MaxValue, float.MaxValue);
                (maxX[i], maxY[i], maxZ[i]) = (float.MinValue, float.MinValue, float.MinValue);
                child[i] = -1;
                continue;
            }
            var node = _binary[slots[i]];
            (minX[i], minY[i], minZ[i]) = (node.Min.X, node.Min.Y, node.Min.Z);
            (maxX[i], maxY[i], maxZ[i]) = (node.Max.X, node.Max.Y, node.Max.Z);
            child[i] = node.Count > 0 ? (node.LeftOrFirst << 3 | node.Count) | LeafFlag : Collapse(slots[i]);
        }
        _nodes[self] = new Node4
        {
            MinX = Vector128.Create(minX[0], minX[1], minX[2], minX[3]),
            MinY = Vector128.Create(minY[0], minY[1], minY[2], minY[3]),
            MinZ = Vector128.Create(minZ[0], minZ[1], minZ[2], minZ[3]),
            MaxX = Vector128.Create(maxX[0], maxX[1], maxX[2], maxX[3]),
            MaxY = Vector128.Create(maxY[0], maxY[1], maxY[2], maxY[3]),
            MaxZ = Vector128.Create(maxZ[0], maxZ[1], maxZ[2], maxZ[3]),
            Child = Vector128.Create(child[0], child[1], child[2], child[3])
        };
        return self;
    }

    /// <summary>Finds the closest intersection within (minDistance, hit.Distance).</summary>
    public bool Intersect(Vector3 origin, Vector3 direction, float minDistance, ref RayHit hit)
    {
        if (_order.Length == 0) return false;
        var ix = SafeInverse(direction.X);
        var iy = SafeInverse(direction.Y);
        var iz = SafeInverse(direction.Z);
        var ox = Vector128.Create(origin.X);
        var oy = Vector128.Create(origin.Y);
        var oz = Vector128.Create(origin.Z);
        var vix = Vector128.Create(ix);
        var viy = Vector128.Create(iy);
        var viz = Vector128.Create(iz);
        var zero = Vector128<float>.Zero;
        Span<int> stackNode = stackalloc int[128];
        Span<float> stackDistance = stackalloc float[128];
        Span<float> childDistance = stackalloc float[4];
        Span<int> childCode = stackalloc int[4];
        var top = 0;
        var code = 0;
        var found = false;
        while (true)
        {
            if (code < 0)
            {
                var first = (code & int.MaxValue) >> 3;
                var count = code & 7;
                for (var slot = first; slot < first + count; slot++)
                    if (IntersectTriangle(slot, origin, direction, minDistance, ref hit)) found = true;
            }
            else
            {
                ref readonly var node = ref _nodes[code];
                var t1x = (node.MinX - ox) * vix;
                var t2x = (node.MaxX - ox) * vix;
                var t1y = (node.MinY - oy) * viy;
                var t2y = (node.MaxY - oy) * viy;
                var t1z = (node.MinZ - oz) * viz;
                var t2z = (node.MaxZ - oz) * viz;
                var near = Vector128.Max(Vector128.Max(Vector128.Min(t1x, t2x), Vector128.Min(t1y, t2y)),
                    Vector128.Max(Vector128.Min(t1z, t2z), zero));
                var far = Vector128.Min(Vector128.Min(Vector128.Max(t1x, t2x), Vector128.Max(t1y, t2y)),
                    Vector128.Min(Vector128.Max(t1z, t2z), Vector128.Create(hit.Distance)));
                var mask = Vector128.ExtractMostSignificantBits(Vector128.LessThanOrEqual(near, far));
                if (mask != 0)
                {
                    var hits = 0;
                    for (var lane = 0; lane < 4; lane++)
                    {
                        if ((mask & (1u << lane)) == 0) continue;
                        var childValue = node.Child.GetElement(lane);
                        if (childValue == -1) continue;
                        var distance = near.GetElement(lane);
                        // Insertion sort by entry distance, nearest first.
                        var at = hits++;
                        while (at > 0 && childDistance[at - 1] > distance)
                        {
                            childDistance[at] = childDistance[at - 1];
                            childCode[at] = childCode[at - 1];
                            at--;
                        }
                        childDistance[at] = distance;
                        childCode[at] = childValue;
                    }
                    if (hits > 0)
                    {
                        for (var k = hits - 1; k >= 1; k--)
                        {
                            stackNode[top] = childCode[k];
                            stackDistance[top++] = childDistance[k];
                        }
                        code = childCode[0];
                        continue;
                    }
                }
            }
            // Pop the next pending subtree whose entry is still closer than the best hit.
            while (true)
            {
                if (top == 0) return found;
                top--;
                if (stackDistance[top] < hit.Distance) { code = stackNode[top]; break; }
            }
        }
    }

    /// <summary>Returns true when anything blocks the segment (used for shadow rays).</summary>
    public bool Occluded(Vector3 origin, Vector3 direction, float minDistance, float maxDistance)
    {
        var hit = new RayHit { Distance = maxDistance };
        return Intersect(origin, direction, minDistance, ref hit);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IntersectTriangle(int slot, Vector3 origin, Vector3 direction, float minDistance, ref RayHit hit)
    {
        ref readonly var triangle = ref _triangles[slot];
        var p = Vector3.Cross(direction, triangle.E2);
        var determinant = Vector3.Dot(triangle.E1, p);
        if (MathF.Abs(determinant) < 1e-9f) return false;
        var inverse = 1 / determinant;
        var t = origin - triangle.V0;
        var u = Vector3.Dot(t, p) * inverse;
        if (u < 0 || u > 1) return false;
        var q = Vector3.Cross(t, triangle.E1);
        var v = Vector3.Dot(direction, q) * inverse;
        if (v < 0 || u + v > 1) return false;
        var distance = Vector3.Dot(triangle.E2, q) * inverse;
        if (distance <= minDistance || distance >= hit.Distance) return false;
        hit.Distance = distance;
        hit.Triangle = _order[slot];
        hit.U = u;
        hit.V = v;
        return true;
    }

    /// <summary>1/x that stays finite for zero so the slab test never produces NaN from 0·∞.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SafeInverse(float value) =>
        MathF.Abs(value) < 1e-20f ? MathF.CopySign(1e20f, value) : 1 / value;

    private static float SurfaceArea(Vector3 min, Vector3 max)
    {
        var size = max - min;
        return size.X * size.Y + size.Y * size.Z + size.Z * size.X;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Component(Vector3 vector, int axis) => axis == 0 ? vector.X : axis == 1 ? vector.Y : vector.Z;
}
