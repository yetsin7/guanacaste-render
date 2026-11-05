using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core;

public static partial class ResidentialComponentGeometry
{
    /// <summary>Builds the pedestal, open bowl, seat, cistern and visible supply stub from the fixture dimensions.</summary>
    private static IEnumerable<ElementPart> Toilet(StructuralElement element)
    {
        var width = Width(element);
        var depth = Depth(element);
        var seat = Math.Min(element.Height * 0.57, 0.43);
        yield return Soft(element, 0, depth * 0.03, width * 0.58, depth * 0.40, 0, seat * 0.72, 0.07, SurfaceKind.Ceramic);
        yield return Soft(element, 0, -depth * 0.08, width * 0.86, depth * 0.60, seat * 0.65, seat * 0.78, 0.08, SurfaceKind.Ceramic);
        foreach (var part in ToiletRim(element, width * 0.49, depth * 0.31, width * 0.34, depth * 0.21,
                     -depth * 0.08, seat * 0.76, seat, SurfaceKind.Ceramic)) yield return part;
        foreach (var part in ToiletRim(element, width * 0.45, depth * 0.28, width * 0.32, depth * 0.19,
                     -depth * 0.08, seat, seat + 0.025, SurfaceKind.OpeningFrame)) yield return part;
        yield return Soft(element, 0, depth * 0.34, width * 0.93, depth * 0.24,
            seat * 0.78, Math.Max(seat + 0.06, element.Height - 0.025), 0.035, SurfaceKind.Ceramic);
        yield return Soft(element, 0, depth * 0.34, width * 0.97, depth * 0.25,
            Math.Max(seat + 0.06, element.Height - 0.025), element.Height, 0.03, SurfaceKind.Ceramic);
        yield return RoundBlock(element, 0, depth * 0.34, 0.045, element.Height, element.Height + 0.008, SurfaceKind.Metal);
        // The exposed inlet marks the nominal connection used by the cold-water routing model.
        yield return RoundBlock(element, 0, depth / 2 - 0.06, 0.035, 0.18, 0.23, SurfaceKind.Metal);
    }

    /// <summary>Approximates an elliptical annulus with individually closed sectors so the bowl remains open.</summary>
    private static IEnumerable<ElementPart> ToiletRim(StructuralElement element, double outerX, double outerY,
        double innerX, double innerY, double centerY, double bottom, double top, SurfaceKind surface)
    {
        const int segments = 24;
        for (var index = 0; index < segments; index++)
        {
            var first = index * Math.Tau / segments;
            var next = (index + 1) * Math.Tau / segments;
            Point2 At(double x, double y, double angle) => new(element.Start.X + x * Math.Cos(angle),
                element.Start.Y + centerY + y * Math.Sin(angle));
            yield return new ElementPart([At(outerX, outerY, first), At(outerX, outerY, next),
                At(innerX, innerY, next), At(innerX, innerY, first)], element.Offset + bottom, element.Offset + top, surface);
        }
    }
}
