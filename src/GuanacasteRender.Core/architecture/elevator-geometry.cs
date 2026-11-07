using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core;

/// <summary>
/// The parts of an elevator: the car at its lowest landing, two guide rails up the hoistway, the machine under the
/// overhead (a machine-room-less traction drive), the pit buffers and a pair of sliding landing doors on the front at
/// every landing. The front is the side of the hoistway that faces -Y, turned in quarter turns by the element's
/// <see cref="StructuralElement.Rotation"/>.
/// </summary>
public static class ElevatorGeometry
{
    /// <summary>Clear height of the car and of its landing doors.</summary>
    public const double CarHeight = 2.30;
    public const double DoorHeight = 2.10;

    /// <summary>Space the hoistway keeps above the top landing, and the pit below the lowest one (EN 81-20).</summary>
    public const double Overhead = 3.60;
    public const double Pit = 1.20;

    /// <summary>The smallest hoistway that takes an accessible 1.10 × 1.40 m car (Ley 7600, EN 81-70 type 1).</summary>
    public const double MinimumWidth = 1.60;
    public const double MinimumDepth = 1.75;

    /// <summary>Vertical distance from the lowest landing to the highest one.</summary>
    public static double Travel(StructuralElement element) => element.Height + element.Offset - Overhead;

    /// <summary>Number of landings the element serves, from its own level up.</summary>
    public static int Landings(StructuralElement element)
    {
        var spacing = element.GridHeight > 0 ? element.GridHeight : 3;
        return Math.Max(1, (int)Math.Round(Travel(element) / spacing) + 1);
    }

    /// <summary>Car, rails, drive, buffers and landing doors, as prisms measured from the elevator's level.</summary>
    public static IEnumerable<ElementPart> Parts(StructuralElement element)
    {
        var extent = element.PlanExtent;
        // Quarter turns pick the front edge: 0 = -Y, 1 = +X, 2 = +Y, 3 = -X.
        var quarter = ((int)Math.Round(element.Rotation / (Math.PI / 2)) % 4 + 4) % 4;
        var sideways = quarter % 2 == 1;
        var width = sideways ? extent.MaxY - extent.MinY : extent.MaxX - extent.MinX;
        var depth = sideways ? extent.MaxX - extent.MinX : extent.MaxY - extent.MinY;
        if (width < 0.5 || depth < 0.5) yield break;
        Point2 Local(double across, double inward) => quarter switch
        {
            0 => new(extent.MinX + across, extent.MinY + inward),
            1 => new(extent.MaxX - inward, extent.MinY + across),
            2 => new(extent.MaxX - across, extent.MaxY - inward),
            _ => new(extent.MinX + inward, extent.MaxY - across)
        };
        IReadOnlyList<Point2> Rect(double a0, double d0, double a1, double d1) =>
            [Local(a0, d0), Local(a1, d0), Local(a1, d1), Local(a0, d1)];

        var bottom = element.Offset;
        var top = element.Offset + element.Height;
        // The car: floor, back and side walls in brushed steel, its roof, and the light panel in its ceiling.
        const double carFront = 0.12;
        var carBack = depth - 0.20;
        const double carLeft = 0.18;
        var carRight = width - 0.18;
        yield return new(Rect(carLeft, carFront, carRight, carBack), 0, 0.06, SurfaceKind.Metal);
        yield return new(Rect(carLeft, carBack - 0.04, carRight, carBack), 0.06, CarHeight, SurfaceKind.Metal);
        yield return new(Rect(carLeft, carFront, carLeft + 0.04, carBack - 0.04), 0.06, CarHeight, SurfaceKind.Metal);
        yield return new(Rect(carRight - 0.04, carFront, carRight, carBack - 0.04), 0.06, CarHeight, SurfaceKind.Metal);
        yield return new(Rect(carLeft, carFront, carRight, carBack), CarHeight + 0.01, CarHeight + 0.08, SurfaceKind.Metal);
        yield return new(Rect(carLeft + 0.25, carFront + 0.25, carRight - 0.25, carBack - 0.25), CarHeight, CarHeight + 0.01,
            SurfaceKind.LightEmitter);
        // Guide rails on both sides, pit floor to overhead.
        foreach (var side in new[] { 0.05, width - 0.11 })
            yield return new(Rect(side, depth / 2 - 0.04, side + 0.06, depth / 2 + 0.04), bottom, top - 0.1, SurfaceKind.Metal);
        // Machine-room-less drive under the overhead slab, and the pit buffers.
        yield return new(Rect(0.10, depth - 0.70, 0.70, depth - 0.10), top - 0.70, top - 0.10, SurfaceKind.Metal);
        foreach (var side in new[] { width / 2 - 0.30, width / 2 + 0.20 })
            yield return new(Rect(side, depth / 2 - 0.05, side + 0.10, depth / 2 + 0.05), bottom, bottom + 0.45, SurfaceKind.FireRed);
        // Landing doors: a steel frame and two leaves, centred on the front, at every landing.
        var door = Math.Min(element.Width > 0 ? element.Width : 0.9, width - 0.3);
        var left = (width - door) / 2;
        var spacing = element.GridHeight > 0 ? element.GridHeight : 3;
        for (var landing = 0; landing < Landings(element); landing++)
        {
            var floor = landing * spacing;
            yield return new(Rect(left - 0.08, 0, left, 0.06), floor, floor + DoorHeight + 0.08, SurfaceKind.Metal);
            yield return new(Rect(left + door, 0, left + door + 0.08, 0.06), floor, floor + DoorHeight + 0.08, SurfaceKind.Metal);
            yield return new(Rect(left, 0, left + door, 0.06), floor + DoorHeight, floor + DoorHeight + 0.08, SurfaceKind.Metal);
            yield return new(Rect(left, 0.01, left + door / 2 - 0.005, 0.04), floor, floor + DoorHeight, SurfaceKind.WallMetal);
            yield return new(Rect(left + door / 2 + 0.005, 0.01, left + door, 0.04), floor, floor + DoorHeight, SurfaceKind.WallMetal);
        }
    }
}
