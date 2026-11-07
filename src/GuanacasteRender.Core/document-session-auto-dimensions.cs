namespace Guanacaste3D.Core;

public sealed partial class DocumentSession
{
    /// <summary>
    /// Dimensions every wall length and every opening width on a level as associative annotations, in one undoable
    /// step. Dimensions that already exist are kept. Returns how many were added.
    /// </summary>
    public int AutoDimensionLevel(Guid levelId)
    {
        if (Document.Levels.All(level => level.Id != levelId)) throw new InvalidDataException("The level does not exist.");
        var added = 0;
        Commit("Transaction.AutoDimension", document =>
        {
            bool Exists(Guid owner, Guid? reference, PlanDimensionKind kind) => document.Dimensions.Any(item =>
                item.OwnerId == owner && item.ReferenceId == reference && item.Kind == kind);
            void Add(PlanDimension dimension)
            {
                if (Exists(dimension.OwnerId, dimension.ReferenceId, dimension.Kind) ||
                    PlanDimensions.Resolve(document, dimension) is null) return;
                document.Dimensions.Add(dimension with { Id = Guid.NewGuid() });
                added++;
            }

            var walls = document.Walls.Where(wall => wall.LevelId == levelId).ToArray();
            foreach (var wall in walls)
                Add(new PlanDimension(Guid.Empty, levelId, wall.Id, null, PlanDimensionKind.WallLength));
            foreach (var opening in document.Openings.Where(opening => walls.Any(wall => wall.Id == opening.WallId)))
                Add(new PlanDimension(Guid.Empty, levelId, opening.Id, opening.WallId, PlanDimensionKind.OpeningWidth));
        });
        return added;
    }
}
