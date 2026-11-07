namespace Guanacaste3D.Core;

/// <summary>Resolves stable elevation markers and their visible plan footprint from model bounds.</summary>
public static class ElevationViewGeometry
{
    private const double MarkerClearance = 3;
    private const double FieldMargin = 1;

    /// <summary>
    /// Returns the stored marker while it still covers the building. If geometry grows outside it, returns an automatically
    /// fitted marker; views without a stored marker always follow the current model bounds.
    /// </summary>
    public static ElevationMarker Resolve(BuildingDocument document, ProjectView view)
    {
        var bounds = ModelBounds(document);
        if (view.ElevationMarker is { } stored && Covers(stored, view.Orientation, bounds)) return stored;
        return Fit(bounds, view.Orientation);
    }

    /// <summary>Returns the four corners of the plan area seen by an elevation, starting at its marker.</summary>
    public static IReadOnlyList<Point2> Coverage(ElevationMarker marker, ViewOrientation orientation)
    {
        var direction = Direction(orientation);
        var across = new Point2(-direction.Y, direction.X);
        var halfWidth = marker.Width / 2;
        var far = new Point2(marker.Position.X + direction.X * marker.Depth, marker.Position.Y + direction.Y * marker.Depth);
        return
        [
            new(marker.Position.X - across.X * halfWidth, marker.Position.Y - across.Y * halfWidth),
            new(marker.Position.X + across.X * halfWidth, marker.Position.Y + across.Y * halfWidth),
            new(far.X + across.X * halfWidth, far.Y + across.Y * halfWidth),
            new(far.X - across.X * halfWidth, far.Y - across.Y * halfWidth)
        ];
    }

    /// <summary>
    /// Creates coverage from a user-chosen marker position, expanding width and depth enough to keep the current building
    /// visible. A marker moved behind the facade is allowed temporarily and will auto-fit on the next resolution.
    /// </summary>
    public static ElevationMarker AtPosition(BuildingDocument document, ViewOrientation orientation, Point2 position)
    {
        var bounds = ModelBounds(document);
        var direction = Direction(orientation);
        var corners = new[]
        {
            new Point2(bounds.MinX, bounds.MinY), new Point2(bounds.MaxX, bounds.MinY),
            new Point2(bounds.MaxX, bounds.MaxY), new Point2(bounds.MinX, bounds.MaxY)
        };
        var relative = corners.Select(point => new Point2(point.X - position.X, point.Y - position.Y)).ToArray();
        var width = relative.Max(point => Math.Abs(point.X * -direction.Y + point.Y * direction.X)) * 2 + FieldMargin * 2;
        var depth = relative.Max(point => point.X * direction.X + point.Y * direction.Y) + FieldMargin;
        return new ElevationMarker(position, Math.Max(0.5, width), Math.Max(0.5, depth));
    }

    /// <summary>Returns the unit plan direction in which an elevation looks.</summary>
    public static Point2 Direction(ViewOrientation orientation) => orientation switch
    {
        ViewOrientation.North => new(0, 1),
        ViewOrientation.South => new(0, -1),
        ViewOrientation.East => new(1, 0),
        ViewOrientation.West => new(-1, 0),
        _ => throw new InvalidDataException("An elevation requires an orientation.")
    };

    /// <summary>Calculates a forgiving plan bound for all primary architectural geometry.</summary>
    private static PlanBounds ModelBounds(BuildingDocument document)
    {
        var points = document.Walls.SelectMany(wall => new[] { wall.Start, wall.End })
            .Concat(document.Floors.SelectMany(floor => floor.Boundary))
            .Concat(document.Columns.Select(column => column.Position)).ToArray();
        return points.Length == 0
            ? new PlanBounds(-5, -5, 5, 5)
            : new PlanBounds(points.Min(point => point.X), points.Min(point => point.Y),
                points.Max(point => point.X), points.Max(point => point.Y));
    }

    /// <summary>Creates a marker outside the relevant facade with enough depth to include the opposite side.</summary>
    private static ElevationMarker Fit(PlanBounds bounds, ViewOrientation orientation)
    {
        var centerX = (bounds.MinX + bounds.MaxX) / 2;
        var centerY = (bounds.MinY + bounds.MaxY) / 2;
        var horizontalWidth = bounds.MaxX - bounds.MinX + FieldMargin * 2;
        var verticalWidth = bounds.MaxY - bounds.MinY + FieldMargin * 2;
        return orientation switch
        {
            ViewOrientation.North => new(new(centerX, bounds.MinY - MarkerClearance), horizontalWidth,
                bounds.MaxY - bounds.MinY + MarkerClearance + FieldMargin),
            ViewOrientation.South => new(new(centerX, bounds.MaxY + MarkerClearance), horizontalWidth,
                bounds.MaxY - bounds.MinY + MarkerClearance + FieldMargin),
            ViewOrientation.East => new(new(bounds.MinX - MarkerClearance, centerY), verticalWidth,
                bounds.MaxX - bounds.MinX + MarkerClearance + FieldMargin),
            ViewOrientation.West => new(new(bounds.MaxX + MarkerClearance, centerY), verticalWidth,
                bounds.MaxX - bounds.MinX + MarkerClearance + FieldMargin),
            _ => throw new InvalidDataException("An elevation requires an orientation.")
        };
    }

    /// <summary>Checks every model-bound corner against the marker's oriented rectangular field.</summary>
    private static bool Covers(ElevationMarker marker, ViewOrientation orientation, PlanBounds bounds) =>
        new[]
        {
            new Point2(bounds.MinX, bounds.MinY), new Point2(bounds.MaxX, bounds.MinY),
            new Point2(bounds.MaxX, bounds.MaxY), new Point2(bounds.MinX, bounds.MaxY)
        }.All(point => Contains(marker, orientation, point));

    /// <summary>Checks a model point against an elevation's forward depth and transverse width.</summary>
    private static bool Contains(ElevationMarker marker, ViewOrientation orientation, Point2 point)
    {
        var direction = Direction(orientation);
        var relative = new Point2(point.X - marker.Position.X, point.Y - marker.Position.Y);
        var forward = relative.X * direction.X + relative.Y * direction.Y;
        var transverse = relative.X * -direction.Y + relative.Y * direction.X;
        return forward >= 0 && forward <= marker.Depth && Math.Abs(transverse) <= marker.Width / 2;
    }
}
