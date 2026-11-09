using System.Numerics;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core.Navigation;

/// <summary>The kinds of project element that can be clicked in a 3D view.</summary>
public enum PickedKind { Wall, Opening, Floor, Column, Structural }

/// <summary>An element hit by a ray: what it is, how far away, and (for openings) the wall that hosts it.</summary>
public readonly record struct PickedElement(PickedKind Kind, Guid Id, float Distance, Guid? HostId = null);

/// <summary>A temporary dimension drawn in the 3D view: a straight measure between two model points.</summary>
public readonly record struct DimensionLine(Vector3 A, Vector3 B, float Meters);

/// <summary>
/// Finds which element of the project is under a point of the 3D view and measures it. Every element is reduced to a
/// vertical prism (its plan footprint between a bottom and a top height), which is exactly how walls, openings, columns,
/// slabs and the parts of stairs or railings are built, so a ray test against the prism is also a test against the
/// element and does not depend on the triangles the viewer happens to draw.
/// </summary>
public sealed class ElementPicker
{
    /// <summary>Openings sit inside their wall; this makes the opening win when the ray enters both at the same spot.</summary>
    private const float OpeningPreference = 0.03f;

    private sealed record Prism(PickedKind Kind, Guid Id, Guid? HostId, Vector2[] Footprint, float Bottom, float Top, float Datum,
        Vector2 Axis, float Preference);

    private readonly List<Prism> _prisms = [];

    /// <summary>Indexes a document snapshot as world-space prisms for selection and element bounds.</summary>
    public ElementPicker(BuildingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var elevations = document.Levels.ToDictionary(level => level.Id, level => (float)level.Elevation);
        float Elevation(Guid levelId) => elevations.GetValueOrDefault(levelId);
        var walls = document.Walls.ToDictionary(wall => wall.Id);

        foreach (var wall in document.Walls)
        {
            var z = Elevation(wall.LevelId);
            _prisms.Add(WallPrism(PickedKind.Wall, wall.Id, null, wall, 0, wall.Length, z + (float)wall.BaseOffset, z + (float)wall.Height, z, 0));
        }
        foreach (var opening in document.Openings)
        {
            if (!walls.TryGetValue(opening.WallId, out var wall)) continue;
            var z = Elevation(wall.LevelId);
            _prisms.Add(WallPrism(PickedKind.Opening, opening.Id, wall.Id, wall, opening.Begin, opening.End,
                z, z + (float)(opening.Sill + opening.Height), z, -OpeningPreference, (float)opening.Sill));
        }
        foreach (var floor in document.Floors)
        {
            if (floor.Boundary.Count < 3) continue;
            var built = RoofGeometry.WithOverhang(floor);
            var top = Elevation(floor.LevelId) + (float)built.ElevationOffset;
            var footprint = built.Boundary.Select(p => new Vector2((float)p.X, (float)p.Y)).ToArray();
            var ridge = 0f;
            if (floor.IsToposolid)
                ridge = (float)Math.Max(0, new Geometry.TopoSurface(floor).Maximum - floor.ElevationOffset);
            else if (floor.IsPitched)
            {
                var span = MathF.Min(footprint.Max(p => p.X) - footprint.Min(p => p.X), footprint.Max(p => p.Y) - footprint.Min(p => p.Y));
                ridge = span / 2 * MathF.Tan((float)floor.Pitch * MathF.PI / 180);
            }
            _prisms.Add(new Prism(PickedKind.Floor, floor.Id, null, footprint, top - (float)floor.Thickness, top + ridge,
                Elevation(floor.LevelId), Vector2.UnitX, 0));
        }
        foreach (var column in document.Columns)
        {
            var z = Elevation(column.LevelId);
            var footprint = column.Outline.Select(p => new Vector2((float)p.X, (float)p.Y)).ToArray();
            if (footprint.Length < 3) continue;
            _prisms.Add(new Prism(PickedKind.Column, column.Id, null, footprint, z, z + (float)column.Height, z, Vector2.UnitX, 0));
        }
        // Voids have no rendered solid, but their cavity must remain selectable for editing.
        foreach (var opening in document.StructuralElements.Where(element =>
                     element.Kind is StructuralElementKind.Opening or StructuralElementKind.Shaft))
        {
            foreach (var floor in SlabOpeningHosts.Resolve(document, opening))
            {
                var top = Elevation(floor.LevelId) + (float)floor.ElevationOffset;
                var footprint = opening.Footprint.Select(point => new Vector2((float)point.X, (float)point.Y)).ToArray();
                _prisms.Add(new Prism(PickedKind.Structural, opening.Id, floor.Id, footprint,
                    top - (float)floor.Thickness, top, Elevation(opening.LevelId), Vector2.UnitX, -0.06f));
            }
        }
        foreach (var element in document.StructuralElements.Where(element => element.HasSolid))
        {
            var datum = Elevation(element.LevelId);
            foreach (var part in ElementGeometry.Parts(element))
            {
                if (part.Footprint.Count < 3) continue;
                var footprint = part.Footprint.Select(p => new Vector2((float)p.X, (float)p.Y)).ToArray();
                var edge = footprint[1] - footprint[0];
                var axis = edge.LengthSquared() > 1e-10f ? Vector2.Normalize(edge) : Vector2.UnitX;
                var top = (float)Math.Max(part.Top, part.EndTop ?? part.Top);
                _prisms.Add(new Prism(PickedKind.Structural, element.Id, null, footprint, datum + (float)part.Bottom, datum + top, datum, axis, 0));
            }
        }
    }

    public int Count => _prisms.Count;

    /// <summary>Returns the world-space bounds of all solids belonging to one selectable element.</summary>
    public (Vector3 Min, Vector3 Max)? Bounds(PickedKind kind, Guid id) => BoundsOf(Of(kind, id));

    /// <summary>Combines a multiple-element selection into one orbit box, ignoring removed identifiers.</summary>
    public (Vector3 Min, Vector3 Max)? Bounds(IEnumerable<Guid> elementIds)
    {
        var selected = elementIds.ToHashSet();
        return BoundsOf(_prisms.Where(prism => selected.Contains(prism.Id)));
    }

    /// <summary>Accumulates component bounds without allocating selection outlines or temporary dimensions.</summary>
    private static (Vector3 Min, Vector3 Max)? BoundsOf(IEnumerable<Prism> prisms)
    {
        var minimum = new Vector3(float.PositiveInfinity);
        var maximum = new Vector3(float.NegativeInfinity);
        var found = false;
        foreach (var prism in prisms)
        foreach (var point in prism.Footprint)
        {
            minimum = Vector3.Min(minimum, new Vector3(point, prism.Bottom));
            maximum = Vector3.Max(maximum, new Vector3(point, prism.Top));
            found = true;
        }
        return found ? (minimum, maximum) : null;
    }

    /// <summary>Builds a wall-aligned prism, retaining the level datum separately from a hosted opening's sill.</summary>
    private static Prism WallPrism(PickedKind kind, Guid id, Guid? host, Wall wall, double from, double to, float datum, float top,
        float bottomDatum, float preference, float sill = 0)
    {
        var start = new Vector2((float)wall.Start.X, (float)wall.Start.Y);
        var end = new Vector2((float)wall.End.X, (float)wall.End.Y);
        var along = end - start;
        var axis = along.LengthSquared() > 1e-10f ? Vector2.Normalize(along) : Vector2.UnitX;
        var normal = new Vector2(-axis.Y, axis.X) * (float)(wall.Thickness / 2);
        var a = start + axis * (float)from;
        var b = start + axis * (float)to;
        return new Prism(kind, id, host, [a - normal, b - normal, b + normal, a + normal], datum + sill, top, bottomDatum, axis, preference);
    }

    /// <summary>The nearest element along a ray, or null when the ray hits nothing.</summary>
    public PickedElement? Pick(Vector3 origin, Vector3 direction)
    {
        PickedElement? best = null;
        var bestKey = float.MaxValue;
        foreach (var prism in _prisms)
        {
            if (Hit(prism, origin, direction) is not { } distance) continue;
            var key = distance + prism.Preference;
            if (key >= bestKey) continue;
            bestKey = key;
            best = new PickedElement(prism.Kind, prism.Id, distance, prism.HostId);
        }
        return best;
    }

    /// <summary>The edges of an element's prism, for drawing a highlight over it.</summary>
    public List<EdgeSegment> Outline(PickedKind kind, Guid id)
        => OutlinePrisms(Of(kind, id));

    /// <summary>
    /// The faces of an element's prism as triangles (three corners each, counted in groups of three), for tinting a
    /// selection. Ear-clipped caps respect concave footprints instead of spilling blue tint outside the selected slab.
    /// </summary>
    public List<Vector3> Fill(PickedKind kind, Guid id)
    {
        var result = new List<Vector3>();
        foreach (var prism in Of(kind, id))
        {
            var ring = prism.Footprint;
            for (var i = 0; i < ring.Length; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Length];
                result.AddRange([new(a, prism.Bottom), new(b, prism.Bottom), new(b, prism.Top),
                    new(a, prism.Bottom), new(b, prism.Top), new(a, prism.Top)]);
            }
            // A triangle fan covers the wrong area on a concave footprint. Reuse the BIM mesher's
            // ear-clipping algorithm so selection tint and the model share the same planar outline.
            // A malformed outline should never crash selection; keep its side faces and outline.
            IReadOnlyList<(int A, int B, int C)> caps;
            try
            {
                caps = PolygonTriangulator.Triangulate(
                    ring.Select(point => new Point2(point.X, point.Y)).ToArray());
            }
            catch (ArgumentException)
            {
                continue;
            }
            foreach (var (a, b, c) in caps)
            {
                result.AddRange([new(ring[a], prism.Top), new(ring[b], prism.Top), new(ring[c], prism.Top)]);
                result.AddRange([new(ring[a], prism.Bottom), new(ring[c], prism.Bottom), new(ring[b], prism.Bottom)]);
            }
        }
        return result;
    }

    /// <summary>Builds a hosted opening's preview edges without indexing the rest of the project.</summary>
    public static List<EdgeSegment> OpeningOutline(Wall wall, WallOpening opening, float levelElevation) =>
        OutlinePrisms([WallPrism(PickedKind.Opening, opening.Id, wall.Id, wall, opening.Begin, opening.End,
            levelElevation, levelElevation + (float)(opening.Sill + opening.Height), levelElevation, 0, (float)opening.Sill)]);

    /// <summary>Expands each prism into matching bottom, top, and vertical edge triples used by selection and previews.</summary>
    private static List<EdgeSegment> OutlinePrisms(IEnumerable<Prism> prisms)
    {
        var result = new List<EdgeSegment>();
        foreach (var prism in prisms)
        {
            var ring = prism.Footprint;
            for (var i = 0; i < ring.Length; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Length];
                result.Add(new EdgeSegment(new Vector3(a, prism.Bottom), new Vector3(b, prism.Bottom)));
                result.Add(new EdgeSegment(new Vector3(a, prism.Top), new Vector3(b, prism.Top)));
                result.Add(new EdgeSegment(new Vector3(a, prism.Bottom), new Vector3(a, prism.Top)));
            }
        }
        return result;
    }

    /// <summary>
    /// Measures an element the way a temporary dimension does: along its length, across its width, up its height, and the
    /// height of its underside above the level when it does not rest on it (the sill of a window).
    /// </summary>
    public List<DimensionLine> Dimensions(PickedKind kind, Guid id)
    {
        var result = new List<DimensionLine>();
        // An element made of several prisms (a stair) is measured as a whole.
        var parts = Of(kind, id).ToList();
        if (parts.Count == 0) return result;
        var axis = parts[0].Axis;
        var perpendicular = new Vector2(-axis.Y, axis.X);
        float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
        float bottom = float.MaxValue, top = float.MinValue;
        foreach (var part in parts)
        {
            bottom = MathF.Min(bottom, part.Bottom);
            top = MathF.Max(top, part.Top);
            foreach (var point in part.Footprint)
            {
                var u = Vector2.Dot(point, axis);
                var v = Vector2.Dot(point, perpendicular);
                minU = MathF.Min(minU, u);
                maxU = MathF.Max(maxU, u);
                minV = MathF.Min(minV, v);
                maxV = MathF.Max(maxV, v);
            }
        }
        Vector3 At(float u, float v, float z) => new(axis * u + perpendicular * v, z);
        void Add(Vector3 a, Vector3 b)
        {
            var length = Vector3.Distance(a, b);
            if (length > 0.005f) result.Add(new DimensionLine(a, b, length));
        }
        Add(At(minU, minV, bottom), At(maxU, minV, bottom));
        Add(At(maxU, minV, bottom), At(maxU, maxV, bottom));
        Add(At(minU, minV, bottom), At(minU, minV, top));
        var datum = parts[0].Datum;
        if (bottom - datum > 0.01f) Add(At(minU, minV, datum), At(minU, minV, bottom));
        return result;
    }

    /// <summary>Collects all component prisms for one element without treating each stair tread as a selection.</summary>
    private IEnumerable<Prism> Of(PickedKind kind, Guid id) => _prisms.Where(prism => prism.Kind == kind && prism.Id == id);

    /// <summary>Clips a forward ray against prism height and footprint, returning its nearest entry distance.</summary>
    private static float? Hit(Prism prism, Vector3 origin, Vector3 direction)
    {
        float t0 = 0, t1 = float.MaxValue;
        if (MathF.Abs(direction.Z) < 1e-8f)
        {
            if (origin.Z < prism.Bottom || origin.Z > prism.Top) return null;
        }
        else
        {
            var a = (prism.Bottom - origin.Z) / direction.Z;
            var b = (prism.Top - origin.Z) / direction.Z;
            if (a > b) (a, b) = (b, a);
            t0 = MathF.Max(0, a);
            t1 = b;
            if (t1 < t0) return null;
        }
        var o = new Vector2(origin.X, origin.Y);
        var d = new Vector2(direction.X, direction.Y);
        // Entering through the top or bottom face.
        if (Inside(prism.Footprint, o + d * t0)) return t0;
        if (d.LengthSquared() < 1e-12f) return null;
        // Otherwise through a side face: the first crossing of the footprint's outline inside the height range.
        float? best = null;
        var ring = prism.Footprint;
        for (var i = 0; i < ring.Length; i++)
        {
            var a = ring[i];
            var e = ring[(i + 1) % ring.Length] - a;
            var denominator = Cross(d, e);
            if (MathF.Abs(denominator) < 1e-12f) continue;
            var w = a - o;
            var t = Cross(w, e) / denominator;
            var s = Cross(w, d) / denominator;
            if (t < t0 || t > t1 || s < 0 || s > 1) continue;
            if (best is null || t < best) best = t;
        }
        return best;
    }

    /// <summary>Evaluates the signed planar determinant used to intersect a ray and a polygon edge.</summary>
    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    /// <summary>Uses crossing parity to determine whether a ray's height entry is inside the footprint.</summary>
    private static bool Inside(Vector2[] ring, Vector2 point)
    {
        var inside = false;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
        {
            var a = ring[i];
            var b = ring[j];
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }
}
