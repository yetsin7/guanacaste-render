using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core;

/// <summary>A planar piece of a pitched roof: a counter-clockwise plan polygon over which the rise is linear.</summary>
public sealed record RoofFacet(IReadOnlyList<Point2> Outline);

/// <summary>
/// Measured lengths of the built roof perimeter and its creases, in meters.
/// Horizontal boundary segments are not automatically gutters (a shed's high edge is also level).
/// Sloped boundary segments are rakes; ridge and hip lengths follow the generated roof facets.
/// </summary>
public sealed record RoofEdgeTakeoff(
    double HorizontalBoundaryLength,
    double SlopedBoundaryLength,
    double RidgeLength,
    double HipLength)
{
    /// <summary>Horizontal boundary at the roof's lowest rise; not necessarily installed gutters.</summary>
    public double LowEaveBoundaryLength { get; init; }

    /// <summary>Horizontal boundary above the lowest rise (for example, a shed roof's high edge).</summary>
    public double ElevatedHorizontalBoundaryLength { get; init; }
}

/// <summary>
/// Height and facet rules of shed, gable and hip roofs. Heights are measured above the eave, which sits at the
/// roof's own elevation. The ridge of a gable or hip roof runs along the longer side of the boundary's bounding box.
/// </summary>
public static class RoofGeometry
{
    /// <summary>Steepest pitch, in degrees, accepted for a roof.</summary>
    public const double MaximumPitch = 70;

    private const double Epsilon = 1e-9;

    /// <summary>Height of the roof surface above the eave at a plan point, never negative.</summary>
    public static double RiseAt(FloorSlab roof, Point2 point)
    {
        if (!roof.IsPitched) return 0;
        var tan = Math.Tan(roof.Pitch * Math.PI / 180);
        var (minX, maxX, minY, maxY) = Extent(roof.Boundary);
        var ridgeAlongX = maxX - minX >= maxY - minY;
        var coordinate = ridgeAlongX ? point.Y : point.X;
        var minimum = ridgeAlongX ? minY : minX;
        var span = ridgeAlongX ? maxY - minY : maxX - minX;
        var rise = roof.Shape switch
        {
            RoofShape.Shed => tan * (coordinate - minimum),
            RoofShape.Gable => tan * (span / 2 - Math.Abs(coordinate - (minimum + span / 2))),
            RoofShape.Hip => tan * Math.Min(Math.Min(point.X - minX, maxX - point.X),
                Math.Min(point.Y - minY, maxY - point.Y)),
            _ => 0
        };
        return Math.Max(0, rise);
    }

    /// <summary>The greatest height of the roof surface above its eave.</summary>
    public static double RidgeRise(FloorSlab roof)
    {
        if (!roof.IsPitched) return 0;
        var tan = Math.Tan(roof.Pitch * Math.PI / 180);
        var (minX, maxX, minY, maxY) = Extent(roof.Boundary);
        var span = Math.Min(maxX - minX, maxY - minY);
        return roof.Shape switch
        {
            RoofShape.Shed => tan * span,
            RoofShape.Gable or RoofShape.Hip => tan * span / 2,
            _ => 0
        };
    }

    /// <summary>The lowest roof height over a set of plan points; a structure standing there rests on this level.</summary>
    public static double LowestRiseOver(FloorSlab roof, IEnumerable<Point2> points) =>
        points.Select(point => RiseAt(roof, point)).DefaultIfEmpty(0).Min();

    /// <summary>Splits a pitched roof into planar facets. A flat roof is a single facet.</summary>
    public static IReadOnlyList<RoofFacet> Facets(FloorSlab roof)
    {
        var boundary = roof.Boundary.ToArray();
        if (PolygonTriangulator.SignedDoubleArea(boundary) < 0) Array.Reverse(boundary);
        var whole = new RoofFacet(boundary);
        if (!roof.IsPitched || roof.Shape == RoofShape.Shed) return [whole];

        var (minX, maxX, minY, maxY) = Extent(boundary);
        var pieces = new List<IReadOnlyList<Point2>>();
        if (roof.Shape == RoofShape.Gable)
        {
            if (maxX - minX >= maxY - minY)
            {
                var middle = (minY + maxY) / 2;
                pieces.Add(Clip(boundary, 0, 1, -middle));
                pieces.Add(Clip(boundary, 0, -1, middle));
            }
            else
            {
                var middle = (minX + maxX) / 2;
                pieces.Add(Clip(boundary, 1, 0, -middle));
                pieces.Add(Clip(boundary, -1, 0, middle));
            }
        }
        else
        {
            // Distance to each side as a linear function a*x + b*y + c; a hip facet is where its side is nearest.
            (double A, double B, double C)[] sides = [(1, 0, -minX), (-1, 0, maxX), (0, 1, -minY), (0, -1, maxY)];
            for (var index = 0; index < sides.Length; index++)
            {
                var piece = (IReadOnlyList<Point2>)boundary;
                for (var other = 0; other < sides.Length && piece.Count >= 3; other++)
                {
                    if (other == index) continue;
                    piece = Clip(piece, sides[index].A - sides[other].A, sides[index].B - sides[other].B,
                        sides[index].C - sides[other].C);
                }
                pieces.Add(piece);
            }
        }
        return pieces.Where(piece => piece.Count >= 3 && Math.Abs(PolygonTriangulator.SignedDoubleArea(piece)) > Epsilon)
            .Select(piece => new RoofFacet(piece)).ToArray();
    }

    /// <summary>
    /// Actual top-surface area in square meters, including the roof overhang and pitch.
    /// All supported roof facets (shed, gable and hip) have the same pitch, so their
    /// combined surface area is the built horizontal projection divided by cos(pitch).
    /// Non-roof slabs retain their plan area.
    /// </summary>
    public static double SurfaceArea(FloorSlab slab)
    {
        ArgumentNullException.ThrowIfNull(slab);
        if (slab.Kind != FloorKind.Roof) return slab.CalculateArea();
        var built = WithOverhang(slab);
        var planArea = built.CalculateArea();
        return built.IsPitched ? planArea / Math.Cos(built.Pitch * Math.PI / 180) : planArea;
    }

    /// <summary>
    /// Perimeter of the built roof's horizontal footprint in meters, including supported overhangs.
    /// This is a plan measurement, not a gutter, fascia or sloping rake takeoff.
    /// </summary>
    public static double ProjectedPerimeter(FloorSlab slab)
    {
        ArgumentNullException.ThrowIfNull(slab);
        var outline = WithOverhang(slab).Boundary;
        var length = 0d;
        for (var index = 0; index < outline.Count; index++)
            length += outline[index].DistanceTo(outline[(index + 1) % outline.Count]);
        return length;
    }

    /// <summary>
    /// Measures actual 3D roof boundary, ridge and hip lengths after overhangs.
    /// Level perimeter includes both low and high horizontal edges; do not price it as gutters
    /// without selecting the specific edges that will receive gutters.
    /// Creases follow the existing shed/gable/hip roof generator.
    /// </summary>
    public static RoofEdgeTakeoff EdgeTakeoff(FloorSlab roof)
    {
        ArgumentNullException.ThrowIfNull(roof);
        if (roof.Kind != FloorKind.Roof)
            throw new ArgumentException("Edge takeoff requires a roof slab.", nameof(roof));

        var built = WithOverhang(roof);
        var outline = built.Boundary;
        var creases = ClipCreasesToFootprint(CreaseLines(built), outline);
        var horizontal = 0d;
        var sloped = 0d;
        var lowEave = 0d;
        var elevatedHorizontal = 0d;
        for (var index = 0; index < outline.Count; index++)
        {
            var from = outline[index];
            var to = outline[(index + 1) % outline.Count];
            // A gable's rake rises to the ridge and falls again. Its endpoints
            // have equal elevation, so measuring only the whole edge's endpoints
            // would incorrectly report a horizontal line. Split at every crease.
            var fractions = new List<double> { 0, 1 };
            foreach (var (creaseFrom, creaseTo) in creases)
            {
                var edgeX = to.X - from.X;
                var edgeY = to.Y - from.Y;
                var creaseX = creaseTo.X - creaseFrom.X;
                var creaseY = creaseTo.Y - creaseFrom.Y;
                var denominator = edgeX * creaseY - edgeY * creaseX;
                if (Math.Abs(denominator) <= Epsilon) continue;
                var offsetX = creaseFrom.X - from.X;
                var offsetY = creaseFrom.Y - from.Y;
                var fraction = (offsetX * creaseY - offsetY * creaseX) / denominator;
                var creaseFraction = (offsetX * edgeY - offsetY * edgeX) / denominator;
                if (fraction > Epsilon && fraction < 1 - Epsilon &&
                    creaseFraction >= -Epsilon && creaseFraction <= 1 + Epsilon)
                    fractions.Add(fraction);
            }
            fractions.Sort();
            for (var part = 1; part < fractions.Count; part++)
            {
                var start = ArchitecturalShapes.Lerp(from, to, fractions[part - 1]);
                var end = ArchitecturalShapes.Lerp(from, to, fractions[part]);
                var riseDifference = RiseAt(built, end) - RiseAt(built, start);
                var length = double.Hypot(start.DistanceTo(end), riseDifference);
                if (Math.Abs(riseDifference) <= Epsilon)
                {
                    horizontal += length;
                    if (Math.Abs(RiseAt(built, start)) <= Epsilon &&
                        Math.Abs(RiseAt(built, end)) <= Epsilon)
                        lowEave += length;
                    else
                        elevatedHorizontal += length;
                }
                else sloped += length;
            }
        }

        var ridges = 0d;
        var hips = 0d;
        foreach (var (from, to) in creases)
        {
            var riseDifference = RiseAt(built, to) - RiseAt(built, from);
            var length = double.Hypot(from.DistanceTo(to), riseDifference);
            if (Math.Abs(riseDifference) <= Epsilon) ridges += length;
            else hips += length;
        }
        return new RoofEdgeTakeoff(horizontal, sloped, ridges, hips)
        {
            LowEaveBoundaryLength = lowEave,
            ElevatedHorizontalBoundaryLength = elevatedHorizontal
        };
    }

    /// <summary>
    /// Intersects generated roof creases with the actual footprint. Bounding-box creases
    /// can cross the empty notch of an L- or U-shaped roof; those spans must not be
    /// reported as installed ridge or hip lengths.
    /// </summary>
    private static IReadOnlyList<(Point2 From, Point2 To)> ClipCreasesToFootprint(
        IReadOnlyList<(Point2 From, Point2 To)> creases, IReadOnlyList<Point2> boundary)
    {
        var clipped = new List<(Point2 From, Point2 To)>();
        foreach (var (from, to) in creases)
        {
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var squaredLength = dx * dx + dy * dy;
            if (squaredLength <= Epsilon * Epsilon) continue;

            var fractions = new List<double> { 0, 1 };
            for (var index = 0; index < boundary.Count; index++)
            {
                var start = boundary[index];
                var end = boundary[(index + 1) % boundary.Count];
                var ex = end.X - start.X;
                var ey = end.Y - start.Y;
                var ox = start.X - from.X;
                var oy = start.Y - from.Y;
                var denominator = dx * ey - dy * ex;
                if (Math.Abs(denominator) <= Epsilon)
                {
                    // Preserve overlap boundaries when a crease lies along a polygon edge.
                    if (Math.Abs(ox * dy - oy * dx) > Epsilon) continue;
                    fractions.Add(Math.Clamp((ox * dx + oy * dy) / squaredLength, 0, 1));
                    fractions.Add(Math.Clamp(((end.X - from.X) * dx +
                        (end.Y - from.Y) * dy) / squaredLength, 0, 1));
                    continue;
                }

                var fraction = (ox * ey - oy * ex) / denominator;
                var edgeFraction = (ox * dy - oy * dx) / denominator;
                if (fraction >= -Epsilon && fraction <= 1 + Epsilon &&
                    edgeFraction >= -Epsilon && edgeFraction <= 1 + Epsilon)
                    fractions.Add(Math.Clamp(fraction, 0, 1));
            }

            fractions.Sort();
            for (var index = 1; index < fractions.Count; index++)
            {
                var first = fractions[index - 1];
                var last = fractions[index];
                if (last - first <= Epsilon) continue;
                var midpoint = ArchitecturalShapes.Lerp(from, to, (first + last) / 2);
                if (!ContainsPlanPoint(boundary, midpoint)) continue;
                clipped.Add((ArchitecturalShapes.Lerp(from, to, first),
                    ArchitecturalShapes.Lerp(from, to, last)));
            }
        }
        return clipped;
    }

    /// <summary>Even-odd point containment, treating a point on the footprint edge as inside.</summary>
    private static bool ContainsPlanPoint(IReadOnlyList<Point2> boundary, Point2 point)
    {
        var inside = false;
        for (var index = 0; index < boundary.Count; index++)
        {
            var a = boundary[index];
            var b = boundary[(index + 1) % boundary.Count];
            var cross = (b.X - a.X) * (point.Y - a.Y) -
                        (b.Y - a.Y) * (point.X - a.X);
            if (Math.Abs(cross) <= Epsilon &&
                point.X >= Math.Min(a.X, b.X) - Epsilon &&
                point.X <= Math.Max(a.X, b.X) + Epsilon &&
                point.Y >= Math.Min(a.Y, b.Y) - Epsilon &&
                point.Y <= Math.Max(a.Y, b.Y) + Epsilon)
                return true;

            if ((a.Y > point.Y) != (b.Y > point.Y) &&
                point.X < a.X + (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y))
                inside = !inside;
        }
        return inside;
    }

    /// <summary>The ridge and hip lines to draw in plan, as pairs of end points. Empty for flat and shed roofs.</summary>
    public static IReadOnlyList<(Point2 From, Point2 To)> CreaseLines(FloorSlab roof)
    {
        if (!roof.IsPitched || roof.Shape is RoofShape.Flat or RoofShape.Shed) return [];
        var (minX, maxX, minY, maxY) = Extent(roof.Boundary);
        var width = maxX - minX;
        var depth = maxY - minY;
        var half = Math.Min(width, depth) / 2;
        if (roof.Shape == RoofShape.Gable)
            return width >= depth
                ? [(new Point2(minX, minY + half), new Point2(maxX, minY + half))]
                : [(new Point2(minX + half, minY), new Point2(minX + half, maxY))];

        var wide = width >= depth;
        var ridgeStart = new Point2(minX + half, minY + half);
        var ridgeEnd = wide ? new Point2(maxX - half, minY + half) : new Point2(minX + half, maxY - half);
        (Point2 From, Point2 To)[] lines =
        [
            (ridgeStart, ridgeEnd),
            (new Point2(minX, minY), ridgeStart),
            (wide ? new Point2(minX, maxY) : new Point2(maxX, minY), ridgeStart),
            (new Point2(maxX, maxY), ridgeEnd),
            (wide ? new Point2(maxX, minY) : new Point2(minX, maxY), ridgeEnd)
        ];
        return lines.Where(line => line.From.DistanceTo(line.To) > Epsilon).ToArray();
    }

    /// <summary>
    /// The roof as built: its boundary pushed out by the overhang and, when pitched, its eave lowered so the roof plane
    /// still crosses the original boundary at the original height. A roof without overhang is returned unchanged.
    /// </summary>
    public static FloorSlab WithOverhang(FloorSlab roof)
    {
        if (roof.Kind != FloorKind.Roof || roof.Overhang <= 0) return roof;
        var grown = Grow(roof.Boundary, roof.Overhang);
        if (grown is null) return roof;
        var drop = roof.IsPitched ? roof.Overhang * Math.Tan(roof.Pitch * Math.PI / 180) : 0;
        return roof with { Boundary = [.. grown], ElevationOffset = roof.ElevationOffset - drop, Overhang = 0 };
    }

    /// <summary>
    /// The vertical outline closing the space between a wall top (the roof's eave underside) and the underside of a
    /// pitched roof along one boundary edge: a gable triangle, or the high wall of a shed. Null where the roof does not
    /// rise along the edge (the eaves of a gable, every side of a hip roof).
    /// </summary>
    public static IReadOnlyList<(Point2 Point, double Rise)>? GableProfile(FloorSlab roof, Point2 from, Point2 to)
    {
        if (!roof.IsPitched) return null;
        const int samples = 24;
        var profile = new List<(Point2, double)>(samples + 1);
        var highest = 0d;
        for (var index = 0; index <= samples; index++)
        {
            var point = new Point2(from.X + (to.X - from.X) * index / samples, from.Y + (to.Y - from.Y) * index / samples);
            var rise = RiseAt(roof, point);
            highest = Math.Max(highest, rise);
            profile.Add((point, rise));
        }
        return highest < 0.05 ? null : profile;
    }

    /// <summary>Offsets a simple polygon outward by a distance (mitred corners); null when it degenerates.</summary>
    private static IReadOnlyList<Point2>? Grow(IReadOnlyList<Point2> polygon, double distance)
    {
        var ordered = PolygonTriangulator.SignedDoubleArea(polygon) > 0 ? polygon.ToArray() : polygon.Reverse().ToArray();
        var count = ordered.Length;
        var result = new Point2[count];
        for (var index = 0; index < count; index++)
        {
            var previous = ordered[(index + count - 1) % count];
            var current = ordered[index];
            var next = ordered[(index + 1) % count];
            var (n1x, n1y) = OutwardNormal(previous, current);
            var (n2x, n2y) = OutwardNormal(current, next);
            var bisectorX = n1x + n2x;
            var bisectorY = n1y + n2y;
            var dot = 1 + n1x * n2x + n1y * n2y;
            if (dot < 0.1) return null;
            // A mitred corner moves along the bisector by distance / cos(half angle) = 2·distance / |n1 + n2|².
            var scale = distance * 2 / (bisectorX * bisectorX + bisectorY * bisectorY);
            result[index] = new Point2(current.X + bisectorX * scale, current.Y + bisectorY * scale);
        }
        return result;

        static (double X, double Y) OutwardNormal(Point2 a, Point2 b)
        {
            var length = Math.Max(a.DistanceTo(b), 1e-12);
            return ((b.Y - a.Y) / length, -(b.X - a.X) / length);
        }
    }

    /// <summary>Axis-aligned bounding box of a polygon.</summary>
    public static (double MinX, double MaxX, double MinY, double MaxY) Extent(IReadOnlyList<Point2> polygon) =>
        (polygon.Min(point => point.X), polygon.Max(point => point.X),
            polygon.Min(point => point.Y), polygon.Max(point => point.Y));

    /// <summary>Keeps the part of a polygon where a*x + b*y + c is not positive (one Sutherland-Hodgman step).</summary>
    private static IReadOnlyList<Point2> Clip(IReadOnlyList<Point2> polygon, double a, double b, double c)
    {
        var result = new List<Point2>();
        for (var index = 0; index < polygon.Count; index++)
        {
            var current = polygon[index];
            var previous = polygon[(index + polygon.Count - 1) % polygon.Count];
            var currentValue = a * current.X + b * current.Y + c;
            var previousValue = a * previous.X + b * previous.Y + c;
            if (currentValue <= Epsilon)
            {
                if (previousValue > Epsilon) result.Add(Cross(previous, current, previousValue, currentValue));
                result.Add(current);
            }
            else if (previousValue <= Epsilon)
            {
                result.Add(Cross(previous, current, previousValue, currentValue));
            }
        }
        // Drop consecutive duplicates introduced when a vertex lies exactly on the clipping line.
        return result.Where((point, index) => point.DistanceTo(result[(index + 1) % result.Count]) > Epsilon).ToArray();
    }

    private static Point2 Cross(Point2 previous, Point2 current, double previousValue, double currentValue)
    {
        var fraction = previousValue / (previousValue - currentValue);
        return new Point2(previous.X + (current.X - previous.X) * fraction,
            previous.Y + (current.Y - previous.Y) * fraction);
    }
}
