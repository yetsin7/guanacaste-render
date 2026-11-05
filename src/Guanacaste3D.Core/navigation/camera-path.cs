using System.Numerics;

namespace Guanacaste3D.Core.Navigation;

/// <summary>A recorded camera mark: where the camera stands and looks, and how long to take to get here from the previous mark.</summary>
public sealed record CameraMark(Vector3 Position, Vector3 Target, float FieldOfViewDegrees, float SecondsFromPrevious,
    string Name = "");

/// <summary>
/// A walk-through path through camera marks, sampled smoothly for video. Positions and look-at points follow a
/// Catmull–Rom spline, so the camera glides through the marks instead of turning sharply at each one, and an optional
/// ease keeps starts and stops gentle. Rendering a path produces one frame per sample.
/// </summary>
public sealed class CameraPath
{
    private readonly List<CameraMark> _marks = [];

    public IReadOnlyList<CameraMark> Marks => _marks;

    /// <summary>Total length in seconds; the first mark is the start, so its own duration does not count.</summary>
    public float Duration => _marks.Skip(1).Sum(mark => Math.Max(0.05f, mark.SecondsFromPrevious));

    public bool Ease { get; set; } = true;

    public void Add(CameraMark mark)
    {
        if (!IsFinite(mark.Position) || !IsFinite(mark.Target)) throw new ArgumentException("A camera mark needs finite coordinates.");
        _marks.Add(Clamped(mark));
    }

    public void RemoveAt(int index) => _marks.RemoveAt(index);

    public void Clear() => _marks.Clear();

    public void Move(int from, int to)
    {
        var mark = _marks[from];
        _marks.RemoveAt(from);
        _marks.Insert(Math.Clamp(to, 0, _marks.Count), mark);
    }

    public void Replace(int index, CameraMark mark) => _marks[index] = Clamped(mark);

    private static CameraMark Clamped(CameraMark mark) =>
        mark with { SecondsFromPrevious = Math.Clamp(mark.SecondsFromPrevious, 0.05f, 600f) };

    /// <summary>Number of frames a render at <paramref name="framesPerSecond"/> produces.</summary>
    public int FrameCount(int framesPerSecond) =>
        _marks.Count < 2 ? _marks.Count : Math.Max(2, (int)MathF.Ceiling(Duration * Math.Max(1, framesPerSecond)) + 1);

    /// <summary>The camera at time <paramref name="seconds"/> along the path.</summary>
    public (Vector3 Position, Vector3 Target, float FieldOfView) Sample(float seconds)
    {
        if (_marks.Count == 0) throw new InvalidOperationException("The path has no marks.");
        if (_marks.Count == 1) return (_marks[0].Position, _marks[0].Target, _marks[0].FieldOfViewDegrees);

        var t = Math.Clamp(seconds, 0, Duration);
        var elapsed = 0f;
        for (var i = 1; i < _marks.Count; i++)
        {
            var length = Math.Max(0.05f, _marks[i].SecondsFromPrevious);
            if (t > elapsed + length && i != _marks.Count - 1)
            {
                elapsed += length;
                continue;
            }
            var segment = i - 1;
            var local = Math.Clamp((t - elapsed) / length, 0f, 1f);
            if (Ease) local = local * local * (3 - 2 * local);
            var p0 = _marks[Math.Max(0, segment - 1)];
            var p1 = _marks[segment];
            var p2 = _marks[segment + 1];
            var p3 = _marks[Math.Min(_marks.Count - 1, segment + 2)];
            return (CatmullRom(p0.Position, p1.Position, p2.Position, p3.Position, local),
                CatmullRom(p0.Target, p1.Target, p2.Target, p3.Target, local),
                p1.FieldOfViewDegrees + (p2.FieldOfViewDegrees - p1.FieldOfViewDegrees) * local);
        }
        var last = _marks[^1];
        return (last.Position, last.Target, last.FieldOfViewDegrees);
    }

    /// <summary>The camera for one frame of a render at the given frame rate.</summary>
    public (Vector3 Position, Vector3 Target, float FieldOfView) Frame(int index, int framesPerSecond)
    {
        var count = FrameCount(framesPerSecond);
        return count <= 1 ? Sample(0) : Sample(Duration * Math.Clamp(index, 0, count - 1) / (count - 1));
    }

    /// <summary>Uniform Catmull–Rom spline through p1 and p2, with p0 and p3 shaping the tangents.</summary>
    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return 0.5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (3 * p1 - p0 - 3 * p2 + p3) * t3);
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X + v.Y + v.Z);
}
