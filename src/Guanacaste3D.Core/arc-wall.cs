namespace Guanacaste3D.Core;

/// <summary>The circle an arc wall follows: where it is centered, how big it is and which part of it the wall covers.</summary>
public readonly record struct ArcFit(Point2 Center, double Radius, double StartAngle, double SweepAngle)
{
    /// <summary>Length along the arc, in meters.</summary>
    public double Length => Radius * Math.Abs(SweepAngle);
}

/// <summary>
/// Geometry of curved walls. A curved wall is kept as a chain of short straight walls that follow the arc, so joins,
/// rooms, openings, quantities and every export keep working on it, and the chain is fine enough that it never leaves
/// the true arc by more than a few millimeters.
/// </summary>
public static class ArcWallGeometry
{
    /// <summary>
    /// How far, in meters, a straight piece may stray from the true arc at its middle. Two centimeters keeps the pieces of
    /// an ordinary arc (a radius of ten meters) about 1.3 m long, so a door or window still fits in one piece.
    /// </summary>
    public const double DefaultTolerance = 0.02;

    /// <summary>Largest radius accepted. Beyond it the three points are as good as a straight line.</summary>
    public const double MaximumRadius = 5000;

    /// <summary>Most pieces one arc is divided into.</summary>
    public const int MaximumSegments = 360;

    /// <summary>
    /// Finds the arc that goes from <paramref name="start"/> to <paramref name="end"/> through <paramref name="through"/>.
    /// Returns null when the three points lie on a line, or the circle through them is too large to tell from one.
    /// </summary>
    public static ArcFit? Fit(Point2 start, Point2 end, Point2 through)
    {
        // Work relative to the start so far-away survey coordinates keep their precision.
        var bx = end.X - start.X;
        var by = end.Y - start.Y;
        var cx = through.X - start.X;
        var cy = through.Y - start.Y;
        var determinant = 2 * (bx * cy - by * cx);
        if (!double.IsFinite(determinant) || Math.Abs(determinant) < 1e-12) return null;
        var bSquared = bx * bx + by * by;
        var cSquared = cx * cx + cy * cy;
        var centerX = (cy * bSquared - by * cSquared) / determinant;
        var centerY = (bx * cSquared - cx * bSquared) / determinant;
        var radius = Math.Sqrt(centerX * centerX + centerY * centerY);
        if (!double.IsFinite(radius) || radius > MaximumRadius || radius < 1e-6) return null;

        var startAngle = Math.Atan2(-centerY, -centerX);
        var endAngle = Math.Atan2(by - centerY, bx - centerX);
        var throughAngle = Math.Atan2(cy - centerY, cx - centerX);
        // Counter-clockwise angles from the start; the arc is the way round that passes through the third point.
        var counterClockwiseToEnd = Normalize(endAngle - startAngle);
        var counterClockwiseToThrough = Normalize(throughAngle - startAngle);
        var sweep = counterClockwiseToThrough <= counterClockwiseToEnd ? counterClockwiseToEnd : counterClockwiseToEnd - 2 * Math.PI;
        return new ArcFit(new Point2(start.X + centerX, start.Y + centerY), radius, startAngle, sweep);
    }

    /// <summary>
    /// The corners of the chain that follows the arc, from <paramref name="start"/> to <paramref name="end"/> exactly.
    /// Each piece stays within <paramref name="tolerance"/> of the true arc, with at least two pieces.
    /// </summary>
    public static IReadOnlyList<Point2> Points(ArcFit arc, Point2 start, Point2 end, double tolerance = DefaultTolerance)
    {
        var step = 2 * Math.Acos(Math.Clamp(1 - tolerance / arc.Radius, -1, 1));
        var segments = step > 1e-9 ? (int)Math.Ceiling(Math.Abs(arc.SweepAngle) / step) : MaximumSegments;
        segments = Math.Clamp(segments, 2, MaximumSegments);
        var points = new Point2[segments + 1];
        points[0] = start;
        points[segments] = end;
        for (var index = 1; index < segments; index++)
        {
            var angle = arc.StartAngle + arc.SweepAngle * index / segments;
            points[index] = new Point2(arc.Center.X + arc.Radius * Math.Cos(angle), arc.Center.Y + arc.Radius * Math.Sin(angle));
        }
        return points;
    }

    private static double Normalize(double angle)
    {
        var turn = 2 * Math.PI;
        var result = angle % turn;
        return result < 0 ? result + turn : result;
    }
}

/// <summary>Gives the copies of a curved wall an identity of their own, so deleting a copy never deletes the original.</summary>
internal static class CurveGroups
{
    /// <summary>Returns the wall with its curve identity replaced through <paramref name="map"/>, one new identity per source curve.</summary>
    public static Wall Remap(Wall wall, Dictionary<Guid, Guid> map)
    {
        if (wall.CurveGroupId is not { } group) return wall;
        if (!map.TryGetValue(group, out var replacement)) map[group] = replacement = Guid.NewGuid();
        return wall with { CurveGroupId = replacement };
    }
}

public sealed partial class DocumentSession
{
    /// <summary>
    /// Adds a curved wall that goes from <paramref name="start"/> to <paramref name="end"/> through
    /// <paramref name="through"/>, as one undoable step. It is a chain of straight walls that follow the arc, joined at
    /// their ends and sharing one curve identity, so deleting or moving one moves the whole curved wall. Returns the
    /// walls of the chain in order.
    /// </summary>
    public IReadOnlyList<Guid> AddArcWall(Guid levelId, Point2 start, Point2 end, Point2 through, double height = 3, Guid? wallTypeId = null)
    {
        var typeId = wallTypeId ?? BuiltInWallTypes.Basic200Id;
        var type = Document.WallTypes.FirstOrDefault(candidate => candidate.Id == typeId)
            ?? throw new InvalidDataException("The selected wall type does not exist.");
        if (ArcWallGeometry.Fit(start, end, through) is not { } arc)
            throw new InvalidDataException("The three points do not form an arc.");
        if (arc.Radius < type.Thickness)
            throw new InvalidDataException("The arc is too tight for the thickness of the wall.");

        var points = ArcWallGeometry.Points(arc, start, end);
        var groupId = Guid.NewGuid();
        var ids = new List<Guid>();
        Commit("Transaction.AddArcWall", document =>
        {
            for (var index = 0; index < points.Count - 1; index++)
            {
                var id = Guid.NewGuid();
                ids.Add(id);
                document.Walls.Add(new Wall(id, levelId, points[index], points[index + 1], type.Thickness, height)
                {
                    WallTypeId = typeId,
                    GridAxisAssociationDisabled = true,
                    CurveGroupId = groupId,
                    CurveRadius = arc.Radius
                });
            }
            RefreshRoomBoundariesIfNeeded(document, levelId);
        });
        return ids;
    }

    /// <summary>The walls that make up the same curved wall as <paramref name="wallId"/>, or just that wall when it is straight.</summary>
    public IReadOnlyList<Guid> CurveMembers(Guid wallId)
    {
        var wall = Document.Walls.FirstOrDefault(candidate => candidate.Id == wallId);
        if (wall?.CurveGroupId is not { } group) return wall is null ? [] : [wallId];
        return Document.Walls.Where(candidate => candidate.CurveGroupId == group).Select(candidate => candidate.Id).ToArray();
    }
}
