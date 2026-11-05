namespace Guanacaste3D.Core;

/// <summary>Subtracts exact flight access intervals from landing boundaries before generating guards.</summary>
internal static class StairLandingGuards
{
    private const double Tolerance = 1e-7;

    /// <summary>
    /// Returns every exposed edge segment, including the gap between switchback flights.
    /// Only mouths at the landing elevation remove guards, so nearby flight geometry cannot hide protection.
    /// </summary>
    internal static IEnumerable<(Point2 Start, Point2 End)> OpenEdges(IReadOnlyList<Point2> outline,
        IReadOnlyList<StairFlight> flights, double width, double elevation)
    {
        for (var index = 0; index < outline.Count; index++)
        {
            var start = outline[index];
            var end = outline[(index + 1) % outline.Count];
            var length = start.DistanceTo(end);
            if (length < Tolerance) continue;
            var direction = new Point2((end.X - start.X) / length, (end.Y - start.Y) / length);
            var access = new List<(double Start, double End)>();
            foreach (var flight in flights)
            {
                if (Math.Abs(flight.Bottom - elevation) < Tolerance)
                    AddAccess(access, start, direction, length, flight.Start, flight.End, width);
                if (Math.Abs(flight.Bottom + flight.Rise - elevation) < Tolerance)
                    AddAccess(access, start, direction, length, flight.End, flight.Start, width);
            }
            var covered = 0.0;
            foreach (var opening in access.OrderBy(interval => interval.Start))
            {
                if (opening.Start - covered >= 0.05 - Tolerance)
                    yield return (ArchitecturalShapes.Lerp(start, end, covered / length),
                        ArchitecturalShapes.Lerp(start, end, opening.Start / length));
                covered = Math.Max(covered, opening.End);
            }
            if (length - covered >= 0.05 - Tolerance)
                yield return (ArchitecturalShapes.Lerp(start, end, covered / length), end);
        }
    }

    /// <summary>Clips one perpendicular flight mouth to an edge when the mouth lies on its supporting line.</summary>
    private static void AddAccess(List<(double Start, double End)> access, Point2 edgeStart, Point2 edgeDirection,
        double edgeLength, Point2 mouth, Point2 otherEnd, double width)
    {
        var relative = new Point2(mouth.X - edgeStart.X, mouth.Y - edgeStart.Y);
        var separation = relative.X * edgeDirection.Y - relative.Y * edgeDirection.X;
        var flightLength = mouth.DistanceTo(otherEnd);
        if (Math.Abs(separation) > Tolerance || flightLength < Tolerance) return;
        var alignment = ((otherEnd.X - mouth.X) * edgeDirection.X +
                         (otherEnd.Y - mouth.Y) * edgeDirection.Y) / flightLength;
        if (Math.Abs(alignment) > Tolerance) return;
        var center = relative.X * edgeDirection.X + relative.Y * edgeDirection.Y;
        var from = Math.Clamp(center - width / 2, 0, edgeLength);
        var to = Math.Clamp(center + width / 2, 0, edgeLength);
        if (to - from > Tolerance) access.Add((from, to));
    }
}
