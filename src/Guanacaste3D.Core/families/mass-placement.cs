namespace Guanacaste3D.Core;

/// <summary>One instance of a mass placement: where it stands, how it is turned (radians) and its size factor.</summary>
public readonly record struct ComponentPlacement(Point2 At, double Rotation, double Scale);

/// <summary>How a mass placement spreads its instances, as Lumion's mass placement and cluster tools do.</summary>
public sealed record MassPlacementOptions
{
    /// <summary>Distance in meters between neighbouring instances.</summary>
    public double Spacing { get; init; } = 4;

    /// <summary>How much each instance's size may differ from the family, as a fraction (0.15 = ±15 %).</summary>
    public double ScaleVariation { get; init; } = 0.15;

    /// <summary>Turns each instance at random; otherwise instances along a path face along it.</summary>
    public bool RandomRotation { get; init; } = true;

    /// <summary>The same seed repeats the same arrangement.</summary>
    public int Seed { get; init; } = 1;

    public void Validate()
    {
        if (!double.IsFinite(Spacing) || Spacing is < 0.1 or > 500)
            throw new InvalidDataException("The mass placement spacing must be between 0.1 and 500 meters.");
        if (!double.IsFinite(ScaleVariation) || ScaleVariation is < 0 or > 0.9)
            throw new InvalidDataException("The mass placement size variation must be between 0 and 90 percent.");
    }
}

/// <summary>
/// Spreads many instances of a family at once: evenly along a path (a row of street trees, parked cars) or scattered
/// through an area with no two closer than the spacing (a grove, a flower bed), each with its own turn and size.
/// </summary>
public static class MassPlacement
{
    /// <summary>The most instances one placement may create, so a slip of the spacing cannot flood the model.</summary>
    public const int MaximumInstances = 2000;

    /// <summary>Candidates tried around each placed point before it stops spawning neighbours (Bridson's k).</summary>
    private const int Attempts = 30;

    /// <summary>Instances every <see cref="MassPlacementOptions.Spacing"/> meters along a polyline, starting at its first point.</summary>
    public static IReadOnlyList<ComponentPlacement> AlongPath(IReadOnlyList<Point2> path, MassPlacementOptions options)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (path.Count < 2 || path.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            throw new InvalidDataException("A mass placement path needs at least two valid points.");
        var length = 0.0;
        for (var i = 1; i < path.Count; i++) length += path[i - 1].DistanceTo(path[i]);
        // Repeated sketch vertices do not define a usable path; do not silently place nothing.
        if (length < 1e-9)
            throw new InvalidDataException("A mass placement path needs at least two valid points.");
        if (length / options.Spacing + 1 > MaximumInstances)
            throw new InvalidDataException("The mass placement would create too many instances; increase the spacing.");

        var random = new Random(options.Seed);
        var placements = new List<ComponentPlacement>();
        // Distance from the start of the current segment to the next instance; carrying it over keeps the
        // spacing even across corners instead of restarting at every vertex.
        var next = 0.0;
        for (var i = 1; i < path.Count; i++)
        {
            var (a, b) = (path[i - 1], path[i]);
            var segment = a.DistanceTo(b);
            if (segment < 1e-9) continue;
            var heading = Math.Atan2(b.Y - a.Y, b.X - a.X);
            for (; next <= segment + 1e-9; next += options.Spacing)
            {
                var t = Math.Min(next / segment, 1);
                placements.Add(Instance(new Point2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t), heading, options, random));
            }
            next -= segment;
        }
        return placements;
    }

    /// <summary>
    /// Instances scattered through a closed boundary by Poisson-disk sampling: they fill it evenly, yet never in rows,
    /// and no two stand closer than the spacing.
    /// </summary>
    public static IReadOnlyList<ComponentPlacement> InArea(IReadOnlyList<Point2> boundary, MassPlacementOptions options)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (boundary.Count < 3 || boundary.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            throw new InvalidDataException("A mass placement area needs at least three valid points.");
        var area = Math.Abs(SignedArea(boundary));
        if (area < 1e-6) throw new InvalidDataException("A mass placement area needs at least three valid points.");
        // Poisson-disk samples cover about one per 1.5·r² at most; refuse before sampling, not after.
        if (area / (options.Spacing * options.Spacing) / 1.5 > MaximumInstances)
            throw new InvalidDataException("The mass placement would create too many instances; increase the spacing.");

        var (minX, maxX) = (boundary.Min(point => point.X), boundary.Max(point => point.X));
        var (minY, maxY) = (boundary.Min(point => point.Y), boundary.Max(point => point.Y));
        var radius = options.Spacing;
        // A cell this size holds at most one sample, so a neighbour check reads only the surrounding 5 × 5 cells.
        var cell = radius / Math.Sqrt(2);
        var grid = new Dictionary<(int, int), Point2>();
        (int, int) Key(Point2 point) => ((int)Math.Floor((point.X - minX) / cell), (int)Math.Floor((point.Y - minY) / cell));
        bool Inside(Point2 point) => point.X >= minX && point.X <= maxX && point.Y >= minY && point.Y <= maxY &&
                                     RoomRegionGeometry.Contains(boundary, point);
        bool Free(Point2 point)
        {
            var (column, row) = Key(point);
            for (var dx = -2; dx <= 2; dx++)
                for (var dy = -2; dy <= 2; dy++)
                    if (grid.TryGetValue((column + dx, row + dy), out var other) && other.DistanceTo(point) < radius)
                        return false;
            return true;
        }

        var random = new Random(options.Seed);
        var samples = new List<Point2>();
        var active = new List<Point2>();
        void Accept(Point2 point)
        {
            // The area-based estimate above is only a heuristic, not a strict upper bound
            // on Poisson-disk density. Enforce the actual limit before allocating another sample.
            if (samples.Count >= MaximumInstances)
                throw new InvalidDataException("The mass placement would create too many instances; increase the spacing.");
            grid[Key(point)] = point;
            samples.Add(point);
            active.Add(point);
        }

        // Seed from a random interior point; a sliver the random tries miss still gets its label point.
        for (var tries = 0; tries < 200 && samples.Count == 0; tries++)
        {
            var candidate = new Point2(minX + random.NextDouble() * (maxX - minX), minY + random.NextDouble() * (maxY - minY));
            if (Inside(candidate)) Accept(candidate);
        }
        if (samples.Count == 0 && RoomRegionGeometry.LabelPoint(boundary) is var label && Inside(label)) Accept(label);

        while (active.Count > 0)
        {
            var index = random.Next(active.Count);
            var origin = active[index];
            var spawned = false;
            for (var attempt = 0; attempt < Attempts; attempt++)
            {
                var angle = random.NextDouble() * Math.Tau;
                var distance = radius * (1 + random.NextDouble());
                var candidate = new Point2(origin.X + Math.Cos(angle) * distance, origin.Y + Math.Sin(angle) * distance);
                if (!Inside(candidate) || !Free(candidate)) continue;
                Accept(candidate);
                spawned = true;
                break;
            }
            if (!spawned)
            {
                active[index] = active[^1];
                active.RemoveAt(active.Count - 1);
            }
        }
        return samples.Select(point => Instance(point, 0, options, random)).ToArray();
    }

    private static ComponentPlacement Instance(Point2 at, double heading, MassPlacementOptions options, Random random)
    {
        var rotation = options.RandomRotation ? random.NextDouble() * Math.Tau : heading;
        var scale = 1 + (random.NextDouble() * 2 - 1) * options.ScaleVariation;
        return new ComponentPlacement(at, rotation, scale);
    }

    private static double SignedArea(IReadOnlyList<Point2> polygon)
    {
        var sum = 0.0;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            sum += polygon[j].X * polygon[i].Y - polygon[i].X * polygon[j].Y;
        return sum / 2;
    }
}
