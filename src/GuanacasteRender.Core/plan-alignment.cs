namespace Guanacaste3D.Core;

/// <summary>Orientation of a reference line drawn through an existing corner.</summary>
public enum PlanGuideAxis
{
    Vertical,
    Horizontal
}

/// <summary>A reference line through an existing corner that the drawing point has reached on one axis.</summary>
public readonly record struct PlanAlignmentGuide(PlanGuideAxis Axis, Point2 Corner);

/// <summary>The model-space rectangle currently visible in a plan viewport.</summary>
public readonly record struct PlanBounds(double MinX, double MinY, double MaxX, double MaxY)
{
    /// <summary>Returns whether a point lies inside the rectangle, edges included.</summary>
    public bool Contains(Point2 point) =>
        point.X >= MinX && point.X <= MaxX && point.Y >= MinY && point.Y <= MaxY;
}

/// <summary>Finds existing corners that a drawing point lines up with on either plan axis.</summary>
public static class PlanAlignment
{
    /// <summary>
    /// Returns one guide per corner and axis where the point lies within tolerance of the corner.
    /// Corners outside the visible bounds are ignored, so a guide only refers to what is on screen.
    /// </summary>
    public static IReadOnlyList<PlanAlignmentGuide> Find(
        Point2 point,
        IEnumerable<Point2> corners,
        PlanBounds visible,
        double tolerance)
    {
        if (!double.IsFinite(tolerance) || tolerance <= 0)
            throw new ArgumentOutOfRangeException(nameof(tolerance));

        var guides = new List<PlanAlignmentGuide>();
        foreach (var corner in corners)
        {
            if (!visible.Contains(corner)) continue;
            if (Math.Abs(point.X - corner.X) <= tolerance) guides.Add(new(PlanGuideAxis.Vertical, corner));
            if (Math.Abs(point.Y - corner.Y) <= tolerance) guides.Add(new(PlanGuideAxis.Horizontal, corner));
        }
        return guides;
    }
}
