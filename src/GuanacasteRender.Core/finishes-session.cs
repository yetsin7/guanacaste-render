namespace Guanacaste3D.Core;

/// <summary>Surface finish assignments; each public method is one validated transaction.</summary>
public sealed partial class DocumentSession
{
    /// <summary>Assigns a finish to a wall's two faces and rejects unknown finish values.</summary>
    public void SetWallFinish(Guid wallId, FinishKind finish)
    {
        if (!Enum.IsDefined(finish))
            throw new InvalidDataException("The selected finish is not supported.");
        var wall = Document.Walls.FirstOrDefault(wall => wall.Id == wallId)
            ?? throw new InvalidDataException("The wall does not exist.");
        if (wall.Finish == finish) return; // No new undo step or expensive scene invalidation.
        Commit("Transaction.SetWallFinish", document =>
        {
            var index = document.Walls.FindIndex(wall => wall.Id == wallId);
            document.Walls[index] = document.Walls[index] with { Finish = finish };
        });
    }

    /// <summary>Assigns a finish to a floor's walking surface.</summary>
    public void SetFloorFinish(Guid floorId, FinishKind finish)
    {
        if (!Enum.IsDefined(finish))
            throw new InvalidDataException("The selected finish is not supported.");
        var floor = Document.Floors.FirstOrDefault(floor => floor.Id == floorId)
            ?? throw new InvalidDataException("The floor slab does not exist.");
        if (floor.Finish == finish) return; // Preserve redo history on identical edits.
        Commit("Transaction.SetFloorFinish", document =>
        {
            var index = document.Floors.FindIndex(floor => floor.Id == floorId);
            document.Floors[index] = document.Floors[index] with { Finish = finish };
        });
    }
}
