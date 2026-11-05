using System.Numerics;

namespace Guanacaste3D.Core;

/// <summary>A visible edge of a family at a floor plan's cut plane.</summary>
public readonly record struct FamilyPlanSegment(Point2 Start, Point2 End);

/// <summary>Projects regenerated family solids into a level's architectural plan.</summary>
public static class FamilyPlanGeometry
{
    private const double Tolerance = 0.0001;

    /// <summary>
    /// Cuts solids at the view plane. Low solids that lie wholly below the cut plane retain their
    /// perimeter, so furniture remains visible in plan. Solids outside the story are excluded. When no
    /// type is specified, the first family type is used.
    /// </summary>
    public static IReadOnlyList<FamilyPlanSegment> ForLevel(FamilyDocument family, double levelElevation,
        double upperElevation, Guid? typeId = null)
    {
        if (!double.IsFinite(levelElevation) || !double.IsFinite(upperElevation) ||
            upperElevation <= levelElevation)
            throw new ArgumentOutOfRangeException(nameof(upperElevation), "A plan requires an ascending story interval.");

        var cutElevation = levelElevation + Math.Min(1.2, (upperElevation - levelElevation) / 2);
        var segments = new List<FamilyPlanSegment>();
        var keys = new HashSet<(long, long, long, long)>();
        foreach (var operation in family.Operations.Where(candidate =>
                     candidate.Kind == FamilyOperationKind.SolidExtrusion && candidate.Profile.Count >= 3))
        {
            var plane = FamilyGeometry.PlaneOf(family, operation);
            var offset = FamilyGeometry.Offset(family, operation, typeId);
            var depth = FamilyGeometry.Depth(family, operation, typeId);
            var profile = operation.Profile;
            var bottom = profile.Select(point => WorldPoint(plane, point, offset)).ToArray();
            var top = profile.Select(point => WorldPoint(plane, point, offset + depth)).ToArray();
            var minimum = Math.Min(bottom.Min(point => point.Z), top.Min(point => point.Z));
            var maximum = Math.Max(bottom.Max(point => point.Z), top.Max(point => point.Z));
            if (maximum < levelElevation - Tolerance || minimum >= upperElevation - Tolerance) continue;

            var before = segments.Count;
            for (var index = 0; index < profile.Count; index++)
            {
                var next = (index + 1) % profile.Count;
                AddTriangleCut(bottom[index], bottom[next], top[next], cutElevation, segments, keys);
                AddTriangleCut(bottom[index], top[next], top[index], cutElevation, segments, keys);
            }
            if (segments.Count != before) continue;

            // A low extrusion has no section intersection; show its true profile instead of disappearing.
            var perimeter = maximum <= cutElevation ? top : bottom;
            for (var index = 0; index < perimeter.Length; index++)
                AddSegment(perimeter[index], perimeter[(index + 1) % perimeter.Length], segments, keys);
        }
        return segments;
    }

    /// <summary>Transforms one profile point into the shared Z-up model coordinates.</summary>
    private static Vector3 WorldPoint(FamilyWorkPlane plane, Point2 point, double distance)
    {
        var yAxis = FamilyPoint3.Cross(plane.Normal, plane.XAxis);
        return new Vector3(
            (float)(plane.Origin.X + plane.XAxis.X * point.X + yAxis.X * point.Y + plane.Normal.X * distance),
            (float)(plane.Origin.Y + plane.XAxis.Y * point.X + yAxis.Y * point.Y + plane.Normal.Y * distance),
            (float)(plane.Origin.Z + plane.XAxis.Z * point.X + yAxis.Z * point.Y + plane.Normal.Z * distance));
    }

    /// <summary>Intersects one side triangle with a horizontal cut, rejecting coplanar edges.</summary>
    private static void AddTriangleCut(Vector3 first, Vector3 second, Vector3 third, double elevation,
        List<FamilyPlanSegment> segments, HashSet<(long, long, long, long)> keys)
    {
        var intersections = new List<Vector3>(2);
        foreach (var (start, end) in new[] { (first, second), (second, third), (third, first) })
        {
            var startDistance = start.Z - elevation;
            var endDistance = end.Z - elevation;
            if (Math.Abs(startDistance) <= Tolerance && Math.Abs(endDistance) <= Tolerance) continue;
            if (startDistance * endDistance > 0 || Math.Abs(startDistance - endDistance) <= Tolerance) continue;
            var fraction = startDistance / (startDistance - endDistance);
            if (fraction < 0 || fraction > 1) continue;
            var intersection = Vector3.Lerp(start, end, (float)fraction);
            if (intersections.All(point => Vector3.DistanceSquared(point, intersection) > Tolerance * Tolerance))
                intersections.Add(intersection);
        }
        if (intersections.Count == 2) AddSegment(intersections[0], intersections[1], segments, keys);
    }

    /// <summary>Deduplicates triangle intersections and omits edges with no plan length.</summary>
    private static void AddSegment(Vector3 start, Vector3 end, List<FamilyPlanSegment> segments,
        HashSet<(long, long, long, long)> keys)
    {
        var first = ((long)Math.Round(start.X / Tolerance), (long)Math.Round(start.Y / Tolerance));
        var second = ((long)Math.Round(end.X / Tolerance), (long)Math.Round(end.Y / Tolerance));
        if (first == second) return;
        if (first.CompareTo(second) > 0) (first, second) = (second, first);
        if (!keys.Add((first.Item1, first.Item2, second.Item1, second.Item2))) return;
        segments.Add(new FamilyPlanSegment(new Point2(start.X, start.Y), new Point2(end.X, end.Y)));
    }
}
