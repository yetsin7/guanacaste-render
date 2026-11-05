namespace Guanacaste3D.Core;

/// <summary>Where an opening lands on a host wall: the wall, its offset from the wall start, and whether it fits there.</summary>
public readonly record struct OpeningHost(Guid WallId, double Offset, bool Fits);

/// <summary>
/// Hosting rules for doors and windows while they are dragged. An opening only exists inside a
/// wall, so a candidate position is always resolved onto the nearest wall of its level, or
/// reported as in the air when no wall is close enough.
/// </summary>
public static class OpeningHosting
{
    /// <summary>Clearance kept between an opening and the ends of its host wall or another opening. Mirrors WallGeometry.Validate.</summary>
    public const double EndClearance = 0.05;

    /// <summary>Returns the plan point of an opening's center on its current host wall.</summary>
    public static Point2 Center(Wall wall, WallOpening opening)
    {
        var axisX = (wall.End.X - wall.Start.X) / wall.Length;
        var axisY = (wall.End.Y - wall.Start.Y) / wall.Length;
        return new Point2(wall.Start.X + axisX * opening.Offset, wall.Start.Y + axisY * opening.Offset);
    }

    /// <summary>
    /// Resolves a candidate center point to a host wall on the opening's level. Returns null when
    /// the nearest wall face is farther than <paramref name="captureDistance"/>, meaning the opening
    /// would float in the air. The offset is clamped so the whole opening stays inside the wall.
    /// </summary>
    public static OpeningHost? Resolve(WallOpening opening, Point2 candidate, IReadOnlyList<Wall> walls,
        IReadOnlyList<WallOpening> openings, double captureDistance)
    {
        var current = walls.FirstOrDefault(wall => wall.Id == opening.WallId)
            ?? throw new ArgumentException("The opening has no host wall.", nameof(opening));
        Wall? best = null;
        var bestGap = double.PositiveInfinity;
        var bestAlong = 0.0;
        foreach (var wall in walls)
        {
            if (wall.LevelId != current.LevelId || wall.Length <= 0) continue;
            var axisX = (wall.End.X - wall.Start.X) / wall.Length;
            var axisY = (wall.End.Y - wall.Start.Y) / wall.Length;
            var along = (candidate.X - wall.Start.X) * axisX + (candidate.Y - wall.Start.Y) * axisY;
            var clamped = Math.Clamp(along, 0, wall.Length);
            var nearestX = wall.Start.X + axisX * clamped;
            var nearestY = wall.Start.Y + axisY * clamped;
            var gap = Math.Sqrt(Math.Pow(candidate.X - nearestX, 2) + Math.Pow(candidate.Y - nearestY, 2))
                      - wall.Thickness / 2;
            if (gap < bestGap)
            {
                best = wall;
                bestGap = gap;
                bestAlong = along;
            }
        }
        if (best is null || bestGap > captureDistance) return null;

        // The small epsilon keeps a clamped opening on the valid side of the clearance after rounding.
        const double epsilon = 1e-9;
        var lower = opening.Width / 2 + EndClearance + epsilon;
        var upper = best.Length - opening.Width / 2 - EndClearance - epsilon;
        var offset = upper < lower ? best.Length / 2 : Math.Clamp(bestAlong, lower, upper);
        var begin = offset - opening.Width / 2;
        var end = offset + opening.Width / 2;
        var fits = begin >= EndClearance && end <= best.Length - EndClearance &&
                   opening.Sill + opening.Height <= best.Height - EndClearance &&
                   openings.Where(other => other.WallId == best.Id && other.Id != opening.Id)
                       .All(other => other.End + EndClearance <= begin || end + EndClearance <= other.Begin);
        return new OpeningHost(best.Id, offset, fits);
    }
}
