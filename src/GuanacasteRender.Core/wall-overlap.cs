namespace Guanacaste3D.Core;

/// <summary>Detects walls laid on top of each other, which double their quantities and hide a drafting mistake.</summary>
public static class WallOverlap
{
    /// <summary>Overlap shorter than this, in meters, is a corner or a tee rather than one wall on another.</summary>
    public const double MinimumSharedLength = 0.05;

    /// <summary>Sine of the largest angle between two walls that still count as running the same way (about 3°).</summary>
    private const double ParallelSine = 0.05;

    /// <summary>
    /// Length in meters over which two walls occupy the same ground: they run the same way, their bodies intersect and
    /// they share more than a corner. Perpendicular or merely touching walls return zero.
    /// </summary>
    public static double SharedLength(Point2 startA, Point2 endA, double thicknessA, Point2 startB, Point2 endB, double thicknessB)
    {
        var length = startA.DistanceTo(endA);
        var otherLength = startB.DistanceTo(endB);
        if (length < 1e-9 || otherLength < 1e-9) return 0;
        var (dx, dy) = ((endA.X - startA.X) / length, (endA.Y - startA.Y) / length);
        var cross = Math.Abs(dx * (endB.Y - startB.Y) - dy * (endB.X - startB.X)) / otherLength;
        if (cross > ParallelSine) return 0;
        double Along(Point2 point) => (point.X - startA.X) * dx + (point.Y - startA.Y) * dy;
        double Across(Point2 point) => -(point.X - startA.X) * dy + (point.Y - startA.Y) * dx;
        var lateral = Math.Abs((Across(startB) + Across(endB)) / 2);
        if (lateral >= (thicknessA + thicknessB) / 2 - 0.005) return 0;
        var shared = Math.Min(length, Math.Max(Along(startB), Along(endB))) - Math.Max(0, Math.Min(Along(startB), Along(endB)));
        return shared >= MinimumSharedLength ? shared : 0;
    }

    /// <summary>The first wall on the level that a wall with this centerline and thickness would overlap, with the shared length.</summary>
    public static (Wall Wall, double Length)? Find(IEnumerable<Wall> walls, Guid levelId, Point2 start, Point2 end,
        double thickness, Guid? ignoreId = null)
    {
        foreach (var wall in walls)
        {
            if (wall.LevelId != levelId || wall.Id == ignoreId) continue;
            var shared = SharedLength(start, end, thickness, wall.Start, wall.End, wall.Thickness);
            if (shared > 0) return (wall, shared);
        }
        return null;
    }
}
