using System.Numerics;
using System.Text.Json;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// A fire, a column of smoke, or both (a campfire): <paramref name="Flames"/> and <paramref name="Smoke"/> are 0–1 strengths
/// and <paramref name="Size"/> the height of the flames in metres (the smoke climbs several times higher). It stands at a
/// point of the project, where it meets the ground.
/// </summary>
public sealed record FireEmitter(Guid Id, string Name, double X, double Y, double Z, double Flames, double Smoke, double Size)
{
    public Vector3 Position => new((float)X, (float)Y, (float)Z);

    public bool IsValid => Id != Guid.Empty && !string.IsNullOrWhiteSpace(Name) && Name.Length <= 80 &&
        new[] { X, Y, Z, Flames, Smoke, Size }.All(double.IsFinite) && Math.Abs(X) < 1e6 && Math.Abs(Y) < 1e6 && Math.Abs(Z) < 1e5 &&
        Flames is >= 0 and <= 1 && Smoke is >= 0 and <= 1 && Size is >= 0.1 and <= 10 && (Flames > 0 || Smoke > 0);
}

/// <summary>Fires as the text the project keeps, and the light they give the scene.</summary>
public static class Fires
{
    public const string Key = "render.fire";
    public const int Maximum = 50;

    public static string? Serialize(IReadOnlyCollection<FireEmitter> fires) =>
        fires.Count == 0 ? null : JsonSerializer.Serialize(new { V = 1, Items = fires.Take(Maximum) });

    /// <summary>Reads saved fires; null when the text is damaged, an empty list when there are none.</summary>
    public static IReadOnlyList<FireEmitter>? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 6 });
            var root = document.RootElement;
            if (root.GetProperty("V").GetInt32() != 1) return null;
            var items = root.GetProperty("Items");
            if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > Maximum) return null;
            var fires = new List<FireEmitter>();
            foreach (var item in items.EnumerateArray())
            {
                var fire = new FireEmitter(item.GetProperty("Id").GetGuid(), item.GetProperty("Name").GetString() ?? "", item.GetProperty("X").GetDouble(),
                    item.GetProperty("Y").GetDouble(), item.GetProperty("Z").GetDouble(), item.GetProperty("Flames").GetDouble(),
                    item.GetProperty("Smoke").GetDouble(), item.GetProperty("Size").GetDouble());
                if (!fire.IsValid) return null;
                fires.Add(fire);
            }
            return fires;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    public static IReadOnlyList<FireEmitter> Of(BuildingDocument document) =>
        TryParse(document.Extensions.GetValueOrDefault(Key)) ?? [];

    /// <summary>
    /// The light a fire gives its surroundings: a warm point light a little above its base, as strong as its flames and as big
    /// as their height. The flames themselves are drawn on the picture; this is what makes the ground and walls near them glow.
    /// </summary>
    public static RenderLight? Light(FireEmitter fire) => fire.Flames <= 0 ? null : new RenderLight(
        fire.Position + new Vector3(0, 0, (float)(fire.Size * 0.4)), new Vector3(1.0f, 0.52f, 0.2f) * (float)(10 * fire.Flames * fire.Size),
        Radius: (float)Math.Max(0.05, fire.Size * 0.15));
}

/// <summary>Where the points of the world fall in the picture a camera takes, with the size an object has there.</summary>
public sealed class ScreenProjection(Vector3 position, Vector3 forward, Vector3 right, Vector3 up, float halfWidth, float halfHeight,
    float horizontalShift, float verticalShift, int width, int height)
{
    /// <summary>Where the camera stands.</summary>
    public Vector3 Position => position;

    /// <summary>The direction of the ray through a point of the picture (column and row in pixels, as <see cref="TryProject"/> gives them).</summary>
    public Vector3 Direction(float column, float row)
    {
        var x = column / width * 2 - 1;
        var y = 1 - row / height * 2;
        return Vector3.Normalize(forward + right * (x * halfWidth + horizontalShift) + up * (y * halfHeight + verticalShift));
    }

    /// <summary>
    /// The column, row and distance of a world point, and how many pixels a metre across measures there. False for a
    /// point behind the camera or too close to it.
    /// </summary>
    public bool TryProject(Vector3 point, out float column, out float row, out float distance, out float pixelsPerMetre)
    {
        var offset = point - position;
        var depth = Vector3.Dot(offset, forward);
        distance = offset.Length();
        (column, row, pixelsPerMetre) = (0, 0, 0);
        if (depth < 0.2f) return false;
        var x = (Vector3.Dot(offset, right) / depth - horizontalShift) / halfWidth;
        var y = (Vector3.Dot(offset, up) / depth - verticalShift) / halfHeight;
        column = (x + 1) / 2 * width;
        row = (1 - y) / 2 * height;
        pixelsPerMetre = height / 2f / (halfHeight * depth);
        return true;
    }
}

/// <summary>
/// One soft round particle on the picture: a piece of flame or a spark (added light), of smoke (a grey veil) or a fleck of
/// ash (<paramref name="Dark"/>: a small dark speck laid over what is behind it).
/// </summary>
internal readonly record struct FireSprite(float Column, float Row, float Radius, float Distance, float WorldRadius, bool Smoke,
    Vector3 Color, float Strength, float Aspect = 1, bool Dark = false);

/// <summary>
/// The particles of the fires at a time of the movie. Every particle is worked out from a hash of its number and the time, so
/// the same instant always looks the same, nothing is stored between frames, and a few dozen particles per fire are enough:
/// flames are short-lived (about half a second) and rise in a flickering cone, cooling from white-yellow through orange to
/// red; smoke lives for several seconds, climbs, widens, leans downwind and fades.
/// </summary>
internal static class FireParticles
{
    private static uint Hash(uint a, uint b)
    {
        unchecked
        {
            var h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u) * 0x85EBCA77u;
            h ^= h >> 15; h *= 0x2C1B3C6Du;
            h ^= h >> 12; h *= 0x297A2D39u;
            return h ^ (h >> 15);
        }
    }

    private static float Unit(uint hash, int shift) => ((hash >> shift) & 0xFFu) / 255f;
    private static float Fraction(float value) => value - MathF.Floor(value);

    public static List<FireSprite> Build(IReadOnlyList<FireEmitter> fires, ScreenProjection view, double seconds)
    {
        var sprites = new List<FireSprite>();
        var time = (float)(seconds % 3600);
        for (var number = 0; number < fires.Count; number++)
        {
            var fire = fires[number];
            var seed = (uint)number * 7919u + 17u;
            var size = (float)fire.Size;
            var origin = fire.Position;
            var flameCount = fire.Flames > 0 ? (int)MathF.Round(24 + 96 * (float)fire.Flames) : 0;
            for (var index = 0; index < flameCount; index++)
            {
                var h = Hash(seed, (uint)index);
                float u1 = Unit(h, 0), u2 = Unit(h, 8), u3 = Unit(h, 16), u4 = Unit(h, 24);
                var age = Fraction(time * (1.3f + 0.8f * u1) + u2);
                var angle = u3 * 2 * MathF.PI;
                var spread = size * 0.2f * MathF.Pow(1 - age, 1.4f) * MathF.Sqrt(u4);
                var sway = MathF.Sin(time * 7 + u1 * 20) * size * 0.07f * age * age + MathF.Sin(time * 3.1f + u2 * 9) * size * 0.04f * age;
                var at = origin + new Vector3(MathF.Cos(angle) * spread + sway, MathF.Sin(angle) * spread, size * 1.25f * MathF.Pow(age, 0.85f));
                var radius = size * (0.15f * MathF.Pow(1 - age, 0.8f) + 0.02f);
                var color = age < 0.5f
                    ? Vector3.Lerp(new Vector3(1.0f, 0.92f, 0.55f), new Vector3(1.0f, 0.55f, 0.12f), age * 2)
                    : Vector3.Lerp(new Vector3(1.0f, 0.55f, 0.12f), new Vector3(0.9f, 0.18f, 0.03f), (age - 0.5f) * 2);
                Add(sprites, view, at, radius, false, color, (float)fire.Flames * (1.0f * MathF.Pow(1 - age, 1.3f) + 0.05f), 1.9f);
            }
            // Sparks leap out of live flames, cool from yellow to red as they climb and flicker; ash flecks drift up with the
            // heat, then wander down on the wind. Both are few and tiny: they are what makes a fire read as a fire.
            if (fire.Flames > 0)
            {
                var sparkCount = (int)MathF.Round(6 + 34 * (float)fire.Flames);
                for (var index = 0; index < sparkCount; index++)
                {
                    var h = Hash(seed + 2000003u, (uint)index);
                    float u1 = Unit(h, 0), u2 = Unit(h, 8), u3 = Unit(h, 16), u4 = Unit(h, 24);
                    var age = Fraction(time / (1.2f + 1.6f * u1) + u2);
                    var angle = u3 * 2 * MathF.PI;
                    var reach = size * 0.18f * MathF.Sqrt(u4) * (1 + 2 * age);
                    var wiggle = MathF.Sin(time * 9 + u3 * 30) * size * 0.12f * age;
                    var at = origin + new Vector3(MathF.Cos(angle) * reach + size * 1.1f * age * (0.6f + u4) + wiggle, MathF.Sin(angle) * reach,
                        size * (0.5f + 3.8f * MathF.Pow(age, 0.8f)));
                    var twinkle = 0.65f + 0.35f * MathF.Sin(time * 23 + u2 * 40);
                    var color = Vector3.Lerp(new Vector3(1.0f, 0.8f, 0.35f), new Vector3(1.0f, 0.3f, 0.05f), age);
                    Add(sprites, view, at, size * 0.03f * (1 - 0.5f * age) + 0.002f, false, color, (float)fire.Flames * 5f * MathF.Pow(1 - age, 1.0f) * twinkle, minimumPixels: 1.3f);
                }
                var ashCount = (int)MathF.Round(10 + 50 * (float)fire.Flames);
                for (var index = 0; index < ashCount; index++)
                {
                    var h = Hash(seed + 3000017u, (uint)index);
                    float u1 = Unit(h, 0), u2 = Unit(h, 8), u3 = Unit(h, 16), u4 = Unit(h, 24);
                    var age = Fraction(time / (4f + 4f * u1) + u2);
                    var angle = u3 * 2 * MathF.PI;
                    var reach = size * 0.3f * MathF.Sqrt(u4);
                    var height = MathF.Max(0.02f, size * (0.4f + 3.6f * age - 3.2f * age * age));
                    var at = origin + new Vector3(MathF.Cos(angle) * reach + 3.2f * age * size, MathF.Sin(angle) * reach + MathF.Sin(time * 1.7f + u1 * 12) * size * 0.3f * age, height);
                    var fade = Math.Clamp(age / 0.08f, 0f, 1f) * Math.Clamp((1 - age) / 0.15f, 0f, 1f);
                    var tone = 0.07f + 0.25f * u4 * u4;
                    Add(sprites, view, at, size * (0.016f + 0.012f * u2), false, new Vector3(tone), 0.9f * fade, dark: true, minimumPixels: 0.9f);
                }
            }
            var smokeCount = fire.Smoke > 0 ? (int)MathF.Round(24 + 110 * (float)fire.Smoke) : 0;
            var smokeBase = fire.Flames > 0.05 ? size * 0.8f : size * 0.1f;
            for (var index = 0; index < smokeCount; index++)
            {
                var h = Hash(seed + 1000003u, (uint)index);
                float u1 = Unit(h, 0), u2 = Unit(h, 8), u3 = Unit(h, 16), u4 = Unit(h, 24);
                var age = Fraction(time * (0.22f + 0.12f * u1) + u2);
                var angle = u3 * 2 * MathF.PI;
                var spread = size * 0.15f * MathF.Sqrt(u4) * (1 + age);
                var at = origin + new Vector3(MathF.Cos(angle) * spread + 2.4f * age * size, MathF.Sin(angle) * spread, smokeBase + size * 5.5f * age);
                var radius = size * (0.18f + 1.1f * age);
                var fade = Math.Clamp(age / 0.12f, 0f, 1f);
                var tone = 0.5f + 0.25f * u4;
                Add(sprites, view, at, radius, true, new Vector3(tone, tone, tone + 0.02f), (float)fire.Smoke * 0.5f * fade * fade * MathF.Pow(1 - age, 1.1f));
            }
        }
        // Smoke first and the farthest first, so what is near covers what is far; flames go on top as added light.
        sprites.Sort((a, b) => a.Smoke != b.Smoke ? (a.Smoke ? -1 : 1) : b.Distance.CompareTo(a.Distance));
        return sprites;
    }

    private static void Add(List<FireSprite> sprites, ScreenProjection view, Vector3 at, float worldRadius, bool smoke, Vector3 color, float strength,
        float aspect = 1, bool dark = false, float minimumPixels = 0)
    {
        if (strength <= 1e-4f || !view.TryProject(at, out var column, out var row, out var distance, out var pixelsPerMetre)) return;
        var radius = worldRadius * pixelsPerMetre;
        // A spark or a fleck is smaller than a pixel from afar: it is kept a pixel wide and fainter by the area it lost, so it twinkles instead of vanishing.
        if (minimumPixels > 0 && radius < minimumPixels)
        {
            strength *= Math.Max(0.15f, radius * radius / (minimumPixels * minimumPixels));
            radius = minimumPixels;
        }
        if (radius < 0.6f || radius > 2000) return;
        sprites.Add(new FireSprite(column, row, radius, distance, worldRadius, smoke, color, strength, aspect, dark));
    }
}
