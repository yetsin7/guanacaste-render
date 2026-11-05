namespace Guanacaste3D.Core;

public sealed partial class DocumentSession
{
    /// <summary>Promotes a temporary measurement to an associative annotation in one undoable operation.</summary>
    public Guid AddDimension(PlanDimension measurement)
    {
        var existing = Document.Dimensions.FirstOrDefault(item => item.OwnerId == measurement.OwnerId &&
            item.ReferenceId == measurement.ReferenceId && item.Kind == measurement.Kind);
        if (existing is not null) return existing.Id;
        if (PlanDimensions.Resolve(Document, measurement) is null)
            throw new InvalidDataException("Invalid plan dimension references.");
        var id = Guid.NewGuid();
        Commit("Transaction.AddDimension", document => document.Dimensions.Add(measurement with { Id = id }));
        return id;
    }

    /// <summary>Deletes an annotation without modifying the geometry it references.</summary>
    public void RemoveDimension(Guid id) =>
        Commit("Transaction.RemoveDimension", document => document.Dimensions.RemoveAll(item => item.Id == id));

    /// <summary>Solves a metric edit against a fixed reference, rejecting collisions atomically.</summary>
    public void EditDimension(PlanDimension dimension, double value)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new InvalidDataException("Dimension value must be positive and finite.");
        var measurement = PlanDimensions.Resolve(Document, dimension)
            ?? throw new InvalidDataException("Invalid plan dimension references.");
        if (dimension.Kind == PlanDimensionKind.Angular)
            throw new InvalidDataException("An angle cannot be typed in; turn one of the walls instead.");
        if (dimension.Kind is PlanDimensionKind.Radius or PlanDimensionKind.Diameter or PlanDimensionKind.ArcLength)
            throw new InvalidDataException("This measurement of a curved wall cannot be typed in; draw the wall again.");
        if (dimension.Kind == PlanDimensionKind.WallLength)
        {
            ApplyWallDimensions(dimension.OwnerId, new WallDimensionEdit(Length: value));
            return;
        }
        var opening = Document.Openings.FirstOrDefault(item => item.Id == dimension.OwnerId);
        if (opening is not null)
        {
            var wall = Document.Walls.Single(item => item.Id == opening.WallId);
            if (dimension.Kind == PlanDimensionKind.OpeningWidth)
                UpdateOpening(opening.Id, opening.Offset, value, opening.Sill, opening.Height);
            else if (dimension.Kind == PlanDimensionKind.OpeningStart)
                MoveOpening(opening.Id, wall.Id, value + opening.Width / 2);
            else if (dimension.Kind == PlanDimensionKind.OpeningEnd)
                MoveOpening(opening.Id, wall.Id, wall.Length - value - opening.Width / 2);
            else
            {
                var horizontal = dimension.Kind is PlanDimensionKind.Left or PlanDimensionKind.Right;
                var axis = horizontal ? (wall.End.X - wall.Start.X) / wall.Length : (wall.End.Y - wall.Start.Y) / wall.Length;
                if (Math.Abs(axis) < 1e-8) throw new InvalidDataException("The opening cannot move across its host wall.");
                var sign = dimension.Kind is PlanDimensionKind.Left or PlanDimensionKind.Below ? 1 : -1;
                MoveOpening(opening.Id, wall.Id, opening.Offset + sign * (value - measurement.Value) / axis);
            }
            return;
        }
        var delta = (value - measurement.Value) *
            (dimension.Kind is PlanDimensionKind.Left or PlanDimensionKind.Below ? 1 : -1);
        var deltaX = dimension.Kind is PlanDimensionKind.Left or PlanDimensionKind.Right ? delta : 0;
        var deltaY = dimension.Kind is PlanDimensionKind.Below or PlanDimensionKind.Above ? delta : 0;
        // Solve on an isolated document so a rejected dependent movement cannot consume redo history.
        var candidate = new DocumentSession(Document);
        var id = dimension.OwnerId;
        if (Document.Walls.Any(item => item.Id == id)) candidate.MoveWall(id, deltaX, deltaY);
        else if (Document.Columns.Any(item => item.Id == id)) candidate.MoveColumn(id, deltaX, deltaY);
        else if (Document.Rooms.Any(item => item.Id == id)) candidate.MoveRoom(id, deltaX, deltaY);
        else if (Document.Floors.Any(item => item.Id == id)) candidate.MoveFloor(id, deltaX, deltaY);
        else candidate.MoveStructuralElement(id, deltaX, deltaY);
        var solved = PlanDimensions.Resolve(candidate.Document, dimension);
        if (solved is null || Math.Abs(solved.Value.Value - value) > 1e-7)
            throw new InvalidDataException("The dimension reference moved with the selected element.");
        Commit("Transaction.EditDimension", document =>
        {
            document.Walls = candidate.Document.Walls;
            document.Columns = candidate.Document.Columns;
            document.Rooms = candidate.Document.Rooms;
            document.Floors = candidate.Document.Floors;
            document.StructuralElements = candidate.Document.StructuralElements;
        });
    }
}
