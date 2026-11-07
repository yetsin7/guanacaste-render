namespace Guanacaste3D.Core;

/// <summary>A set of rooms treated as one thermal zone, for the loads of the air conditioning.</summary>
public sealed record Zone(Guid Id, string Name, List<Guid> RoomIds);

/// <summary>What a zone holds: its rooms' area and volume.</summary>
public sealed record ZoneSummary(Guid ZoneId, string Name, int Rooms, double Area, double Volume);

/// <summary>The measure that colours the rooms when the results fill is on.</summary>
public enum ResultsMetric
{
    None,
    Area,
    WindowRatio,
    Perimeter
}

/// <summary>A variant of a mass with the same floor area and height, so the same volume, and what that does to its walls.</summary>
public sealed record MassVariant(Guid Id, string Name, double Perimeter, double FacadeArea, double Compactness);

public static class ResultsFill
{
    /// <summary>The value of the metric for a room: its area, the window area over its floor area, or its perimeter.</summary>
    public static double ValueOf(BuildingDocument document, Room room, ResultsMetric metric)
    {
        var width = Math.Abs(room.SecondCorner.X - room.FirstCorner.X);
        var depth = Math.Abs(room.SecondCorner.Y - room.FirstCorner.Y);
        switch (metric)
        {
            case ResultsMetric.Area: return room.Area;
            case ResultsMetric.Perimeter: return 2 * (width + depth);
            case ResultsMetric.WindowRatio:
                if (room.Area < 1e-9) return 0;
                // The windows of the walls that lie along the room's sides (within 30 cm of its rectangle).
                double MinX() => Math.Min(room.FirstCorner.X, room.SecondCorner.X) - 0.3;
                double MaxX() => Math.Max(room.FirstCorner.X, room.SecondCorner.X) + 0.3;
                double MinY() => Math.Min(room.FirstCorner.Y, room.SecondCorner.Y) - 0.3;
                double MaxY() => Math.Max(room.FirstCorner.Y, room.SecondCorner.Y) + 0.3;
                var windows = document.Walls.Where(wall => wall.LevelId == room.LevelId &&
                        wall.Start.X >= MinX() && wall.Start.X <= MaxX() && wall.End.X >= MinX() && wall.End.X <= MaxX() &&
                        wall.Start.Y >= MinY() && wall.Start.Y <= MaxY() && wall.End.Y >= MinY() && wall.End.Y <= MaxY())
                    .SelectMany(wall => document.Openings.Where(opening => opening.WallId == wall.Id && opening.Kind == OpeningKind.Window))
                    .Sum(opening => opening.Width * opening.Height);
                return windows / room.Area;
            default: return 0;
        }
    }

    /// <summary>Position of a value between the least and the greatest of the rooms of its level, 0 to 1; 0.5 when they are all equal.</summary>
    public static double Position(BuildingDocument document, Room room, ResultsMetric metric)
    {
        var values = document.Rooms.Where(item => item.LevelId == room.LevelId).Select(item => ValueOf(document, item, metric)).ToList();
        var least = values.Min();
        var most = values.Max();
        return most - least < 1e-9 ? 0.5 : Math.Clamp((ValueOf(document, room, metric) - least) / (most - least), 0, 1);
    }

    /// <summary>Blue for the least, through green and yellow, to red for the most.</summary>
    public static (byte R, byte G, byte B) ColorAt(double position)
    {
        var t = Math.Clamp(position, 0, 1);
        (double R, double G, double B) Lerp((double, double, double) from, (double, double, double) to, double amount) =>
            (from.Item1 + (to.Item1 - from.Item1) * amount, from.Item2 + (to.Item2 - from.Item2) * amount, from.Item3 + (to.Item3 - from.Item3) * amount);
        var c = t < 0.5 ? Lerp((52, 120, 220), (90, 200, 90), t * 2) : Lerp((90, 200, 90), (230, 60, 50), (t - 0.5) * 2);
        return ((byte)Math.Round(c.R), (byte)Math.Round(c.G), (byte)Math.Round(c.B));
    }
}

public sealed partial class DocumentSession
{
    // ------------------------------------------------------------------ zones

    private static double LevelHeight(BuildingDocument document, Guid levelId) =>
        document.Walls.Where(wall => wall.LevelId == levelId).Select(wall => wall.Height).DefaultIfEmpty(3.0).Max();

    public Guid CreateZone(string name, IReadOnlyCollection<Guid> roomIds)
    {
        var clean = name?.Trim() ?? string.Empty;
        if (clean.Length == 0 || clean.Length > 120) throw new InvalidDataException("The name is empty or too long.");
        var rooms = roomIds.Distinct().ToList();
        if (rooms.Count == 0 || rooms.Any(id => Document.Rooms.All(room => room.Id != id))) throw new InvalidDataException("The room does not exist.");
        if (Document.Zones.Any(zone => zone.Name.Equals(clean, StringComparison.CurrentCultureIgnoreCase))) throw new InvalidDataException("A zone with that name already exists.");
        if (Document.Zones.Any(zone => zone.RoomIds.Intersect(rooms).Any())) throw new InvalidDataException("A room is already in another zone.");
        var id = Guid.NewGuid();
        Commit("Transaction.CreateZone", document => document.Zones.Add(new Zone(id, clean, rooms)));
        return id;
    }

    public void RemoveZone(Guid id)
    {
        if (Document.Zones.All(zone => zone.Id != id)) throw new InvalidDataException("The zone does not exist.");
        Commit("Transaction.RemoveZone", document => document.Zones.RemoveAll(zone => zone.Id == id));
    }

    public IReadOnlyList<ZoneSummary> SummarizeZones() => Document.Zones.Select(zone =>
    {
        var rooms = Document.Rooms.Where(room => zone.RoomIds.Contains(room.Id)).ToList();
        return new ZoneSummary(zone.Id, zone.Name, rooms.Count, rooms.Sum(room => room.Area), rooms.Sum(room => room.Area * LevelHeight(Document, room.LevelId)));
    }).ToList();

    // ------------------------------------------------------------------ results fill

    public void SetResultsMetric(ResultsMetric metric)
    {
        if (!Enum.IsDefined(metric)) throw new InvalidDataException("The metric is invalid.");
        if (metric == Document.ResultsMetric) return;
        Commit("Transaction.SetResultsMetric", document => document.ResultsMetric = metric);
    }

    // ------------------------------------------------------------------ form creation

    /// <summary>
    /// Explores alternative shapes for a mass: the same floor area and height (so the same volume) as rectangles of the given
    /// proportions, centred where the mass is. They go in a design option set, the original in its primary option, so each can be
    /// looked at by itself. Returns the variants with the length of their walls and how compact they are (1 is a square).
    /// </summary>
    public IReadOnlyList<MassVariant> GenerateMassVariants(Guid massId, IReadOnlyList<double> proportions)
    {
        var mass = Document.StructuralElements.FirstOrDefault(item => item.Id == massId && item.Kind == StructuralElementKind.Mass) ?? throw new InvalidDataException("The mass does not exist.");
        if (proportions is null || proportions.Count == 0 || proportions.Count > 8 || proportions.Any(value => !double.IsFinite(value) || value < 1 || value > 10))
            throw new InvalidDataException("The proportions are invalid.");
        var footprint = mass.Footprint;
        var width = footprint.Max(p => p.X) - footprint.Min(p => p.X);
        var depth = footprint.Max(p => p.Y) - footprint.Min(p => p.Y);
        var area = width * depth;
        var centreX = (footprint.Max(p => p.X) + footprint.Min(p => p.X)) / 2;
        var centreY = (footprint.Max(p => p.Y) + footprint.Min(p => p.Y)) / 2;
        var setName = UniqueSetName($"Formas {mass.Name}");
        var variants = new List<MassVariant>();
        using var group = BeginGroup("Transaction.GenerateMassVariants");
        var set = AddOptionSet(setName, "Original");
        var original = Document.OptionSets.Single(item => item.Id == set).PrimaryOptionId;
        AssignToOption([massId], original);
        variants.Add(Describe(massId, mass.Name, width, depth, mass.Height));
        foreach (var proportion in proportions.Distinct())
        {
            var w = Math.Sqrt(area * proportion);
            var d = area / w;
            var name = $"{proportion:0.##}:1";
            var option = AddDesignOption(set, name);
            var id = AddStructuralElement(mass.LevelId, StructuralElementKind.Mass, $"{mass.Name} {name}",
                new Point2(centreX - w / 2, centreY - d / 2), new Point2(centreX + w / 2, centreY + d / 2));
            UpdateStructuralElement(id, $"{mass.Name} {name}", mass.Width, mass.Height, mass.Offset, ElementStyle.Default);
            AssignToOption([id], option);
            variants.Add(Describe(id, name, w, d, mass.Height));
        }
        group.Assimilate();
        return variants;
    }

    private static MassVariant Describe(Guid id, string name, double width, double depth, double height)
    {
        var perimeter = 2 * (width + depth);
        return new MassVariant(id, name, perimeter, perimeter * height, 4 * Math.Sqrt(width * depth) / perimeter);
    }

    private string UniqueSetName(string name)
    {
        var taken = Document.OptionSets.Select(item => item.Name.ToUpperInvariant()).ToHashSet();
        if (!taken.Contains(name.ToUpperInvariant())) return name;
        for (var number = 2; ; number++)
            if (!taken.Contains($"{name} {number}".ToUpperInvariant())) return $"{name} {number}";
    }
}
