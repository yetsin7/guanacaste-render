namespace Guanacaste3D.Core;

/// <summary>Closed prismatic components shared by plan display and the 3D mesh.</summary>
public static class CirculationGeometry
{
    /// <summary>Builds a straight stair or guard; dimensions are modeling defaults, not a compliance certification.</summary>
    public static IEnumerable<StructuralElement> Parts(StructuralElement element)
    {
        if (element.Length < 0.05) yield break;
        if (element.Kind == StructuralElementKind.Stair)
        {
            var count = StairGeometry.RiserCount(element);
            for (var index = 0; index < count; index++)
                yield return element with { Start = Along(element, (double)index / count),
                    End = Along(element, (double)(index + 1) / count), Height = element.Height * (index + 1) / count };
        }
        else if (element.Kind == StructuralElementKind.Railing)
        {
            var intervals = (int)Math.Ceiling(element.Length / 1.20);
            for (var index = 0; index <= intervals; index++)
            {
                var position = Along(element, (double)index / intervals);
                yield return element with { Start = position, End = position };
            }
            foreach (var fraction in new[] { 0.5, 1.0 })
                yield return element with { Offset = element.Offset + element.Height * fraction - 0.05, Height = 0.05 };
        }
    }

    /// <summary>Bounds new circulation runs before meshing and rejects degenerate or non-finite geometry.</summary>
    public static void Validate(BuildingDocument document)
    {
        foreach (var element in document.StructuralElements.Where(element =>
                     element.Kind is StructuralElementKind.Stair or StructuralElementKind.Railing))
            if (element.Id == Guid.Empty || !document.Levels.Any(level => level.Id == element.LevelId) ||
                !double.IsFinite(element.Length) || element.Length is < 0.05 or > 1000 ||
                !double.IsFinite(element.Height) || element.Height is <= 0 or > 100 ||
                !double.IsFinite(element.Width) || element.Width is < 0.03 or > 10 || !double.IsFinite(element.Offset))
                throw new InvalidDataException("Invalid stair or railing geometry.");
    }

    /// <summary>Interpolates a component station along the run axis.</summary>
    private static Point2 Along(StructuralElement element, double fraction) => new(
        element.Start.X + (element.End.X - element.Start.X) * fraction,
        element.Start.Y + (element.End.Y - element.Start.Y) * fraction);
}
