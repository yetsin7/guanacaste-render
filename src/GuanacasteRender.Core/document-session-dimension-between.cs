namespace Guanacaste3D.Core;

public sealed partial class DocumentSession
{
    /// <summary>Smallest clear distance, in meters, worth a dimension: anything closer touches or overlaps.</summary>
    public const double MinimumMeasurableGap = 0.001;

    /// <summary>
    /// Adds the radius, the diameter or the arc length of the curved wall that includes <paramref name="wallId"/>. The
    /// measurement is recomputed from the wall after every edit, like any other dimension, and the arc length is kept
    /// once per curved wall however many of its pieces are picked.
    /// </summary>
    public Guid AddCurveDimension(Guid wallId, PlanDimensionKind kind)
    {
        if (kind is not (PlanDimensionKind.Radius or PlanDimensionKind.Diameter or PlanDimensionKind.ArcLength))
            throw new InvalidDataException("Only the radius, diameter and arc length are measured on a curved wall.");
        if (Document.Walls.All(wall => wall.Id != wallId)) throw new InvalidDataException("The element does not exist.");
        var chain = PlanDimensions.CurveChain(Document, wallId);
        if (chain.Count == 0) throw new InvalidDataException("The wall is straight, so it has no radius.");
        if (PlanDimensions.CurveCircle(Document, wallId) is null) throw new InvalidDataException("The wall no longer follows an arc.");
        var owner = kind == PlanDimensionKind.ArcLength ? chain[0].Id : wallId;
        return AddDimension(new PlanDimension(Guid.Empty, chain[0].LevelId, owner, null, kind));
    }

    /// <summary>
    /// Adds the angle between two walls of one level. It follows both walls when either moves or turns, and it is
    /// removed with them; walls that never meet, being parallel, have no angle to measure.
    /// </summary>
    public Guid AddAngularDimension(Guid firstWallId, Guid secondWallId)
    {
        if (firstWallId == secondWallId) throw new InvalidDataException("Two different elements are needed.");
        var first = Document.Walls.FirstOrDefault(wall => wall.Id == firstWallId);
        var second = Document.Walls.FirstOrDefault(wall => wall.Id == secondWallId);
        if (first is null || second is null) throw new InvalidDataException("The element does not exist.");
        if (first.LevelId != second.LevelId) throw new InvalidDataException("The elements are on different levels.");
        var dimension = new PlanDimension(Guid.Empty, first.LevelId, firstWallId, secondWallId, PlanDimensionKind.Angular);
        if (PlanDimensions.Resolve(Document, dimension) is null) throw new InvalidDataException("The walls are parallel, so they form no angle.");
        return AddDimension(dimension);
    }

    /// <summary>
    /// Adds the dimension of the clear distance between two elements of one level: walls, openings, columns, rooms,
    /// floors, structural elements, or a grid axis as the second one. The direction is the one in which they are
    /// farthest apart. The dimension follows both elements when either moves, like every other one.
    /// </summary>
    public Guid AddDimensionBetween(Guid ownerId, Guid referenceId)
    {
        if (ownerId == referenceId) throw new InvalidDataException("Two different elements are needed.");
        if (PlanDimensions.Bounds(Document, ownerId) is not { } owner) throw new InvalidDataException("The element does not exist.");
        PlanBounds target;
        if (Document.Grids.FirstOrDefault(axis => axis.Id == referenceId) is { } grid)
            target = new PlanBounds(Math.Min(grid.Start.X, grid.End.X), Math.Min(grid.Start.Y, grid.End.Y),
                Math.Max(grid.Start.X, grid.End.X), Math.Max(grid.Start.Y, grid.End.Y));
        else if (PlanDimensions.Bounds(Document, referenceId) is { } reference)
        {
            if (reference.LevelId != owner.LevelId) throw new InvalidDataException("The elements are on different levels.");
            target = reference.Bounds;
        }
        else throw new InvalidDataException("The element does not exist.");

        var gaps = new (PlanDimensionKind Kind, double Gap)[]
        {
            (PlanDimensionKind.Left, owner.Bounds.MinX - target.MaxX),
            (PlanDimensionKind.Right, target.MinX - owner.Bounds.MaxX),
            (PlanDimensionKind.Below, owner.Bounds.MinY - target.MaxY),
            (PlanDimensionKind.Above, target.MinY - owner.Bounds.MaxY)
        };
        var best = gaps.MaxBy(candidate => candidate.Gap);
        if (!(best.Gap > MinimumMeasurableGap)) throw new InvalidDataException("The elements touch or overlap, so there is no distance to measure.");
        return AddDimension(new PlanDimension(Guid.Empty, owner.LevelId, ownerId, referenceId, best.Kind));
    }
}
