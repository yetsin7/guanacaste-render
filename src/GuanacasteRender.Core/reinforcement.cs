using System.Globalization;
using System.Text;

namespace Guanacaste3D.Core;

/// <summary>The wires of a welded fabric sheet: their diameter and the spacing of the grid, in meters.</summary>
public sealed record WireSpec(double Diameter, double Spacing)
{
    public bool IsValid => Diameter is >= 0.002 and <= 0.02 && Spacing is >= 0.05 and <= 0.5;
}

/// <summary>How much concrete covers the steel in each kind of element, the bar the project uses by default and the steel itself.</summary>
public sealed record ReinforcementSettings(double BeamCover, double ColumnCover, double SlabCover, double FoundationCover, double DefaultDiameter, double SteelDensity, string SteelGrade)
{
    public static ReinforcementSettings Default { get; } = new(0.04, 0.04, 0.025, 0.075, 0.012, 7850, "Grado 60");
    public bool IsValid => new[] { BeamCover, ColumnCover, SlabCover, FoundationCover }.All(cover => cover is >= 0.01 and <= 0.15) &&
                           DefaultDiameter is >= 0.006 and <= 0.05 && SteelDensity is >= 7000 and <= 8500 && !string.IsNullOrWhiteSpace(SteelGrade) && SteelGrade.Length <= 40;
}

/// <summary>How walls are cut into precast panels, and the bar that reinforces each panel.</summary>
public sealed record PrecastSettings(double MaxPanelWidth, string Prefix, double PanelBarDiameter)
{
    public static PrecastSettings Default { get; } = new(2.4, "P", 0.010);
    public bool IsValid => MaxPanelWidth is >= 0.5 and <= 12 && !string.IsNullOrWhiteSpace(Prefix) && Prefix.Length <= 10 && PanelBarDiameter is >= 0.006 and <= 0.032;
}

/// <summary>The concrete: its strength grade, how heavy it is, and the weight of the steel it holds per cubic meter for the reports.</summary>
public sealed record ConcreteSettings(string Grade, double UnitWeight, double SteelKgPerM3)
{
    public static ConcreteSettings Default { get; } = new("f'c 210 kg/cm²", 2400, 100);
    public bool IsValid => !string.IsNullOrWhiteSpace(Grade) && Grade.Length <= 40 && UnitWeight is >= 1500 and <= 3000 && SteelKgPerM3 is >= 0 and <= 500;
}

/// <summary>The cover of one element when it is not the project's default. The <c>Id</c> is the element's own.</summary>
public sealed record CoverOverride(Guid Id, double Cover);

/// <summary>The bars and fabric of a project, counted: marks of equal bars and the steel they weigh.</summary>
public sealed record BarMark(double Diameter, double Length, int Count)
{
    public double TotalLength => Length * Count;
    public double Weight(double density) => TotalLength * Math.PI * Diameter * Diameter / 4 * density;
}

public sealed record ReinforcementReport(IReadOnlyList<BarMark> Bars, double FabricArea, double FabricWeight, double ConcreteVolume, double Density)
{
    public double BarWeight => Bars.Sum(mark => mark.Weight(Density));
    public double TotalWeight => BarWeight + FabricWeight;

    /// <summary>Bars counted by diameter and length (to the centimeter), plus the fabric, for the concrete elements of the project or the given ones.</summary>
    public static ReinforcementReport Build(BuildingDocument document, IReadOnlyCollection<Guid>? onlyIds = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var bars = document.StructuralElements.Where(item => item.Kind == StructuralElementKind.Rebar && (onlyIds is null || onlyIds.Contains(item.Id)))
            .GroupBy(item => (Math.Round(item.Width, 4), Math.Round(item.Length, 2)))
            .Select(group => new BarMark(group.Key.Item1, group.Key.Item2, group.Count())).OrderBy(mark => mark.Diameter).ThenBy(mark => mark.Length).ToList();
        var sheets = document.StructuralElements.Where(item => item.Kind == StructuralElementKind.FabricSheet && (onlyIds is null || onlyIds.Contains(item.Id))).ToList();
        double area = 0, weight = 0;
        foreach (var sheet in sheets)
        {
            var wires = sheet.Wires ?? new WireSpec(0.0055, 0.15);
            var sheetArea = sheet.Length * sheet.Width;
            area += sheetArea;
            weight += 2 * sheetArea / wires.Spacing * Math.PI * wires.Diameter * wires.Diameter / 4 * document.Reinforcement.SteelDensity;
        }
        var concrete = document.Floors.Sum(floor => floor.CalculateArea() * floor.Thickness) +
                       document.StructuralElements.Where(item => item.Kind is StructuralElementKind.Beam or StructuralElementKind.WallFoundation or StructuralElementKind.IsolatedFoundation)
                           .Sum(item => Math.Max(item.Length, item.Width) * item.Width * item.Height) +
                       document.Columns.Sum(column => column.Width * column.Depth * column.Height);
        return new ReinforcementReport(bars, area, weight, concrete, document.Reinforcement.SteelDensity);
    }

    /// <summary>The report as a table: one row for each kind of bar, then the fabric and the totals.</summary>
    public string ToCsv()
    {
        var text = new StringBuilder();
        text.AppendLine("Marca;Diametro_mm;Largo_m;Cantidad;Largo_total_m;Peso_kg");
        for (var index = 0; index < Bars.Count; index++)
        {
            var mark = Bars[index];
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"B{index + 1};{mark.Diameter * 1000:0.#};{mark.Length:0.00};{mark.Count};{mark.TotalLength:0.00};{mark.Weight(Density):0.00}"));
        }
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Malla;;;;{FabricArea:0.00} m2;{FabricWeight:0.00}"));
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Total;;;;;{TotalWeight:0.00}"));
        return text.ToString();
    }

    /// <summary>
    /// A cutting list a bending or cutting machine program can read, one line for each bar mark, in a form modelled on the BVBS BF2D
    /// lines ("BF2D@Hj{project}@r{revision}@i{item}@p{position}@l{length mm}@n{count}@e{kg per bar}@d{diameter mm}@g{grade}@s{roller}@v@a@") for straight bars.
    /// It is not certified BVBS: the checksum at the end of each line is this program's own, to catch a line damaged in transit.
    /// </summary>
    public string ToCutList(string project, string grade)
    {
        var text = new StringBuilder();
        for (var index = 0; index < Bars.Count; index++)
        {
            var mark = Bars[index];
            var line = string.Create(CultureInfo.InvariantCulture,
                $"BF2D@Hj{Clean(project)}@r0@i{index + 1}@p{index + 1}@l{mark.Length * 1000:0}@n{mark.Count}@e{mark.Weight(Density) / Math.Max(1, mark.Count):0.000}@d{mark.Diameter * 1000:0}@g{Clean(grade)}@s{mark.Diameter * 1000 * 2:0}@v@a@");
            text.AppendLine(line + "C" + Checksum(line) + "@");
        }
        return text.ToString();
    }

    private static string Clean(string value) => new string(value.Where(letter => char.IsLetterOrDigit(letter) || letter is '-' or '_').ToArray()) is { Length: > 0 } clean ? clean : "X";

    /// <summary>The checksum at the end of a cutting-list line: 97 minus the sum of the character codes of the line, modulo 97, kept between 1 and 97.</summary>
    public static int Checksum(string line)
    {
        var sum = 0;
        foreach (var letter in line) sum += letter;
        var value = 97 - (sum % 97);
        return value < 1 ? value + 97 : value;
    }
}

public sealed partial class DocumentSession
{
    private static bool IsLineMember(StructuralElement item) =>
        item.Kind is StructuralElementKind.Beam or StructuralElementKind.Brace or StructuralElementKind.Truss or StructuralElementKind.WallFoundation;

    /// <summary>The cover of an element: the one set for it, or the project's for its kind.</summary>
    public double CoverOf(Guid elementId)
    {
        if (Document.CoverOverrides.FirstOrDefault(item => item.Id == elementId) is { } own) return own.Cover;
        var settings = Document.Reinforcement;
        if (Document.StructuralElements.FirstOrDefault(item => item.Id == elementId) is { } member)
            return member.Kind is StructuralElementKind.WallFoundation or StructuralElementKind.IsolatedFoundation ? settings.FoundationCover : settings.BeamCover;
        if (Document.Columns.Any(item => item.Id == elementId)) return settings.ColumnCover;
        if (Document.Floors.Any(item => item.Id == elementId)) return settings.SlabCover;
        if (Document.Walls.Any(item => item.Id == elementId)) return settings.SlabCover;
        return settings.BeamCover;
    }

    public void SetReinforcementSettings(ReinforcementSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.IsValid) throw new InvalidDataException("The reinforcement settings are invalid.");
        Commit("Transaction.SetReinforcementSettings", document => document.Reinforcement = settings);
    }

    public void SetPrecastSettings(PrecastSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.IsValid) throw new InvalidDataException("The precast settings are invalid.");
        Commit("Transaction.SetPrecastSettings", document => document.Precast = settings);
    }

    public void SetConcreteSettings(ConcreteSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.IsValid) throw new InvalidDataException("The concrete settings are invalid.");
        Commit("Transaction.SetConcreteSettings", document => document.Concrete = settings);
    }

    /// <summary>Sets the cover of one element, in meters, or (null) goes back to the project's.</summary>
    public void SetCover(Guid elementId, double? cover)
    {
        if (!ElementGroups.Exists(Document, elementId) && !Document.Columns.Any(item => item.Id == elementId)) throw new InvalidDataException("The element does not exist.");
        if (cover is { } value && (!double.IsFinite(value) || value < 0.01 || value > 0.15)) throw new InvalidDataException("The cover is invalid.");
        Commit("Transaction.SetCover", document =>
        {
            document.CoverOverrides.RemoveAll(item => item.Id == elementId);
            if (cover is { } set) document.CoverOverrides.Add(new CoverOverride(elementId, set));
        });
    }

    private StructuralElement NewBar(Guid levelId, Point2 from, Point2 to, double diameter, double height, Guid? group) =>
        new(Guid.NewGuid(), levelId, StructuralElementKind.Rebar, "Barra", from, to, diameter, diameter, height) { GroupId = group };

    /// <summary>
    /// Reinforces a beam, brace, truss or wall footing with bars along its length, spread across its width inside the cover, along the bottom or
    /// the top face, and grouped so they move with each other. Returns the bars.
    /// </summary>
    public IReadOnlyList<Guid> AddMemberRebar(Guid hostId, int count, double diameter, bool top = false)
    {
        var host = Document.StructuralElements.FirstOrDefault(item => item.Id == hostId && IsLineMember(item)) ?? throw new InvalidDataException("The element to reinforce does not exist.");
        var cover = CoverOf(hostId);
        if (count < 1 || count > 20 || !double.IsFinite(diameter) || diameter < 0.006 || diameter > 0.05) throw new InvalidDataException("The bars are invalid.");
        var available = host.Width - 2 * cover - diameter;
        if (available < 0 || host.Height < 2 * cover + diameter || (count > 1 && available / (count - 1) < diameter * 1.5)) throw new InvalidDataException("The bars do not fit in the member.");
        var length = host.Length;
        if (length < 2 * cover + 0.1) throw new InvalidDataException("The member is too short for bars.");
        var dx = (host.End.X - host.Start.X) / length;
        var dy = (host.End.Y - host.Start.Y) / length;
        var group = count > 1 ? Guid.NewGuid() : (Guid?)null;
        var height = top ? host.Offset + host.Height - cover - diameter / 2 : host.Offset + cover + diameter / 2;
        var ids = new List<Guid>();
        Commit("Transaction.AddRebar", document =>
        {
            for (var index = 0; index < count; index++)
            {
                var across = count == 1 ? 0 : -available / 2 + available * index / (count - 1);
                Point2 At(double along) => new(host.Start.X + dx * along - dy * across, host.Start.Y + dy * along + dx * across);
                var bar = NewBar(host.LevelId, At(cover), At(length - cover), diameter, height, group);
                ids.Add(bar.Id);
                document.StructuralElements.Add(bar);
            }
        });
        return ids;
    }

    /// <summary>Draws a bar that follows a path of at least three points (a stirrup, a hook), a straight piece between each pair, grouped as one.</summary>
    public IReadOnlyList<Guid> AddRebarPath(Guid levelId, IReadOnlyList<Point2> points, double diameter, double height)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (Document.Levels.All(level => level.Id != levelId)) throw new InvalidDataException("The level does not exist.");
        if (points.Count < 3 || points.Count > 60) throw new InvalidDataException("A bar path needs from 3 to 60 points.");
        if (!double.IsFinite(diameter) || diameter < 0.006 || diameter > 0.05 || !double.IsFinite(height) || Math.Abs(height) > 100) throw new InvalidDataException("The bars are invalid.");
        for (var index = 1; index < points.Count; index++)
            if (points[index].DistanceTo(points[index - 1]) < 0.02) throw new InvalidDataException("Two points of the route are too close together.");
        var group = Guid.NewGuid();
        var ids = new List<Guid>();
        Commit("Transaction.AddRebarPath", document =>
        {
            for (var index = 1; index < points.Count; index++)
            {
                var bar = NewBar(levelId, points[index - 1], points[index], diameter, height, group);
                ids.Add(bar.Id);
                document.StructuralElements.Add(bar);
            }
        });
        return ids;
    }

    /// <summary>Fills a rectangle (a slab, a footing) with parallel bars every <paramref name="spacing"/>, inside the cover. Returns the bars.</summary>
    public IReadOnlyList<Guid> AddRebarArea(Guid levelId, Point2 first, Point2 opposite, double spacing, double diameter, bool alongX, double height)
    {
        if (Document.Levels.All(level => level.Id != levelId)) throw new InvalidDataException("The level does not exist.");
        if (!double.IsFinite(spacing) || spacing < 0.05 || spacing > 1 || !double.IsFinite(diameter) || diameter < 0.006 || diameter > 0.05 || !double.IsFinite(height) || Math.Abs(height) > 100)
            throw new InvalidDataException("The bars are invalid.");
        var cover = Document.Reinforcement.SlabCover;
        double minX = Math.Min(first.X, opposite.X) + cover, maxX = Math.Max(first.X, opposite.X) - cover;
        double minY = Math.Min(first.Y, opposite.Y) + cover, maxY = Math.Max(first.Y, opposite.Y) - cover;
        var run = alongX ? maxX - minX : maxY - minY;
        var across = alongX ? maxY - minY : maxX - minX;
        if (run < 0.1 || across < 0) throw new InvalidDataException("The area is too small for bars.");
        var count = (int)Math.Floor(across / spacing) + 1;
        if (count > 400) throw new InvalidDataException("The area would get too many bars.");
        var group = Guid.NewGuid();
        var ids = new List<Guid>();
        Commit("Transaction.AddRebarArea", document =>
        {
            for (var index = 0; index < count; index++)
            {
                var position = (alongX ? minY : minX) + index * spacing;
                var bar = alongX ? NewBar(levelId, new Point2(minX, position), new Point2(maxX, position), diameter, height, group)
                                 : NewBar(levelId, new Point2(position, minY), new Point2(position, maxY), diameter, height, group);
                ids.Add(bar.Id);
                document.StructuralElements.Add(bar);
            }
        });
        return ids;
    }

    /// <summary>Lays one sheet of welded fabric along a line, <paramref name="width"/> wide, at any angle.</summary>
    public Guid AddFabricSheet(Guid levelId, Point2 start, Point2 end, double width, WireSpec wires, double height = 0.03)
    {
        if (Document.Levels.All(level => level.Id != levelId)) throw new InvalidDataException("The level does not exist.");
        if (wires is null || !wires.IsValid || !double.IsFinite(width) || width < 0.2 || width > 4 || start.DistanceTo(end) < 0.2 || start.DistanceTo(end) > 12 || !double.IsFinite(height) || Math.Abs(height) > 100)
            throw new InvalidDataException("The fabric sheet is invalid.");
        var id = Guid.NewGuid();
        Commit("Transaction.AddFabricSheet", document => document.StructuralElements.Add(
            new StructuralElement(id, levelId, StructuralElementKind.FabricSheet, "Malla", start, end, width, 0.006, height) { Wires = wires }));
        return id;
    }

    /// <summary>Covers a rectangle with sheets of fabric <paramref name="sheetLength"/> by <paramref name="sheetWidth"/>, lapping each over the next by <paramref name="lap"/>. Returns the sheets.</summary>
    public IReadOnlyList<Guid> AddFabricArea(Guid levelId, Point2 first, Point2 opposite, WireSpec wires, double sheetLength = 6, double sheetWidth = 2.4, double lap = 0.2, double height = 0.03)
    {
        if (Document.Levels.All(level => level.Id != levelId)) throw new InvalidDataException("The level does not exist.");
        if (wires is null || !wires.IsValid || sheetLength < 1 || sheetLength > 12 || sheetWidth < 0.5 || sheetWidth > 4 || lap < 0 || lap > 0.5) throw new InvalidDataException("The fabric sheet is invalid.");
        double minX = Math.Min(first.X, opposite.X), maxX = Math.Max(first.X, opposite.X), minY = Math.Min(first.Y, opposite.Y), maxY = Math.Max(first.Y, opposite.Y);
        if (maxX - minX < 0.5 || maxY - minY < 0.5) throw new InvalidDataException("The area is too small for fabric.");
        var rows = (int)Math.Ceiling((maxY - minY - lap) / (sheetWidth - lap) - 1e-9);
        var columns = (int)Math.Ceiling((maxX - minX - lap) / (sheetLength - lap) - 1e-9);
        if (rows * columns > 400) throw new InvalidDataException("The area would get too many sheets.");
        var group = Guid.NewGuid();
        var ids = new List<Guid>();
        Commit("Transaction.AddFabricArea", document =>
        {
            for (var row = 0; row < rows; row++)
                for (var column = 0; column < columns; column++)
                {
                    var x0 = minX + column * (sheetLength - lap);
                    var x1 = Math.Min(maxX, x0 + sheetLength);
                    var y0 = minY + row * (sheetWidth - lap);
                    var y1 = Math.Min(maxY, y0 + sheetWidth);
                    if (x1 - x0 < 0.2 || y1 - y0 < 0.2) continue;
                    var id = Guid.NewGuid();
                    ids.Add(id);
                    document.StructuralElements.Add(new StructuralElement(id, levelId, StructuralElementKind.FabricSheet, "Malla", new Point2(x0, (y0 + y1) / 2), new Point2(x1, (y0 + y1) / 2), y1 - y0, 0.006, height)
                    { Wires = wires, GroupId = group });
                }
        });
        return ids;
    }

    /// <summary>
    /// Divides a wall into precast panels no wider than the project's limit, all of one width, and gathers them in an assembly named
    /// with the project's prefix. Doors and windows must not fall on a joint. Returns the panels, the original wall first.
    /// </summary>
    public IReadOnlyList<Guid> DivideWallIntoPanels(Guid wallId)
    {
        var wall = Document.Walls.FirstOrDefault(item => item.Id == wallId) ?? throw new InvalidDataException("The wall does not exist.");
        var settings = Document.Precast;
        var count = (int)Math.Ceiling(wall.Length / settings.MaxPanelWidth - 1e-9);
        if (count < 2) throw new InvalidDataException("The wall is no wider than a panel.");
        if (count > 60) throw new InvalidDataException("The wall would get too many panels.");
        var ids = new List<Guid> { wallId };
        using var group = BeginGroup("Transaction.DivideWallIntoPanels");
        var rest = wallId;
        for (var piece = 1; piece < count; piece++)
        {
            var current = Document.Walls.Single(item => item.Id == rest);
            var width = wall.Length / count;
            var cut = new Point2(current.Start.X + (current.End.X - current.Start.X) / current.Length * width, current.Start.Y + (current.End.Y - current.Start.Y) / current.Length * width);
            rest = SplitWall(rest, cut);
            ids.Add(rest);
        }
        CreateElementGroup(ids, UniqueGroupName($"{settings.Prefix}-{wallId.ToString("N")[..4]}", ElementGroupKind.Assembly), ElementGroupKind.Assembly);
        group.Assimilate();
        return ids;
    }

    /// <summary>Reinforces precast panels: two long bars in each, one near each face, at the middle of its height. Returns the bars.</summary>
    public IReadOnlyList<Guid> AddPanelReinforcement(IReadOnlyCollection<Guid> wallIds)
    {
        ArgumentNullException.ThrowIfNull(wallIds);
        var walls = Document.Walls.Where(item => wallIds.Contains(item.Id)).ToList();
        if (walls.Count == 0) throw new InvalidDataException("The wall does not exist.");
        var diameter = Document.Precast.PanelBarDiameter;
        var ids = new List<Guid>();
        var group = Guid.NewGuid();
        Commit("Transaction.AddPanelReinforcement", document =>
        {
            foreach (var wall in walls)
            {
                var cover = CoverOf(wall.Id);
                var inset = wall.Thickness / 2 - cover - diameter / 2;
                if (inset < 0 || wall.Length < 2 * cover + 0.1) throw new InvalidDataException("The bars do not fit in the member.");
                var dx = (wall.End.X - wall.Start.X) / wall.Length;
                var dy = (wall.End.Y - wall.Start.Y) / wall.Length;
                foreach (var side in new[] { -1, 1 })
                {
                    Point2 At(double along) => new(wall.Start.X + dx * along - dy * inset * side, wall.Start.Y + dy * along + dx * inset * side);
                    var bar = NewBar(wall.LevelId, At(cover), At(wall.Length - cover), diameter, wall.Height / 2, group);
                    ids.Add(bar.Id);
                    document.StructuralElements.Add(bar);
                }
            }
        });
        return ids;
    }
}
