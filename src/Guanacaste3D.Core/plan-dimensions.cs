namespace Guanacaste3D.Core;

/// <summary>Semantic measurements recomputed from model references after every edit.</summary>
public enum PlanDimensionKind { WallLength, OpeningWidth, OpeningStart, OpeningEnd, Left, Right, Below, Above, Radius, Diameter, ArcLength, Angular }

/// <summary>A persistent measurement references model identities instead of a cached numeric label.</summary>
public sealed record PlanDimension(Guid Id, Guid LevelId, Guid OwnerId, Guid? ReferenceId, PlanDimensionKind Kind);

/// <summary>Resolved dimension line coordinates and its actual metric value.</summary>
public readonly record struct PlanMeasurement(Point2 From, Point2 To, double Value, bool IsAngle = false);

/// <summary>Builds and resolves plan dimensions for walls, openings and other supported BIM elements.</summary>
public static partial class PlanDimensions
{
    /// <summary>Gets the active-level bounding rectangle; opening bounds follow their host axis.</summary>
    public static (Guid LevelId, PlanBounds Bounds)? Bounds(BuildingDocument document, Guid id)
    {
        if (document.Walls.FirstOrDefault(wall => wall.Id == id) is { } wall)
            return (wall.LevelId, Enclose(WallPlacementGeometry.Footprint(wall.Start, wall.End, wall.Thickness,
                0, WallLocationReference.Centerline).Points));
        if (document.Openings.FirstOrDefault(opening => opening.Id == id) is { } opening)
        {
            var host = document.Walls.Single(wall => wall.Id == opening.WallId);
            return (host.LevelId, Enclose(new[] { At(host, opening.Begin, -host.Thickness / 2),
                At(host, opening.End, -host.Thickness / 2), At(host, opening.End, host.Thickness / 2),
                At(host, opening.Begin, host.Thickness / 2) }));
        }
        if (document.Columns.FirstOrDefault(column => column.Id == id) is { } column)
            return (column.LevelId, new PlanBounds(column.Position.X - column.Width / 2,
                column.Position.Y - column.Depth / 2, column.Position.X + column.Width / 2,
                column.Position.Y + column.Depth / 2));
        if (document.Rooms.FirstOrDefault(room => room.Id == id) is { } room)
            return (room.LevelId, Enclose(new[] { room.FirstCorner, room.SecondCorner }));
        if (document.Floors.FirstOrDefault(floor => floor.Id == id) is { } floor)
            return (floor.LevelId, Enclose(floor.Boundary));
        if (document.StructuralElements.FirstOrDefault(element => element.Id == id) is { } element)
            return (element.LevelId, Enclose(element.Footprint));
        return null;
    }

    /// <summary>Finds the nearest overlapping wall or orthogonal axis in each direction plus intrinsic measurements.</summary>
    public static IReadOnlyList<PlanDimension> ForSelection(BuildingDocument document, Guid ownerId)
    {
        if (Bounds(document, ownerId) is not { } owner) return [];
        var result = new List<PlanDimension>();
        if (document.Walls.Any(wall => wall.Id == ownerId))
            result.Add(new(Guid.Empty, owner.LevelId, ownerId, null, PlanDimensionKind.WallLength));
        var opening = document.Openings.FirstOrDefault(candidate => candidate.Id == ownerId);
        if (opening is not null)
            foreach (var kind in new[] { PlanDimensionKind.OpeningStart, PlanDimensionKind.OpeningEnd, PlanDimensionKind.OpeningWidth })
                result.Add(new(Guid.Empty, owner.LevelId, ownerId, opening.WallId, kind));
        foreach (var kind in new[] { PlanDimensionKind.Left, PlanDimensionKind.Right, PlanDimensionKind.Below, PlanDimensionKind.Above })
        {
            PlanDimension? nearest = null;
            var minimum = double.PositiveInfinity;
            var references = document.Walls.Where(wall => wall.LevelId == owner.LevelId &&
                    wall.Id != ownerId && wall.Id != opening?.WallId).Select(wall => wall.Id)
                .Concat(document.Grids.Select(axis => axis.Id));
            foreach (var referenceId in references)
            {
                if (ReferenceBounds(document, referenceId, owner.LevelId, kind) is not { } target) continue;
                var horizontal = kind is PlanDimensionKind.Left or PlanDimensionKind.Right;
                if (horizontal ? target.MaxY <= owner.Bounds.MinY || target.MinY >= owner.Bounds.MaxY
                    : target.MaxX <= owner.Bounds.MinX || target.MinX >= owner.Bounds.MaxX) continue;
                var candidate = new PlanDimension(Guid.Empty, owner.LevelId, ownerId, referenceId, kind);
                var value = Resolve(document, candidate)?.Value ?? -1;
                if (value < 0.01 || value >= minimum) continue;
                nearest = candidate;
                minimum = value;
            }
            if (nearest is not null) result.Add(nearest);
        }
        return result;
    }

    /// <summary>Resolves current geometry; null means an owner, host or reference was deleted.</summary>
    public static PlanMeasurement? Resolve(BuildingDocument document, PlanDimension dimension)
    {
        if (Bounds(document, dimension.OwnerId) is not { } owner || owner.LevelId != dimension.LevelId) return null;
        if (dimension.Kind == PlanDimensionKind.WallLength)
        {
            var wall = document.Walls.FirstOrDefault(wall => wall.Id == dimension.OwnerId);
            return wall is null ? null : new(At(wall, 0, wall.Thickness / 2 + 0.45),
                At(wall, wall.Length, wall.Thickness / 2 + 0.45), wall.Length);
        }
        if (dimension.Kind is PlanDimensionKind.Radius or PlanDimensionKind.Diameter or PlanDimensionKind.ArcLength)
            return ResolveCurve(document, dimension);
        if (dimension.Kind == PlanDimensionKind.Angular) return ResolveAngle(document, dimension);
        if (dimension.Kind is PlanDimensionKind.OpeningStart or PlanDimensionKind.OpeningEnd or PlanDimensionKind.OpeningWidth)
        {
            var opening = document.Openings.FirstOrDefault(opening => opening.Id == dimension.OwnerId);
            if (opening is null) return null;
            var host = document.Walls.First(wall => wall.Id == opening.WallId);
            var begin = dimension.Kind == PlanDimensionKind.OpeningStart ? 0 :
                dimension.Kind == PlanDimensionKind.OpeningEnd ? opening.End : opening.Begin;
            var end = dimension.Kind == PlanDimensionKind.OpeningEnd ? host.Length :
                dimension.Kind == PlanDimensionKind.OpeningStart ? opening.Begin : opening.End;
            var across = (host.Thickness / 2 + 0.45) * (dimension.Kind == PlanDimensionKind.OpeningWidth ? -1 : 1);
            return new(At(host, begin, across), At(host, end, across), end - begin);
        }
        if (dimension.ReferenceId is not Guid referenceId ||
            ReferenceBounds(document, referenceId, owner.LevelId, dimension.Kind) is not { } target) return null;
        var bounds = owner.Bounds;
        var x = (bounds.MinX + bounds.MaxX) / 2;
        var y = (bounds.MinY + bounds.MaxY) / 2;
        return dimension.Kind switch
        {
            PlanDimensionKind.Left => new(new(bounds.MinX, y), new(target.MaxX, y), bounds.MinX - target.MaxX),
            PlanDimensionKind.Right => new(new(bounds.MaxX, y), new(target.MinX, y), target.MinX - bounds.MaxX),
            PlanDimensionKind.Below => new(new(x, bounds.MinY), new(x, target.MaxY), bounds.MinY - target.MaxY),
            PlanDimensionKind.Above => new(new(x, bounds.MaxY), new(x, target.MinY), target.MinY - bounds.MaxY),
            _ => null
        };
    }

    /// <summary>Resolves level-local faces and project-wide orthogonal grid lines without treating a grid as a solid.</summary>
    private static PlanBounds? ReferenceBounds(BuildingDocument document, Guid id, Guid levelId, PlanDimensionKind kind)
    {
        if (document.Grids.FirstOrDefault(axis => axis.Id == id) is { } axis)
        {
            var horizontal = kind is PlanDimensionKind.Left or PlanDimensionKind.Right;
            if (horizontal ? Math.Abs(axis.End.X - axis.Start.X) > 0.00001 :
                Math.Abs(axis.End.Y - axis.Start.Y) > 0.00001) return null;
            return Enclose([axis.Start, axis.End]);
        }
        return Bounds(document, id) is { } reference && reference.LevelId == levelId ? reference.Bounds : null;
    }

    /// <summary>
    /// The pieces of the curved wall that includes <paramref name="wallId"/>, in order from its start to its end, or an
    /// empty list when that wall is straight or does not exist.
    /// </summary>
    public static IReadOnlyList<Wall> CurveChain(BuildingDocument document, Guid wallId)
    {
        if (document.Walls.FirstOrDefault(wall => wall.Id == wallId)?.CurveGroupId is not { } group) return [];
        var pieces = document.Walls.Where(wall => wall.CurveGroupId == group).ToList();
        var head = pieces.FirstOrDefault(piece => pieces.All(other => other.End != piece.Start));
        if (head is null) return pieces;
        var ordered = new List<Wall> { head };
        while (ordered.Count < pieces.Count &&
               pieces.FirstOrDefault(piece => piece.Start == ordered[^1].End && !ordered.Contains(piece)) is { } next)
            ordered.Add(next);
        return ordered.Count == pieces.Count ? ordered : pieces;
    }

    /// <summary>
    /// The circle a curved wall follows, fitted through its first corner, a corner in the middle and its last corner,
    /// together with the chain's ends. Null when the pieces no longer lie on a circle.
    /// </summary>
    public static (ArcFit Arc, Point2 Start, Point2 End)? CurveCircle(BuildingDocument document, Guid wallId)
    {
        var chain = CurveChain(document, wallId);
        if (chain.Count < 2) return null;
        var start = chain[0].Start;
        var end = chain[^1].End;
        return ArcWallGeometry.Fit(start, end, chain[chain.Count / 2].Start) is { } arc ? (arc, start, end) : null;
    }

    /// <summary>
    /// Radius and diameter are drawn from the center through the middle of the wall piece the dimension belongs to;
    /// the arc length runs between the ends of the whole curved wall and carries the length measured along the arc.
    /// </summary>
    private static PlanMeasurement? ResolveCurve(BuildingDocument document, PlanDimension dimension)
    {
        if (CurveCircle(document, dimension.OwnerId) is not { } curve) return null;
        if (dimension.Kind == PlanDimensionKind.ArcLength) return new(curve.Start, curve.End, curve.Arc.Length);
        var piece = document.Walls.First(wall => wall.Id == dimension.OwnerId);
        var dx = (piece.Start.X + piece.End.X) / 2 - curve.Arc.Center.X;
        var dy = (piece.Start.Y + piece.End.Y) / 2 - curve.Arc.Center.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < 1e-9) return null;
        var point = new Point2(curve.Arc.Center.X + dx / distance * curve.Arc.Radius, curve.Arc.Center.Y + dy / distance * curve.Arc.Radius);
        return dimension.Kind == PlanDimensionKind.Radius
            ? new(curve.Arc.Center, point, curve.Arc.Radius)
            : new(new Point2(2 * curve.Arc.Center.X - point.X, 2 * curve.Arc.Center.Y - point.Y), point, 2 * curve.Arc.Radius);
    }

    /// <summary>Length, in meters, of the two arms drawn from the corner of an angular dimension.</summary>
    private const double AngleArm = 1.0;

    /// <summary>
    /// The angle, in degrees from 0 to 180, between two walls of one level. Its corner is where their lines cross, and
    /// each arm points from the corner toward the middle of its wall, so the value is the angle the walls enclose.
    /// Parallel walls have no corner and no angle.
    /// </summary>
    private static PlanMeasurement? ResolveAngle(BuildingDocument document, PlanDimension dimension)
    {
        if (dimension.ReferenceId is not Guid referenceId) return null;
        var first = document.Walls.FirstOrDefault(wall => wall.Id == dimension.OwnerId);
        var second = document.Walls.FirstOrDefault(wall => wall.Id == referenceId);
        if (first is null || second is null || first.LevelId != dimension.LevelId || second.LevelId != dimension.LevelId ||
            first.Length < 1e-9 || second.Length < 1e-9) return null;
        var d1x = (first.End.X - first.Start.X) / first.Length;
        var d1y = (first.End.Y - first.Start.Y) / first.Length;
        var d2x = (second.End.X - second.Start.X) / second.Length;
        var d2y = (second.End.Y - second.Start.Y) / second.Length;
        var cross = d1x * d2y - d1y * d2x;
        if (Math.Abs(cross) < 1e-9) return null;
        var offsetX = second.Start.X - first.Start.X;
        var offsetY = second.Start.Y - first.Start.Y;
        var along = (offsetX * d2y - offsetY * d2x) / cross;
        var corner = new Point2(first.Start.X + d1x * along, first.Start.Y + d1y * along);
        if (!double.IsFinite(corner.X) || !double.IsFinite(corner.Y)) return null;

        (double X, double Y) Arm(Wall wall, double dx, double dy)
        {
            var toMiddleX = (wall.Start.X + wall.End.X) / 2 - corner.X;
            var toMiddleY = (wall.Start.Y + wall.End.Y) / 2 - corner.Y;
            return toMiddleX * dx + toMiddleY * dy >= 0 ? (dx, dy) : (-dx, -dy);
        }
        var arm1 = Arm(first, d1x, d1y);
        var arm2 = Arm(second, d2x, d2y);
        var degrees = Math.Acos(Math.Clamp(arm1.X * arm2.X + arm1.Y * arm2.Y, -1, 1)) * 180 / Math.PI;
        return new(new Point2(corner.X + arm1.X * AngleArm, corner.Y + arm1.Y * AngleArm),
            new Point2(corner.X + arm2.X * AngleArm, corner.Y + arm2.Y * AngleArm), degrees, IsAngle: true);
    }

    /// <summary>Rejects invalid annotation identities, references and unsupported measurement kinds.</summary>
    public static void Validate(BuildingDocument document)
    {
        if (document.Dimensions is null || document.Dimensions.Count > 100000 ||
            document.Dimensions.Select(item => item?.Id).Distinct().Count() != document.Dimensions.Count ||
            document.Dimensions.Any(item => item is null || item.Id == Guid.Empty || !Enum.IsDefined(item.Kind) ||
                Resolve(document, item) is null))
            throw new InvalidDataException("Invalid plan dimension references.");
    }

    /// <summary>Returns the axis-aligned envelope used for cardinal clear-gap measurements.</summary>
    private static PlanBounds Enclose(IEnumerable<Point2> points)
    {
        var vertices = points.ToArray();
        return new(vertices.Min(point => point.X), vertices.Min(point => point.Y),
            vertices.Max(point => point.X), vertices.Max(point => point.Y));
    }

    /// <summary>Converts along/across coordinates into world coordinates for a wall.</summary>
    private static Point2 At(Wall wall, double along, double across)
    {
        var x = (wall.End.X - wall.Start.X) / wall.Length;
        var y = (wall.End.Y - wall.Start.Y) / wall.Length;
        return new(wall.Start.X + x * along - y * across, wall.Start.Y + y * along + x * across);
    }
}
