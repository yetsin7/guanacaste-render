using System.Numerics;

namespace Guanacaste3D.Core.Import;

/// <summary>Double-precision vector, used for placements so survey-scale coordinates do not lose millimeters.</summary>
internal readonly record struct D3(double X, double Y, double Z)
{
    public static D3 operator +(D3 a, D3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static D3 operator -(D3 a, D3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static D3 operator *(D3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public double Dot(D3 o) => X * o.X + Y * o.Y + Z * o.Z;
    public D3 Cross(D3 o) => new(Y * o.Z - Z * o.Y, Z * o.X - X * o.Z, X * o.Y - Y * o.X);
    public double Length => Math.Sqrt(Dot(this));
    public D3 Normalized() => Length < 1e-12 ? this : this * (1 / Length);
    public Vector3 ToVector() => new((float)X, (float)Y, (float)Z);
    public static readonly D3 UnitX = new(1, 0, 0), UnitY = new(0, 1, 0), UnitZ = new(0, 0, 1);
}

/// <summary>An affine transform in row-vector form: p' = p · (X, Y, Z axes) + origin.</summary>
internal readonly record struct Xform(D3 AxisX, D3 AxisY, D3 AxisZ, D3 Origin)
{
    public static readonly Xform Identity = new(D3.UnitX, D3.UnitY, D3.UnitZ, new D3(0, 0, 0));

    public D3 Apply(D3 p) => AxisX * p.X + AxisY * p.Y + AxisZ * p.Z + Origin;

    /// <summary>Applies this transform first and <paramref name="next"/> second.</summary>
    public Xform Then(Xform next) => new(next.Rotate(AxisX), next.Rotate(AxisY), next.Rotate(AxisZ), next.Apply(Origin));

    private D3 Rotate(D3 d) => AxisX * d.X + AxisY * d.Y + AxisZ * d.Z;

    /// <summary>The inverse of a rigid or uniformly scaled transform.</summary>
    public Xform Inverse()
    {
        var sx = AxisX.Dot(AxisX);
        var sy = AxisY.Dot(AxisY);
        var sz = AxisZ.Dot(AxisZ);
        // Rows of the rotation become columns of the inverse, each divided by its squared scale.
        var ix = new D3(AxisX.X / sx, AxisY.X / sy, AxisZ.X / sz);
        var iy = new D3(AxisX.Y / sx, AxisY.Y / sy, AxisZ.Y / sz);
        var iz = new D3(AxisX.Z / sx, AxisY.Z / sy, AxisZ.Z / sz);
        var inverse = new Xform(ix, iy, iz, new D3(0, 0, 0));
        return inverse with { Origin = inverse.Apply(Origin) * -1 };
    }

    public Matrix4x4 ToMatrix(D3 shift)
    {
        var o = Origin - shift;
        return new Matrix4x4((float)AxisX.X, (float)AxisX.Y, (float)AxisX.Z, 0, (float)AxisY.X, (float)AxisY.Y,
            (float)AxisY.Z, 0, (float)AxisZ.X, (float)AxisZ.Y, (float)AxisZ.Z, 0, (float)o.X, (float)o.Y, (float)o.Z, 1);
    }

    /// <summary>Builds a frame from an IFC axis (Z) and reference direction (X), either of which may be missing.</summary>
    public static Xform FromAxes(D3? axis, D3? reference, D3 origin)
    {
        var z = axis is { } a && a.Length > 1e-9 ? a.Normalized() : D3.UnitZ;
        var xHint = reference is { } r && r.Length > 1e-9 ? r.Normalized() : (Math.Abs(z.X) < 0.9 ? D3.UnitX : D3.UnitY);
        var x = xHint - z * xHint.Dot(z);
        x = x.Length < 1e-9 ? (Math.Abs(z.X) < 0.9 ? D3.UnitX : D3.UnitY) : x.Normalized();
        var y = z.Cross(x).Normalized();
        return new Xform(x, y, z, origin);
    }
}

/// <summary>A piece of geometry and the representation item whose surface style colors it.</summary>
internal readonly record struct Part(TriSoup Soup, int StyleItem);

/// <summary>
/// Builds triangle geometry for IFC representation items: swept solids, faceted and tessellated shells, mapped
/// instances and plane-clipped solids. Lengths are converted to meters as they are read, and each item is returned in
/// the coordinates of its representation, ready for the product's placement.
/// </summary>
internal sealed class IfcGeometry
{
    private const int MaximumDepth = 12;

    private readonly StepFile _step;
    private readonly double _unit;
    private readonly double _angle;
    private readonly Dictionary<int, Xform> _placements = [];
    private readonly Dictionary<int, List<Part>> _mapCache = [];
    private readonly Dictionary<string, int> _unsupported = [];

    public IfcGeometry(StepFile step, double lengthUnitMeters, double angleUnitRadians)
    {
        _step = step;
        _unit = lengthUnitMeters;
        _angle = angleUnitRadians;
    }

    /// <summary>Counts of representation types that could not be converted, for a warning.</summary>
    public IReadOnlyDictionary<string, int> Unsupported => _unsupported;

    public int Clipped { get; private set; }

    // ------------------------------------------------------------------ points, directions, placements

    public D3 Point(int id)
    {
        var p = _step.Point(id);
        return new D3(p.X * _unit, p.Y * _unit, p.Z * _unit);
    }

    /// <summary>Reads a point or a direction exactly as stored (no unit scale), for entities that hold direction ratios.</summary>
    private D3? Direction(StepRef? reference)
    {
        if (reference is not { } r || _step.Get(r.Id, cache: false) is not { } entity || entity.List(0) is not { } ratios) return null;
        double At(int i) => i < ratios.Count ? StepFile.AsNumber(ratios[i]) ?? 0 : 0;
        return new D3(At(0), At(1), At(2));
    }

    private D3 LocationOf(StepRef? reference) => reference is { } r ? Point(r.Id) : new D3(0, 0, 0);

    /// <summary>Resolves IfcAxis2Placement3D, IfcAxis2Placement2D or IfcLocalPlacement into a transform.</summary>
    public Xform Placement(int id)
    {
        if (_placements.TryGetValue(id, out var cached)) return cached;
        var entity = _step.Get(id);
        Xform result = Xform.Identity;
        if (entity is not null)
        {
            switch (entity.Type)
            {
                case "IFCAXIS2PLACEMENT3D":
                    // Attributes: Location, Axis (Z), RefDirection (X).
                    result = Xform.FromAxes(Direction(entity.Ref(1)), Direction(entity.Ref(2)), LocationOf(entity.Ref(0)));
                    break;
                case "IFCAXIS2PLACEMENT2D":
                {
                    var reference = Direction(entity.Ref(1));
                    result = Xform.FromAxes(null, reference, LocationOf(entity.Ref(0)));
                    break;
                }
                case "IFCLOCALPLACEMENT":
                {
                    var relative = entity.Ref(1) is { } rel ? Placement(rel.Id) : Xform.Identity;
                    result = entity.Ref(0) is { } parent ? relative.Then(Placement(parent.Id)) : relative;
                    break;
                }
                case "IFCCARTESIANTRANSFORMATIONOPERATOR3D" or "IFCCARTESIANTRANSFORMATIONOPERATOR3DNONUNIFORM"
                    or "IFCCARTESIANTRANSFORMATIONOPERATOR2D":
                    result = Operator(entity);
                    break;
            }
        }
        _placements[id] = result;
        return result;
    }

    private Xform Operator(StepEntity entity)
    {
        var x = Direction(entity.Ref(0)) ?? D3.UnitX;
        var y = Direction(entity.Ref(1)) ?? D3.UnitY;
        var origin = LocationOf(entity.Ref(2));
        var scale = entity.Number(3, 1);
        x = x.Normalized();
        var z = entity.Type.EndsWith("2D", StringComparison.Ordinal)
            ? D3.UnitZ
            : Direction(entity.Ref(4))?.Normalized() ?? x.Cross(y.Normalized()).Normalized();
        y = z.Cross(x).Normalized();
        return new Xform(x * scale, y * scale, z * scale, origin);
    }

    // ------------------------------------------------------------------ items

    /// <summary>Builds one representation item; returns an empty list for unsupported ones.</summary>
    public List<Part> Item(int id, int depth = 0)
    {
        var result = new List<Part>();
        if (depth > MaximumDepth || _step.Get(id) is not { } entity) return result;
        void Add(TriSoup soup)
        {
            if (soup.TriangleCount > 0) result.Add(new Part(soup, id));
        }
        switch (entity.Type)
        {
            case "IFCEXTRUDEDAREASOLID" or "IFCEXTRUDEDAREASOLIDTAPERED":
                foreach (var soup in Extrusion(entity)) Add(soup);
                break;
            case "IFCFACETEDBREP" or "IFCADVANCEDBREP" or "IFCFACETEDBREPWITHVOIDS" or "IFCADVANCEDBREPWITHVOIDS":
                if (entity.Type.StartsWith("IFCADVANCED", StringComparison.Ordinal)) Note(entity.Type);
                else Add(Brep(entity));
                break;
            case "IFCSHELLBASEDSURFACEMODEL" or "IFCFACEBASEDSURFACEMODEL":
            {
                var soup = new TriSoup();
                foreach (var shell in Refs(entity.List(0))) AddShell(soup, shell);
                Add(soup);
                break;
            }
            case "IFCTRIANGULATEDFACESET" or "IFCPOLYGONALFACESET":
                Add(FaceSet(entity));
                break;
            case "IFCMAPPEDITEM":
                result.AddRange(Mapped(entity, depth));
                break;
            case "IFCBOOLEANCLIPPINGRESULT" or "IFCBOOLEANRESULT":
                result.AddRange(Boolean(entity, depth));
                break;
            case "IFCBLOCK":
                Add(Block(entity));
                break;
            case "IFCCSGSOLID":
                if (entity.Ref(0) is { } tree) result.AddRange(Item(tree.Id, depth + 1));
                break;
            case "IFCSHAPEREPRESENTATION" or "IFCGEOMETRICSET" or "IFCGEOMETRICCURVESET":
                break;
            default:
                Note(entity.Type);
                break;
        }
        return result;
    }

    private void Note(string type) => _unsupported[type] = _unsupported.GetValueOrDefault(type) + 1;

    private static IEnumerable<int> Refs(List<object?>? list) =>
        list is null ? [] : list.OfType<StepRef>().Select(r => r.Id);

    // ------------------------------------------------------------------ swept solids

    private IEnumerable<TriSoup> Extrusion(StepEntity solid)
    {
        if (solid.Ref(0) is not { } profileRef) yield break;
        var position = solid.Ref(1) is { } p ? Placement(p.Id) : Xform.Identity;
        var direction = (Direction(solid.Ref(2)) ?? D3.UnitZ).Normalized();
        var depth = solid.Number(3) * _unit;
        if (depth <= 1e-9) yield break;
        foreach (var (outer, holes) in Profile(profileRef.Id))
        {
            var soup = new TriSoup();
            Prism(soup, outer, holes, direction * depth, position);
            if (soup.TriangleCount > 0) yield return soup;
        }
    }

    /// <summary>
    /// Extrudes a planar profile (z = 0 in the solid's frame) along <paramref name="offset"/>, then moves it by the
    /// solid's placement. Caps are triangulated with their holes; sides follow every boundary loop.
    /// </summary>
    private static void Prism(TriSoup soup, List<P2> outer, List<List<P2>> holes, D3 offset, Xform position)
    {
        if (outer.Count < 3) return;
        var upward = offset.Z >= 0;
        var outerRing = outer;
        if (PolygonMesher.SignedArea(outerRing, Enumerable.Range(0, outerRing.Count).ToList()) < 0)
            outerRing = Enumerable.Reverse(outerRing).ToList();
        var holeRings = holes.Select(h => PolygonMesher.SignedArea(h, Enumerable.Range(0, h.Count).ToList()) > 0
            ? Enumerable.Reverse(h).ToList() : h).ToList();
        var loops = new List<List<P2>> { outerRing };
        loops.AddRange(holeRings);

        Vector3 Bottom(P2 p) => position.Apply(new D3(p.X, p.Y, 0)).ToVector();
        Vector3 Top(P2 p) => position.Apply(new D3(p.X, p.Y, 0) + offset).ToVector();

        var combined = loops.SelectMany(l => l).ToList();
        var triangles = PolygonMesher.Triangulate(outerRing, holeRings.Select(h => (IReadOnlyList<P2>)h).ToList());
        foreach (var (a, b, c) in triangles)
        {
            // Caps: the top follows the extrusion direction, the bottom faces away from it.
            if (upward)
            {
                soup.Add(Top(combined[a]), Top(combined[b]), Top(combined[c]));
                soup.Add(Bottom(combined[a]), Bottom(combined[c]), Bottom(combined[b]));
            }
            else
            {
                soup.Add(Top(combined[a]), Top(combined[c]), Top(combined[b]));
                soup.Add(Bottom(combined[a]), Bottom(combined[b]), Bottom(combined[c]));
            }
        }
        foreach (var loop in loops)
        {
            for (var i = 0; i < loop.Count; i++)
            {
                var from = loop[i];
                var to = loop[(i + 1) % loop.Count];
                if (from == to) continue;
                var b0 = Bottom(from);
                var b1 = Bottom(to);
                var t0 = Top(from);
                var t1 = Top(to);
                if (upward)
                {
                    soup.Add(b0, b1, t1);
                    soup.Add(b0, t1, t0);
                }
                else
                {
                    soup.Add(b0, t1, b1);
                    soup.Add(b0, t0, t1);
                }
            }
        }
    }

    // ------------------------------------------------------------------ profiles

    /// <summary>Returns one or more outlines (with holes) for an IFC profile definition, in profile coordinates.</summary>
    private List<(List<P2> Outer, List<List<P2>> Holes)> Profile(int id, int depth = 0)
    {
        var result = new List<(List<P2>, List<List<P2>>)>();
        if (depth > 4 || _step.Get(id) is not { } e) return result;
        var u = _unit;
        Xform? place = e.Type is "IFCARBITRARYCLOSEDPROFILEDEF" or "IFCARBITRARYPROFILEDEFWITHVOIDS" or "IFCCOMPOSITEPROFILEDEF"
            or "IFCDERIVEDPROFILEDEF"
            ? null
            : e.Ref(2) is { } pl ? Placement(pl.Id) : Xform.Identity;

        List<P2> Place(IEnumerable<P2> points) => place is { } transform
            ? points.Select(p =>
            {
                var q = transform.Apply(new D3(p.X, p.Y, 0));
                return new P2(q.X, q.Y);
            }).ToList()
            : points.ToList();

        switch (e.Type)
        {
            case "IFCRECTANGLEPROFILEDEF":
            {
                double x = e.Number(3) * u / 2, y = e.Number(4) * u / 2;
                result.Add((Place([new(-x, -y), new(x, -y), new(x, y), new(-x, y)]), []));
                break;
            }
            case "IFCROUNDEDRECTANGLEPROFILEDEF":
            {
                double x = e.Number(3) * u / 2, y = e.Number(4) * u / 2;
                var r = Math.Min(e.Number(5) * u, Math.Min(x, y));
                result.Add((Place(RoundedRectangle(x, y, r)), []));
                break;
            }
            case "IFCRECTANGLEHOLLOWPROFILEDEF":
            {
                double x = e.Number(3) * u / 2, y = e.Number(4) * u / 2, t = e.Number(5) * u;
                var holes = new List<List<P2>>();
                if (x - t > 1e-6 && y - t > 1e-6)
                    holes.Add(Place([new(-(x - t), -(y - t)), new(-(x - t), y - t), new(x - t, y - t), new(x - t, -(y - t))]));
                result.Add((Place([new(-x, -y), new(x, -y), new(x, y), new(-x, y)]), holes));
                break;
            }
            case "IFCCIRCLEPROFILEDEF":
                result.Add((Place(Circle(e.Number(3) * u)), []));
                break;
            case "IFCCIRCLEHOLLOWPROFILEDEF":
            {
                var radius = e.Number(3) * u;
                var inner = radius - e.Number(4) * u;
                result.Add((Place(Circle(radius)), inner > 1e-6 ? [Place(Circle(inner))] : []));
                break;
            }
            case "IFCELLIPSEPROFILEDEF":
            {
                double a = e.Number(3) * u, b = e.Number(4) * u;
                result.Add((Place(Enumerable.Range(0, 32).Select(i => new P2(a * Math.Cos(i * Math.PI / 16), b * Math.Sin(i * Math.PI / 16)))), []));
                break;
            }
            case "IFCISHAPEPROFILEDEF":
            {
                double w = e.Number(3) * u / 2, h = e.Number(4) * u / 2, tw = e.Number(5) * u / 2, tf = e.Number(6) * u;
                result.Add((Place([new(-w, -h), new(w, -h), new(w, -h + tf), new(tw, -h + tf), new(tw, h - tf), new(w, h - tf),
                    new(w, h), new(-w, h), new(-w, h - tf), new(-tw, h - tf), new(-tw, -h + tf), new(-w, -h + tf)]), []));
                break;
            }
            case "IFCLSHAPEPROFILEDEF":
            {
                double d = e.Number(3) * u, w = (e.Number(4, e.Number(3)) * u), t = e.Number(5) * u;
                result.Add((Place([new(-w / 2, -d / 2), new(w / 2, -d / 2), new(w / 2, -d / 2 + t), new(-w / 2 + t, -d / 2 + t),
                    new(-w / 2 + t, d / 2), new(-w / 2, d / 2)]), []));
                break;
            }
            case "IFCTSHAPEPROFILEDEF":
            {
                // Attributes: Depth, FlangeWidth, WebThickness, FlangeThickness.
                double d = e.Number(3) * u / 2, fw = e.Number(4) * u / 2, tw = e.Number(5) * u / 2, tf = e.Number(6) * u;
                result.Add((Place([new(-tw, -d), new(tw, -d), new(tw, d - tf), new(fw, d - tf), new(fw, d), new(-fw, d),
                    new(-fw, d - tf), new(-tw, d - tf)]), []));
                break;
            }
            case "IFCUSHAPEPROFILEDEF":
            {
                double d = e.Number(3) * u / 2, fw = e.Number(4) * u / 2, tw = e.Number(5) * u, tf = e.Number(6) * u;
                result.Add((Place([new(-fw, -d), new(fw, -d), new(fw, d), new(fw - tw, d), new(fw - tw, -d + tf), new(-fw, -d + tf)]), []));
                break;
            }
            case "IFCARBITRARYCLOSEDPROFILEDEF" or "IFCARBITRARYPROFILEDEFWITHVOIDS":
            {
                if (e.Ref(2) is not { } curve) break;
                var holes = e.Type.EndsWith("VOIDS", StringComparison.Ordinal)
                    ? Refs(e.List(3)).Select(c => Curve(c)).Where(c => c.Count >= 3).ToList()
                    : [];
                var outer = Curve(curve.Id);
                if (outer.Count >= 3) result.Add((outer, holes));
                break;
            }
            case "IFCCOMPOSITEPROFILEDEF":
                foreach (var part in Refs(e.List(2))) result.AddRange(Profile(part, depth + 1));
                break;
            case "IFCDERIVEDPROFILEDEF":
            {
                if (e.Ref(2) is not { } parent || e.Ref(3) is not { } op) break;
                var transform = Placement(op.Id);
                P2 Move(P2 p)
                {
                    var q = transform.Apply(new D3(p.X, p.Y, 0));
                    return new P2(q.X, q.Y);
                }
                foreach (var (outer, holes) in Profile(parent.Id, depth + 1))
                    result.Add((outer.Select(Move).ToList(), holes.Select(h => h.Select(Move).ToList()).ToList()));
                break;
            }
            default:
                Note(e.Type);
                break;
        }
        return result;
    }

    private static List<P2> Circle(double radius)
    {
        var segments = Math.Clamp((int)Math.Ceiling(2 * Math.PI * radius / 0.04), 16, 72);
        return Enumerable.Range(0, segments)
            .Select(i => new P2(radius * Math.Cos(2 * Math.PI * i / segments), radius * Math.Sin(2 * Math.PI * i / segments)))
            .ToList();
    }

    private static IEnumerable<P2> RoundedRectangle(double x, double y, double r)
    {
        if (r < 1e-6)
        {
            yield return new P2(-x, -y);
            yield return new P2(x, -y);
            yield return new P2(x, y);
            yield return new P2(-x, y);
            yield break;
        }
        (double Cx, double Cy, double Start)[] corners = [(x - r, -y + r, -90), (x - r, y - r, 0), (-x + r, y - r, 90), (-x + r, -y + r, 180)];
        foreach (var (cx, cy, start) in corners)
            for (var i = 0; i <= 6; i++)
            {
                var angle = (start + 90.0 * i / 6) * Math.PI / 180;
                yield return new P2(cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
            }
    }

    // ------------------------------------------------------------------ curves

    /// <summary>Points of a planar curve in its own XY plane (polylines, indexed poly-curves, composite curves, circles).</summary>
    private List<P2> Curve(int id, int depth = 0)
    {
        var points = new List<P2>();
        if (depth > 6 || _step.Get(id) is not { } e) return points;
        switch (e.Type)
        {
            case "IFCPOLYLINE":
                foreach (var r in Refs(e.List(0)))
                {
                    var p = Point(r);
                    points.Add(new P2(p.X, p.Y));
                }
                break;
            case "IFCINDEXEDPOLYCURVE":
                IndexedPolyCurve(e, points);
                break;
            case "IFCCOMPOSITECURVE":
                foreach (var segmentId in Refs(e.List(0)))
                {
                    if (_step.Get(segmentId) is not { } segment || segment.Ref(2) is not { } parent) continue;
                    var part = Curve(parent.Id, depth + 1);
                    if (!segment.Flag(1)) part.Reverse();
                    foreach (var p in part)
                        if (points.Count == 0 || points[^1] != p) points.Add(p);
                }
                break;
            case "IFCTRIMMEDCURVE":
                Trimmed(e, points);
                break;
            case "IFCCIRCLE":
            {
                var radius = e.Number(1) * _unit;
                var place = e.Ref(0) is { } pl ? Placement(pl.Id) : Xform.Identity;
                points.AddRange(Circle(radius).Select(p =>
                {
                    var q = place.Apply(new D3(p.X, p.Y, 0));
                    return new P2(q.X, q.Y);
                }));
                break;
            }
        }
        // A closed curve often repeats its first point; the mesher wants it listed once.
        if (points.Count > 2 && points[0] == points[^1]) points.RemoveAt(points.Count - 1);
        return points;
    }

    private void IndexedPolyCurve(StepEntity e, List<P2> output)
    {
        if (e.Ref(0) is not { } pointList || _step.Get(pointList.Id) is not { } list || list.List(0) is not { } coordinates) return;
        P2 At(int oneBased)
        {
            var row = (coordinates[Math.Clamp(oneBased - 1, 0, coordinates.Count - 1)] as List<object?>) ?? [];
            return new P2((StepFile.AsNumber(row.ElementAtOrDefault(0)) ?? 0) * _unit,
                (StepFile.AsNumber(row.ElementAtOrDefault(1)) ?? 0) * _unit);
        }
        if (e.List(1) is not { Count: > 0 } segments)
        {
            for (var i = 1; i <= coordinates.Count; i++) output.Add(At(i));
            return;
        }
        foreach (var segment in segments.OfType<StepTyped>())
        {
            var indices = (segment.Value as List<object?> ?? []).Select(v => (int)(StepFile.AsNumber(v) ?? 1)).ToList();
            if (segment.Type == "IFCARCINDEX" && indices.Count == 3)
            {
                var arc = ArcThrough(At(indices[0]), At(indices[1]), At(indices[2]));
                foreach (var p in arc) if (output.Count == 0 || output[^1] != p) output.Add(p);
            }
            else
            {
                foreach (var index in indices)
                {
                    var p = At(index);
                    if (output.Count == 0 || output[^1] != p) output.Add(p);
                }
            }
        }
    }

    /// <summary>Points along the circular arc from a through b to c (inclusive).</summary>
    private static List<P2> ArcThrough(P2 a, P2 b, P2 c)
    {
        var d = 2 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
        if (Math.Abs(d) < 1e-12) return [a, c];
        var ux = ((a.X * a.X + a.Y * a.Y) * (b.Y - c.Y) + (b.X * b.X + b.Y * b.Y) * (c.Y - a.Y) + (c.X * c.X + c.Y * c.Y) * (a.Y - b.Y)) / d;
        var uy = ((a.X * a.X + a.Y * a.Y) * (c.X - b.X) + (b.X * b.X + b.Y * b.Y) * (a.X - c.X) + (c.X * c.X + c.Y * c.Y) * (b.X - a.X)) / d;
        var radius = Math.Sqrt((a.X - ux) * (a.X - ux) + (a.Y - uy) * (a.Y - uy));
        var start = Math.Atan2(a.Y - uy, a.X - ux);
        var middle = Math.Atan2(b.Y - uy, b.X - ux);
        var end = Math.Atan2(c.Y - uy, c.X - ux);
        // Choose the sweep direction that passes through b.
        var ccw = Normalize(middle - start) < Normalize(end - start);
        var sweep = ccw ? Normalize(end - start) : -Normalize(start - end);
        var steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 24)));
        var points = new List<P2>(steps + 1);
        for (var i = 0; i <= steps; i++)
        {
            var angle = start + sweep * i / steps;
            points.Add(new P2(ux + radius * Math.Cos(angle), uy + radius * Math.Sin(angle)));
        }
        return points;
    }

    private static double Normalize(double angle)
    {
        while (angle < 0) angle += 2 * Math.PI;
        while (angle >= 2 * Math.PI) angle -= 2 * Math.PI;
        return angle;
    }

    /// <summary>A trimmed circle: an arc between two parameters (angles) or two points.</summary>
    private void Trimmed(StepEntity e, List<P2> output)
    {
        if (e.Ref(0) is not { } basis || _step.Get(basis.Id) is not { Type: "IFCCIRCLE" } circle)
        {
            if (e.Ref(0) is { } line && _step.Get(line.Id) is { Type: "IFCLINE" or "IFCPOLYLINE" })
                output.AddRange(Curve(line.Id));
            return;
        }
        var radius = circle.Number(1) * _unit;
        var place = circle.Ref(0) is { } pl ? Placement(pl.Id) : Xform.Identity;
        double? Angle(List<object?>? trim)
        {
            if (trim is null) return null;
            foreach (var item in trim)
            {
                if (item is StepRef point)
                {
                    var p = place.Inverse().Apply(Point(point.Id));
                    return Math.Atan2(p.Y, p.X);
                }
                if (item is StepTyped { Value: double value }) return value * _angle;
            }
            return null;
        }
        var start = Angle(e.List(1));
        var end = Angle(e.List(2));
        if (start is null || end is null) return;
        var sense = e.Flag(3);
        var s = start.Value;
        var t = end.Value;
        if (sense) while (t <= s) t += 2 * Math.PI;
        else while (t >= s) t -= 2 * Math.PI;
        var steps = Math.Max(3, (int)Math.Ceiling(Math.Abs(t - s) / (Math.PI / 24)));
        for (var i = 0; i <= steps; i++)
        {
            var angle = s + (t - s) * i / steps;
            var q = place.Apply(new D3(radius * Math.Cos(angle), radius * Math.Sin(angle), 0));
            output.Add(new P2(q.X, q.Y));
        }
    }

    // ------------------------------------------------------------------ shells

    private TriSoup Brep(StepEntity brep)
    {
        var soup = new TriSoup();
        if (brep.Ref(0) is { } outer) AddShell(soup, outer.Id);
        if (brep.Type.EndsWith("VOIDS", StringComparison.Ordinal))
            foreach (var shell in Refs(brep.List(1))) AddShell(soup, shell);
        return soup;
    }

    private void AddShell(TriSoup soup, int shellId)
    {
        if (_step.Get(shellId, cache: false) is not { } shell) return;
        var faces = shell.Type is "IFCCLOSEDSHELL" or "IFCOPENSHELL" or "IFCCONNECTEDFACESET" ? shell.List(0) : null;
        // A surface model may point straight at a shell wrapper or at a face set.
        foreach (var faceId in Refs(faces)) AddFace(soup, faceId);
    }

    private void AddFace(TriSoup soup, int faceId)
    {
        if (_step.Get(faceId, cache: false) is not { } face) return;
        var loops = new List<(List<Vector3> Points, bool Outer)>();
        foreach (var boundId in Refs(face.List(0)))
        {
            if (_step.Get(boundId, cache: false) is not { } bound || bound.Ref(0) is not { } loopRef) continue;
            if (_step.Get(loopRef.Id, cache: false) is not { Type: "IFCPOLYLOOP" } loop) continue;
            var points = Refs(loop.List(0)).Select(r => Point(r).ToVector()).ToList();
            if (points.Count > 1 && points[0] == points[^1]) points.RemoveAt(points.Count - 1);
            if (bound.Args.Count > 1 && !bound.Flag(1)) points.Reverse();
            if (points.Count >= 3) loops.Add((points, bound.Type == "IFCFACEOUTERBOUND" || loops.Count == 0));
        }
        if (loops.Count == 0) return;
        var outer = loops.FirstOrDefault(l => l.Outer).Points ?? loops[0].Points;
        var holes = loops.Where(l => !ReferenceEquals(l.Points, outer)).Select(l => l.Points).ToList();
        AddPolygon(soup, outer, holes);
    }

    /// <summary>Triangulates a planar polygon with holes in 3D, keeping the winding of the outer loop.</summary>
    private static void AddPolygon(TriSoup soup, List<Vector3> outer, List<List<Vector3>> holes)
    {
        if (outer.Count == 3 && holes.Count == 0)
        {
            soup.Add(outer[0], outer[1], outer[2]);
            return;
        }
        var normal = Vector3.Zero;
        for (var i = 0; i < outer.Count; i++)
        {
            var c = outer[i];
            var n = outer[(i + 1) % outer.Count];
            normal += new Vector3((c.Y - n.Y) * (c.Z + n.Z), (c.Z - n.Z) * (c.X + n.X), (c.X - n.X) * (c.Y + n.Y));
        }
        if (normal.LengthSquared() < 1e-14f) return;
        normal = Vector3.Normalize(normal);
        var (tangent, bitangent) = Rendering.Sampling.Basis(normal);
        P2 Project(Vector3 p) => new(Vector3.Dot(p, tangent), Vector3.Dot(p, bitangent));
        var all = new List<Vector3>(outer);
        foreach (var hole in holes) all.AddRange(hole);
        var triangles = PolygonMesher.Triangulate(outer.Select(Project).ToList(),
            holes.Select(h => (IReadOnlyList<P2>)h.Select(Project).ToList()).ToList());
        // The mesher works counter-clockwise in the (tangent, bitangent) basis, whose normal is the Newell normal,
        // so triangles already follow the outer loop's own winding.
        foreach (var (a, b, c) in triangles) soup.Add(all[a], all[b], all[c]);
    }

    private TriSoup FaceSet(StepEntity set)
    {
        var soup = new TriSoup();
        if (set.Ref(0) is not { } coordinatesRef || _step.Get(coordinatesRef.Id) is not { } coordinateList ||
            coordinateList.List(0) is not { } rows) return soup;
        var points = new Vector3[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i] as List<object?> ?? [];
            points[i] = new Vector3((float)((StepFile.AsNumber(row.ElementAtOrDefault(0)) ?? 0) * _unit),
                (float)((StepFile.AsNumber(row.ElementAtOrDefault(1)) ?? 0) * _unit),
                (float)((StepFile.AsNumber(row.ElementAtOrDefault(2)) ?? 0) * _unit));
        }
        if (set.Type == "IFCTRIANGULATEDFACESET")
        {
            var indices = set.List(3);
            var remap = set.List(4);
            if (indices is null) return soup;
            foreach (var triangle in indices.OfType<List<object?>>())
            {
                if (triangle.Count < 3) continue;
                var v = new Vector3[3];
                var valid = true;
                for (var k = 0; k < 3; k++)
                {
                    var index = (int)(StepFile.AsNumber(triangle[k]) ?? 0) - 1;
                    if (remap is not null && (uint)index < (uint)remap.Count) index = (int)(StepFile.AsNumber(remap[index]) ?? 0) - 1;
                    if ((uint)index >= (uint)points.Length) valid = false;
                    else v[k] = points[index];
                }
                if (valid) soup.Add(v[0], v[1], v[2]);
            }
            return soup;
        }
        var pn = set.List(3);
        foreach (var faceId in Refs(set.List(2)))
        {
            if (_step.Get(faceId, cache: false) is not { } face) continue;
            List<Vector3> Loop(List<object?>? indices) => (indices ?? []).Select(v =>
            {
                var index = (int)(StepFile.AsNumber(v) ?? 0) - 1;
                if (pn is not null && (uint)index < (uint)pn.Count) index = (int)(StepFile.AsNumber(pn[index]) ?? 0) - 1;
                return (uint)index < (uint)points.Length ? points[index] : Vector3.Zero;
            }).ToList();
            var outer = Loop(face.List(0));
            var holes = face.Type.EndsWith("VOIDS", StringComparison.Ordinal)
                ? (face.List(1) ?? []).OfType<List<object?>>().Select(Loop).ToList()
                : [];
            if (outer.Count >= 3) AddPolygon(soup, outer, holes);
        }
        return soup;
    }

    private TriSoup Block(StepEntity block)
    {
        var position = block.Ref(0) is { } p ? Placement(p.Id) : Xform.Identity;
        double x = block.Number(1) * _unit, y = block.Number(2) * _unit, z = block.Number(3) * _unit;
        var soup = new TriSoup();
        Prism(soup, [new(0, 0), new(x, 0), new(x, y), new(0, y)], [], new D3(0, 0, z), position);
        return soup;
    }

    // ------------------------------------------------------------------ instances and booleans

    private IEnumerable<Part> Mapped(StepEntity item, int depth)
    {
        if (item.Ref(0) is not { } sourceRef || _step.Get(sourceRef.Id) is not { Type: "IFCREPRESENTATIONMAP" } map) yield break;
        var origin = map.Ref(0) is { } o ? Placement(o.Id) : Xform.Identity;
        var target = item.Ref(1) is { } t ? Placement(t.Id) : Xform.Identity;
        var transform = origin.Inverse().Then(target);
        if (!_mapCache.TryGetValue(sourceRef.Id, out var parts))
        {
            parts = [];
            if (map.Ref(1) is { } representation && _step.Get(representation.Id) is { } shape)
                foreach (var child in Refs(shape.List(3))) parts.AddRange(Item(child, depth + 1));
            _mapCache[sourceRef.Id] = parts;
        }
        var matrix = transform.ToMatrix(new D3(0, 0, 0));
        foreach (var part in parts) yield return part with { Soup = part.Soup.Transformed(matrix) };
    }

    private IEnumerable<Part> Boolean(StepEntity result, int depth)
    {
        if (result.Ref(1) is not { } firstRef) yield break;
        var first = Item(firstRef.Id, depth + 1);
        var operation = result.Enum(0);
        if (operation != "DIFFERENCE" || result.Ref(2) is not { } secondRef || _step.Get(secondRef.Id) is not { } second)
        {
            foreach (var part in first) yield return part;
            yield break;
        }

        if (second.Type is "IFCHALFSPACESOLID" or "IFCPOLYGONALBOUNDEDHALFSPACE")
        {
            if (second.Ref(0) is not { } surface || _step.Get(surface.Id) is not { Type: "IFCPLANE" } plane ||
                plane.Ref(0) is not { } placementRef)
            {
                foreach (var part in first) yield return part;
                yield break;
            }
            var frame = Placement(placementRef.Id);
            // A bounded half-space has its own placement that moves the plane into the solid's frame.
            if (second.Type == "IFCPOLYGONALBOUNDEDHALFSPACE" && second.Ref(2) is { } boundPlacement)
                frame = frame.Then(Placement(boundPlacement.Id));
            var normal = frame.AxisZ.ToVector();
            var d = Vector3.Dot(normal, frame.Origin.ToVector());
            // The half-space's material lies opposite its normal when the agreement flag is true; that side is removed.
            var keepPositive = second.Flag(1);
            foreach (var part in first)
            {
                Clipped++;
                var clipped = MeshClipper.Clip(part.Soup, normal, d, keepPositive);
                if (clipped.TriangleCount > 0) yield return part with { Soup = clipped };
            }
            yield break;
        }

        // A solid subtracted from a solid: exact for convex cutters, ignored otherwise.
        var cutters = Item(secondRef.Id, depth + 1);
        foreach (var part in first)
        {
            var current = new List<TriSoup> { part.Soup };
            foreach (var cutter in cutters)
            {
                if (MeshClipper.ConvexPlanes(cutter.Soup) is not { } planes) continue;
                var next = new List<TriSoup>();
                foreach (var piece in current) next.AddRange(MeshClipper.Subtract(piece, planes));
                current = next;
                Clipped++;
            }
            foreach (var piece in current) yield return part with { Soup = piece };
        }
    }
}
