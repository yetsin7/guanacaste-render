using Guanacaste3D.Core;

/// <summary>Regression checks for contextual line and rectangle wall placement geometry.</summary>
static class WallPlacementChecks
{
    /// <summary>Verifies orthogonal constraints, face references, offsets, and connected rectangle loops.</summary>
    public static void Run(Action<bool, string> check)
    {
        var horizontal = WallPlacementGeometry.ConstrainEndpoint(
            new Point2(0, 0), new Point2(4, 1), orthogonal: true);
        var vertical = WallPlacementGeometry.ConstrainEndpoint(
            new Point2(0, 0), new Point2(1, 4), orthogonal: true);
        check(horizontal == new Point2(4, 0) && vertical == new Point2(0, 4),
            "Orthogonal wall placement follows the dominant drawing axis");

        var exterior = WallPlacementGeometry.OffsetLine(
            new Point2(0, 0), new Point2(5, 0), 0.20, 0.10, WallLocationReference.ExteriorFace);
        check(exterior.Start == new Point2(0, 0.20) && exterior.End == new Point2(5, 0.20),
            "Exterior-face placement combines half-thickness and explicit offset");

        var rectangle = WallPlacementGeometry.Rectangle(
            new Point2(0, 0), new Point2(6, 4), 0.20, 0, WallLocationReference.ExteriorFace);
        var connected = rectangle.Count == 4 && Enumerable.Range(0, rectangle.Count)
            .All(index => rectangle[index].End == rectangle[(index + 1) % rectangle.Count].Start);
        check(connected && rectangle[0].Start == new Point2(0.10, 0.10) &&
              rectangle[1].End == new Point2(5.90, 3.90),
            "Rectangle placement produces four joined inset wall centerlines");

        var rejected = false;
        try
        {
            WallPlacementGeometry.Rectangle(
                new Point2(0, 0), new Point2(0.15, 2), 0.20, 0, WallLocationReference.ExteriorFace);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }
        check(rejected, "Face offsets cannot collapse a rectangular wall loop");

        var existingWalls = new[]
        {
            new WallCenterline(new Point2(0, 0), new Point2(6, 0)),
            new WallCenterline(new Point2(6, 0), new Point2(6, 4))
        };
        var endpointSnap = WallPlacementGeometry.FindNearestSnap(
            new Point2(6.08, 0.03), existingWalls, 0.15);
        var midpointSnap = WallPlacementGeometry.FindNearestSnap(
            new Point2(3.02, -0.04), existingWalls, 0.15);
        var outsideSnap = WallPlacementGeometry.FindNearestSnap(
            new Point2(2, 1), existingWalls, 0.15);
        check(endpointSnap == new WallSnapResult(new Point2(6, 0), WallSnapKind.Endpoint),
            "Object snapping joins a new wall to an existing endpoint");
        check(midpointSnap == new WallSnapResult(new Point2(3, 0), WallSnapKind.Midpoint),
            "Object snapping locates the midpoint of an existing wall");
        check(outsideSnap is null, "Object snapping leaves distant placement coordinates unchanged");

        var exactHorizontal = WallPlacementGeometry.EndpointAtLength(
            new Point2(2, 3), new Point2(8, 4), 5.5, orthogonal: true);
        var exactDiagonal = WallPlacementGeometry.EndpointAtLength(
            new Point2(0, 0), new Point2(3, 4), 10, orthogonal: false);
        check(exactHorizontal == new Point2(7.5, 3),
            "Exact-length placement respects the active orthogonal direction");
        check(exactDiagonal == new Point2(6, 8),
            "Exact-length placement preserves a free drawing direction");

        var nearestSnap = WallPlacementGeometry.FindNearestSnap(new Point2(4.5, 0.06), existingWalls, 0.15);
        check(nearestSnap == new WallSnapResult(new Point2(4.5, 0), WallSnapKind.Nearest),
            "Object snapping falls back to the nearest point on a wall when no endpoint or midpoint is in reach");

        var closing = WallPlacementGeometry.FindChainClosure(
            new Point2(0.2, 2.75), new Point2(0, 3), new Point2(0, 0), 0.4);
        var firstSegment = WallPlacementGeometry.FindChainClosure(
            new Point2(0.1, 0.1), new Point2(0, 0), new Point2(0, 0), 0.4);
        check(closing == new WallSnapResult(new Point2(0, 3), WallSnapKind.ChainStart) && firstSegment is null,
            "A chain closes on its first point from nearby, but its first segment never snaps back onto its own start");

        var nearlyVertical = WallPlacementGeometry.SnapAngle(new Point2(0, 0), new Point2(0.2, 3), 90, 5);
        var free = WallPlacementGeometry.SnapAngle(new Point2(0, 0), new Point2(1, 3), 90, 5);
        check(nearlyVertical is Point2 up && Math.Abs(up.X) < 1e-9 && Math.Abs(up.Y - Math.Sqrt(9.04)) < 1e-9 && free is null,
            "Angular snapping straightens a nearly vertical wall and leaves a clearly oblique one alone");
        check(WallPlacementGeometry.RoundLength(new Point2(1, 1), new Point2(1, 3.27), 0.1).DistanceTo(new Point2(1, 3.3)) < 1e-9,
            "Snapped walls round their length to the drawing increment, keeping their direction");

        var footprint = WallPlacementGeometry.Footprint(
            new Point2(0, 0), new Point2(5, 0), 0.20, 0.10, WallLocationReference.ExteriorFace);
        check(footprint.StartLeft.DistanceTo(new Point2(0, 0.30)) < 1e-12 &&
              footprint.EndRight.DistanceTo(new Point2(5, 0.10)) < 1e-12,
            "Wall preview footprint reflects type thickness, face reference, and placement offset");
    }
}
