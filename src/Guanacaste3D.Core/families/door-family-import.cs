using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Guanacaste3D.Core;

/// <summary>
/// An opening size read from an external file, in meters. <see cref="Depth"/> is the
/// leaf thickness, or 0 when the file does not state one (the host wall governs).
/// </summary>
public sealed record ImportedDoor(string SourceFormat, double Width, double Height, double Depth);

/// <summary>
/// Reads opening dimensions from neutral exports prepared in Revit, SketchUp, or CAD.
/// Supported inputs are IFC instances, DXF plans, OBJ (a SketchUp or Revit mesh), and
/// Revit family files (.rfa), read for their category and type size only. Native .rvt,
/// .skp, and .dwg files need their vendor SDK or a converter, so they are not read here.
/// </summary>
public static partial class DoorFamilyImport
{
    /// <summary>Height assumed for plan-only drawings, which carry no vertical dimension.</summary>
    public const double PlanDoorHeight = 2.10;

    /// <summary>Largest file accepted, which keeps a mistaken selection from exhausting memory.</summary>
    private const long MaximumFileBytes = 200L * 1024 * 1024;

    /// <summary>Reads one door file synchronously. Plan-only formats take the height from <paramref name="planHeight"/>.</summary>
    public static ImportedDoor Read(string path, double planHeight = PlanDoorHeight, OpeningKind kind = OpeningKind.Door) =>
        ReadCore(path, planHeight, kind, progress: null, CancellationToken.None);

    /// <summary>
    /// Reads one file on a worker thread and reports progress from 0 to 100. The file bytes are
    /// read in chunks, which is where most of the time goes for large Revit families.
    /// </summary>
    public static Task<ImportedDoor> ReadAsync(string path, double planHeight, OpeningKind kind,
        IProgress<int>? progress, CancellationToken cancellation = default) =>
        Task.Run(() => ReadCore(path, planHeight, kind, progress, cancellation), cancellation);

    /// <summary>Shared reader: loads the bytes with progress, then parses them by format.</summary>
    private static ImportedDoor ReadCore(string path, double planHeight, OpeningKind kind,
        IProgress<int>? progress, CancellationToken cancellation)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".ifc" or ".dxf" or ".obj" or ".rfa"))
            throw new InvalidDataException("The door file format is not supported.");
        if (new FileInfo(path).Length > MaximumFileBytes)
            throw new InvalidDataException("The door file is too large to import.");

        var bytes = ReadBytesWithProgress(path, progress, cancellation);
        var door = extension switch
        {
            ".rfa" => ReadRfa(bytes, kind),
            ".ifc" => ReadIfc(Encoding.UTF8.GetString(bytes), kind),
            ".dxf" => ReadDxf(Encoding.UTF8.GetString(bytes), planHeight),
            _ => ReadObj(Encoding.UTF8.GetString(bytes).Split('\n'), kind)
        };
        cancellation.ThrowIfCancellationRequested();
        if (!double.IsFinite(door.Width) || !double.IsFinite(door.Height) || !double.IsFinite(door.Depth) ||
            door.Width is < 0.3 or > 10 || door.Height is < 0.3 or > 20 || door.Depth is < 0 or > 1)
            throw new InvalidDataException("The imported door size is outside the supported range.");
        progress?.Report(100);
        return door;
    }

    /// <summary>Loads a file in 64 KB chunks, reporting the first 80 percent of the progress bar.</summary>
    private static byte[] ReadBytesWithProgress(string path, IProgress<int>? progress, CancellationToken cancellation)
    {
        using var stream = File.OpenRead(path);
        var bytes = new byte[stream.Length];
        var total = 0;
        while (total < bytes.Length)
        {
            cancellation.ThrowIfCancellationRequested();
            var read = stream.Read(bytes, total, Math.Min(1 << 16, bytes.Length - total));
            if (read == 0) break;
            total += read;
            progress?.Report((int)(80L * total / bytes.Length));
        }
        return bytes;
    }

    /// <summary>
    /// Reads the family metadata that Revit stores as plain UTF-8 XML (PartAtom) inside an
    /// .rfa container. It yields the category, which must match the requested kind, and the
    /// type's width, height, and optional leaf thickness. Geometry lives in Revit's binary
    /// streams and is not read, so the imported opening stays parametric.
    /// </summary>
    private static ImportedDoor ReadRfa(byte[] bytes, OpeningKind kind)
    {
        var text = Encoding.UTF8.GetString(bytes);
        var match = Regex.Match(text, @"<\?xml[^>]*\?>\s*<entry\b.*?</entry>", RegexOptions.Singleline);
        if (!match.Success) throw new InvalidDataException("The Revit family has no readable family metadata.");
        XElement entry;
        try { entry = XDocument.Parse(match.Value).Root!; }
        catch (XmlException) { throw new InvalidDataException("The Revit family metadata is damaged."); }

        var groupings = entry.Descendants().Where(element => element.Name.LocalName == "category")
            .Where(category => category.Elements().Any(child =>
                child.Name.LocalName == "scheme" && child.Value == "adsk:revit:grouping"))
            .Select(category => category.Elements().First(child => child.Name.LocalName == "term").Value)
            .ToArray();
        var familyKind = groupings.Any(name => name is "Doors" or "Puertas") ? OpeningKind.Door
            : groupings.Any(name => name is "Windows" or "Ventanas") ? OpeningKind.Window
            : (OpeningKind?)null;
        if (familyKind is null)
            throw new InvalidDataException("The family is not a door or window; only openings can be imported for now.");
        if (familyKind != kind)
            throw new InvalidDataException("The family category does not match the selected kind.");

        var part = entry.Descendants().FirstOrDefault(element => element.Name.LocalName == "part");
        var parameters = part?.Elements().Where(element => element.Attribute("typeOfParameter")?.Value == "Longitud")
            .ToArray() ?? [];
        var width = ParameterLength(parameters, name => Regex.IsMatch(name, "^(width|ancho|anchura)$", RegexOptions.IgnoreCase));
        var height = ParameterLength(parameters, name => Regex.IsMatch(name, "^(height|alto|altura)$", RegexOptions.IgnoreCase));
        var depth = ParameterLength(parameters, name => Regex.IsMatch(name, "(thick|grosor|espesor|depth|profund)", RegexOptions.IgnoreCase)) ?? 0;

        if (width is null || height is null)
        {
            // Revit names metric door types "986 x 2089 mm", which is the size the user sees.
            var title = part?.Elements().FirstOrDefault(element => element.Name.LocalName == "title")?.Value ?? string.Empty;
            var size = Regex.Match(title, @"(\d+(?:[.,]\d+)?)\s*[x×]\s*(\d+(?:[.,]\d+)?)\s*(mm|cm|m)\b", RegexOptions.IgnoreCase);
            if (!size.Success) throw new InvalidDataException("The Revit family does not declare a width and height.");
            var scale = UnitScale(size.Groups[3].Value);
            width ??= ParseLocalized(size.Groups[1].Value) * scale;
            height ??= ParseLocalized(size.Groups[2].Value) * scale;
        }
        return new ImportedDoor("RFA", width.Value, height.Value, depth);
    }

    /// <summary>Returns the first length parameter whose name matches, converted to meters.</summary>
    private static double? ParameterLength(IEnumerable<XElement> parameters, Func<string, bool> nameMatches)
    {
        foreach (var parameter in parameters)
        {
            if (!nameMatches(parameter.Name.LocalName) ||
                !TryDouble(parameter.Value.Replace(',', '.'), out var value)) continue;
            return value * UnitScale(parameter.Attribute("units")?.Value ?? "mm");
        }
        return null;
    }

    /// <summary>Meters per Revit length unit. Revit metric families default to millimeters.</summary>
    private static double UnitScale(string unit) => unit.ToLowerInvariant() switch
    {
        "m" => 1,
        "cm" => 0.01,
        "ft" => 0.3048,
        "in" => 0.0254,
        _ => 0.001
    };

    /// <summary>Parses a size written with either decimal separator.</summary>
    private static double ParseLocalized(string text) =>
        double.Parse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>
    /// Reads OverallHeight and OverallWidth from the first IfcDoor instance. Those
    /// are direct attributes (positions 8 and 9) of IFC2X3 and IFC4 doors, and Revit
    /// writes them on export.
    /// </summary>
    private static ImportedDoor ReadIfc(string text, OpeningKind kind)
    {
        foreach (Match match in IfcOpeningPattern().Matches(text))
        {
            if (!match.Groups[1].Value.Equals(kind.ToString(), StringComparison.OrdinalIgnoreCase)) continue;
            var attributes = SplitStepArguments(match.Groups[2].Value);
            if (attributes.Count < 10) continue;
            if (!TryStepLength(attributes[8], out var height) || !TryStepLength(attributes[9], out var width))
                continue;
            var scale = IfcLengthScale(text);
            return new ImportedDoor("IFC", width * scale, height * scale, 0);
        }
        throw new InvalidDataException("The IFC file has no matching opening with declared dimensions.");
    }

    /// <summary>
    /// Takes the extents of the drawing's line, polyline, face, and point geometry.
    /// Plan drawings have no height, so <paramref name="planHeight"/> applies. Units
    /// come from $INSUNITS when present and otherwise from the size of the drawing.
    /// </summary>
    private static ImportedDoor ReadDxf(string text, double planHeight)
    {
        var lines = text.Split('\n');
        var points = new List<(double X, double Y)>();
        var geometry = new HashSet<string>(StringComparer.Ordinal)
            { "LINE", "LWPOLYLINE", "VERTEX", "3DFACE", "SOLID", "POINT" };
        string? entity = null;
        double? pendingX = null;
        int? units = null;
        var expectUnits = false;
        for (var index = 0; index + 1 < lines.Length; index += 2)
        {
            var code = lines[index].Trim();
            var value = lines[index + 1].Trim();
            if (code == "0")
            {
                entity = value;
                pendingX = null;
            }
            else if (code == "9")
            {
                expectUnits = value == "$INSUNITS";
            }
            else if (expectUnits && code == "70")
            {
                units = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code70)
                    ? code70 : null;
                expectUnits = false;
            }
            else if (entity is not null && geometry.Contains(entity))
            {
                if (code is "10" or "11" or "12" or "13")
                {
                    pendingX = TryDouble(value, out var x) ? x : null;
                }
                else if (code is "20" or "21" or "22" or "23" && pendingX is double px && TryDouble(value, out var y))
                {
                    points.Add((px, y));
                    pendingX = null;
                }
            }
        }
        if (points.Count < 2) throw new InvalidDataException("The DXF file has no door geometry.");

        var extentX = points.Max(p => p.X) - points.Min(p => p.X);
        var extentY = points.Max(p => p.Y) - points.Min(p => p.Y);
        var largest = Math.Max(extentX, extentY);
        var scale = units switch
        {
            6 => 1.0,
            5 => 0.01,
            4 => 0.001,
            1 => 0.0254,
            2 => 0.3048,
            _ => MetersFor(largest)
        };
        return new ImportedDoor("DXF", largest * scale, planHeight, Math.Min(extentX, extentY) * scale);
    }

    /// <summary>
    /// Reads the vertex bounding box of an OBJ mesh. A door is taller than it is wide
    /// and wider than it is thick, so the sorted extents give height, width, and depth
    /// regardless of which axis the exporter used for up. Windows instead require X-width, Y-up, meters.
    /// </summary>
    private static ImportedDoor ReadObj(IEnumerable<string> lines, OpeningKind kind)
    {
        var min = new[] { double.MaxValue, double.MaxValue, double.MaxValue };
        var max = new[] { double.MinValue, double.MinValue, double.MinValue };
        var count = 0;
        foreach (var line in lines)
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts[0] != "v") continue;
            if (parts.Length < 4 || !TryDouble(parts[1], out var x) ||
                !TryDouble(parts[2], out var y) || !TryDouble(parts[3], out var z)) continue;
            var position = new[] { x, y, z };
            for (var axis = 0; axis < 3; axis++)
            {
                min[axis] = Math.Min(min[axis], position[axis]);
                max[axis] = Math.Max(max[axis], position[axis]);
            }
            count++;
        }
        if (count < 3) throw new InvalidDataException("The OBJ file has no door geometry.");

        if (kind == OpeningKind.Window)
            return new ImportedDoor("OBJ", max[0] - min[0], max[1] - min[1], max[2] - min[2]);
        var extents = Enumerable.Range(0, 3).Select(axis => max[axis] - min[axis]).OrderByDescending(e => e).ToArray();
        var scale = MetersFor(extents[0]);
        return new ImportedDoor("OBJ", extents[1] * scale, extents[0] * scale, extents[2] * scale);
    }

    /// <summary>Resolves the assigned IFC length unit; unassigned legacy fragments use meters.</summary>
    private static double IfcLengthScale(string text)
    {
        var assignment = Regex.Match(text, @"IFCUNITASSIGNMENT\s*\(\((.*?)\)\)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!assignment.Success) return 1;
        foreach (Match reference in Regex.Matches(assignment.Groups[1].Value, @"#\d+"))
        {
            var unit = Regex.Match(text, Regex.Escape(reference.Value) + @"\s*=\s*IFCSIUNIT\s*\(\s*\$\s*,\s*\.LENGTHUNIT\.\s*,\s*(.*?)\s*,\s*\.METRE\.\s*\)", RegexOptions.IgnoreCase);
            if (!unit.Success) continue;
            return unit.Groups[1].Value.ToUpperInvariant() switch
            {
                "$" => 1,
                ".MILLI." => 0.001,
                ".CENTI." => 0.01,
                ".DECI." => 0.1,
                _ => throw new InvalidDataException("The IFC length unit is not supported.")
            };
        }
        throw new InvalidDataException("The IFC length unit is not supported.");
    }

    /// <summary>
    /// Assumes millimeters when the drawing is larger than any plausible door in
    /// meters. CAD and mesh exports often omit their unit, so this is a stated heuristic.
    /// </summary>
    private static double MetersFor(double largestExtent) => largestExtent > 50 ? 0.001 : 1;

    /// <summary>Parses a STEP length such as <c>2.1</c> or <c>IFCLENGTHMEASURE(2.1)</c>; <c>$</c> means unset.</summary>
    private static bool TryStepLength(string attribute, out double value)
    {
        value = 0;
        var trimmed = attribute.Trim();
        if (trimmed == "$") return false;
        var number = NumberPattern().Match(trimmed);
        return number.Success && TryDouble(number.Value, out value) && value > 0;
    }

    /// <summary>Splits STEP attribute text at top-level commas, respecting quoted strings and nested parentheses.</summary>
    private static List<string> SplitStepArguments(string arguments)
    {
        var parts = new List<string>();
        var depth = 0;
        var inString = false;
        var start = 0;
        for (var index = 0; index < arguments.Length; index++)
        {
            var character = arguments[index];
            if (character == '\'') inString = !inString;
            else if (!inString && character == '(') depth++;
            else if (!inString && character == ')') depth--;
            else if (!inString && depth == 0 && character == ',')
            {
                parts.Add(arguments[start..index]);
                start = index + 1;
            }
        }
        parts.Add(arguments[start..]);
        return parts;
    }

    /// <summary>Parses a number with invariant culture, so decimal points work on every locale.</summary>
    private static bool TryDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
        double.IsFinite(value);

    /// <summary>Matches door and window records while keeping STEP strings intact for argument splitting.</summary>
    [GeneratedRegex(@"#\d+\s*=\s*IFC(DOOR|WINDOW)\s*\((.*?)\)\s*;", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex IfcOpeningPattern();

    /// <summary>Matches invariant STEP numeric literals, including scientific notation.</summary>
    [GeneratedRegex(@"[-+]?\d*\.?\d+(?:[Ee][-+]?\d+)?")]
    private static partial Regex NumberPattern();
}
