using Guanacaste3D.Core.Interop;

namespace Guanacaste3D.Core;

/// <summary>
/// The plot the building stands on: a rectangle of the given width along the street and depth away from it. The street
/// runs along the plot's south edge (low Y). Without a saved plot the location view proposes one around the building.
/// </summary>
public sealed record SiteLot(double Width, double Depth, string StreetName)
{
    /// <summary>Whether the dimensions are usable for drawing.</summary>
    public bool IsValid => double.IsFinite(Width) && double.IsFinite(Depth) && Width is >= 1 and <= 2000 &&
        Depth is >= 1 and <= 2000 && StreetName is not null && StreetName.Length <= 120;
}

/// <summary>
/// Draws the location plan ("plano de localización") in model meters: the plot, the street in front of it, the
/// neighbouring plots, the ground-floor footprint and a north arrow. The sheet fits it to a standard scale and prints
/// the scale in the view caption, as the reference drawings do.
/// </summary>
public static class LocationPlan
{
    private static readonly (double, double, double) Ink = (0, 0, 0);
    private static readonly (double, double, double) Light = (0.55, 0.55, 0.55);
    private const double FrontSetback = 3;
    private const double SideMargin = 3;
    private const double StreetWidth = 6;

    /// <summary>Returns the saved plot, or one sized around the ground-floor walls when none is saved.</summary>
    public static SiteLot LotFor(BuildingDocument document)
    {
        if (document.Lot is { IsValid: true } saved) return saved;
        var (min, max) = Footprint(document);
        return new(Math.Max(6, max.X - min.X + 2 * SideMargin), Math.Max(6, max.Y - min.Y + FrontSetback + SideMargin),
            DefaultLabels.Get("Location.Street"));
    }

    /// <summary>Builds the drawing; coordinates are meters with the street along the bottom.</summary>
    public static IReadOnlyList<SheetPrimitive> Primitives(BuildingDocument document)
    {
        var lot = LotFor(document);
        var (min, max) = Footprint(document);
        var left = (min.X + max.X) / 2 - lot.Width / 2;
        var front = min.Y - FrontSetback;
        var graphics = new List<SheetPrimitive>();

        SheetPolygon Rectangle(double x, double y, double width, double depth, double weight, (double, double, double) color,
            (double, double, double)? fill = null) =>
            new([new(x, y), new(x + width, y), new(x + width, y + depth), new(x, y + depth)], weight, color, fill);

        // Neighbouring plots, drawn lightly like the lots beside the subject plot.
        foreach (var x in new[] { left - lot.Width, left + lot.Width })
            graphics.Add(Rectangle(x, front, lot.Width, lot.Depth, 0.1, Light));
        // The street: a band in front of the plot with its name.
        var streetLeft = left - lot.Width;
        var streetWidth = lot.Width * 3;
        graphics.Add(new SheetPolyline([new(streetLeft, front), new(streetLeft + streetWidth, front)], 0.2, Ink));
        graphics.Add(new SheetPolyline([new(streetLeft, front - StreetWidth), new(streetLeft + streetWidth, front - StreetWidth)], 0.2, Ink));
        graphics.Add(new SheetText(new(left + lot.Width / 2, front - StreetWidth / 2),
            lot.StreetName.ToUpperInvariant(), 3, Ink));
        // The subject plot, heavy, with its label and the building footprint inside.
        graphics.Add(Rectangle(left, front, lot.Width, lot.Depth, 0.6, Ink, (0.93, 0.93, 0.93)));
        graphics.Add(new SheetText(new(left + lot.Width / 2, front + lot.Depth - FrontSetback / 2), DefaultLabels.Get("Location.Lot"), 3, Ink, true));
        foreach (var wall in document.Walls.Where(wall => wall.LevelId == document.Levels[0].Id))
            graphics.Add(WallOutline(wall));
        // North arrow in the free corner above the street, sized to the plot.
        var size = Math.Max(lot.Width, lot.Depth) * 0.12;
        graphics.AddRange(NorthArrow(new(left + lot.Width + lot.Width * 0.5, front + lot.Depth - size * 1.2), size));
        return graphics;
    }

    /// <summary>Ground-floor bounding box; a project without walls uses a small default footprint.</summary>
    private static (Point2 Min, Point2 Max) Footprint(BuildingDocument document)
    {
        var points = document.Walls.Where(wall => document.Levels.Count > 0 && wall.LevelId == document.Levels[0].Id)
            .SelectMany(wall => new[] { wall.Start, wall.End }).ToList();
        return points.Count == 0 ? (new(0, 0), new(8, 6)) :
            (new(points.Min(point => point.X), points.Min(point => point.Y)), new(points.Max(point => point.X), points.Max(point => point.Y)));
    }

    /// <summary>A wall as a filled rectangle of its thickness.</summary>
    private static SheetPolygon WallOutline(Wall wall)
    {
        var length = Math.Max(wall.Length, 1e-9);
        var nx = -(wall.End.Y - wall.Start.Y) / length * wall.Thickness / 2;
        var ny = (wall.End.X - wall.Start.X) / length * wall.Thickness / 2;
        return new([new(wall.Start.X + nx, wall.Start.Y + ny), new(wall.End.X + nx, wall.End.Y + ny),
            new(wall.End.X - nx, wall.End.Y - ny), new(wall.Start.X - nx, wall.Start.Y - ny)], 0.15, Ink, Ink);
    }

    /// <summary>A circle with a filled arrow and the letter N, pointing to +Y (north).</summary>
    private static IEnumerable<SheetPrimitive> NorthArrow(Point2 center, double size)
    {
        yield return new SheetPolygon(Enumerable.Range(0, 32).Select(index => new Point2(
            center.X + size * 0.5 * Math.Cos(index * Math.PI / 16), center.Y + size * 0.5 * Math.Sin(index * Math.PI / 16))).ToArray(),
            0.25, Ink, null);
        yield return new SheetPolygon([new(center.X, center.Y + size * 0.5), new(center.X - size * 0.18, center.Y - size * 0.35),
            new(center.X, center.Y - size * 0.15), new(center.X + size * 0.18, center.Y - size * 0.35)], 0.2, Ink, Ink);
        yield return new SheetText(new(center.X, center.Y + size * 0.5 + size * 0.35), "N", 3.5, Ink, true);
    }
}
