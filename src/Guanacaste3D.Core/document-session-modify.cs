namespace Guanacaste3D.Core;

/// <summary>Editing commands for walls: mirror, offset, array, split, trim, align, pin and match properties.</summary>
public sealed partial class DocumentSession
{
    /// <summary>Most members a single array command may create, so a typo cannot flood the project.</summary>
    public const int MaximumArrayCount = 500;

    /// <summary>
    /// Mirrors walls and their hosted openings about the line through two points. Mirroring swaps a wall's start and
    /// end so its left and right faces keep their meaning, and flips the hinge of doors. With
    /// <paramref name="copy"/> the originals stay and the mirrored walls are new.
    /// </summary>
    public IReadOnlyList<Guid> MirrorWalls(IReadOnlyCollection<Guid> wallIds, Point2 axisStart, Point2 axisEnd, bool copy = true)
    {
        if (axisStart.DistanceTo(axisEnd) < 0.05) throw new InvalidDataException("The mirror axis is too short.");
        var created = new List<Guid>();
        Commit("Transaction.MirrorWalls", document =>
        {
            var ids = RequireWalls(document, wallIds);
            if (!copy) foreach (var id in ids) EnsureNotPinned(document, id);
            var curves = new Dictionary<Guid, Guid>();
            foreach (var id in ids)
            {
                var source = document.Walls.Single(wall => wall.Id == id);
                var newStart = WallEditGeometry.Reflect(source.End, axisStart, axisEnd);
                var newEnd = WallEditGeometry.Reflect(source.Start, axisStart, axisEnd);
                var mirrored = source with
                {
                    Start = newStart, End = newEnd, StartJoin = source.EndJoin, EndJoin = source.StartJoin,
                    GridAxisId = null, GridAxisAssociationDisabled = true
                };
                created.Add(PlaceWall(document, source, mirrored, copy, opening =>
                    opening with { Offset = source.Length - opening.Offset, FlipHand = !opening.FlipHand }, curves));
            }
            foreach (var level in ids.Select(id => document.Walls.Single(wall => wall.Id == id).LevelId).Distinct())
                RefreshRoomBoundariesIfNeeded(document, level);
        });
        return created;
    }

    /// <summary>
    /// Moves or copies walls sideways by a distance; positive goes to the left looking from start to end. A moved
    /// wall keeps its corners by stretching the walls it meets.
    /// </summary>
    public IReadOnlyList<Guid> OffsetWalls(IReadOnlyCollection<Guid> wallIds, double distance, bool copy)
    {
        if (!double.IsFinite(distance) || Math.Abs(distance) > 100000 || Math.Abs(distance) < 1e-6)
            throw new InvalidDataException("The offset distance is invalid.");
        var created = new List<Guid>();
        Commit("Transaction.OffsetWalls", document =>
        {
            var ids = RequireWalls(document, wallIds);
            if (!copy) foreach (var id in ids) EnsureNotPinned(document, id);
            var before = WallJoinIndex.Build(document.Walls);
            var curves = new Dictionary<Guid, Guid>();
            foreach (var id in ids)
            {
                var source = document.Walls.Single(wall => wall.Id == id);
                var (normalX, normalY) = WallEditGeometry.LeftNormal(source);
                var moved = source with
                {
                    Start = new Point2(source.Start.X + normalX * distance, source.Start.Y + normalY * distance),
                    End = new Point2(source.End.X + normalX * distance, source.End.Y + normalY * distance),
                    GridAxisId = null, GridAxisAssociationDisabled = true
                };
                created.Add(PlaceWall(document, source, moved, copy, opening => opening, curves));
            }
            if (!copy)
                WallReferenceLine.ReconnectCorners(document, WallReferenceLine.IndexOf(document.Walls), before, ids, null);
            foreach (var level in ids.Select(id => document.Walls.Single(wall => wall.Id == id).LevelId).Distinct())
                RefreshRoomBoundariesIfNeeded(document, level);
        });
        return created;
    }

    /// <summary>
    /// Makes copies of walls and their openings in a line: <paramref name="count"/> members in all, the original
    /// included, each one step further along <c>(deltaX, deltaY)</c>.
    /// </summary>
    public IReadOnlyList<Guid> ArrayWallsLinear(IReadOnlyCollection<Guid> wallIds, int count, double deltaX, double deltaY)
    {
        if (count < 2 || count > MaximumArrayCount) throw new InvalidDataException("The array count is invalid.");
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY) || Math.Abs(deltaX) + Math.Abs(deltaY) < 1e-6 ||
            Math.Abs(deltaX) > 100000 || Math.Abs(deltaY) > 100000)
            throw new InvalidDataException("The array spacing is invalid.");
        return ArrayWalls(wallIds, count, (point, member) => new Point2(point.X + deltaX * member, point.Y + deltaY * member));
    }

    /// <summary>
    /// Makes copies of walls and their openings around a center: <paramref name="count"/> members in all, each
    /// <paramref name="stepDegrees"/> further around, counter-clockwise when positive.
    /// </summary>
    public IReadOnlyList<Guid> ArrayWallsRadial(IReadOnlyCollection<Guid> wallIds, int count, Point2 center, double stepDegrees)
    {
        if (count < 2 || count > MaximumArrayCount) throw new InvalidDataException("The array count is invalid.");
        if (!double.IsFinite(stepDegrees) || Math.Abs(stepDegrees) < 1e-6 || Math.Abs(stepDegrees) > 360)
            throw new InvalidDataException("The array angle is invalid.");
        return ArrayWalls(wallIds, count, (point, member) => WallEditGeometry.Rotate(point, center, stepDegrees * member));
    }

    private IReadOnlyList<Guid> ArrayWalls(IReadOnlyCollection<Guid> wallIds, int count, Func<Point2, int, Point2> place)
    {
        var created = new List<Guid>();
        Commit("Transaction.ArrayWalls", document =>
        {
            var ids = RequireWalls(document, wallIds);
            if (document.Walls.Count + ids.Count * (count - 1) > 100000)
                throw new InvalidDataException("The document exceeds the supported element limits.");
            var curvesByMember = new Dictionary<int, Dictionary<Guid, Guid>>();
            foreach (var id in ids)
            {
                var source = document.Walls.Single(wall => wall.Id == id);
                for (var member = 1; member < count; member++)
                {
                    if (!curvesByMember.TryGetValue(member, out var curves)) curvesByMember[member] = curves = [];
                    var copy = source with
                    {
                        Start = place(source.Start, member), End = place(source.End, member),
                        GridAxisId = null, GridAxisAssociationDisabled = true, Pinned = false
                    };
                    created.Add(PlaceWall(document, source, copy, true, opening => opening, curves));
                }
            }
            foreach (var level in ids.Select(id => document.Walls.Single(wall => wall.Id == id).LevelId).Distinct())
                RefreshRoomBoundariesIfNeeded(document, level);
        });
        return created;
    }

    /// <summary>
    /// Splits a wall in two at the point of its centerline nearest to <paramref name="point"/>. The first part keeps
    /// the wall's identity; the openings go with the part that holds them. Returns the new second part.
    /// </summary>
    public Guid SplitWall(Guid wallId, Point2 point)
    {
        var secondId = Guid.NewGuid();
        Commit("Transaction.SplitWall", document =>
        {
            EnsureNotPinned(document, wallId);
            var index = document.Walls.FindIndex(wall => wall.Id == wallId);
            if (index < 0) throw new InvalidDataException("The wall does not exist.");
            var wall = document.Walls[index];
            var station = WallEditGeometry.Station(wall, point);
            if (station < 0.05 || station > wall.Length - 0.05) throw new InvalidDataException("The split point is too close to a wall end.");
            var cut = WallEditGeometry.PointAt(wall, station);
            var hosted = document.Openings.Where(opening => opening.WallId == wallId).ToArray();
            if (hosted.Any(opening => opening.Begin < station + 0.05 && opening.End > station - 0.05))
                throw new InvalidDataException("An opening crosses the split point.");

            document.Walls[index] = wall with
            {
                End = cut, EndJoin = WallJoinSetting.Miter,
                TopProfile = wall.TopProfile is { } profile ? WallProfiles.KeepBefore(profile, station) : null
            };
            document.Walls.Add(wall with
            {
                Id = secondId, Start = cut, StartJoin = WallJoinSetting.Miter, Pinned = false,
                TopProfile = wall.TopProfile is { } rest ? WallProfiles.KeepAfter(rest, station, wall.Length - station) : null
            });
            foreach (var opening in hosted.Where(opening => opening.Begin >= station))
            {
                var at = document.Openings.FindIndex(candidate => candidate.Id == opening.Id);
                document.Openings[at] = opening with { WallId = secondId, Offset = opening.Offset - station };
            }
            RefreshRoomBoundariesIfNeeded(document, wall.LevelId);
        });
        return secondId;
    }

    /// <summary>
    /// Trims or extends two walls to the corner where their centerlines cross. The part of each wall that holds
    /// its pick point is kept; the end on the other side of the corner moves to it.
    /// </summary>
    public void TrimToCorner(Guid firstId, Point2 firstPick, Guid secondId, Point2 secondPick)
    {
        if (firstId == secondId) throw new InvalidDataException("Two different walls are needed.");
        Commit("Transaction.TrimWalls", document =>
        {
            EnsureNotPinned(document, firstId);
            EnsureNotPinned(document, secondId);
            var first = document.Walls.FirstOrDefault(wall => wall.Id == firstId);
            var second = document.Walls.FirstOrDefault(wall => wall.Id == secondId);
            if (first is null || second is null) throw new InvalidDataException("The wall does not exist.");
            if (first.LevelId != second.LevelId) throw new InvalidDataException("The walls are on different levels.");
            if (WallPlacementGeometry.CenterlineIntersection(
                    new WallCenterline(first.Start, first.End), new WallCenterline(second.Start, second.End)) is not { } corner)
                throw new InvalidDataException("The walls are parallel and never meet.");

            var indexById = WallReferenceLine.IndexOf(document.Walls);
            foreach (var (wall, pick) in new[] { (first, firstPick), (second, secondPick) })
            {
                var keepsStart = WallEditGeometry.Station(wall, pick) < WallEditGeometry.Station(wall, corner);
                // The farther end belongs to the part that is not kept, unless the kept part is already past the corner.
                WallReferenceLine.MoveEnd(document, indexById, wall.Id, keepsStart ? WallEnd.End : WallEnd.Start, corner);
            }
            RefreshRoomBoundariesIfNeeded(document, first.LevelId);
        });
    }

    /// <summary>
    /// Moves a wall sideways, parallel to a reference wall, until one of its faces lies on one of the reference
    /// wall's faces. Sides are -1 for the right face, 0 for the centerline and 1 for the left face. The walls it
    /// meets stretch to follow.
    /// </summary>
    public void AlignWall(Guid movingId, int movingSide, Guid referenceId, int referenceSide)
    {
        if (movingId == referenceId) throw new InvalidDataException("Two different walls are needed.");
        if (movingSide is < -1 or > 1 || referenceSide is < -1 or > 1) throw new InvalidDataException("The wall face is invalid.");
        Commit("Transaction.AlignWall", document =>
        {
            EnsureNotPinned(document, movingId);
            var moving = document.Walls.FirstOrDefault(wall => wall.Id == movingId);
            var reference = document.Walls.FirstOrDefault(wall => wall.Id == referenceId);
            if (moving is null || reference is null) throw new InvalidDataException("The wall does not exist.");
            if (moving.LevelId != reference.LevelId) throw new InvalidDataException("The walls are on different levels.");
            var (movingNormalX, movingNormalY) = WallEditGeometry.LeftNormal(moving);
            var (referenceNormalX, referenceNormalY) = WallEditGeometry.LeftNormal(reference);
            if (Math.Abs(movingNormalX * referenceNormalY - movingNormalY * referenceNormalX) > 1e-6)
                throw new InvalidDataException("Only parallel walls can be aligned.");

            var target = new Point2(
                reference.Start.X + referenceNormalX * referenceSide * reference.Thickness / 2,
                reference.Start.Y + referenceNormalY * referenceSide * reference.Thickness / 2);
            var current = new Point2(
                moving.Start.X + movingNormalX * movingSide * moving.Thickness / 2,
                moving.Start.Y + movingNormalY * movingSide * moving.Thickness / 2);
            var distance = (target.X - current.X) * movingNormalX + (target.Y - current.Y) * movingNormalY;
            if (Math.Abs(distance) < 1e-9) return;

            var before = WallJoinIndex.Build(document.Walls);
            var index = document.Walls.FindIndex(wall => wall.Id == movingId);
            document.Walls[index] = moving with
            {
                Start = new Point2(moving.Start.X + movingNormalX * distance, moving.Start.Y + movingNormalY * distance),
                End = new Point2(moving.End.X + movingNormalX * distance, moving.End.Y + movingNormalY * distance),
                GridAxisId = null, GridAxisAssociationDisabled = true
            };
            WallReferenceLine.ReconnectCorners(document, WallReferenceLine.IndexOf(document.Walls), before, [movingId], null);
            RefreshRoomBoundariesIfNeeded(document, moving.LevelId);
        });
    }

    /// <summary>
    /// Flips walls so their left and right faces trade places, without moving anything in the model. The ends swap,
    /// so the sides each finish and each placement face refer to are the other way round; doors and windows keep
    /// their place, their hinge and the side they swing to.
    /// </summary>
    public void FlipWalls(IReadOnlyCollection<Guid> wallIds) =>
        Commit("Transaction.FlipWalls", document =>
        {
            var ids = RequireWalls(document, wallIds);
            foreach (var id in ids) EnsureNotPinned(document, id);
            foreach (var id in ids)
            {
                var index = document.Walls.FindIndex(wall => wall.Id == id);
                var wall = document.Walls[index];
                document.Walls[index] = wall with
                {
                    Start = wall.End, End = wall.Start, StartJoin = wall.EndJoin, EndJoin = wall.StartJoin,
                    TopProfile = wall.TopProfile is { } flipped ? WallProfiles.Mirror(flipped, wall.Length) : null,
                    LocationReference = wall.LocationReference switch
                    {
                        WallLocationReference.ExteriorFace => WallLocationReference.InteriorFace,
                        WallLocationReference.InteriorFace => WallLocationReference.ExteriorFace,
                        var other => other
                    }
                };
                for (var opening = 0; opening < document.Openings.Count; opening++)
                {
                    if (document.Openings[opening].WallId != id) continue;
                    var hosted = document.Openings[opening];
                    document.Openings[opening] = hosted with
                    {
                        Offset = wall.Length - hosted.Offset, FlipHand = !hosted.FlipHand, FlipFacing = !hosted.FlipFacing
                    };
                }
            }
        });

    /// <summary>Pins or unpins walls. A pinned wall cannot be moved, rotated, edited by the commands here or deleted.</summary>
    public void SetWallsPinned(IReadOnlyCollection<Guid> wallIds, bool pinned) =>
        Commit(pinned ? "Transaction.PinWalls" : "Transaction.UnpinWalls", document =>
        {
            foreach (var id in RequireWalls(document, wallIds))
            {
                var index = document.Walls.FindIndex(wall => wall.Id == id);
                document.Walls[index] = document.Walls[index] with { Pinned = pinned };
            }
        });

    /// <summary>
    /// Gives walls the type, height, top constraint and finish of a source wall. Their position, length and
    /// openings stay as they are, and each keeps the face it was placed by.
    /// </summary>
    public void MatchWallProperties(Guid sourceId, IReadOnlyCollection<Guid> targetIds)
    {
        Commit("Transaction.MatchWallProperties", document =>
        {
            var source = document.Walls.FirstOrDefault(wall => wall.Id == sourceId)
                ?? throw new InvalidDataException("The wall does not exist.");
            var type = document.WallTypes.Single(candidate => candidate.Id == source.WallTypeId);
            var targets = RequireWalls(document, targetIds).Where(id => id != sourceId).ToArray();
            foreach (var id in targets)
            {
                var index = document.Walls.FindIndex(wall => wall.Id == id);
                document.Walls[index] = document.Walls[index] with
                {
                    WallTypeId = source.WallTypeId, Height = source.Height, TopLevelId = source.TopLevelId,
                    TopOffset = source.TopOffset, Finish = source.Finish
                };
            }
            WallReferenceLine.Resize(document, targets.ToDictionary(id => id, _ => type.Thickness));
            foreach (var level in targets.Select(id => document.Walls.Single(wall => wall.Id == id).LevelId).Distinct())
                RefreshRoomBoundariesIfNeeded(document, level);
        });
    }

    /// <summary>Adds a wall (or replaces the source) and the copies of its openings that go with it.</summary>
    private static Guid PlaceWall(BuildingDocument document, Wall source, Wall result, bool copy,
        Func<WallOpening, WallOpening> openingFor, Dictionary<Guid, Guid>? curves = null)
    {
        var hosted = document.Openings.Where(opening => opening.WallId == source.Id).ToArray();
        if (!copy)
        {
            document.Walls[document.Walls.FindIndex(wall => wall.Id == source.Id)] = result;
            foreach (var opening in hosted)
                document.Openings[document.Openings.FindIndex(candidate => candidate.Id == opening.Id)] = openingFor(opening);
            return source.Id;
        }

        var id = Guid.NewGuid();
        document.Walls.Add((curves is null ? result : CurveGroups.Remap(result, curves)) with { Id = id, Pinned = false });
        foreach (var opening in hosted) document.Openings.Add(openingFor(opening) with { Id = Guid.NewGuid(), WallId = id });
        return id;
    }

    /// <summary>Returns the distinct ids, failing when any of them is not a wall of the document.</summary>
    private static IReadOnlyList<Guid> RequireWalls(BuildingDocument document, IReadOnlyCollection<Guid> wallIds)
    {
        ArgumentNullException.ThrowIfNull(wallIds);
        var ids = wallIds.Distinct().ToArray();
        if (ids.Length == 0) throw new InvalidDataException("No walls were selected.");
        var known = document.Walls.Select(wall => wall.Id).ToHashSet();
        if (ids.Any(id => !known.Contains(id))) throw new InvalidDataException("The wall does not exist.");
        return ids;
    }

    /// <summary>Fails when the wall is pinned.</summary>
    private static void EnsureNotPinned(BuildingDocument document, Guid wallId)
    {
        if (document.Walls.FirstOrDefault(wall => wall.Id == wallId)?.Pinned == true)
            throw new InvalidDataException("The wall is pinned.");
    }
}

/// <summary>Plane geometry used by the wall editing commands.</summary>
internal static class WallEditGeometry
{
    /// <summary>Reflects a point about the line through two points.</summary>
    public static Point2 Reflect(Point2 point, Point2 lineStart, Point2 lineEnd)
    {
        var dx = lineEnd.X - lineStart.X;
        var dy = lineEnd.Y - lineStart.Y;
        var t = ((point.X - lineStart.X) * dx + (point.Y - lineStart.Y) * dy) / (dx * dx + dy * dy);
        var footX = lineStart.X + dx * t;
        var footY = lineStart.Y + dy * t;
        return new Point2(2 * footX - point.X, 2 * footY - point.Y);
    }

    /// <summary>Rotates a point counter-clockwise about a center.</summary>
    public static Point2 Rotate(Point2 point, Point2 center, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var x = point.X - center.X;
        var y = point.Y - center.Y;
        return new Point2(center.X + x * cos - y * sin, center.Y + x * sin + y * cos);
    }

    /// <summary>Returns the unit normal on the left of a wall's start-to-end direction.</summary>
    public static (double X, double Y) LeftNormal(Wall wall) =>
        (-(wall.End.Y - wall.Start.Y) / wall.Length, (wall.End.X - wall.Start.X) / wall.Length);

    /// <summary>Returns how far along the centerline, from the start, the point nearest to <paramref name="point"/> lies.</summary>
    public static double Station(Wall wall, Point2 point) =>
        ((point.X - wall.Start.X) * (wall.End.X - wall.Start.X) + (point.Y - wall.Start.Y) * (wall.End.Y - wall.Start.Y)) / wall.Length;

    /// <summary>Returns the centerline point at a distance from the start.</summary>
    public static Point2 PointAt(Wall wall, double station) => new(
        wall.Start.X + (wall.End.X - wall.Start.X) * station / wall.Length,
        wall.Start.Y + (wall.End.Y - wall.Start.Y) * station / wall.Length);
}
