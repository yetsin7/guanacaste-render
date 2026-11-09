using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core;

/// <summary>
/// Rules for the closed plan profile that an extrusion sweeps. Profile coordinates are local to
/// its work plane, and the last vertex connects back to the first.
/// </summary>
public static class InPlaceProfileRules
{
    public const int MinimumVertices = 3;
    public const int MaximumVertices = 500;

    /// <summary>Rejects a profile that cannot bound a solid, naming the first problem found.</summary>
    public static void Validate(IReadOnlyList<Point2> profile)
    {
        if (profile.Count is < MinimumVertices or > MaximumVertices)
            throw new InvalidDataException("A profile needs between 3 and 500 vertices.");
        if (profile.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            throw new InvalidDataException("A profile vertex has a non-finite coordinate.");
        var tolerance = LengthTolerance(profile);
        for (var index = 0; index < profile.Count; index++)
        {
            var next = (index + 1) % profile.Count;
            if (profile[index].DistanceTo(profile[next]) <= tolerance)
                throw new InvalidDataException($"Profile vertices {index + 1} and {next + 1} coincide.");
        }
        if (Math.Abs(PolygonTriangulator.SignedDoubleArea(profile)) <= tolerance * tolerance)
            throw new InvalidDataException("The profile encloses no area; its vertices may be collinear.");
        if (FindSelfIntersection(profile, tolerance) is { } crossing)
            throw new InvalidDataException(
                $"Profile edges {crossing.First + 1} and {crossing.Second + 1} cross each other.");
    }

    /// <summary>Returns the loop in counter-clockwise order, so every caller sees the same winding.</summary>
    public static List<Point2> CounterClockwise(IReadOnlyList<Point2> profile)
    {
        var ordered = profile.ToList();
        if (PolygonTriangulator.SignedDoubleArea(ordered) < 0) ordered.Reverse();
        return ordered;
    }

    /// <summary>
    /// Distance below which two profile vertices count as the same point, in meters.
    /// </summary>
    public static double LengthTolerance(IReadOnlyList<Point2> profile)
    {
        // TODO(human): decide the tolerance policy. Trade-offs: a fixed 1 µm is simple but misjudges
        // a profile drawn at a very different scale; a fraction of the profile's bounding-box diagonal
        // scales with the drawing but needs a floor so degenerate loops are still rejected.
        return 1e-6;
    }

    /// <summary>Returns the first pair of non-adjacent edges that cross or touch, or null for a simple loop.</summary>
    public static (int First, int Second)? FindSelfIntersection(IReadOnlyList<Point2> loop, double tolerance)
    {
        var count = loop.Count;
        for (var first = 0; first < count; first++)
        {
            for (var second = first + 2; second < count; second++)
            {
                // The first and last edges share the closing vertex, so they are adjacent.
                if (first == 0 && second == count - 1) continue;
                if (SegmentsMeet(loop[first], loop[(first + 1) % count],
                        loop[second], loop[(second + 1) % count], tolerance))
                    return (first, second);
            }
        }
        return null;
    }

    /// <summary>Tests proper crossings and every touching case, including collinear overlap.</summary>
    private static bool SegmentsMeet(Point2 a, Point2 b, Point2 c, Point2 d, double tolerance)
    {
        var epsilon = tolerance * tolerance;
        var cdA = Sign(Orientation(c, d, a), epsilon);
        var cdB = Sign(Orientation(c, d, b), epsilon);
        var abC = Sign(Orientation(a, b, c), epsilon);
        var abD = Sign(Orientation(a, b, d), epsilon);
        if (cdA * cdB < 0 && abC * abD < 0) return true;
        return (cdA == 0 && OnSegment(c, d, a, tolerance)) ||
               (cdB == 0 && OnSegment(c, d, b, tolerance)) ||
               (abC == 0 && OnSegment(a, b, c, tolerance)) ||
               (abD == 0 && OnSegment(a, b, d, tolerance));
    }

    /// <summary>Returns twice the signed area of triangle (a, b, c); positive when counter-clockwise.</summary>
    private static double Orientation(Point2 a, Point2 b, Point2 c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static int Sign(double value, double epsilon) => value > epsilon ? 1 : value < -epsilon ? -1 : 0;

    /// <summary>Tests whether point p lies on segment ab within the tolerance.</summary>
    private static bool OnSegment(Point2 a, Point2 b, Point2 p, double tolerance) =>
        p.X >= Math.Min(a.X, b.X) - tolerance && p.X <= Math.Max(a.X, b.X) + tolerance &&
        p.Y >= Math.Min(a.Y, b.Y) - tolerance && p.Y <= Math.Max(a.Y, b.Y) + tolerance;
}
