using System.Numerics;
using System.Text.Json;

namespace Guanacaste3D.Core.Rendering;

/// <summary>A point of an actor's route, in project metres: where the feet or the wheels touch the ground.</summary>
public readonly record struct ActorPoint(double X, double Y, double Z);

/// <summary>What an actor is: a person who walks, or a car that drives.</summary>
public enum ActorKind { Person, Car }

/// <summary>
/// A person or a car of the render that follows a route, as in Lumion's animated people and vehicles. <see cref="Look"/>
/// chooses the person's skin, clothes and height, or the car's model and paint (0–5); <see cref="Speed"/> is in metres per
/// second; with <see cref="Loop"/> the actor goes back and forth, and without it stops at the last point.
/// <see cref="Start"/> is the second of the movie at which the actor sets off; before that, and with a single point, it
/// stands still.
/// </summary>
public sealed record Actor(Guid Id, string Name, int Look, double Speed, bool Loop, double Start, IReadOnlyList<ActorPoint> Route,
    ActorKind Kind = ActorKind.Person)
{
    public const int Looks = 6;
    public const int MaximumRoutePoints = 200;
    public const double MaximumSpeed = 40;

    public bool IsValid => Id != Guid.Empty && !string.IsNullOrWhiteSpace(Name) && Name.Length <= 80 && Look is >= 0 and < Looks &&
        Enum.IsDefined(Kind) && double.IsFinite(Speed) && Speed is >= 0.1 and <= MaximumSpeed && double.IsFinite(Start) && Start is >= 0 and <= 36000 &&
        Route.Count is >= 1 and <= MaximumRoutePoints &&
        Route.All(point => double.IsFinite(point.X + point.Y + point.Z) && Math.Abs(point.X) < 1e6 && Math.Abs(point.Y) < 1e6 && Math.Abs(point.Z) < 1e5);
}

/// <summary>
/// Where an actor is, and how it is moving, at a time of the movie. <paramref name="Turn"/> is how sharply it is turning, in
/// radians per metre travelled (positive to the left, 0 on a straight), and <paramref name="Speed"/> its speed in metres per
/// second; a car steers its front wheels and leans in its suspension from them.
/// </summary>
public readonly record struct ActorState(Vector3 Position, float Heading, float Distance, bool Walking, float Turn = 0, float Speed = 0);

/// <summary>The route of an actor followed over time, and the actors as the text the project keeps.</summary>
public static class Actors
{
    public const string Key = "render.actors";
    public const int Maximum = 100;

    /// <summary>
    /// The actor's place at a time: it follows its route at its speed from <see cref="Actor.Start"/>, stands at the first
    /// point before that, and either stops at the end or turns round and goes back (a looping actor). The heading is the
    /// direction of travel, in radians from the +X axis, taken over a short stretch of the route either side of the actor,
    /// so it turns smoothly round a corner instead of snapping.
    /// </summary>
    public static ActorState At(Actor actor, double seconds)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var route = actor.Route;
        var first = new Vector3((float)route[0].X, (float)route[0].Y, (float)route[0].Z);
        double length = 0;
        for (var index = 1; index < route.Count; index++) length += Flat(route[index - 1], route[index]);
        if (route.Count == 1 || length < 1e-3)
            return new ActorState(first, 0, 0, false);

        var walked = Math.Max(0, seconds - actor.Start) * actor.Speed;
        var reversed = false;
        double along;
        bool moving;
        if (actor.Loop)
        {
            var cycle = 2 * length;
            var inCycle = walked % cycle;
            reversed = inCycle > length;
            along = reversed ? cycle - inCycle : inCycle;
            moving = walked > 0;
        }
        else
        {
            along = Math.Min(walked, length);
            moving = walked > 0 && walked < length;
        }

        // The distance covered: it keeps growing for an actor that goes back and forth (it always drives forward), but stops at the end
        // of a route that is followed once, so a parked car's wheels stand still.
        var covered = actor.Loop ? walked : along;
        var position = PointAt(route, along);
        var (ahead, behind) = (PointAt(route, Math.Min(length, along + 1.2)), PointAt(route, Math.Max(0, along - 1.2)));
        var heading = (float)Math.Atan2(ahead.Y - behind.Y, ahead.X - behind.X);
        if (Math.Abs(ahead.X - behind.X) + Math.Abs(ahead.Y - behind.Y) < 1e-6) heading = HeadingAt(route, along);
        if (reversed) heading += MathF.PI;
        // The curvature of the way ahead: how much the direction changes over a metre and a bit of the route, seen in the
        // direction of travel (a looping actor coming back sees the route backwards).
        var turn = moving ? AngleBetween(DirectionAt(route, Math.Max(0, along - 0.6)), DirectionAt(route, Math.Min(length, along + 0.6))) / 1.2f : 0f;
        if (reversed) turn = -turn;
        return new ActorState(position, heading, (float)covered, moving, turn, moving ? (float)actor.Speed : 0f);
    }

    private static float DirectionAt(IReadOnlyList<ActorPoint> route, double along)
    {
        var (ahead, behind) = (PointAt(route, along + 1.2), PointAt(route, Math.Max(0, along - 1.2)));
        return Math.Abs(ahead.X - behind.X) + Math.Abs(ahead.Y - behind.Y) < 1e-6 ? HeadingAt(route, along) : (float)Math.Atan2(ahead.Y - behind.Y, ahead.X - behind.X);
    }

    private static float AngleBetween(float from, float to)
    {
        var difference = to - from;
        while (difference > MathF.PI) difference -= 2 * MathF.PI;
        while (difference < -MathF.PI) difference += 2 * MathF.PI;
        return difference;
    }

    private static double Flat(ActorPoint a, ActorPoint b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

    /// <summary>The point at a distance along the route (clamped to it), with its height interpolated.</summary>
    private static Vector3 PointAt(IReadOnlyList<ActorPoint> route, double along)
    {
        double covered = 0;
        for (var index = 1; index < route.Count; index++)
        {
            var segment = Flat(route[index - 1], route[index]);
            if (segment < 1e-6) continue;
            if (along <= covered + segment || index == route.Count - 1)
            {
                var t = Math.Clamp((along - covered) / segment, 0, 1);
                var (a, b) = (route[index - 1], route[index]);
                return new Vector3((float)(a.X + (b.X - a.X) * t), (float)(a.Y + (b.Y - a.Y) * t), (float)(a.Z + (b.Z - a.Z) * t));
            }
            covered += segment;
        }
        return new Vector3((float)route[0].X, (float)route[0].Y, (float)route[0].Z);
    }

    /// <summary>The direction of the segment the actor is on, used when the stretch round it has no direction of its own.</summary>
    private static float HeadingAt(IReadOnlyList<ActorPoint> route, double along)
    {
        double covered = 0;
        for (var index = 1; index < route.Count; index++)
        {
            var segment = Flat(route[index - 1], route[index]);
            if (segment < 1e-6) continue;
            if (along <= covered + segment || index == route.Count - 1)
                return (float)Math.Atan2(route[index].Y - route[index - 1].Y, route[index].X - route[index - 1].X);
            covered += segment;
        }
        return 0;
    }

    public static string? Serialize(IReadOnlyCollection<Actor> actors) =>
        actors.Count == 0 ? null : JsonSerializer.Serialize(new
        {
            V = 1,
            Items = actors.Take(Maximum).Select(actor => new
            {
                actor.Id, actor.Name, actor.Look, actor.Speed, actor.Loop, actor.Start, Kind = (int)actor.Kind,
                Route = actor.Route.Select(point => new[] { point.X, point.Y, point.Z })
            })
        });

    /// <summary>Reads saved actors; null when the text is damaged, an empty list when there are none.</summary>
    public static IReadOnlyList<Actor>? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.GetProperty("V").GetInt32() != 1) return null;
            var items = root.GetProperty("Items");
            if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > Maximum) return null;
            var actors = new List<Actor>();
            foreach (var item in items.EnumerateArray())
            {
                var route = new List<ActorPoint>();
                foreach (var point in item.GetProperty("Route").EnumerateArray())
                {
                    if (point.GetArrayLength() != 3) return null;
                    route.Add(new ActorPoint(point[0].GetDouble(), point[1].GetDouble(), point[2].GetDouble()));
                    if (route.Count > Actor.MaximumRoutePoints) return null;
                }
                // Projects saved before cars existed have no kind: they hold people.
                var kind = item.TryGetProperty("Kind", out var kindElement) ? (ActorKind)kindElement.GetInt32() : ActorKind.Person;
                var actor = new Actor(item.GetProperty("Id").GetGuid(), item.GetProperty("Name").GetString() ?? "",
                    item.GetProperty("Look").GetInt32(), item.GetProperty("Speed").GetDouble(), item.GetProperty("Loop").GetBoolean(),
                    item.GetProperty("Start").GetDouble(), route, kind);
                if (!actor.IsValid) return null;
                actors.Add(actor);
            }
            return actors;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>The actors saved in a document; empty when there are none or the text cannot be read.</summary>
    public static IReadOnlyList<Actor> Of(BuildingDocument document) =>
        TryParse(document.Extensions.GetValueOrDefault(Key)) ?? [];
}
