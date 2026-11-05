namespace Guanacaste3D.Core;

/// <summary>A named building datum shared by every level, independent of the drawing background grid.</summary>
public sealed record GridAxis(Guid Id, string Name, Point2 Start, Point2 End)
{
    public bool IsValid => Id != Guid.Empty && !string.IsNullOrWhiteSpace(Name) && Name.Length <= 32 &&
        new SectionCut(Start, End).IsValid;

    /// <summary>
    /// A datum represents an infinite reference line. Shortening its visible segment must not
    /// detach a wall whose centreline is still coincident with that reference.
    /// </summary>
    internal bool IsCollinearWith(Point2 start, Point2 end)
    {
        const double toleranceMeters = 0.015;
        var dx = End.X - Start.X;
        var dy = End.Y - Start.Y;
        var length = double.Hypot(dx, dy);
        if (!double.IsFinite(length) || length < 1e-8) return false;
        var ux = dx / length;
        var uy = dy / length;
        return Math.Abs((start.X - Start.X) * uy - (start.Y - Start.Y) * ux) <= toleranceMeters &&
               Math.Abs((end.X - Start.X) * uy - (end.Y - Start.Y) * ux) <= toleranceMeters;
    }
}

/// <summary>Reports the existing datum so the UI can offer to locate it instead of silently failing.</summary>
public sealed class DuplicateGridAxisNameException : IOException
{
    public Guid ExistingAxisId { get; }
    public string AxisName { get; }

    public DuplicateGridAxisNameException(string axisName, Guid existingAxisId)
        : base($"El eje '{axisName}' ya existe en el proyecto.")
    {
        AxisName = axisName;
        ExistingAxisId = existingAxisId;
    }
}

public sealed partial class DocumentSession
{
    private static string NormalizeAxisName(string? name)
    {
        var normalized = (name ?? string.Empty).Trim();
        if (normalized.Length is < 1 or > 32)
            throw new InvalidDataException("The grid axis name must contain between 1 and 32 characters.");
        return normalized;
    }

    private void CheckGridAxisName(string name, Guid editingId = default)
    {
        var duplicate = Document.Grids.FirstOrDefault(axis => axis.Id != editingId &&
            string.Equals(axis.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null) throw new DuplicateGridAxisNameException(name, duplicate.Id);
    }

    /// <summary>Creates a uniquely named project-wide reference axis in a single undoable transaction.</summary>
    public Guid AddGridAxis(string name, Point2 start, Point2 end)
    {
        name = NormalizeAxisName(name);
        CheckGridAxisName(name);
        var axis = new GridAxis(Guid.NewGuid(), name, start, end);
        Commit("Transaction.AddGridAxis", document =>
        {
            document.Grids.Add(axis);
            for (var i = 0; i < document.Walls.Count; i++)
            {
                var wall = document.Walls[i];
                if (wall.GridAxisId is null && !wall.GridAxisAssociationDisabled && IsAligned(axis, wall.Start, wall.End))
                    document.Walls[i] = wall with { GridAxisId = axis.Id };
            }
        });
        return axis.Id;
    }

    /// <summary>
    /// Renames or edits an axis, moving linked walls rigidly and refreshing affected automatic rooms.
    /// </summary>
    public void UpdateGridAxis(Guid id, string name, Point2 start, Point2 end)
    {
        var old = Document.Grids.FirstOrDefault(axis => axis.Id == id)
            ?? throw new InvalidDataException("The grid axis does not exist.");
        name = NormalizeAxisName(name);
        CheckGridAxisName(name, id);
        Commit("Transaction.EditGridAxis", document =>
        {
            var index = document.Grids.FindIndex(axis => axis.Id == id);
            document.Grids[index] = new GridAxis(id, name, start, end);
            if (old.Start == start && old.End == end) return;
            var oldAngle = Math.Atan2(old.End.Y - old.Start.Y, old.End.X - old.Start.X);
            var newAngle = Math.Atan2(end.Y - start.Y, end.X - start.X);
            var cosine = Math.Cos(newAngle - oldAngle);
            var sine = Math.Sin(newAngle - oldAngle);
            Point2 Transform(Point2 point) => new(
                start.X + (point.X - old.Start.X) * cosine - (point.Y - old.Start.Y) * sine,
                start.Y + (point.X - old.Start.X) * sine + (point.Y - old.Start.Y) * cosine);
            var affectedLevels = new HashSet<Guid>();
            for (var wallIndex = 0; wallIndex < document.Walls.Count; wallIndex++)
            {
                var wall = document.Walls[wallIndex];
                if (wall.GridAxisId != id && (wall.GridAxisAssociationDisabled || wall.GridAxisId is not null ||
                    !IsAligned(old, wall.Start, wall.End))) continue;
                affectedLevels.Add(wall.LevelId);
                document.Walls[wallIndex] = wall with { Start = Transform(wall.Start), End = Transform(wall.End), GridAxisId = id };
            }
            foreach (var level in affectedLevels) RefreshRoomBoundariesIfNeeded(document, level);
        });
    }

    /// <summary>Moves an axis and every centre-line-aligned wall in a single undoable model transaction.</summary>
    public void MoveGridAxis(Guid id, double deltaX, double deltaY)
    {
        var axis = Document.Grids.FirstOrDefault(grid => grid.Id == id)
            ?? throw new InvalidDataException("The grid axis does not exist.");
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY))
            throw new InvalidDataException("The grid axis translation must be finite.");
        if (Math.Abs(deltaX) < 1e-9 && Math.Abs(deltaY) < 1e-9) return;
        UpdateGridAxis(id, axis.Name,
            new Point2(axis.Start.X + deltaX, axis.Start.Y + deltaY),
            new Point2(axis.End.X + deltaX, axis.End.Y + deltaY));
    }

    /// <summary>Finds an axis coinciding with the centerline of the entire wall.</summary>
    private static Guid? FindAlignedGridAxis(BuildingDocument doc, Point2 start, Point2 end) =>
        doc.Grids.FirstOrDefault(axis => IsAligned(axis, start, end))?.Id;

    private static bool IsAligned(GridAxis axis, Point2 start, Point2 end)
    {
        if (!axis.IsCollinearWith(start, end)) return false;
        var dx = axis.End.X - axis.Start.X;
        var dy = axis.End.Y - axis.Start.Y;
        var length = double.Hypot(dx, dy);
        var ux = dx / length;
        var uy = dy / length;
        var from = (start.X - axis.Start.X) * ux + (start.Y - axis.Start.Y) * uy;
        var to = (end.X - axis.Start.X) * ux + (end.Y - axis.Start.Y) * uy;
        return Math.Max(Math.Min(from, to), 0) < Math.Min(Math.Max(from, to), length) - 1e-8;
    }

    /// <summary>Explicitly binds/unbinds a wall without changing either element's geometry.</summary>
    public void SetWallGridAxis(Guid wallId, Guid? axisId)
    {
        var wall = Document.Walls.FirstOrDefault(w => w.Id == wallId)
            ?? throw new InvalidDataException("The wall does not exist.");
        if (axisId is Guid grid && !Document.Grids.Any(axis => axis.Id == grid))
            throw new InvalidDataException("The grid axis does not exist.");
        if (axisId is Guid selected && !IsAligned(Document.Grids.Single(a => a.Id == selected), wall.Start, wall.End))
            throw new InvalidDataException("The wall centerline must align with the grid axis.");
        Commit("Transaction.BindWallGridAxis", doc =>
        {
            var index = doc.Walls.FindIndex(w => w.Id == wallId);
            doc.Walls[index] = doc.Walls[index] with
            {
                GridAxisId = axisId,
                GridAxisAssociationDisabled = axisId is null
            };
        });
    }

    /// <summary>Associates a wall with a collinear grid axis without moving either element.</summary>
    public void AttachWallToGridAxis(Guid wallId, Guid axisId) => SetWallGridAxis(wallId, axisId);

    /// <summary>Moves only linked walls, plus legacy aligned walls not linked to another axis.</summary>
    private static void TranslateAxisAlignedWalls(BuildingDocument document, GridAxis axis, double dx, double dy)
    {
        for (var i = 0; i < document.Walls.Count; i++)
        {
            var wall = document.Walls[i];
            if (wall.GridAxisId != axis.Id &&
                (wall.GridAxisAssociationDisabled || wall.GridAxisId is not null ||
                 !IsAligned(axis, wall.Start, wall.End))) continue;
            document.Walls[i] = wall with
            {
                Start = new Point2(wall.Start.X + dx, wall.Start.Y + dy),
                End = new Point2(wall.End.X + dx, wall.End.Y + dy),
                GridAxisId = axis.Id,
                GridAxisAssociationDisabled = false
            };
        }
    }

    /// <summary>Removes the datum and dependent annotations, preserving existing building elements.</summary>
    public void RemoveGridAxis(Guid id) =>
        Commit("Transaction.RemoveGridAxis", document =>
        {
            document.Grids.RemoveAll(axis => axis.Id == id);
            for (var i = 0; i < document.Walls.Count; i++)
                if (document.Walls[i].GridAxisId == id)
                    document.Walls[i] = document.Walls[i] with
                    {
                        GridAxisId = null,
                        GridAxisAssociationDisabled = true
                    };
            // Existing walls and their hosted openings are deliberately retained.
        });
}
