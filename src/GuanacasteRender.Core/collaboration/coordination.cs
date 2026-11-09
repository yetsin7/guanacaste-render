using System.Text;
using System.Text.Json;
using Guanacaste3D.Core.Interop;

namespace Guanacaste3D.Core;

/// <summary>Two elements that occupy the same space.</summary>
public sealed record Interference(string KindA, Guid IdA, string KindB, Guid IdB, double Area, double Height)
{
    public double Volume => Area * Height;
}

/// <summary>Finds the places where walls, columns, beams, stairs and other solids of the building run into each other.</summary>
public static class InterferenceCheck
{
    private const double MinimumArea = 0.01;
    private const double MinimumHeight = 0.02;
    public const int MaximumResults = 500;

    private sealed record Solid(string Kind, Guid Id, Guid? Group, IReadOnlyList<Point2> Plan, double Bottom, double Top, double MinX, double MinY, double MaxX, double MaxY);

    private static readonly HashSet<StructuralElementKind> Considered =
    [
        StructuralElementKind.Beam, StructuralElementKind.Brace, StructuralElementKind.Truss, StructuralElementKind.Stair, StructuralElementKind.Ramp,
        StructuralElementKind.Elevator, StructuralElementKind.CurtainWall, StructuralElementKind.Duct, StructuralElementKind.CableTray,
        StructuralElementKind.FabricationPart, StructuralElementKind.FlexDuct, StructuralElementKind.Conduit
    ];

    private static bool Framing(string kind) => kind is "Beam" or "Brace" or "Truss";

    public static IReadOnlyList<Interference> Run(BuildingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var elevations = document.Levels.ToDictionary(level => level.Id, level => level.Elevation);
        var joins = WallJoinIndex.Build(document.Walls);
        var solids = new List<Solid>();

        void Add(string kind, Guid id, Guid? group, IReadOnlyList<Point2> plan, double bottom, double top)
        {
            if (plan.Count < 3 || top - bottom < MinimumHeight) return;
            solids.Add(new Solid(kind, id, group, plan, bottom, top, plan.Min(p => p.X), plan.Min(p => p.Y), plan.Max(p => p.X), plan.Max(p => p.Y)));
        }

        foreach (var wall in document.Walls.Where(wall => elevations.ContainsKey(wall.LevelId)))
            Add("Wall", wall.Id, wall.CurveGroupId, joins.SectionOutline(wall, 0, wall.Length), elevations[wall.LevelId], elevations[wall.LevelId] + wall.Height);
        foreach (var column in document.Columns.Where(column => elevations.ContainsKey(column.LevelId)))
            Add("Column", column.Id, null, column.Outline, elevations[column.LevelId], elevations[column.LevelId] + column.Height);
        foreach (var element in document.StructuralElements.Where(item => Considered.Contains(item.Kind) && elevations.ContainsKey(item.LevelId)))
            foreach (var part in ElementGeometry.Parts(element))
                Add(element.Kind.ToString(), element.Id, element.GroupId, part.Footprint, elevations[element.LevelId] + part.Bottom,
                    elevations[element.LevelId] + Math.Max(part.Top, part.EndTop ?? part.Top));

        var found = new Dictionary<(Guid, Guid), Interference>();
        for (var first = 0; first < solids.Count && found.Count < MaximumResults; first++)
            for (var second = first + 1; second < solids.Count && found.Count < MaximumResults; second++)
            {
                var a = solids[first];
                var b = solids[second];
                if (a.Id == b.Id || (a.Group is not null && a.Group == b.Group) ||
                    a.MaxX < b.MinX || b.MaxX < a.MinX || a.MaxY < b.MinY || b.MaxY < a.MinY) continue;
                var height = Math.Min(a.Top, b.Top) - Math.Max(a.Bottom, b.Bottom);
                if (height < MinimumHeight) continue;
                // Framing is designed to bear on or tie into columns and walls at its ends; stairs are built against walls only at their top.
                if ((Framing(a.Kind) && b.Kind == "Column") || (Framing(b.Kind) && a.Kind == "Column")) continue;
                var area = OverlapArea(a.Plan, b.Plan);
                if (area < MinimumArea) continue;
                var key = a.Id.CompareTo(b.Id) < 0 ? (a.Id, b.Id) : (b.Id, a.Id);
                if (found.TryGetValue(key, out var existing) && existing.Volume >= area * height) continue;
                found[key] = new Interference(a.Kind, a.Id, b.Kind, b.Id, area, height);
            }
        return found.Values.OrderByDescending(item => item.Volume).ToList();
    }

    private static double SignedArea(IReadOnlyList<Point2> polygon)
    {
        var sum = 0.0;
        for (var index = 0; index < polygon.Count; index++)
        {
            var next = polygon[(index + 1) % polygon.Count];
            sum += polygon[index].X * next.Y - next.X * polygon[index].Y;
        }
        return sum / 2;
    }

    private static bool Convex(IReadOnlyList<Point2> polygon)
    {
        var sign = 0;
        for (var index = 0; index < polygon.Count; index++)
        {
            var p = polygon[index];
            var q = polygon[(index + 1) % polygon.Count];
            var r = polygon[(index + 2) % polygon.Count];
            var cross = (q.X - p.X) * (r.Y - q.Y) - (q.Y - p.Y) * (r.X - q.X);
            if (Math.Abs(cross) < 1e-12) continue;
            var current = cross > 0 ? 1 : -1;
            if (sign != 0 && current != sign) return false;
            sign = current;
        }
        return true;
    }

    /// <summary>The area two plan outlines share. One of them must be convex (a wall, a column or a beam always is); otherwise the pair is not measured.</summary>
    private static double OverlapArea(IReadOnlyList<Point2> first, IReadOnlyList<Point2> second)
    {
        IReadOnlyList<Point2> clip, subject;
        if (Convex(second)) { clip = second; subject = first; }
        else if (Convex(first)) { clip = first; subject = second; }
        else return 0;
        if (SignedArea(clip) < 0) clip = clip.Reverse().ToList();
        var output = subject.ToList();
        for (var edge = 0; edge < clip.Count && output.Count > 0; edge++)
        {
            var from = clip[edge];
            var to = clip[(edge + 1) % clip.Count];
            var input = output;
            output = [];
            bool Inside(Point2 p) => (to.X - from.X) * (p.Y - from.Y) - (to.Y - from.Y) * (p.X - from.X) >= -1e-12;
            Point2 Cross(Point2 p, Point2 q)
            {
                var d1x = q.X - p.X;
                var d1y = q.Y - p.Y;
                var d2x = to.X - from.X;
                var d2y = to.Y - from.Y;
                var denominator = d1x * d2y - d1y * d2x;
                if (Math.Abs(denominator) < 1e-15) return q;
                var t = ((from.X - p.X) * d2y - (from.Y - p.Y) * d2x) / denominator;
                return new Point2(p.X + d1x * t, p.Y + d1y * t);
            }
            for (var index = 0; index < input.Count; index++)
            {
                var current = input[index];
                var previous = input[(index + input.Count - 1) % input.Count];
                if (Inside(current))
                {
                    if (!Inside(previous)) output.Add(Cross(previous, current));
                    output.Add(current);
                }
                else if (Inside(previous)) output.Add(Cross(previous, current));
            }
        }
        return output.Count < 3 ? 0 : Math.Abs(SignedArea(output));
    }
}

/// <summary>What the project publishes: which sheets, in which formats, into which folder.</summary>
public sealed record PublishSettings(List<Guid> SheetIds, bool Pdf, bool Dxf, bool Ifc, string Folder)
{
    public static PublishSettings Default => new([], true, false, false, string.Empty);
    public bool IsValid => SheetIds is not null && SheetIds.Count <= 1000 && Folder is not null && Folder.Length <= 500;
}

/// <summary>Writes the files that go out of the building: a package to share and the published set.</summary>
public static class Publishing
{
    private static string Safe(string name)
    {
        var clean = new string((name ?? string.Empty).Select(letter => Path.GetInvalidFileNameChars().Contains(letter) ? '_' : letter).ToArray()).Trim();
        return clean.Length == 0 ? "proyecto" : clean.Length > 80 ? clean[..80] : clean;
    }

    private static byte[] Pdf(BuildingDocument document, DateTimeOffset when)
    {
        var paper = document.Sheets.FirstOrDefault()?.PaperSize ?? SheetPaperSize.A3;
        return document.Sheets.Count > 0 ? PdfPlanExporter.ExportSheets(document, when) : PdfPlanExporter.ExportDocumentation(document, paper, when);
    }

    /// <summary>
    /// Writes a package for someone else to open or coordinate with: the project, the model as IFC, the plans as DXF, the
    /// sheets as PDF and a short summary. Returns the names of the files written.
    /// </summary>
    public static IReadOnlyList<string> CreateSharePackage(BuildingDocument document, string folder, DateTimeOffset when)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(folder)) throw new InvalidDataException("Choose a folder.");
        Directory.CreateDirectory(folder);
        var name = Safe(document.Name);
        var written = new List<string>();
        void Put(string file, Action<string> write)
        {
            write(Path.Combine(folder, file));
            written.Add(file);
        }
        Put($"{name}.g3d", path => ProjectFile.Save(path, document));
        Put($"{name}.ifc", path => File.WriteAllText(path, IfcExporter.ToStep(document, when), new UTF8Encoding(false)));
        Put($"{name}.dxf", path => File.WriteAllText(path, DxfExporter.ToAscii(document), new UTF8Encoding(false)));
        Put($"{name}.pdf", path => File.WriteAllBytes(path, Pdf(document, when)));
        var summary = new StringBuilder()
            .AppendLine($"{document.Name} — {when:yyyy-MM-dd HH:mm}")
            .AppendLine($"Niveles: {document.Levels.Count}  Muros: {document.Walls.Count}  Aberturas: {document.Openings.Count}  Losas: {document.Floors.Count}  Columnas: {document.Columns.Count}  Hojas: {document.Sheets.Count}")
            .AppendLine("Archivos: " + string.Join(", ", written));
        Put("LEEME.txt", path => File.WriteAllText(path, summary.ToString(), new UTF8Encoding(false)));
        return written;
    }

    /// <summary>Writes the published set: the chosen sheets as one PDF, and the DXF and IFC of the model when asked.</summary>
    public static IReadOnlyList<string> Publish(BuildingDocument document, PublishSettings settings, DateTimeOffset when)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(settings.Folder)) throw new InvalidDataException("Choose a folder.");
        if (!settings.Pdf && !settings.Dxf && !settings.Ifc) throw new InvalidDataException("Choose at least one format.");
        Directory.CreateDirectory(settings.Folder);
        var name = Safe(document.Name);
        var written = new List<string>();
        if (settings.Pdf)
        {
            var subset = document.Clone();
            subset.Sheets.RemoveAll(sheet => !settings.SheetIds.Contains(sheet.Id));
            if (document.Sheets.Count > 0 && subset.Sheets.Count == 0) throw new InvalidDataException("Choose at least one sheet.");
            File.WriteAllBytes(Path.Combine(settings.Folder, $"{name}-hojas.pdf"), Pdf(subset, when));
            written.Add($"{name}-hojas.pdf");
        }
        if (settings.Dxf)
        {
            File.WriteAllText(Path.Combine(settings.Folder, $"{name}.dxf"), DxfExporter.ToAscii(document), new UTF8Encoding(false));
            written.Add($"{name}.dxf");
        }
        if (settings.Ifc)
        {
            File.WriteAllText(Path.Combine(settings.Folder, $"{name}.ifc"), IfcExporter.ToStep(document, when), new UTF8Encoding(false));
            written.Add($"{name}.ifc");
        }
        return written;
    }
}

/// <summary>The central models this computer has used, most recent first, kept in a small file beside the synchronization copies.</summary>
public static class RecentCentralModels
{
    private static string FilePath()
    {
        var folder = Environment.GetEnvironmentVariable("G3D_CENTRAL_DIR") is { Length: > 0 } custom
            ? custom : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Guanacaste 3D", "Central");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "recent-central-models.json");
    }

    public static IReadOnlyList<string> List()
    {
        try { return File.Exists(FilePath()) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath())) ?? [] : []; }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    public static void Add(string path)
    {
        try
        {
            var list = List().Where(item => !string.Equals(item, path, StringComparison.OrdinalIgnoreCase)).Prepend(path).Take(20).ToList();
            File.WriteAllText(FilePath(), JsonSerializer.Serialize(list));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    public static void Remove(string path)
    {
        try { File.WriteAllText(FilePath(), JsonSerializer.Serialize(List().Where(item => !string.Equals(item, path, StringComparison.OrdinalIgnoreCase)).ToList())); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}

public sealed partial class DocumentSession
{
    /// <summary>Chooses what the project publishes, as one undoable step.</summary>
    public void SetPublishSettings(PublishSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var clean = settings with { SheetIds = settings.SheetIds.Where(id => Document.Sheets.Any(sheet => sheet.Id == id)).Distinct().ToList(), Folder = settings.Folder?.Trim() ?? string.Empty };
        if (!clean.IsValid) throw new InvalidDataException("The publish settings are invalid.");
        Commit("Transaction.SetPublishSettings", document => document.Publish = clean);
    }
}
