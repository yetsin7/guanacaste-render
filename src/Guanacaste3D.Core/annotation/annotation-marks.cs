using System.Text;
using System.Text.RegularExpressions;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core;

/// <summary>The text of the marks that read their value from the model: spot elevations, spot coordinates and spot slopes.</summary>
public static class SpotMarks
{
    /// <summary>The text a spot mark shows, or the element's own name for the marks that carry their own text.</summary>
    public static string Label(BuildingDocument document, StructuralElement element)
    {
        switch (element.Kind)
        {
            case StructuralElementKind.SpotElevation:
                var level = document.Levels.FirstOrDefault(item => item.Id == element.LevelId);
                if (level is null) return string.Empty;
                var elevation = level.Elevation + element.Offset;
                return (elevation >= 0 ? "+" : "−") + document.Units.FormatLength(Math.Abs(elevation));
            case StructuralElementKind.SpotCoordinate:
                var shared = document.Site.ToShared(element.Start);
                return $"E {shared.X:0.00}  N {shared.Y:0.00}";
            case StructuralElementKind.SpotSlope:
                if (element.Length < 1e-6) return "—";
                var slope = element.Rise / element.Length;
                return Math.Abs(slope) < 1e-9 ? "0 %" : $"{slope * 100:0.0} %  (1:{Math.Abs(1 / slope):0.#})";
            default:
                return element.Name;
        }
    }

    /// <summary>
    /// The rise of the terrain between two points, when a terrain slab of the level covers both; null otherwise. Positive
    /// means the ground is higher at the second point.
    /// </summary>
    public static double? TerrainRise(BuildingDocument document, Guid levelId, Point2 start, Point2 end)
    {
        foreach (var floor in document.Floors.Where(item => item.IsToposolid && item.LevelId == levelId))
        {
            if (!Contains(floor.Boundary, start) || !Contains(floor.Boundary, end)) continue;
            var surface = new TopoSurface(floor);
            return surface.ElevationAt(end.X, end.Y) - surface.ElevationAt(start.X, start.Y);
        }
        return null;
    }

    private static bool Contains(IReadOnlyList<Point2> ring, Point2 point)
    {
        var inside = false;
        for (int index = 0, previous = ring.Count - 1; index < ring.Count; previous = index++)
            if ((ring[index].Y > point.Y) != (ring[previous].Y > point.Y) &&
                point.X < (ring[previous].X - ring[index].X) * (point.Y - ring[index].Y) / (ring[previous].Y - ring[index].Y) + ring[index].X)
                inside = !inside;
        return inside;
    }
}

/// <summary>The keynotes a project can call out: a code and what it stands for, in the manner of a keynote file.</summary>
public static class Keynotes
{
    public static readonly (string Code, string Text)[] Table =
    [
        ("03 30 00", "Hormigón colado en sitio"), ("04 20 00", "Mampostería de bloques"), ("04 22 00", "Mampostería de cobogó (celosía de bloques)"),
        ("05 12 00", "Estructura de acero"), ("06 10 00", "Carpintería en bruto"), ("07 21 00", "Aislamiento térmico"),
        ("07 92 00", "Selladores de juntas"), ("08 11 00", "Puertas metálicas"), ("08 51 00", "Ventanas de aluminio"),
        ("09 29 00", "Placa de yeso"), ("09 30 00", "Revestimiento cerámico"), ("09 91 00", "Pintura"),
        ("32 12 00", "Pavimento asfáltico"), ("32 31 00", "Cercas y portones")
    ];

    /// <summary>"03 30 00 · Hormigón colado en sitio" for a code of the table (with or without spaces, or its start); any other text stands for itself.</summary>
    public static string Resolve(string? input)
    {
        var text = input?.Trim() ?? string.Empty;
        if (text.Length == 0) return string.Empty;
        var digits = new string(text.Where(char.IsDigit).ToArray());
        if (digits.Length >= 2 && text.All(c => char.IsDigit(c) || c == ' ' || c == '.'))
            foreach (var (code, description) in Table)
                if (code.Replace(" ", string.Empty).StartsWith(digits, StringComparison.Ordinal)) return $"{code} · {description}";
        return text;
    }
}

/// <summary>The 2D symbols that can be placed in a plan, and the detail components that share the same drawing.</summary>
public static class PlanSymbols
{
    public static readonly string[] Symbols = ["NorthArrow", "Benchmark", "Entrance", "Warning"];
    public static readonly string[] DetailComponents = ["Anchor", "Bolt", "RebarSection", "BreakLine"];

    public static bool IsKnown(string? key) => key is not null && (Symbols.Contains(key) || DetailComponents.Contains(key));
}

/// <summary>The colours rooms take when a colour legend colours the rooms of a level, one for each name.</summary>
public static class RoomColors
{
    private static readonly (byte R, byte G, byte B)[] Palette =
    [
        (230, 126, 34), (52, 152, 219), (46, 204, 113), (155, 89, 182), (241, 196, 15), (231, 76, 60),
        (26, 188, 156), (127, 140, 141), (211, 84, 0), (41, 128, 185), (39, 174, 96), (142, 68, 173)
    ];

    /// <summary>The same name always gets the same colour, in any project.</summary>
    public static (byte R, byte G, byte B) Of(string name)
    {
        var hash = 17;
        foreach (var letter in (name ?? string.Empty).Trim().ToUpperInvariant()) hash = unchecked(hash * 31 + letter);
        return Palette[(hash & int.MaxValue) % Palette.Length];
    }

    /// <summary>Each room name on the level, with how many rooms bear it and their combined area.</summary>
    public static IReadOnlyList<(string Name, int Rooms, double Area)> Legend(BuildingDocument document, Guid levelId) =>
        document.Rooms.Where(room => room.LevelId == levelId && !string.IsNullOrWhiteSpace(room.Name))
            .GroupBy(room => room.Name.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .Select(group => (group.Key, group.Count(), group.Sum(room => room.Area))).OrderBy(entry => entry.Key, StringComparer.CurrentCultureIgnoreCase).ToList();
}

/// <summary>One thing found wrong in a text of the project.</summary>
public sealed record TextIssue(string Source, string Text, string Problem, string? Suggestion);

/// <summary>A basic check of the texts of the project: repeated words, spacing, brackets and common misspellings.</summary>
public static class TextChecker
{
    private static readonly Dictionary<string, string> Misspellings = new(StringComparer.CurrentCultureIgnoreCase)
    {
        ["impermiabilizante"] = "impermeabilizante", ["inpermeabilizante"] = "impermeabilizante", ["cimentacion"] = "cimentación",
        ["habitacion"] = "habitación", ["construccion"] = "construcción", ["ventilacion"] = "ventilación", ["iluminacion"] = "iluminación",
        ["instalacion"] = "instalación", ["electrica"] = "eléctrica", ["electrico"] = "eléctrico", ["hormigon"] = "hormigón", ["bano"] = "baño",
        ["sotano"] = "sótano", ["dormtorio"] = "dormitorio", ["pasadiso"] = "pasadizo",
        ["estacionamineto"] = "estacionamiento", ["balcon"] = "balcón",
        ["jardin"] = "jardín", ["cobogo"] = "cobogó", ["ceramica"] = "cerámica",
        ["fachda"] = "fachada", ["cubirta"] = "cubierta", ["ventan"] = "ventana", ["pueta"] = "puerta"
    };

    /// <summary>The corrected text, or the text itself when it needs nothing.</summary>
    public static string Fix(string text)
    {
        var fixedText = Regex.Replace(text, @"\p{L}+", match =>
            Misspellings.TryGetValue(match.Value, out var correct) && !string.Equals(correct, match.Value, StringComparison.Ordinal)
                ? char.IsUpper(match.Value[0]) ? char.ToUpperInvariant(correct[0]) + correct[1..] : correct
                : match.Value);
        fixedText = Regex.Replace(fixedText, @"\b(\p{L}+)(\s+\1\b)+", "$1", RegexOptions.IgnoreCase);
        fixedText = Regex.Replace(fixedText, @"[ \t]{2,}", " ");
        return fixedText.Trim();
    }

    public static IReadOnlyList<TextIssue> Check(BuildingDocument document)
    {
        var issues = new List<TextIssue>();
        void Look(string source, string? text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (Regex.IsMatch(text, @"\b(\p{L}+)(\s+\1\b)+", RegexOptions.IgnoreCase)) issues.Add(new(source, text, "RepeatedWord", Fix(text)));
            if (Regex.IsMatch(text, @"[ \t]{2,}")) issues.Add(new(source, text, "DoubleSpace", Fix(text)));
            if (text != text.Trim()) issues.Add(new(source, text, "EdgeSpace", Fix(text)));
            foreach (Match word in Regex.Matches(text, @"\p{L}+"))
                if (Misspellings.TryGetValue(word.Value, out var correct) && !string.Equals(correct, word.Value, StringComparison.Ordinal))
                    issues.Add(new(source, text, "Misspelled", $"{word.Value} → {correct}"));
            if (text.Count(c => c == '(') != text.Count(c => c == ')') || text.Count(c => c == '[') != text.Count(c => c == ']') || text.Count(c => c == '"') % 2 != 0)
                issues.Add(new(source, text, "Unbalanced", null));
        }
        foreach (var note in document.StructuralElements.Where(item => item.Kind == StructuralElementKind.ModelText)) Look("ModelText", note.Name);
        foreach (var room in document.Rooms) Look("Room", room.Name);
        foreach (var level in document.Levels) Look("Level", level.Name);
        foreach (var sheet in document.Sheets)
        {
            Look("Sheet", sheet.Name);
            Look("Sheet", sheet.Description);
        }
        return issues;
    }
}

public sealed partial class DocumentSession
{
    private Guid AddMark(Guid levelId, StructuralElementKind kind, string name, Point2 start, Point2 end, double width = 0, double height = 0, double offset = 0, double rise = 0)
    {
        if (Document.Levels.All(level => level.Id != levelId)) throw new InvalidDataException("The level does not exist.");
        var id = Guid.NewGuid();
        Commit("Transaction.AddAnnotationMark", document => document.StructuralElements.Add(
            new StructuralElement(id, levelId, kind, name, start, end, width, height, offset) { Rise = rise }));
        return id;
    }

    /// <summary>A spot elevation at a point: it reads the level's elevation plus the given height, and follows the level if it moves.</summary>
    public Guid AddSpotElevation(Guid levelId, Point2 position, double heightAboveLevel = 0)
    {
        if (!double.IsFinite(heightAboveLevel) || Math.Abs(heightAboveLevel) > 1000) throw new InvalidDataException("The height is invalid.");
        return AddMark(levelId, StructuralElementKind.SpotElevation, "Cota de nivel", position, position, offset: heightAboveLevel);
    }

    /// <summary>A spot coordinate at a point: it reads the shared coordinates of that place, and follows the project's site.</summary>
    public Guid AddSpotCoordinate(Guid levelId, Point2 position) =>
        AddMark(levelId, StructuralElementKind.SpotCoordinate, "Coordenada", position, position);

    /// <summary>A spot slope between two points, for a rise of the ground (positive when it is higher at the second point).</summary>
    public Guid AddSpotSlope(Guid levelId, Point2 start, Point2 end, double rise)
    {
        if (!double.IsFinite(rise) || Math.Abs(rise) > 100) throw new InvalidDataException("The height is invalid.");
        if (start.DistanceTo(end) < 0.05) throw new InvalidDataException("The slope needs two different points.");
        return AddMark(levelId, StructuralElementKind.SpotSlope, "Pendiente", start, end, rise: rise);
    }

    /// <summary>A keynote at a point, from a code of the keynote table or free text.</summary>
    public Guid AddKeynote(Guid levelId, Point2 position, string codeOrText)
    {
        var text = Keynotes.Resolve(codeOrText);
        if (text.Length == 0 || text.Length > 120) throw new InvalidDataException("The name is empty or too long.");
        return AddMark(levelId, StructuralElementKind.Keynote, text, position, position);
    }

    /// <summary>A 2D symbol or detail component of the library at a point, <paramref name="size"/> meters across.</summary>
    public Guid AddSymbol(Guid levelId, Point2 position, string key, double size = 1.0)
    {
        if (!PlanSymbols.IsKnown(key)) throw new InvalidDataException("The symbol does not exist.");
        if (!double.IsFinite(size) || size is < 0.05 or > 50) throw new InvalidDataException("The symbol size is invalid.");
        return AddMark(levelId, StructuralElementKind.Symbol, key, position, position, width: size);
    }

    /// <summary>
    /// A colour legend at a point. While a level has one, its rooms are coloured by name and the legend lists each name
    /// with the number of rooms and their area.
    /// </summary>
    public Guid AddColorLegend(Guid levelId, Point2 position) =>
        AddMark(levelId, StructuralElementKind.ColorLegend, "Leyenda de colores", position, position);

    /// <summary>Corrects what can be corrected without asking in the notes, room names and sheet descriptions. Returns how many texts changed.</summary>
    public int FixTexts()
    {
        var changes = 0;
        var notes = Document.StructuralElements.Where(item => item.Kind == StructuralElementKind.ModelText && TextChecker.Fix(item.Name) != item.Name).Select(item => item.Id).ToHashSet();
        var rooms = Document.Rooms.Where(room => TextChecker.Fix(room.Name) != room.Name && TextChecker.Fix(room.Name).Length > 0).Select(room => room.Id).ToHashSet();
        var sheets = Document.Sheets.Where(sheet => TextChecker.Fix(sheet.Description) != sheet.Description).Select(sheet => sheet.Id).ToHashSet();
        if (notes.Count + rooms.Count + sheets.Count == 0) return 0;
        Commit("Transaction.FixTexts", document =>
        {
            for (var index = 0; index < document.StructuralElements.Count; index++)
                if (notes.Contains(document.StructuralElements[index].Id))
                    document.StructuralElements[index] = document.StructuralElements[index] with { Name = TextChecker.Fix(document.StructuralElements[index].Name) };
            for (var index = 0; index < document.Rooms.Count; index++)
                if (rooms.Contains(document.Rooms[index].Id)) document.Rooms[index] = document.Rooms[index] with { Name = TextChecker.Fix(document.Rooms[index].Name) };
            for (var index = 0; index < document.Sheets.Count; index++)
                if (sheets.Contains(document.Sheets[index].Id)) document.Sheets[index] = document.Sheets[index] with { Description = TextChecker.Fix(document.Sheets[index].Description) };
            changes = notes.Count + rooms.Count + sheets.Count;
        });
        return changes;
    }
}
