namespace Guanacaste3D.Core;

/// <summary>Wall dimension edits; each public method is one validated transaction.</summary>
public sealed partial class DocumentSession
{
    /// <summary>
    /// Applies length, height, thickness, or face-area edits to one wall, letting
    /// <see cref="WallDimensionSolver"/> move the free dimension. A height on a
    /// top-constrained wall keeps its top offset logic, and hosted openings that no
    /// longer fit make the whole edit fail without changing the document.
    /// </summary>
    public void ApplyWallDimensions(Guid id, WallDimensionEdit edit)
    {
        if (!Document.Walls.Any(wall => wall.Id == id))
            throw new InvalidDataException("The wall does not exist.");
        Commit("Transaction.EditWallDimensions", doc =>
        {
            var index = doc.Walls.FindIndex(wall => wall.Id == id);
            var resolved = WallDimensionSolver.Resolve(doc.Walls[index], edit);
            var levels = doc.Levels.ToDictionary(level => level.Id);
            var offset = resolved.TopLevelId is Guid topId
                ? levels[resolved.LevelId].Elevation + resolved.Height - levels[topId].Elevation
                : 0;
            doc.Walls[index] = resolved with { TopOffset = offset };
        });
    }
}
