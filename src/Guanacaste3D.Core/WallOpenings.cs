using System.Text.Json.Serialization;

namespace Guanacaste3D.Core;

/// <summary>Types of wall-hosted openings.</summary>
public enum OpeningKind { Door, Window }

/// <summary>Aperture dimensions are meters; offset is measured from wall start to opening center.</summary>
public sealed record WallOpening(Guid Id, Guid WallId, OpeningKind Kind,
    double Offset, double Width, double Sill, double Height)
{
    /// <summary>The door or window type whose width and height drive this instance.</summary>
    public Guid OpeningTypeId { get; init; }

    /// <summary>Hinge on the other jamb: a door drawn hinged at the start of the wall is hinged at its end instead.</summary>
    public bool FlipHand { get; init; }

    /// <summary>Swing toward the other face of the wall.</summary>
    public bool FlipFacing { get; init; }

    [JsonIgnore] public double Begin => Offset - Width / 2;
    [JsonIgnore] public double End => Offset + Width / 2;
}

/// <summary>A portion of solid wall excluding an opening.</summary>
public readonly record struct WallSolid(double From, double To, double Bottom, double Top, double? TopAtFrom = null, double? TopAtTo = null)
{
    /// <summary>The height of the top at the start of the solid; for a flat top, the same as everywhere else.</summary>
    public double TopStart => TopAtFrom ?? Top;

    /// <summary>The height of the top at the end of the solid.</summary>
    public double TopEnd => TopAtTo ?? Top;

    /// <summary>Whether the top slopes, which makes the solid a wedge rather than a box.</summary>
    public bool Sloped => Math.Abs(TopStart - TopEnd) > 1e-9;

    /// <summary>The height of the top at a distance along the wall, continuing the slope past either end.</summary>
    public double TopHeightAt(double along) =>
        To - From < 1e-9 ? Top : TopStart + (TopEnd - TopStart) * (along - From) / (To - From);
}

/// <summary>Checks hosted openings and builds rectangular wall-solid sections.</summary>
public static class WallGeometry
{
    /// <summary>Validates hosting, dimensions, bounds, and collision rules for all openings.</summary>
    public static void Validate(IReadOnlyList<Wall> walls, IReadOnlyList<WallOpening> openings)
    {
        var hosts = walls.ToDictionary(w => w.Id);
        if (openings.Count > 200000 || openings.Select(o => o.Id).Distinct().Count() != openings.Count)
            throw new InvalidDataException("The opening count or identifiers are invalid.");
        foreach (var group in openings.GroupBy(o => o.WallId))
        {
            if (!hosts.TryGetValue(group.Key, out var host))
                throw new InvalidDataException("An opening has no host wall.");
            var sorted = group.OrderBy(o => o.Begin).ToArray();
            for (var i = 0; i < sorted.Length; i++)
            {
                var o = sorted[i];
                if (o.Id == Guid.Empty || !Enum.IsDefined(o.Kind) ||
                    !double.IsFinite(o.Offset) || !double.IsFinite(o.Width) ||
                    !double.IsFinite(o.Sill) || !double.IsFinite(o.Height) ||
                    o.Width < 0.3 || o.Height < 0.3 || o.Sill < 0 ||
                    o.Begin < 0.05 || o.End > host.Length - 0.05 ||
                    o.Sill + o.Height > host.Height - 0.05 ||
                    (host.TopProfile is not null && Math.Min(host.TopHeightAt(o.Begin), host.TopHeightAt(o.End)) < o.Sill + o.Height + 0.05) ||
                    (o.Kind == OpeningKind.Door && o.Sill != 0))
                    throw new InvalidDataException("An opening exceeds its host wall bounds.");
                if (i > 0 && sorted[i - 1].End + 0.05 > o.Begin)
                    throw new InvalidDataException("Hosted openings cannot overlap.");
            }
        }
    }

    /// <summary>
    /// Segments a wall into rectangular solids excluding hosted openings. Validated
    /// openings do not overlap; sorting once avoids scanning every opening for every
    /// interval (O(n log n) instead of O(n²) for walls with many hosted families).
    /// </summary>
    public static IReadOnlyList<WallSolid> Solids(Wall wall, IEnumerable<WallOpening> openings)
    {
        var hosted = openings.ToArray();
        if (hosted.Any(o => o.WallId != wall.Id))
            throw new ArgumentException("The opening does not belong to this wall.");

        var parts = new List<WallSolid>();
        var cursor = 0d;
        foreach (var opening in hosted.OrderBy(o => o.Begin))
        {
            // Keep the same minimum interval tolerance as the previous segmenter.
            if (opening.Begin - cursor >= 0.000001)
                parts.Add(new WallSolid(cursor, opening.Begin, 0, wall.Height));

            if (opening.Sill > 0)
                parts.Add(new WallSolid(opening.Begin, opening.End, 0, opening.Sill));

            var top = opening.Sill + opening.Height;
            if (top < wall.Height)
                parts.Add(new WallSolid(opening.Begin, opening.End, top, wall.Height));

            cursor = opening.End;
        }

        if (wall.Length - cursor >= 0.000001)
            parts.Add(new WallSolid(cursor, wall.Length, 0, wall.Height));

        var shaped = wall.TopProfile is { Count: > 1 } profile ? FollowProfile(wall, profile, parts) : parts;
        return wall.BaseOffset == 0 ? shaped : ReshapeBase(wall, shaped);
    }

    /// <summary>
    /// Moves the base of the solids to the wall's base: a raised base trims what lies below it, and a base taken below the level
    /// lengthens every piece that started at the level. Openings keep their place, measured from the level.
    /// </summary>
    private static List<WallSolid> ReshapeBase(Wall wall, List<WallSolid> parts)
    {
        var result = new List<WallSolid>();
        foreach (var part in parts)
        {
            if (wall.BaseOffset > 0)
            {
                var highest = Math.Max(part.Top, Math.Max(part.TopAtFrom ?? part.Top, part.TopAtTo ?? part.Top));
                if (highest <= wall.BaseOffset + 1e-9) continue;
                result.Add(part.Bottom < wall.BaseOffset ? part with { Bottom = wall.BaseOffset } : part);
            }
            else result.Add(part.Bottom <= 1e-9 ? part with { Bottom = wall.BaseOffset } : part);
        }
        return result;
    }

    /// <summary>
    /// Cuts every solid that reaches the top of the wall at each point of the profile, so that the top of each piece
    /// is a straight slope. Solids below an opening stay as they are.
    /// </summary>
    private static List<WallSolid> FollowProfile(Wall wall, List<Point2> profile, List<WallSolid> parts)
    {
        var result = new List<WallSolid>();
        foreach (var part in parts)
        {
            if (part.Top < wall.Height - 1e-9)
            {
                result.Add(part);
                continue;
            }
            var stations = profile.Select(point => point.X).Where(x => x > part.From + 1e-6 && x < part.To - 1e-6)
                .Append(part.From).Append(part.To).Order().ToArray();
            for (var index = 1; index < stations.Length; index++)
            {
                var from = wall.TopHeightAt(stations[index - 1]);
                var to = wall.TopHeightAt(stations[index]);
                result.Add(new WallSolid(stations[index - 1], stations[index], part.Bottom, Math.Max(from, to), from, to));
            }
        }
        return result;
    }

    /// <summary>
    /// Returns the leaf thickness an opening carries inside its host wall: the type's
    /// depth, capped at the wall thickness so a thin wall never gets a leaf thicker
    /// than itself. Zero means the opening stays a hole.
    /// </summary>
    public static double LeafDepth(OpeningType type, Wall wall) =>
        type.Depth > 0 ? Math.Min(type.Depth, wall.Thickness) : 0;

    /// <summary>Calculates net wall volume after subtracting hosted openings.</summary>
    public static double NetVolume(Wall wall, IEnumerable<WallOpening> openings) =>
        Solids(wall, openings).Sum(part =>
            (part.To - part.From) * ((part.TopStart + part.TopEnd) / 2 - part.Bottom)) * wall.Thickness;
}
