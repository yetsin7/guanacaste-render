using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core;

/// <summary>One straight flight of a stair: its run axis, the height its first step starts at and how much it rises.</summary>
public readonly record struct StairFlight(Point2 Start, Point2 End, double Bottom, double Rise, int Risers);

/// <summary>Kinds of advisory finding about a stair's comfort; the value is the measured meters, the limit the guideline.</summary>
public enum StairWarningKind { RiserTooHigh, RiserTooLow, TreadTooShallow, UncomfortablePace }

/// <summary>One advisory finding about a stair, with the measured value and the limit it crossed, in meters.</summary>
public readonly record struct StairWarning(StairWarningKind Kind, double Value, double Limit);

/// <summary>
/// Stair layouts by component, as Revit offers them: straight runs, an L with a quarter landing, a U (switchback) with
/// a half landing, and a spiral around a central column. The two placement points give the direction and the first
/// flight's run (to the far side of the landing for L and U); a spiral is centered on the first point, starts toward
/// the second and has the stair width as its diameter. Risers never exceed <see cref="MaximumRiser"/>; the flights
/// share them as evenly as possible. Heights are relative to the stair's level, starting at its offset.
/// </summary>
public static class StairGeometry
{
    /// <summary>Modeling default for the maximum riser, in meters; this is not a code-compliance certification.</summary>
    public const double MaximumRiser = 0.175;

    /// <summary>Minimum fallback run when placement leaves no room before a landing.</summary>
    public const double Tread = 0.28;

    private const double TreadThickness = 0.05;
    private const double SwitchbackGap = 0.10;
    private const double ColumnRadius = 0.10;

    /// <summary>Largest riser (contrahuella) a stair should have before the editor warns, in meters.</summary>
    public const double RecommendedMaximumRiser = 0.18;

    /// <summary>Smallest riser a stair should have before the editor warns, in meters.</summary>
    public const double RecommendedMinimumRiser = 0.10;

    /// <summary>Smallest tread going (huella) a stair should have before the editor warns, in meters.</summary>
    public const double RecommendedMinimumTread = 0.28;

    /// <summary>Shares one riser count across solids, walking paths, and generated guards, including low stairs.</summary>
    public static int RiserCount(double height) => Math.Max(2, (int)Math.Ceiling(height / MaximumRiser - 1e-9));

    /// <summary>Riser count that honors the stair's requested riser height when it has one.</summary>
    public static int RiserCount(StructuralElement stair) => stair.RiserHeight > 0
        ? Math.Max(2, (int)Math.Ceiling(stair.Height / stair.RiserHeight - 1e-9))
        : RiserCount(stair.Height);

    /// <summary>The going (huella) each tread actually has, per flight, in meters; a spiral reports its walking-line going.</summary>
    public static double ActualTread(StructuralElement stair)
    {
        if (stair.Style == ElementStyle.Spiral)
        {
            var (_, radius, _, step, _) = Spiral(stair);
            return radius * 0.6 * step;
        }
        var flights = Flights(stair);
        return flights.Count == 0 ? 0 : flights.Min(flight => flight.Start.DistanceTo(flight.End) / flight.Risers);
    }

    /// <summary>Plan length the straight or composite run needs so every tread has the requested going.</summary>
    public static double RunLengthForTread(StructuralElement stair, double tread)
    {
        var risers = RiserCount(stair);
        return stair.Style is ElementStyle.LShaped or ElementStyle.UShaped
            ? stair.Width + risers / 2 * tread : risers * tread;
    }

    /// <summary>Advisory findings for a stair's riser and tread; none of them blocks the design.</summary>
    public static IReadOnlyList<StairWarning> Check(StructuralElement stair)
    {
        var warnings = new List<StairWarning>();
        if (stair.Kind != StructuralElementKind.Stair || stair.Length < 0.05 && stair.Style != ElementStyle.Spiral) return warnings;
        var riser = stair.Height / RiserCount(stair);
        var tread = ActualTread(stair);
        if (riser > RecommendedMaximumRiser + 1e-6) warnings.Add(new(StairWarningKind.RiserTooHigh, riser, RecommendedMaximumRiser));
        if (riser < RecommendedMinimumRiser - 1e-6) warnings.Add(new(StairWarningKind.RiserTooLow, riser, RecommendedMinimumRiser));
        if (tread < RecommendedMinimumTread - 1e-6) warnings.Add(new(StairWarningKind.TreadTooShallow, tread, RecommendedMinimumTread));
        // Blondel's rule: two risers plus one tread should be a comfortable pace, 0.60 to 0.65 m.
        var pace = 2 * riser + tread;
        if (warnings.Count == 0 && tread > 0 && (pace < 0.57 || pace > 0.67)) warnings.Add(new(StairWarningKind.UncomfortablePace, pace, 0.63));
        return warnings;
    }

    /// <summary>Whether the stair is laid out by this class rather than as a single straight run.</summary>
    public static bool IsComposite(StructuralElement stair) =>
        stair.Kind == StructuralElementKind.Stair && stair.Style is ElementStyle.LShaped or ElementStyle.UShaped or ElementStyle.Spiral;

    /// <summary>The straight flights of a stair; a spiral has none.</summary>
    public static IReadOnlyList<StairFlight> Flights(StructuralElement stair)
    {
        var risers = RiserCount(stair);
        var riser = stair.Height / risers;
        if (stair.Style == ElementStyle.Spiral || stair.Length < 0.05) return [];
        if (stair.Style is not (ElementStyle.LShaped or ElementStyle.UShaped))
            return [new StairFlight(stair.Start, stair.End, stair.Offset, stair.Height, risers)];

        var (direction, left) = Axes(stair);
        var width = stair.Width;
        var firstRun = Math.Max(Tread, stair.Length - width);
        var firstRisers = risers / 2;
        var secondRisers = risers - firstRisers;
        var going = firstRun / firstRisers;
        var landingTop = stair.Offset + firstRisers * riser;
        var first = new StairFlight(stair.Start, Offset(stair.Start, direction, firstRun), stair.Offset, firstRisers * riser, firstRisers);
        var landingCenter = Offset(stair.Start, direction, firstRun + width / 2);
        StairFlight second;
        if (stair.Style == ElementStyle.LShaped)
        {
            var start = Offset(landingCenter, left, width / 2);
            second = new StairFlight(start, Offset(start, left, secondRisers * going), landingTop, secondRisers * riser, secondRisers);
        }
        else
        {
            var start = Offset(Offset(stair.Start, direction, firstRun), left, width + SwitchbackGap);
            second = new StairFlight(start, Offset(start, direction, -secondRisers * going), landingTop, secondRisers * riser, secondRisers);
        }
        return [first, second];
    }

    /// <summary>The solids of an L, U or spiral stair: steps (or treads), the landing, and the column of a spiral.</summary>
    public static IEnumerable<ElementPart> Parts(StructuralElement stair)
    {
        if (stair.Style == ElementStyle.Spiral)
        {
            foreach (var part in SpiralParts(stair)) yield return part;
            yield break;
        }
        if (stair.Length < 0.05) yield break;
        var flights = Flights(stair);
        foreach (var flight in flights)
        {
            var riser = flight.Rise / flight.Risers;
            for (var index = 0; index < flight.Risers; index++)
            {
                var from = ArchitecturalShapes.Lerp(flight.Start, flight.End, (double)index / flight.Risers);
                var to = ArchitecturalShapes.Lerp(flight.Start, flight.End, (double)(index + 1) / flight.Risers);
                var top = flight.Bottom + riser * (index + 1);
                yield return new ElementPart(ArchitecturalShapes.Strip(from, to, 0, stair.Width), flight.Bottom, top, SurfaceKind.Structure);
            }
        }
        if (flights.Count == 2) yield return Landing(stair, flights[0]);
    }

    /// <summary>Plan path of a person climbing the stair, from the first riser to the top, for the "up" arrow.</summary>
    public static IReadOnlyList<Point2> WalkLine(StructuralElement stair)
    {
        if (stair.Style == ElementStyle.Spiral)
        {
            var (center, radius, startAngle, step, risers) = Spiral(stair);
            var walk = radius * 0.6;
            return Enumerable.Range(0, risers + 1).Select(index => new Point2(
                center.X + walk * Math.Cos(startAngle + step * index), center.Y + walk * Math.Sin(startAngle + step * index))).ToArray();
        }
        var flights = Flights(stair);
        if (flights.Count < 2) return [stair.Start, stair.End];
        var (direction, left) = Axes(stair);
        var landing = Offset(Offset(flights[0].End, direction, stair.Width / 2),
            left, stair.Style == ElementStyle.UShaped ? (stair.Width + SwitchbackGap) / 2 : 0);
        return [flights[0].Start, flights[0].End, landing, flights[1].Start, flights[1].End];
    }

    /// <summary>
    /// Guard lines for a stair: both sides of every flight, sloped with it, and the open sides of the landing, level.
    /// For a spiral, an outer handrail following each tread.
    /// </summary>
    public static IReadOnlyList<(Point2 Start, Point2 End, double Offset, double Rise)> GuardLines(StructuralElement stair)
    {
        var lines = new List<(Point2, Point2, double, double)>();
        var lateral = stair.Width / 2;
        if (stair.Style == ElementStyle.Spiral)
        {
            var (center, radius, startAngle, step, risers) = Spiral(stair);
            var riser = stair.Height / risers;
            var rail = radius - 0.03;
            for (var index = 0; index < risers; index++)
            {
                var a = startAngle + step * index;
                var b = a + step;
                lines.Add((new Point2(center.X + rail * Math.Cos(a), center.Y + rail * Math.Sin(a)),
                    new Point2(center.X + rail * Math.Cos(b), center.Y + rail * Math.Sin(b)), stair.Offset + riser * index, riser));
            }
            return lines;
        }
        var flights = Flights(stair);
        foreach (var flight in flights)
        {
            var length = flight.Start.DistanceTo(flight.End);
            if (length < 1e-6) continue;
            var normal = new Point2(-(flight.End.Y - flight.Start.Y) / length, (flight.End.X - flight.Start.X) / length);
            foreach (var side in new[] { -1.0, 1.0 })
                lines.Add((Offset(flight.Start, normal, lateral * side), Offset(flight.End, normal, lateral * side),
                    flight.Bottom, flight.Rise));
        }
        if (flights.Count == 2)
        {
            var outline = Landing(stair, flights[0]).Footprint;
            var top = flights[0].Bottom + flights[0].Rise;
            foreach (var edge in StairLandingGuards.OpenEdges(outline, flights, stair.Width, top))
                lines.Add((edge.Start, edge.End, top, 0));
        }
        return lines;
    }

    /// <summary>Builds the shared landing slab between the two flight mouths.</summary>
    private static ElementPart Landing(StructuralElement stair, StairFlight first)
    {
        var (direction, left) = Axes(stair);
        var top = first.Bottom + first.Rise;
        var near = first.End;
        var far = Offset(near, direction, stair.Width);
        var reach = stair.Style == ElementStyle.UShaped ? stair.Width * 1.5 + SwitchbackGap : stair.Width / 2;
        Point2[] outline =
        [
            Offset(near, left, -stair.Width / 2), Offset(far, left, -stair.Width / 2),
            Offset(far, left, reach), Offset(near, left, reach)
        ];
        return new ElementPart(outline, Math.Max(stair.Offset, top - 0.20), top, SurfaceKind.Structure);
    }

    /// <summary>Builds radial treads and their supporting central column from the shared spiral layout.</summary>
    private static IEnumerable<ElementPart> SpiralParts(StructuralElement stair)
    {
        var (center, radius, startAngle, step, risers) = Spiral(stair);
        var riser = stair.Height / risers;
        const int arcSteps = 4;
        for (var index = 0; index < risers; index++)
        {
            var a = startAngle + step * index;
            var outline = new List<Point2>();
            for (var k = 0; k <= arcSteps; k++)
            {
                var angle = a + step * k / arcSteps;
                outline.Add(new Point2(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle)));
            }
            outline.Add(new Point2(center.X + ColumnRadius * Math.Cos(a + step), center.Y + ColumnRadius * Math.Sin(a + step)));
            outline.Add(new Point2(center.X + ColumnRadius * Math.Cos(a), center.Y + ColumnRadius * Math.Sin(a)));
            var top = stair.Offset + riser * (index + 1);
            yield return new ElementPart(outline, top - TreadThickness, top, SurfaceKind.Structure);
        }
        yield return new ElementPart(ArchitecturalShapes.PointFootprint(center, ColumnRadius * 2, true),
            stair.Offset, stair.Offset + stair.Height + 0.9, SurfaceKind.OpeningFrame);
    }

    /// <summary>Center, outer radius, start angle, angle per tread (counter-clockwise) and riser count of a spiral.</summary>
    private static (Point2 Center, double Radius, double StartAngle, double Step, int Risers) Spiral(StructuralElement stair)
    {
        var risers = RiserCount(stair);
        var radius = Math.Max(0.6, stair.Width / 2);
        var startAngle = stair.Length > 1e-6 ? Math.Atan2(stair.End.Y - stair.Start.Y, stair.End.X - stair.Start.X) : 0;
        // About 22 cm of going (or the requested tread) on the walking line at 60 % of the radius, kept between 15° and 40° per tread.
        var step = Math.Clamp((stair.TreadDepth > 0 ? stair.TreadDepth : 0.22) / (radius * 0.6),
            15 * Math.PI / 180, 40 * Math.PI / 180);
        return (stair.Start, radius, startAngle, step, risers);
    }

    /// <summary>Resolves a horizontal orthonormal basis from the stair's placement direction.</summary>
    private static (Point2 Direction, Point2 Left) Axes(StructuralElement stair)
    {
        var length = Math.Max(stair.Length, 1e-9);
        var direction = new Point2((stair.End.X - stair.Start.X) / length, (stair.End.Y - stair.Start.Y) / length);
        return (direction, new Point2(-direction.Y, direction.X));
    }

    /// <summary>Translates a layout point along a unit direction in model meters.</summary>
    private static Point2 Offset(Point2 point, Point2 direction, double distance) =>
        new(point.X + direction.X * distance, point.Y + direction.Y * distance);
}
