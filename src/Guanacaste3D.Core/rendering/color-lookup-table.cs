using System.Globalization;
using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>Immutable 3D display-color LUT, red sample index varying fastest, supporting Adobe .cube files.</summary>
public sealed class ColorLookupTable
{
    private readonly Vector3[] _colors;
    private readonly Vector3 _min, _max;
    private ColorLookupTable(int size, Vector3[] colors, Vector3 min, Vector3 max)
    { Size = size; _colors = colors; _min = min; _max = max; }
    public int Size { get; }
    public const int MaxCubeBytes = 8 * 1024 * 1024;

    public static ColorLookupTable Create(int size, Func<Vector3, Vector3> grade)
    {
        ArgumentNullException.ThrowIfNull(grade);
        if (size is < 2 or > 64) throw new ArgumentOutOfRangeException(nameof(size));
        var pixels = new Vector3[size * size * size];
        for (var b = 0; b < size; b++)
        for (var g = 0; g < size; g++)
        for (var r = 0; r < size; r++)
        {
            var result = grade(new Vector3(r, g, b) / (size - 1f));
            if (!float.IsFinite(result.X + result.Y + result.Z)) throw new ArgumentException("The LUT transform must be finite.");
            pixels[(b * size + g) * size + r] = Vector3.Clamp(result, Vector3.Zero, Vector3.One);
        }
        return new ColorLookupTable(size, pixels, Vector3.Zero, Vector3.One);
    }

    /// <summary>Reads .cube with LUT_3D_SIZE, optional TITLE and DOMAIN_*; source limited to 8 MiB and 64 cubed samples.</summary>
    public static ColorLookupTable FromCube(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        const string invalid = "Invalid color LUT.";
        if (text.Length > MaxCubeBytes) throw new InvalidDataException(invalid);
        var pixels = new List<Vector3>();
        var size = 0;
        var min = Vector3.Zero; var max = Vector3.One;
        var lines = 0;
        foreach (var raw in text.Split('\n'))
        {
            if (++lines > 270000) throw new InvalidDataException(invalid);
            var line = raw.Split('#', 2)[0].Trim();
            // UTF-8 .cube files may begin with a BOM; it is not a LUT directive.
            if (lines == 1) line = line.TrimStart('\uFEFF');
            if (line.Length == 0) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts[0].Equals("TITLE", StringComparison.OrdinalIgnoreCase)) continue;
            if (parts[0].Equals("LUT_3D_SIZE", StringComparison.OrdinalIgnoreCase))
            {
                if (size != 0 || pixels.Count != 0 || parts.Length != 2 ||
                    !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out size) ||
                    size is < 2 or > 64) throw new InvalidDataException(invalid);
            }
            else if (parts[0].Equals("DOMAIN_MIN", StringComparison.OrdinalIgnoreCase) ||
                     parts[0].Equals("DOMAIN_MAX", StringComparison.OrdinalIgnoreCase))
            {
                if (pixels.Count != 0 || parts.Length != 4) throw new InvalidDataException(invalid);
                var value = Parse(parts, 1);
                if (parts[0].Equals("DOMAIN_MIN", StringComparison.OrdinalIgnoreCase)) min = value; else max = value;
            }
            else
            {
                if (size == 0 || parts.Length != 3 || pixels.Count >= size * size * size)
                    throw new InvalidDataException(invalid);
                pixels.Add(Parse(parts, 0));
            }
        }
        if (size == 0 || pixels.Count != size * size * size ||
            min.X >= max.X || min.Y >= max.Y || min.Z >= max.Z)
            throw new InvalidDataException(invalid);
        return new ColorLookupTable(size, [..pixels], min, max);
    }

    private static Vector3 Parse(string[] parts, int start)
    {
        const string invalid = "Invalid color LUT.";
        if (!float.TryParse(parts[start], NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ||
            !float.TryParse(parts[start + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var g) ||
            !float.TryParse(parts[start + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out var b) ||
            !float.IsFinite(r + g + b) || MathF.Max(MathF.Abs(r), MathF.Max(MathF.Abs(g), MathF.Abs(b))) > 16)
            throw new InvalidDataException(invalid);
        return new Vector3(r, g, b);
    }

    /// <summary>Trilinear sampling, with clamp (never wrap) at the LUT domain limits.</summary>
    public Vector3 Sample(Vector3 color)
    {
        if (!float.IsFinite(color.X + color.Y + color.Z)) return Vector3.Zero;
        var xyz = Vector3.Clamp((color - _min) / (_max - _min), Vector3.Zero, Vector3.One) * (Size - 1f);
        var x = (int)xyz.X; var y = (int)xyz.Y; var z = (int)xyz.Z;
        var x1 = Math.Min(x + 1, Size - 1); var y1 = Math.Min(y + 1, Size - 1); var z1 = Math.Min(z + 1, Size - 1);
        var tx = xyz.X - x; var ty = xyz.Y - y; var tz = xyz.Z - z;
        Vector3 V(int r, int g, int b) => _colors[(b * Size + g) * Size + r];
        return Vector3.Max(Vector3.Zero, Vector3.Lerp(
            Vector3.Lerp(Vector3.Lerp(V(x,y,z), V(x1,y,z), tx), Vector3.Lerp(V(x,y1,z), V(x1,y1,z), tx), ty),
            Vector3.Lerp(Vector3.Lerp(V(x,y,z1), V(x1,y,z1), tx), Vector3.Lerp(V(x,y1,z1), V(x1,y1,z1), tx), ty), tz));
    }
}

public sealed record ColorLutPreset(string Id, string Name, ColorLookupTable Map);

/// <summary>Original built-in color filter library with no downloads, royalties or GPU requirement.</summary>
public static class ColorLutPresets
{
    private static float L(Vector3 c) => .2126f * c.X + .7152f * c.Y + .0722f * c.Z;
    private static ColorLutPreset Grade(string id, string name, Func<Vector3, Vector3> transform) =>
        new(id, name, ColorLookupTable.Create(17, c => Vector3.Clamp(transform(c), Vector3.Zero, Vector3.One)));

    public static IReadOnlyList<ColorLutPreset> All { get; } =
    [
        Grade("none", "Sin filtro", c => c),
        Grade("natural", "Arquitectura natural", c => Vector3.Lerp(c, new Vector3(L(c)), MathF.Max(0, L(c) - .7f) * .12f)),
        Grade("warm", "Luz cálida", c => c * new Vector3(1.08f, 1.00f, .89f)),
        Grade("cool", "Azul moderno", c => c * new Vector3(.90f, .99f, 1.09f)),
        Grade("tropical", "Vegetación tropical", c => c + new Vector3(-.024f, .055f, -.012f) * (.4f + c.Y)),
        Grade("golden", "Atardecer dorado", c => c * new Vector3(1.12f, .94f, .76f) + new Vector3(.025f, .008f, 0)),
        Grade("cinematic", "Azul y naranja cinematográfico", c =>
            c * Vector3.Lerp(new Vector3(.82f, 1.02f, 1.13f), new Vector3(1.11f, 1.00f, .86f), L(c))),
        Grade("soft", "Película analógica suave", c => new Vector3(.02f) + Vector3.Lerp(new Vector3(L(c)), c, .82f) * .91f),
        Grade("sepia", "Fotografía sepia", c => Vector3.Lerp(c, new Vector3(L(c) * 1.10f, L(c) * .97f, L(c) * .77f), .58f)),
        Grade("studio", "Estudio luminoso", c => Vector3.Lerp(c, new Vector3(.84f), .08f) * 1.035f),
        Grade("cobalt", "Noche cobalto", c => c * new Vector3(.78f, .86f, 1.16f)),
        Grade("forest", "Bosque mate", c => c * new Vector3(.92f, .98f, .91f) * .9f + new Vector3(.04f)),
        Grade("pastel", "Pastel fotográfico", c => Vector3.Lerp(new Vector3(L(c)), c, .72f) * .96f + new Vector3(.018f)),
        Grade("monochrome", "Monocromo cálido", c => new Vector3(L(c) * 1.06f, L(c) * .94f, L(c) * .74f))
    ];

    public static ColorLookupTable? Find(string? id) =>
        id is null or "none" ? null : All.FirstOrDefault(p => p.Id == id)?.Map;
}
