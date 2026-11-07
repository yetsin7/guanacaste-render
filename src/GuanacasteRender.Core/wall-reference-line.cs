namespace Guanacaste3D.Core;

/// <summary>
/// Changes wall thicknesses while the plane each wall was placed by stays where it is, then re-closes the simple
/// corners the shift would otherwise leave open. A wall's <c>Start</c> and <c>End</c> are its centerline, so a wall
/// placed by a face has to move its centerline sideways by half the thickness change to keep that face fixed.
/// </summary>
public static class WallReferenceLine
{
    /// <summary>Distance a centerline must move to its left when a wall of one thickness becomes another.</summary>
    public static double CenterlineShift(WallLocationReference reference, double oldThickness, double newThickness) =>
        reference switch
        {
            // The exterior face is the right-hand one, so a thicker wall grows toward the left.
            WallLocationReference.ExteriorFace => (newThickness - oldThickness) / 2,
            WallLocationReference.InteriorFace => (oldThickness - newThickness) / 2,
            _ => 0
        };

    /// <summary>
    /// Gives each wall in <paramref name="thicknesses"/> its new thickness. Walls tied to a grid axis keep their
    /// centerline on that axis. Where exactly two walls met before, both ends move to the crossing of the new
    /// centerlines, so the corner stays joined, and openings stay where they were in the model.
    /// </summary>
    internal static void Resize(BuildingDocument document, IReadOnlyDictionary<Guid, double> thicknesses)
    {
        var walls = document.Walls;
        var before = WallJoinIndex.Build(walls);
        var indexById = IndexOf(walls);

        var changed = new List<Guid>();
        foreach (var (id, thickness) in thicknesses)
        {
            if (!indexById.TryGetValue(id, out var index) || walls[index].Thickness == thickness) continue;
            var wall = walls[index];
            var shift = wall.GridAxisId is null && wall.Length > 1e-9
                ? CenterlineShift(wall.LocationReference, wall.Thickness, thickness)
                : 0;
            var (normalX, normalY) = LeftNormal(wall);
            walls[index] = wall with
            {
                Thickness = thickness,
                Start = new Point2(wall.Start.X + normalX * shift, wall.Start.Y + normalY * shift),
                End = new Point2(wall.End.X + normalX * shift, wall.End.Y + normalY * shift)
            };
            changed.Add(id);
        }

        ReconnectCorners(document, indexById, before, changed, AcceptsCornerSlide);
    }

    /// <summary>
    /// After some walls moved, closes the corners and tees they had: where exactly two walls met, both ends go to
    /// the crossing of their centerlines; a tee end slides along its own line to its host. <paramref name="accepts"/>
    /// receives the slide and the larger thickness and decides whether a corner may follow; null always accepts.
    /// </summary>
    internal static void ReconnectCorners(BuildingDocument document, Dictionary<Guid, int> indexById,
        WallJoinIndex before, IReadOnlyCollection<Guid> changed, Func<double, double, bool>? accepts)
    {
        var walls = document.Walls;
        var closed = new HashSet<(Guid, WallEnd, Guid, WallEnd)>();
        foreach (var id in changed)
        foreach (var end in new[] { WallEnd.Start, WallEnd.End })
        {
            if (before.JoinAt(id, end) is not { } join) continue;
            var key = id.CompareTo(join.Other.Id) < 0 ? (id, end, join.Other.Id, join.OtherEnd)
                : (join.Other.Id, join.OtherEnd, id, end);
            if (!closed.Add(key)) continue;

            var wall = walls[indexById[id]];
            var other = walls[indexById[join.Other.Id]];
            // Reconnecting a moved wall must not drag a pinned neighbour. Leave the corner
            // disconnected instead; pinning protects geometry even from indirect edits.
            if (wall.Pinned || other.Pinned) continue;
            if (WallPlacementGeometry.CenterlineIntersection(
                    new WallCenterline(wall.Start, wall.End), new WallCenterline(other.Start, other.End)) is not { } corner)
                continue;
            var slide = Math.Max(corner.DistanceTo(EndPoint(wall, end)), corner.DistanceTo(EndPoint(other, join.OtherEnd)));
            if (slide <= WallJoinIndex.Tolerance || !Accepted(slide, Math.Max(wall.Thickness, other.Thickness)))
                continue;
            MoveEnd(document, indexById, wall.Id, end, corner);
            MoveEnd(document, indexById, other.Id, join.OtherEnd, corner);
        }

        // A tee follows the wall it rests on: its end slides along its own line to the host's new centerline.
        var changedSet = changed.ToHashSet();
        foreach (var (stemId, stemEnd, host) in before.Tees.ToArray())
        {
            if (!changedSet.Contains(stemId) && !changedSet.Contains(host.Id)) continue;
            var stem = walls[indexById[stemId]];
            // A tee end may follow its host only when the stem is not pinned.
            if (stem.Pinned) continue;
            var current = walls[indexById[host.Id]];
            if (WallPlacementGeometry.CenterlineIntersection(
                    new WallCenterline(stem.Start, stem.End), new WallCenterline(current.Start, current.End)) is not { } meeting)
                continue;
            var slide = meeting.DistanceTo(EndPoint(stem, stemEnd));
            if (slide > WallJoinIndex.Tolerance && Accepted(slide, Math.Max(stem.Thickness, current.Thickness)))
                MoveEnd(document, indexById, stemId, stemEnd, meeting);
        }

        bool Accepted(double slide, double thickness) => accepts is null || accepts(slide, thickness);
    }

    /// <summary>Maps each wall id to its position in the document's wall list.</summary>
    internal static Dictionary<Guid, int> IndexOf(List<Wall> walls)
    {
        var indexById = new Dictionary<Guid, int>(walls.Count);
        for (var index = 0; index < walls.Count; index++) indexById[walls[index].Id] = index;
        return indexById;
    }

    /// <summary>
    /// Decides whether a corner may slide along its walls to follow a thickness change. A very acute corner
    /// would send the end far along the wall, so it stays where the shift left it and loses its join instead.
    /// </summary>
    private static bool AcceptsCornerSlide(double slide, double thickness)
    {
        // TODO(human): choose the rule. Starting point: allow a slide of up to four wall thicknesses, which
        // matches the miter limit in WallJoinIndex. Consider whether it should depend on the thickness change.
        return slide <= 4 * thickness;
    }

    /// <summary>Moves one end of a wall; an opening measured from the start keeps its place in the model.</summary>
    internal static void MoveEnd(BuildingDocument document, Dictionary<Guid, int> indexById, Guid wallId, WallEnd end, Point2 point)
    {
        var index = indexById[wallId];
        var wall = document.Walls[index];
        if (end == WallEnd.End)
        {
            document.Walls[index] = wall with { End = point };
            return;
        }

        var (directionX, directionY) = ((wall.End.X - wall.Start.X) / wall.Length, (wall.End.Y - wall.Start.Y) / wall.Length);
        var slide = (point.X - wall.Start.X) * directionX + (point.Y - wall.Start.Y) * directionY;
        document.Walls[index] = wall with { Start = point };
        for (var opening = 0; opening < document.Openings.Count; opening++)
            if (document.Openings[opening].WallId == wallId)
                document.Openings[opening] = document.Openings[opening] with { Offset = document.Openings[opening].Offset - slide };
    }

    private static Point2 EndPoint(Wall wall, WallEnd end) => end == WallEnd.Start ? wall.Start : wall.End;

    /// <summary>Returns the unit normal on the left of the start-to-end direction.</summary>
    private static (double X, double Y) LeftNormal(Wall wall)
    {
        if (wall.Length <= 1e-9) return (0, 0);
        return (-(wall.End.Y - wall.Start.Y) / wall.Length, (wall.End.X - wall.Start.X) / wall.Length);
    }
}
