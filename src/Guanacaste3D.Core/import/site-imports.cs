using System.Globalization;
using System.Xml.Linq;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core;

/// <summary>What reading a text of survey points found: the points and how many lines could not be used.</summary>
public sealed record SurveyPoints(IReadOnlyList<(double X, double Y, double Z)> Points, int Skipped);

/// <summary>Reads survey points from the text files that surveyors and scanners give: x, y, z on each line.</summary>
public static class SurveyPointReader
{
    public const int MaximumTerrainPoints = 1500;
    public const int MaximumCloudPoints = 30000;

    /// <summary>
    /// One point per line, as "x y z", "x,y,z" or "x;y;z" (tabs work too); a point cloud may add colours after the
    /// third number. Headers, comments (#) and lines with fewer than three numbers are counted and left out.
    /// </summary>
    public static SurveyPoints Parse(string? text)
    {
        var points = new List<(double, double, double)>();
        var skipped = 0;
        foreach (var raw in (text ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var fields = line.Split([(char)32, ',', ';', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 3 ||
                !double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                !double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z) ||
                !double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z) || Math.Abs(x) > 1e7 || Math.Abs(y) > 1e7 || Math.Abs(z) > 1e5)
            {
                // A header line ("x y z") is not a failure worth counting once, but a broken data line is.
                if (points.Count > 0 || fields.Length >= 3 && double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _)) skipped++;
                continue;
            }
            points.Add((x, y, z));
        }
        return new SurveyPoints(points, skipped);
    }

    /// <summary>
    /// Keeps at most <paramref name="limit"/> points, spread evenly: the plan is cut into a grid and the lowest point of
    /// each cell is kept, so a dense survey keeps its shape. Points closer than 1 cm to a kept one are dropped.
    /// </summary>
    public static IReadOnlyList<(double X, double Y, double Z)> Thin(IReadOnlyList<(double X, double Y, double Z)> points, int limit)
    {
        if (points.Count <= limit)
        {
            var seen = new HashSet<(long, long)>();
            return points.Where(point => seen.Add(((long)Math.Round(point.X * 100), (long)Math.Round(point.Y * 100)))).ToList();
        }
        var minX = points.Min(point => point.X);
        var maxX = points.Max(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxY = points.Max(point => point.Y);
        var cells = (int)Math.Ceiling(Math.Sqrt(limit));
        var width = Math.Max(1e-9, (maxX - minX) / cells);
        var height = Math.Max(1e-9, (maxY - minY) / cells);
        var best = new Dictionary<(int, int), (double X, double Y, double Z)>();
        foreach (var point in points)
        {
            var key = (Math.Min(cells - 1, (int)((point.X - minX) / width)), Math.Min(cells - 1, (int)((point.Y - minY) / height)));
            if (!best.TryGetValue(key, out var kept) || point.Z < kept.Z) best[key] = point;
        }
        return best.Values.Take(limit).ToList();
    }
}

/// <summary>A scanned or surveyed cloud of points kept as a reference in the plan.</summary>
public sealed record PointCloud(Guid Id, Guid LevelId, string Name, List<Point3> Points);

/// <summary>One point of a cloud, in meters.</summary>
public readonly record struct Point3(double X, double Y, double Z);

/// <summary>A space of a gbXML energy model: its name and the outline of its floor, in meters.</summary>
public sealed record GbxmlSpace(string Name, IReadOnlyList<Point2> Outline);

public static class GbxmlReader
{
    private const double MetersPerFoot = 0.3048;

    /// <summary>Reads the spaces of a gbXML file, using the floor outline of each (its lowest horizontal loop) and the file's length unit.</summary>
    public static IReadOnlyList<GbxmlSpace> Read(string? text)
    {
        XDocument document;
        try { document = XDocument.Parse(text ?? string.Empty); }
        catch (System.Xml.XmlException) { throw new InvalidDataException("The file is not a gbXML file."); }
        var root = document.Root;
        if (root is null || root.Name.LocalName != "gbXML") throw new InvalidDataException("The file is not a gbXML file.");
        var unit = (string?)root.Attribute("lengthUnit") ?? "Meters";
        var scale = unit switch
        {
            "Feet" => MetersPerFoot, "Inches" => 0.0254, "Millimeters" => 0.001, "Centimeters" => 0.01, _ => 1.0
        };
        var spaces = new List<GbxmlSpace>();
        foreach (var space in root.Descendants().Where(element => element.Name.LocalName == "Space"))
        {
            var name = space.Elements().FirstOrDefault(element => element.Name.LocalName == "Name")?.Value.Trim();
            if (string.IsNullOrWhiteSpace(name)) name = (string?)space.Attribute("id") ?? $"Space {spaces.Count + 1}";
            var loops = space.Descendants().Where(element => element.Name.LocalName == "PolyLoop")
                .Select(loop => loop.Elements().Where(element => element.Name.LocalName == "CartesianPoint")
                    .Select(point => point.Elements().Where(coordinate => coordinate.Name.LocalName == "Coordinate")
                        .Select(coordinate => double.TryParse(coordinate.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value * scale : double.NaN).ToArray())
                    .Where(coordinates => coordinates.Length >= 3 && coordinates.All(double.IsFinite)).ToArray())
                .Where(points => points.Length >= 3).ToList();
            // The floor is the horizontal loop at the lowest height.
            var floor = loops.Where(points => points.Max(p => p[2]) - points.Min(p => p[2]) < 1e-6).OrderBy(points => points[0][2]).FirstOrDefault();
            if (floor is null) continue;
            spaces.Add(new GbxmlSpace(name!, floor.Select(p => new Point2(p[0], p[1])).ToList()));
        }
        return spaces;
    }
}

public sealed partial class DocumentSession
{
    /// <summary>
    /// Makes a terrain slab from survey points (x, y and absolute elevation), as one undoable step. Its outline is the
    /// rectangle around the points and its elevations are taken relative to the level, so the ground keeps its heights.
    /// More than 1500 points are thinned evenly. Returns the terrain and how many points it kept.
    /// </summary>
    public (Guid Id, int Points) AddTerrainFromPoints(Guid levelId, IReadOnlyList<(double X, double Y, double Z)> points, string name = "Terreno")
    {
        ArgumentNullException.ThrowIfNull(points);
        var level = Document.Levels.FirstOrDefault(item => item.Id == levelId) ?? throw new InvalidDataException("The level does not exist.");
        var kept = SurveyPointReader.Thin(points, SurveyPointReader.MaximumTerrainPoints);
        if (kept.Count < 3) throw new InvalidDataException("A terrain needs at least three survey points.");
        const double margin = 0.1;
        var minX = kept.Min(p => p.X) - margin;
        var maxX = kept.Max(p => p.X) + margin;
        var minY = kept.Min(p => p.Y) - margin;
        var maxY = kept.Max(p => p.Y) + margin;
        if (maxX - minX < 0.5 || maxY - minY < 0.5) throw new InvalidDataException("The survey points lie too close together.");
        var relative = kept.Select(p => new TopoPoint(p.X, p.Y, p.Z - level.Elevation)).ToList();
        var lowest = relative.Min(p => p.Elevation);
        var id = Guid.NewGuid();
        Commit("Transaction.AddTerrainFromPoints", document => document.Floors.Add(new FloorSlab(
            id, levelId, name, [new(minX, minY), new(maxX, minY), new(maxX, maxY), new(minX, maxY)], 1.0)
        {
            Kind = FloorKind.Toposolid, ElevationOffset = lowest, TopoPoints = relative
        }));
        return (id, kept.Count);
    }

    /// <summary>Keeps a point cloud as a reference in the plan of a level, thinned to at most 30 000 points.</summary>
    public Guid AddPointCloud(Guid levelId, string name, IReadOnlyList<(double X, double Y, double Z)> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (Document.Levels.All(item => item.Id != levelId)) throw new InvalidDataException("The level does not exist.");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120) throw new InvalidDataException("The name is empty or too long.");
        var kept = SurveyPointReader.Thin(points, SurveyPointReader.MaximumCloudPoints);
        if (kept.Count < 1) throw new InvalidDataException("The file has no points.");
        var id = Guid.NewGuid();
        Commit("Transaction.AddPointCloud", document => document.PointClouds.Add(new PointCloud(id, levelId, name.Trim(), kept.Select(p => new Point3(p.X, p.Y, p.Z)).ToList())));
        return id;
    }

    public void RemovePointCloud(Guid id)
    {
        if (Document.PointClouds.All(item => item.Id != id)) throw new InvalidDataException("The point cloud does not exist.");
        Commit("Transaction.RemovePointCloud", document => document.PointClouds.RemoveAll(item => item.Id == id));
    }

    /// <summary>Makes a room for each space of a gbXML model, on a level, as one undoable step. Returns the rooms made.</summary>
    public int ImportGbxmlSpaces(Guid levelId, IReadOnlyList<GbxmlSpace> spaces)
    {
        ArgumentNullException.ThrowIfNull(spaces);
        if (Document.Levels.All(item => item.Id != levelId)) throw new InvalidDataException("The level does not exist.");
        var usable = spaces.Where(space => space.Outline.Count >= 3).Select(space =>
            (space.Name, Min: new Point2(space.Outline.Min(p => p.X), space.Outline.Min(p => p.Y)), Max: new Point2(space.Outline.Max(p => p.X), space.Outline.Max(p => p.Y))))
            .Where(space => space.Max.X - space.Min.X >= 0.3 && space.Max.Y - space.Min.Y >= 0.3).ToList();
        if (usable.Count == 0) throw new InvalidDataException("The file has no spaces that can become rooms.");
        using var group = BeginGroup("Transaction.ImportGbxml");
        foreach (var space in usable) AddRoom(levelId, space.Min, space.Max, space.Name.Length > 100 ? space.Name[..100] : space.Name);
        group.Assimilate();
        return usable.Count;
    }
}
