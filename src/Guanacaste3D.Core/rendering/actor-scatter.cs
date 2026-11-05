using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// Spreads many people or cars over an area in one go, as Lumion's mass placement does: a crowd in a square, traffic on a
/// street. Each one gets its own look, speed and set-off time, and, if asked, a short route of its own, so the group does not
/// move in step. The result is plain actors; the project keeps them like any other.
/// </summary>
public static class ActorScatter
{
    public const int MaximumPerBatch = 60;

    /// <summary>The height of the ground at a point (x, y) of the area, or null where there is none to stand on (under a roof, off the site).</summary>
    public delegate double? GroundAt(double x, double y);

    /// <summary>
    /// Up to <paramref name="count"/> actors of <paramref name="kind"/> inside the circle of <paramref name="radius"/> metres round
    /// <paramref name="centre"/>, no two closer than a person's or a car's width, on ground found by <paramref name="ground"/>. The same
    /// seed gives the same group. With <paramref name="walk"/> each follows a route of 4–14 m (people) or 20–45 m (cars) from where it stands.
    /// Fewer come back when the area is too small or has too little ground.
    /// </summary>
    public static List<Actor> Scatter(ActorPoint centre, double radius, int count, ActorKind kind, bool walk, int seed, GroundAt ground, int firstNumber = 1)
    {
        ArgumentNullException.ThrowIfNull(ground);
        var made = new List<Actor>();
        if (!(radius >= 1) || count < 1 || !double.IsFinite(radius) || !double.IsFinite(centre.X + centre.Y + centre.Z)) return made;
        radius = Math.Min(radius, 200);
        count = Math.Min(count, MaximumPerBatch);
        var random = new Random(seed);
        var car = kind == ActorKind.Car;
        var spacing = car ? 4.0 : 0.8;
        var spots = new List<ActorPoint>();
        for (var attempt = 0; attempt < count * 30 && spots.Count < count; attempt++)
        {
            var (angle, distance) = (random.NextDouble() * 2 * Math.PI, radius * Math.Sqrt(random.NextDouble()));
            double x = centre.X + Math.Cos(angle) * distance, y = centre.Y + Math.Sin(angle) * distance;
            if (spots.Any(spot => (spot.X - x) * (spot.X - x) + (spot.Y - y) * (spot.Y - y) < spacing * spacing)) continue;
            if (ground(x, y) is not { } z || !double.IsFinite(z)) continue;
            spots.Add(new ActorPoint(x, y, z));
        }
        foreach (var spot in spots)
        {
            var route = new List<ActorPoint> { spot };
            if (walk)
            {
                // A straight run, in a direction of its own, as far as the ground goes: it stops where there is none.
                var (heading, length) = (random.NextDouble() * 2 * Math.PI, car ? 20 + random.NextDouble() * 25 : 4 + random.NextDouble() * 10);
                for (var step = 1; step <= 4; step++)
                {
                    double x = spot.X + Math.Cos(heading) * length * step / 4, y = spot.Y + Math.Sin(heading) * length * step / 4;
                    if (ground(x, y) is not { } z || !double.IsFinite(z) || Math.Abs(z - spot.Z) > 1.2) break;
                    route.Add(new ActorPoint(x, y, z));
                }
            }
            var number = firstNumber + made.Count;
            made.Add(new Actor(Guid.NewGuid(), $"{(car ? "Coche" : "Persona")} {number}", random.Next(Actor.Looks),
                car ? 6 + random.NextDouble() * 6 : 0.9 + random.NextDouble() * 0.7, true, Math.Round(random.NextDouble() * 6, 1), route, kind));
        }
        return made;
    }
}
