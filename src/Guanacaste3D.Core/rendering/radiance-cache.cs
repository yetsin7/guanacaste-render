using System.Numerics;
using System.Runtime.CompilerServices;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// A world-space cache of the radiance that diffuse surfaces send back, shared by all paths and, unlike an image,
/// still valid when the camera moves. Light that was already computed for a wall is reused instead of recomputed.
/// </summary>
/// <remarks>
/// Entries are keyed by a 3D cell and the dominant axis of the surface normal, packed losslessly into 64 bits, so a
/// region of the building can be invalidated by box (when a wall moves or a luminaire changes) without touching the
/// rest. Values are exponential moving averages, which makes concurrent updates safe in the only way that matters:
/// every stored value is a convex blend of valid samples, so a lost update merely drops one sample.
/// The cache trades a little spatial blur (up to one cell) for far fewer rays; it is meant for the light that bounces
/// at least once before it is seen, never for the first visible surface.
/// </remarks>
public sealed class RadianceCache
{
    private const int PositionBits = 20;
    private const int PositionOffset = 1 << (PositionBits - 1);
    private const int Probes = 4;

    private readonly ulong[] _keys;
    private readonly Vector3[] _values;
    private readonly ushort[] _counts;
    private readonly int _mask;
    private readonly float _inverseCell;

    /// <summary>Edge length of a cache cell in meters.</summary>
    public float CellSize { get; }

    public int Capacity => _keys.Length;

    /// <summary>Samples an entry needs before it may stand in for a real path.</summary>
    public int MinimumSamples { get; init; } = 4;

    /// <summary>
    /// Samples averaged with equal weight before older ones start to fade. A long window keeps the estimate steady, which
    /// matters because estimates feed one another; a short one follows change faster but lets early noise linger.
    /// </summary>
    public int WindowSamples { get; init; } = 48;

    public RadianceCache(float cellSize, int capacityLog2 = 20)
    {
        if (!(cellSize >= 0.01f && cellSize <= 100f)) throw new ArgumentOutOfRangeException(nameof(cellSize));
        if (capacityLog2 is < 10 or > 24) throw new ArgumentOutOfRangeException(nameof(capacityLog2));
        CellSize = cellSize;
        _inverseCell = 1 / cellSize;
        _keys = new ulong[1 << capacityLog2];
        _values = new Vector3[1 << capacityLog2];
        _counts = new ushort[1 << capacityLog2];
        _mask = (1 << capacityLog2) - 1;
    }

    /// <summary>Approximate heap size of the table.</summary>
    public long MemoryBytes => (long)_keys.Length * (sizeof(ulong) + 12 + sizeof(ushort));

    /// <summary>Entries currently holding data (scans the table, so use for diagnostics only).</summary>
    public int OccupiedEntries()
    {
        var used = 0;
        for (var i = 0; i < _keys.Length; i++) if (_counts[i] > 0) used++;
        return used;
    }

    /// <summary>Returns the cached outgoing radiance near a surface point, if enough samples back it.</summary>
    public bool TryGet(Vector3 position, Vector3 normal, out Vector3 radiance)
    {
        var key = KeyOf(position, normal);
        var slot = (int)(Hash(key) & (uint)_mask);
        for (var probe = 0; probe < Probes; probe++, slot = (slot + 1) & _mask)
        {
            if (_keys[slot] != key) continue;
            if (_counts[slot] < MinimumSamples) break;
            radiance = _values[slot];
            return true;
        }
        radiance = default;
        return false;
    }

    /// <summary>Folds one radiance estimate into the entry for a surface point.</summary>
    public void Add(Vector3 position, Vector3 normal, Vector3 radiance)
    {
        if (!float.IsFinite(radiance.X + radiance.Y + radiance.Z)) return;
        var key = KeyOf(position, normal);
        var start = (int)(Hash(key) & (uint)_mask);
        var slot = start;
        var weakest = start;
        var weakestCount = int.MaxValue;
        for (var probe = 0; probe < Probes; probe++, slot = (slot + 1) & _mask)
        {
            var count = _counts[slot];
            if (_keys[slot] == key)
            {
                var alpha = 1f / (Math.Min((int)count, WindowSamples) + 1);
                _values[slot] = Vector3.Lerp(_values[slot], radiance, alpha);
                if (count < ushort.MaxValue) _counts[slot] = (ushort)(count + 1);
                return;
            }
            if (count < weakestCount)
            {
                weakestCount = count;
                weakest = slot;
            }
        }
        // No matching entry among the probed slots: take over the one with the least evidence.
        _counts[weakest] = 0;
        _keys[weakest] = key;
        _values[weakest] = radiance;
        _counts[weakest] = 1;
    }

    /// <summary>Forgets everything (the lighting changed).</summary>
    public void Clear()
    {
        Array.Clear(_keys);
        Array.Clear(_counts);
        Array.Clear(_values);
    }

    /// <summary>Forgets every entry whose cell touches the box (a wall moved, a luminaire changed); returns how many were dropped.</summary>
    public int InvalidateBox(Vector3 min, Vector3 max)
    {
        var lowX = CellOf(min.X) - 1;
        var lowY = CellOf(min.Y) - 1;
        var lowZ = CellOf(min.Z) - 1;
        var highX = CellOf(max.X) + 1;
        var highY = CellOf(max.Y) + 1;
        var highZ = CellOf(max.Z) + 1;
        var removed = 0;
        for (var slot = 0; slot < _keys.Length; slot++)
        {
            var key = _keys[slot];
            if (key == 0 || _counts[slot] == 0) continue;
            var x = (int)(key & 0xFFFFF) - PositionOffset;
            var y = (int)(key >> PositionBits & 0xFFFFF) - PositionOffset;
            var z = (int)(key >> (2 * PositionBits) & 0xFFFFF) - PositionOffset;
            if (x < lowX || x > highX || y < lowY || y > highY || z < lowZ || z > highZ) continue;
            _counts[slot] = 0;
            _keys[slot] = 0;
            removed++;
        }
        return removed;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int CellOf(float coordinate) => (int)MathF.Floor(coordinate * _inverseCell);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ulong KeyOf(Vector3 position, Vector3 normal)
    {
        var x = (ulong)(Math.Clamp(CellOf(position.X) + PositionOffset, 0, (1 << PositionBits) - 1));
        var y = (ulong)(Math.Clamp(CellOf(position.Y) + PositionOffset, 0, (1 << PositionBits) - 1));
        var z = (ulong)(Math.Clamp(CellOf(position.Z) + PositionOffset, 0, (1 << PositionBits) - 1));
        var ax = MathF.Abs(normal.X);
        var ay = MathF.Abs(normal.Y);
        var az = MathF.Abs(normal.Z);
        // Dominant axis and its sign: six orientations are enough for the smooth light this cache stores.
        ulong orientation = ax >= ay && ax >= az ? (normal.X >= 0 ? 0UL : 1UL)
            : ay >= az ? (normal.Y >= 0 ? 2UL : 3UL)
            : (normal.Z >= 0 ? 4UL : 5UL);
        return x | y << PositionBits | z << (2 * PositionBits) | (orientation + 1) << (3 * PositionBits);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Hash(ulong key)
    {
        key ^= key >> 33;
        key *= 0xFF51AFD7ED558CCDUL;
        key ^= key >> 33;
        key *= 0xC4CEB9FE1A85EC53UL;
        key ^= key >> 33;
        return (uint)key;
    }
}
