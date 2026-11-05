namespace Guanacaste3D.Core;

/// <summary>What the building services runs and devices are made of: how they are sectioned and where the schematic gets its nodes.</summary>
public static class MepElements
{
    /// <summary>The kinds that are runs: ducts, trays, conduits and pipes drawn between two points.</summary>
    public static readonly StructuralElementKind[] Runs =
    [
        StructuralElementKind.Duct, StructuralElementKind.DuctPlaceholder, StructuralElementKind.FlexDuct, StructuralElementKind.CableTray,
        StructuralElementKind.Conduit, StructuralElementKind.FlexPipe, StructuralElementKind.FabricationPart
    ];

    /// <summary>The kinds placed at a point: an air terminal and a control device.</summary>
    public static readonly StructuralElementKind[] Devices = [StructuralElementKind.AirTerminal, StructuralElementKind.MechanicalControlDevice];

    public static bool IsMep(StructuralElementKind kind) => Runs.Contains(kind) || Devices.Contains(kind) || kind == StructuralElementKind.DuctStiffener;

    /// <summary>Runs with a round section: drawn with an octagon-like silhouette instead of a box.</summary>
    public static bool IsRound(StructuralElementKind kind) =>
        kind is StructuralElementKind.Conduit or StructuralElementKind.FlexDuct or StructuralElementKind.FlexPipe;
}

public sealed partial class DocumentSession
{
    /// <summary>The largest number of stiffeners one duct gets in one go.</summary>
    public const int MaximumStiffeners = 500;

    /// <summary>
    /// Puts a stiffening band round a duct every <paramref name="spacing"/> meters, from one end to the other, and groups the
    /// bands so they move and go with each other. Returns how many. The bands follow the duct's section and height.
    /// </summary>
    public int AddDuctStiffeners(Guid ductId, double spacing)
    {
        var duct = Document.StructuralElements.FirstOrDefault(item => item.Id == ductId && item.Kind == StructuralElementKind.Duct)
            ?? throw new InvalidDataException("The duct does not exist.");
        if (!double.IsFinite(spacing) || spacing < 0.3 || spacing > 20) throw new InvalidDataException("The stiffener spacing is invalid.");
        var length = duct.Length;
        var count = (int)Math.Floor(length / spacing);
        if (count < 1) throw new InvalidDataException("The duct is too short for stiffeners at that spacing.");
        if (count > MaximumStiffeners) throw new InvalidDataException("The duct would get too many stiffeners.");
        var group = Guid.NewGuid();
        var directionX = (duct.End.X - duct.Start.X) / length;
        var directionY = (duct.End.Y - duct.Start.Y) / length;
        const double band = 0.04;
        Commit("Transaction.AddDuctStiffeners", document =>
        {
            for (var index = 1; index <= count; index++)
            {
                var at = Math.Min(index * spacing, length - band);
                var from = new Point2(duct.Start.X + directionX * at, duct.Start.Y + directionY * at);
                var to = new Point2(from.X + directionX * band, from.Y + directionY * band);
                document.StructuralElements.Add(new StructuralElement(Guid.NewGuid(), duct.LevelId, StructuralElementKind.DuctStiffener, "Rigidizador", from, to,
                    duct.Width + 0.08, duct.Height + 0.08, duct.Offset - 0.04) { GroupId = group });
            }
        });
        return count;
    }

    /// <summary>
    /// Draws a run through several points at once: a straight piece between each pair, all of one kind and joined end to end, in
    /// one undoable step, and grouped so the route moves and goes as one. Returns the pieces.
    /// </summary>
    public IReadOnlyList<Guid> AddRoute(Guid levelId, StructuralElementKind kind, IReadOnlyList<Point2> points, string name = "Ruta")
    {
        ArgumentNullException.ThrowIfNull(points);
        if (!MepElements.Runs.Contains(kind)) throw new InvalidDataException("A route is made of ducts, trays, conduits or pipes.");
        if (Document.Levels.All(level => level.Id != levelId)) throw new InvalidDataException("The level does not exist.");
        if (points.Count < 3) throw new InvalidDataException("A route needs at least three points.");
        if (points.Count > 200) throw new InvalidDataException("A route can have at most 200 points.");
        for (var index = 1; index < points.Count; index++)
            if (points[index].DistanceTo(points[index - 1]) < 0.1) throw new InvalidDataException("Two points of the route are too close together.");
        var ids = new List<Guid>();
        using var group = BeginGroup("Transaction.AddRoute");
        for (var index = 1; index < points.Count; index++)
            ids.Add(AddStructuralElement(levelId, kind, name, points[index - 1], points[index]));
        CreateElementGroup(ids, UniqueGroupName(name, ElementGroupKind.Assembly), ElementGroupKind.Assembly);
        group.Assimilate();
        return ids;
    }
}
