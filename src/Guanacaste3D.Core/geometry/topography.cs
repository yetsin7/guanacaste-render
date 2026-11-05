using System.Numerics;

namespace Guanacaste3D.Core.Geometry;

/// <summary>A surveyed point of a toposolid's surface: plan position and elevation above the level, in meters.</summary>
public readonly record struct TopoPoint(double X, double Y, double Elevation)
{
    public Point2 Plan => new(X, Y);
}

/// <summary>One straight piece of a contour line at <see cref="Elevation"/> (relative to the level).</summary>
public readonly record struct ContourSegment(Point2 Start, Point2 End, double Elevation);

/// <summary>
/// The terrain surface of a toposolid. It passes exactly through every elevation point and through the boundary
/// corners, which rest at the slab's <see cref="FloorSlab.ElevationOffset"/> unless a point sits on them; between them
/// it is a thin-plate spline, the smoothest surface through the data (it bends the least), which is what a graded site
/// looks like. Elevations are relative to the toposolid's level.
/// </summary>
public sealed class TopoSurface
{
    private const double CornerTolerance = 0.05;
    private readonly double _constant;
    private readonly double[]? _weights;
    private readonly double[] _affine = new double[3];
    private readonly Vector2[] _centers = [];
    private readonly double _cx, _cy, _scale;

    public TopoSurface(FloorSlab toposolid)
    {
        ArgumentNullException.ThrowIfNull(toposolid);
        var anchors = new List<TopoPoint>();
        var points = toposolid.TopoPoints ?? [];
        foreach (var corner in toposolid.Boundary)
            if (!points.Any(point => point.Plan.DistanceTo(corner) <= CornerTolerance))
                anchors.Add(new TopoPoint(corner.X, corner.Y, toposolid.ElevationOffset));
        foreach (var point in points)
        {
            // Two points at one place would make the system singular; the later one wins.
            anchors.RemoveAll(existing => existing.Plan.DistanceTo(point.Plan) < 0.01);
            anchors.Add(point);
        }
        _constant = toposolid.ElevationOffset;
        Minimum = Maximum = _constant;
        if (anchors.Count < 3 || anchors.All(anchor => Math.Abs(anchor.Elevation - anchors[0].Elevation) < 1e-9))
        {
            _constant = anchors.Count > 0 ? anchors[0].Elevation : _constant;
            Minimum = Maximum = _constant;
            return;
        }

        _cx = anchors.Average(anchor => anchor.X);
        _cy = anchors.Average(anchor => anchor.Y);
        _scale = Math.Max(1e-6, anchors.Max(anchor => Math.Max(Math.Abs(anchor.X - _cx), Math.Abs(anchor.Y - _cy))));
        _centers = anchors.Select(anchor => new Vector2((float)((anchor.X - _cx) / _scale), (float)((anchor.Y - _cy) / _scale))).ToArray();
        var count = anchors.Count;
        var size = count + 3;
        var matrix = new double[size, size];
        var values = new double[size];
        for (var row = 0; row < count; row++)
        {
            for (var column = 0; column < count; column++)
                matrix[row, column] = Kernel(Vector2.Distance(_centers[row], _centers[column]));
            matrix[row, row] += 1e-9;
            matrix[row, count] = matrix[count, row] = 1;
            matrix[row, count + 1] = matrix[count + 1, row] = _centers[row].X;
            matrix[row, count + 2] = matrix[count + 2, row] = _centers[row].Y;
            values[row] = anchors[row].Elevation;
        }
        var solution = Solve(matrix, values);
        if (solution is null)
        {
            _constant = anchors.Average(anchor => anchor.Elevation);
            return;
        }
        _weights = solution[..count];
        _affine[0] = solution[count];
        _affine[1] = solution[count + 1];
        _affine[2] = solution[count + 2];
        Minimum = anchors.Min(anchor => anchor.Elevation);
        Maximum = anchors.Max(anchor => anchor.Elevation);
    }

    /// <summary>Lowest and highest anchor elevations; the spline may slightly overshoot them between points.</summary>
    public double Minimum { get; }

    public double Maximum { get; }

    /// <summary>Whether the surface is level everywhere.</summary>
    public bool IsFlat => _weights is null;

    /// <summary>Surface elevation above the level at a plan point.</summary>
    public double ElevationAt(double x, double y)
    {
        if (_weights is null) return _constant;
        var u = (float)((x - _cx) / _scale);
        var v = (float)((y - _cy) / _scale);
        var sum = _affine[0] + _affine[1] * u + _affine[2] * v;
        var position = new Vector2(u, v);
        for (var index = 0; index < _weights.Length; index++)
            sum += _weights[index] * Kernel(Vector2.Distance(position, _centers[index]));
        return sum;
    }

    /// <summary>Upward unit normal of the surface at a plan point.</summary>
    public Vector3 NormalAt(double x, double y)
    {
        if (_weights is null) return Vector3.UnitZ;
        // Analytic gradient: d/dp of r² ln r is (2 ln r + 1)(p − c).
        var u = (x - _cx) / _scale;
        var v = (y - _cy) / _scale;
        double gu = _affine[1], gv = _affine[2];
        for (var index = 0; index < _weights.Length; index++)
        {
            var du = u - _centers[index].X;
            var dv = v - _centers[index].Y;
            var squared = du * du + dv * dv;
            if (squared < 1e-18) continue;
            var factor = _weights[index] * (Math.Log(squared) + 1);
            gu += factor * du;
            gv += factor * dv;
        }
        return Vector3.Normalize(new Vector3((float)(-gu / _scale), (float)(-gv / _scale), 1));
    }

    private static double Kernel(float distance) => distance < 1e-9 ? 0 : distance * distance * Math.Log(distance);

    /// <summary>Gaussian elimination with partial pivoting; null when the system is singular.</summary>
    private static double[]? Solve(double[,] matrix, double[] values)
    {
        var size = values.Length;
        for (var pivot = 0; pivot < size; pivot++)
        {
            var best = pivot;
            for (var row = pivot + 1; row < size; row++)
                if (Math.Abs(matrix[row, pivot]) > Math.Abs(matrix[best, pivot])) best = row;
            if (Math.Abs(matrix[best, pivot]) < 1e-12) return null;
            if (best != pivot)
            {
                for (var column = 0; column < size; column++)
                    (matrix[pivot, column], matrix[best, column]) = (matrix[best, column], matrix[pivot, column]);
                (values[pivot], values[best]) = (values[best], values[pivot]);
            }
            for (var row = pivot + 1; row < size; row++)
            {
                var factor = matrix[row, pivot] / matrix[pivot, pivot];
                if (factor == 0) continue;
                for (var column = pivot; column < size; column++) matrix[row, column] -= factor * matrix[pivot, column];
                values[row] -= factor * values[pivot];
            }
        }
        var result = new double[size];
        for (var row = size - 1; row >= 0; row--)
        {
            var sum = values[row];
            for (var column = row + 1; column < size; column++) sum -= matrix[row, column] * result[column];
            result[row] = sum / matrix[row, row];
        }
        return result.All(double.IsFinite) ? result : null;
    }
}

/// <summary>Meshes toposolids for the 3D views and the renderer, and traces their contour lines for plans.</summary>
public static class TopographyGeometry
{
    /// <summary>Grid spacing used to sample a toposolid: fine enough for a garden, bounded for a large site.</summary>
    public static double GridSpacing(IReadOnlyList<Point2> boundary)
    {
        var (minX, maxX, minY, maxY) = Extent(boundary);
        return Math.Clamp(Math.Max(maxX - minX, maxY - minY) / 90, 0.2, 5);
    }

    /// <summary>
    /// Appends the solid: the smooth-shaded terrain surface (lawn), vertical sides that follow it down to a flat
    /// bottom <see cref="FloorSlab.Thickness"/> below its lowest point, and that bottom (earth).
    /// </summary>
    public static void AppendTo(SceneMesh mesh, FloorSlab toposolid, double levelElevation)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var surface = new TopoSurface(toposolid);
        var boundary = PolygonTriangulator.SignedDoubleArea(toposolid.Boundary) > 0
            ? toposolid.Boundary.ToArray()
            : toposolid.Boundary.AsEnumerable().Reverse().ToArray();
        var spacing = GridSpacing(boundary);
        var (minX, maxX, minY, maxY) = Extent(boundary);
        var datum = (float)levelElevation;
        var lowest = double.MaxValue;

        Vector3 Lift(Point2 point) => new((float)point.X, (float)point.Y, datum + (float)surface.ElevationAt(point.X, point.Y));
        Vector3 Normal(Point2 point) => surface.NormalAt(point.X, point.Y);

        var columns = Math.Max(1, (int)Math.Ceiling((maxX - minX) / spacing));
        var rows = Math.Max(1, (int)Math.Ceiling((maxY - minY) / spacing));
        for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
            {
                var x0 = minX + column * spacing;
                var y0 = minY + row * spacing;
                Point2[] cell = [new(x0, y0), new(x0 + spacing, y0), new(x0 + spacing, y0 + spacing), new(x0, y0 + spacing)];
                var piece = ClipToConvex(boundary, cell);
                if (piece.Count < 3 || Math.Abs(PolygonTriangulator.SignedDoubleArea(piece)) < 1e-10) continue;
                IReadOnlyList<(int A, int B, int C)> triangles;
                try { triangles = PolygonTriangulator.Triangulate(piece); }
                catch (ArgumentException)
                {
                    // A concave boundary can cut a cell into touching pieces the ear clipper refuses; keep the
                    // halves of the cell whose centre is on the site rather than leaving a hole.
                    piece = [.. cell];
                    triangles = new[] { (0, 1, 2), (0, 2, 3) }.Where(triangle => Contains(boundary, new Point2(
                        (cell[triangle.Item1].X + cell[triangle.Item2].X + cell[triangle.Item3].X) / 3,
                        (cell[triangle.Item1].Y + cell[triangle.Item2].Y + cell[triangle.Item3].Y) / 3))).ToArray();
                }
                var lifted = piece.Select(Lift).ToArray();
                var normals = piece.Select(Normal).ToArray();
                foreach (var point in lifted) lowest = Math.Min(lowest, point.Z - datum);
                foreach (var (a, b, c) in triangles)
                    mesh.AddSmoothTriangle(lifted[a], lifted[b], lifted[c], normals[a], normals[b], normals[c], SurfaceKind.Ground);
            }

        // Sides: each boundary edge is divided at the grid spacing so the top edge follows the terrain.
        var edgePoints = new List<Point2>();
        for (var index = 0; index < boundary.Length; index++)
        {
            var from = boundary[index];
            var to = boundary[(index + 1) % boundary.Length];
            var steps = Math.Max(1, (int)Math.Ceiling(from.DistanceTo(to) / spacing));
            for (var step = 0; step < steps; step++)
                edgePoints.Add(new Point2(from.X + (to.X - from.X) * step / steps, from.Y + (to.Y - from.Y) * step / steps));
        }
        foreach (var point in edgePoints) lowest = Math.Min(lowest, surface.ElevationAt(point.X, point.Y));
        if (lowest == double.MaxValue) lowest = surface.Minimum;
        var bottom = datum + (float)(lowest - Math.Max(0.03, toposolid.Thickness));
        for (var index = 0; index < edgePoints.Count; index++)
        {
            var a = edgePoints[index];
            var b = edgePoints[(index + 1) % edgePoints.Count];
            var topA = Lift(a);
            var topB = Lift(b);
            mesh.AddQuad(new Vector3(topA.X, topA.Y, bottom), new Vector3(topB.X, topB.Y, bottom), topB, topA, SurfaceKind.Earth);
        }
        try
        {
            foreach (var (a, b, c) in PolygonTriangulator.Triangulate(boundary))
                mesh.AddTriangle(new Vector3((float)boundary[a].X, (float)boundary[a].Y, bottom),
                    new Vector3((float)boundary[c].X, (float)boundary[c].Y, bottom),
                    new Vector3((float)boundary[b].X, (float)boundary[b].Y, bottom), SurfaceKind.Earth);
        }
        catch (ArgumentException)
        {
            // A self-touching boundary still gets its surface and sides; only the hidden underside is skipped.
        }
    }

    /// <summary>
    /// Contour lines at every multiple of <paramref name="interval"/> (relative to the level), traced by marching
    /// squares over the sampling grid and kept inside the boundary.
    /// </summary>
    public static IReadOnlyList<ContourSegment> Contours(FloorSlab toposolid, double interval)
    {
        var segments = new List<ContourSegment>();
        if (interval <= 0 || toposolid.Boundary.Count < 3) return segments;
        var surface = new TopoSurface(toposolid);
        if (surface.IsFlat) return segments;
        var boundary = toposolid.Boundary;
        var spacing = GridSpacing(boundary) * 0.75;
        var (minX, maxX, minY, maxY) = Extent(boundary);
        var columns = Math.Max(1, (int)Math.Ceiling((maxX - minX) / spacing));
        var rows = Math.Max(1, (int)Math.Ceiling((maxY - minY) / spacing));
        var heights = new double[rows + 1, columns + 1];
        double low = double.MaxValue, high = double.MinValue;
        for (var row = 0; row <= rows; row++)
            for (var column = 0; column <= columns; column++)
            {
                var value = surface.ElevationAt(minX + column * spacing, minY + row * spacing);
                heights[row, column] = value;
                low = Math.Min(low, value);
                high = Math.Max(high, value);
            }
        var first = Math.Ceiling(low / interval);
        var last = Math.Floor(high / interval);
        if (last - first > 400) return segments;
        for (var level = first; level <= last; level++)
        {
            var elevation = level * interval;
            for (var row = 0; row < rows; row++)
                for (var column = 0; column < columns; column++)
                {
                    var x0 = minX + column * spacing;
                    var y0 = minY + row * spacing;
                    Point2[] corners = [new(x0, y0), new(x0 + spacing, y0), new(x0 + spacing, y0 + spacing), new(x0, y0 + spacing)];
                    double[] values = [heights[row, column], heights[row, column + 1], heights[row + 1, column + 1], heights[row + 1, column]];
                    var crossings = new List<Point2>(4);
                    for (var edge = 0; edge < 4; edge++)
                    {
                        var a = values[edge];
                        var b = values[(edge + 1) % 4];
                        if ((a < elevation) == (b < elevation)) continue;
                        var t = (elevation - a) / (b - a);
                        var p = corners[edge];
                        var q = corners[(edge + 1) % 4];
                        crossings.Add(new Point2(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t));
                    }
                    for (var index = 0; index + 1 < crossings.Count; index += 2)
                    {
                        var midpoint = new Point2((crossings[index].X + crossings[index + 1].X) / 2,
                            (crossings[index].Y + crossings[index + 1].Y) / 2);
                        if (Contains(boundary, midpoint))
                            segments.Add(new ContourSegment(crossings[index], crossings[index + 1], elevation));
                    }
                }
        }
        return segments;
    }

    /// <summary>Whether a plan point lies inside a polygon (even-odd rule).</summary>
    public static bool Contains(IReadOnlyList<Point2> polygon, Point2 point)
    {
        var inside = false;
        for (int index = 0, previous = polygon.Count - 1; index < polygon.Count; previous = index++)
        {
            var a = polygon[index];
            var b = polygon[previous];
            if ((a.Y > point.Y) != (b.Y > point.Y) &&
                point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>Sutherland–Hodgman: the part of <paramref name="subject"/> inside the convex, counter-clockwise <paramref name="clip"/>.</summary>
    private static List<Point2> ClipToConvex(IReadOnlyList<Point2> subject, IReadOnlyList<Point2> clip)
    {
        var output = subject.ToList();
        for (var edge = 0; edge < clip.Count && output.Count > 0; edge++)
        {
            var a = clip[edge];
            var b = clip[(edge + 1) % clip.Count];
            var input = output;
            output = new List<Point2>(input.Count + 4);
            double Side(Point2 p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            for (var index = 0; index < input.Count; index++)
            {
                var current = input[index];
                var previous = input[(index + input.Count - 1) % input.Count];
                var currentSide = Side(current);
                var previousSide = Side(previous);
                if (currentSide >= 0)
                {
                    if (previousSide < 0) output.Add(Intersect(previous, current, previousSide, currentSide));
                    output.Add(current);
                }
                else if (previousSide >= 0)
                {
                    output.Add(Intersect(previous, current, previousSide, currentSide));
                }
            }
        }
        // Drop repeated vertices, which the ear clipper would reject.
        var cleaned = new List<Point2>(output.Count);
        foreach (var point in output)
            if (cleaned.Count == 0 || cleaned[^1].DistanceTo(point) > 1e-9) cleaned.Add(point);
        if (cleaned.Count > 1 && cleaned[0].DistanceTo(cleaned[^1]) <= 1e-9) cleaned.RemoveAt(cleaned.Count - 1);
        return cleaned;

        static Point2 Intersect(Point2 p, Point2 q, double sideP, double sideQ)
        {
            var t = sideP / (sideP - sideQ);
            return new Point2(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t);
        }
    }

    private static (double MinX, double MaxX, double MinY, double MaxY) Extent(IReadOnlyList<Point2> boundary) =>
        (boundary.Min(point => point.X), boundary.Max(point => point.X), boundary.Min(point => point.Y), boundary.Max(point => point.Y));
}
