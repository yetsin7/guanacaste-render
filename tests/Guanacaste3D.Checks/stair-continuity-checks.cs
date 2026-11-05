using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Checks dimensional agreement between stair solids, flights, landings, and generated guards.</summary>
internal static class StairContinuityChecks
{
    /// <summary>Exercises every stair style at multiple heights and placement rotations, including very low stairs.</summary>
    internal static void Run(Action<bool, string> check)
    {
        foreach (var preset in ArchitecturalLibrary.PresetsFor(StructuralElementKind.Stair))
        foreach (var height in new[] { 0.05, 0.15, 2.8, 3.1 })
        foreach (var angle in new[] { 0.0, 0.61, -1.17 })
        {
            var session = new DocumentSession(BuildingDocument.Create("Stair continuity regression"));
            var level = session.Document.Levels[0].Id;
            var id = session.AddElementFromPreset(level, preset with { Height = height, Offset = 0.4 },
                new(3, -2), new(3 + 4 * Math.Cos(angle), -2 + 4 * Math.Sin(angle)));
            var stair = session.Document.StructuralElements.Single(element => element.Id == id);
            var count = StairGeometry.RiserCount(height);
            var parts = ElementGeometry.Parts(stair).Where(part => part.Kind == SurfaceKind.Structure).ToArray();
            check(parts.All(part => part.Top > part.Bottom && part.Footprint.Count >= 3) &&
                  Math.Abs(parts.Max(part => part.Top) - stair.Offset - height) < 1e-8,
                $"{preset.Style} stair solids are positive and terminate at the exact upper elevation");
            check(Enumerable.Range(1, count).All(index => parts.Any(part =>
                    Math.Abs(part.Top - stair.Offset - height * index / count) < 1e-8)),
                $"{preset.Style} stair solids share the riser elevations at height {height}");
            if (stair.Style == ElementStyle.Open)
                check(parts.All(part => part.Top - part.Bottom <= 0.20 + 1e-8),
                    "An open stair retains thin treads and stringers instead of filling both sides down to the floor");
            var guards = StairGeometry.GuardLines(stair);
            check(guards.All(guard => guard.Offset >= stair.Offset - 1e-8 &&
                                     guard.Offset + guard.Rise <= stair.Offset + height + 1e-8),
                $"{preset.Style} stair guard baselines stay within the stair's rise");
            session.AddRailingsOnHost(id);
            session.Document.Validate();
            check(session.Document.StructuralElements.Count(element => element.RailingHostId == id) == guards.Count,
                $"{preset.Style} stair generates every guard as valid hosted geometry");
            var flights = StairGeometry.Flights(stair);
            if (flights.Count != 2) continue;
            var firstGoing = flights[0].Start.DistanceTo(flights[0].End) / flights[0].Risers;
            var secondGoing = flights[1].Start.DistanceTo(flights[1].End) / flights[1].Risers;
            check(Math.Abs(firstGoing - secondGoing) < 1e-8 &&
                  Math.Abs(flights[0].Rise / flights[0].Risers - flights[1].Rise / flights[1].Risers) < 1e-8,
                "Both turning stair flights use identical goings and riser heights");
            var landingTop = flights[0].Bottom + flights[0].Rise;
            var upperParts = parts.Where(part => part.Top > landingTop + 1e-8).ToArray();
            check(upperParts.All(part => Math.Abs(part.Bottom - landingTop) < 1e-8),
                "The upper flight begins at its landing instead of filling the space down to the ground");
            var flat = guards.Where(guard => Math.Abs(guard.Rise) < 1e-8).ToArray();
            check(!flat.Any(guard => OnSegment(flights[0].End, guard.Start, guard.End) ||
                                    OnSegment(flights[1].Start, guard.Start, guard.End)),
                "Landing guards leave both flight mouths fully open");
            if (stair.Style == ElementStyle.UShaped)
                check(flat.Any(guard => Math.Abs(guard.Start.DistanceTo(guard.End) - 0.10) < 1e-8),
                    "The exposed gap between switchback flights has its own landing guard");
        }
    }

    /// <summary>Tests whether a flight mouth lies on a guard, including rotated placements.</summary>
    private static bool OnSegment(Point2 point, Point2 start, Point2 end) =>
        Math.Abs(start.DistanceTo(point) + point.DistanceTo(end) - start.DistanceTo(end)) < 1e-8;
}
