using System.Globalization;
using System.Numerics;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core.Import.Revit;

/// <summary>What a conversion produced, element by element, and what it had to keep as plain geometry.</summary>
public sealed class ExchangeReport
{
    public int Levels { get; set; }
    public int Walls { get; set; }
    public int WallTypes { get; set; }
    public int Doors { get; set; }
    public int Windows { get; set; }
    public int OpeningTypes { get; set; }
    public int Floors { get; set; }
    public int Ceilings { get; set; }
    public int Roofs { get; set; }
    public int SlabOpenings { get; set; }
    public int Rooms { get; set; }
    public int Components { get; set; }
    public int Families { get; set; }
    public int Triangles { get; set; }
    /// <summary>Elements kept as mesh families because their shape has no native equivalent, by reason.</summary>
    public Dictionary<string, int> MeshFallbacks { get; } = new(StringComparer.Ordinal);
    /// <summary>Elements left out, by reason.</summary>
    public Dictionary<string, int> Skipped { get; } = new(StringComparer.Ordinal);

    internal void Fallback(string reason) => MeshFallbacks[reason] = MeshFallbacks.GetValueOrDefault(reason) + 1;
    internal void Skip(string reason) => Skipped[reason] = Skipped.GetValueOrDefault(reason) + 1;
}

/// <summary>A converted project and its report.</summary>
public sealed record ExchangeConversion(BuildingDocument Document, ExchangeReport Report);

/// <summary>
/// Turns an exchange file into an editable Guanacaste project. Straight walls, wall-hosted doors and windows, flat
/// floors, ceilings and roofs, and rectangular rooms become native elements that the user edits like any other;
/// every other element (furniture, fixtures, stairs, railings, sloped roofs, curved walls...) becomes an instance of
/// a mesh component family (.g3df), one family per distinct Revit geometry, so repeated furniture shares one family.
/// A wall whose openings cannot be hosted natively stays together with them as Revit drew it.
/// </summary>
public static class ExchangeConverter
{
    /// <summary>Families kept below the project limit, leaving room for the user's own.</summary>
    private const int FamilyBudget = ComponentFamilyRules.MaximumFamilies - 40;

    public static ExchangeConversion Convert(ExchangeFile file, string? projectName = null) =>
        new Builder(file, projectName).Build();

    private sealed class Builder(ExchangeFile file, string? projectName)
    {
        private readonly ExchangeReport _report = new();
        private BuildingDocument _document = null!;
        private Vector2 _shift;
        private readonly Dictionary<long, Level> _levels = [];
        private readonly Dictionary<long, (Wall Wall, double BaseZ)> _walls = [];
        private readonly HashSet<long> _meshWalls = [];
        private readonly List<ExchangeElement> _components = [];
        private readonly Dictionary<int, FamilyDocument> _families = [];
        private readonly HashSet<string> _familyNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, OpeningType> _openingTypes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, WallType> _wallTypes = new(StringComparer.Ordinal);

        public ExchangeConversion Build()
        {
            var name = Trim(projectName ?? (string.IsNullOrWhiteSpace(file.ProjectName) ? "Proyecto Revit" : file.ProjectName), 120);
            _document = BuildingDocument.Create(name);
            _shift = CenterShift();
            BuildLevels();
            var byHost = file.Elements.Where(element => element.Opening is not null && element.Kind is "door" or "window")
                .GroupBy(element => element.Opening!.Host).ToDictionary(group => group.Key, group => group.ToList());
            foreach (var element in file.Elements)
            {
                switch (element.Kind)
                {
                    case "wall":
                        AddWall(element, byHost.GetValueOrDefault(element.Id) ?? []);
                        break;
                    case "door" or "window" when element.Opening is not null:
                        // Placed with its host wall, natively or as part of the wall's own geometry.
                        break;
                    case "floor" or "ceiling" or "roof":
                        AddSlab(element);
                        break;
                    case "room":
                        AddRoom(element);
                        break;
                    default:
                        Component(element, null);
                        break;
                }
            }
            // Openings whose host wall did not exist in the file at all.
            foreach (var (host, openings) in byHost)
                if (!_walls.ContainsKey(host) && !_meshWalls.Contains(host) && file.Elements.All(element => element.Id != host))
                    foreach (var opening in openings) Component(opening, "Opening without wall");
            AddComponents();
            _report.Levels = _document.Levels.Count;
            _report.WallTypes = _wallTypes.Count;
            _report.OpeningTypes = _openingTypes.Count;
            _report.Families = _document.ComponentFamilies.Count;
            _document.Validate();
            return new ExchangeConversion(_document, _report);
        }

        /// <summary>Moves the model's plan center near the origin, in whole meters, so coordinates stay precise.</summary>
        private Vector2 CenterShift()
        {
            var points = new List<Vector2>();
            foreach (var element in file.Elements)
            {
                if (element.Wall is { Start: { } start, End: { } end })
                {
                    points.Add(new((float)start[0], (float)start[1]));
                    points.Add(new((float)end[0], (float)end[1]));
                }
                if (element.Slab?.Loops is { } loops)
                    points.AddRange(loops.SelectMany(loop => loop).Select(point => new Vector2((float)point[0], (float)point[1])));
            }
            if (points.Count == 0)
                points.AddRange(file.Elements.Where(element => element.Origin is { Length: 3 })
                    .Select(element => new Vector2((float)element.Origin![0], (float)element.Origin[1])));
            if (points.Count == 0) return Vector2.Zero;
            var min = points.Aggregate(Vector2.Min);
            var max = points.Aggregate(Vector2.Max);
            var center = (min + max) / 2;
            return new(MathF.Round(center.X), MathF.Round(center.Y));
        }

        private Point2 Plan(double[] point) => new(point[0] - _shift.X, point[1] - _shift.Y);

        private void BuildLevels()
        {
            var levels = new List<Level>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in file.Levels.OrderBy(level => level.Elevation).Take(200))
            {
                var name = Trim(string.IsNullOrWhiteSpace(source.Name) ? "Nivel" : source.Name, 100);
                for (var suffix = 2; !names.Add(name); suffix++) name = Trim(source.Name, 94) + $" ({suffix})";
                var level = new Level(Guid.NewGuid(), name, Math.Round(source.Elevation, 4));
                levels.Add(level);
                _levels[source.Id] = level;
            }
            if (levels.Count == 0) levels.Add(new Level(Guid.NewGuid(), "Nivel 1", 0));
            // The ground floor is the level most walls stand on, whatever its name; footing and beam levels are not.
            var ground = file.Elements.Where(element => element.Wall?.BaseLevel is { } id && _levels.ContainsKey(id))
                .GroupBy(element => element.Wall!.BaseLevel!.Value).MaxBy(group => group.Count())?.Key;
            if (ground is { } groundId)
            {
                var index = levels.FindIndex(level => level.Id == _levels[groundId].Id);
                levels[index] = levels[index] with { Role = LevelRole.BaseFloor };
            }
            _document.Levels = levels;
            _document.Views = BuiltInProjectViews.CreateDefaults(levels);
            BaseLevels.Ensure(_document);
            // Ensure may rewrite the records with their roles; keep the map pointing at the stored ones.
            foreach (var key in _levels.Keys.ToList())
                _levels[key] = _document.Levels.First(level => level.Id == _levels[key].Id);
        }

        /// <summary>The element's own level when known, otherwise the highest level at or below <paramref name="z"/>.</summary>
        private Level LevelFor(long? id, double z)
        {
            if (id is { } key && _levels.TryGetValue(key, out var level)) return level;
            return _document.Levels.Where(candidate => candidate.Elevation <= z + 0.01).MaxBy(candidate => candidate.Elevation)
                   ?? _document.Levels.MinBy(candidate => candidate.Elevation)!;
        }

        // ---------------------------------------------------------------- walls and openings

        private void AddWall(ExchangeElement element, List<ExchangeElement> openings)
        {
            var data = element.Wall!;
            var reason = WallFallbackReason(data);
            if (reason is null)
            {
                var baseZ = data.BaseZ ?? data.BottomZ ?? 0;
                // Revit walls often start a few centimeters off a level (a base offset to a slab top); within 5 cm the
                // nearest level hosts the wall and the height absorbs the difference.
                // A wall sunk a little below its own level (into the slab or the ground) stays on that level: only its
                // buried part is lost, and it remains in that level's plan.
                var level = data.BaseLevel is { } baseId && _levels.TryGetValue(baseId, out var own) &&
                            baseZ - own.Elevation is >= -0.15 and <= 0.05
                    ? own
                    : _document.Levels.Where(candidate => Math.Abs(candidate.Elevation - baseZ) < 0.05)
                        .MinBy(candidate => Math.Abs(candidate.Elevation - baseZ));
                if (level is null) reason = "Wall base between levels";
                else
                {
                    var top = data.TopZ ?? baseZ + data.Height;
                    // A wall attached to a roof or with an edited profile keeps a native rectangular body up to its
                    // lowest top; the gable or notch above it keeps Revit's geometry as a family of its own.
                    TriSoup? above = null;
                    if (data.TopAttached || data.ProfileEdited)
                    {
                        if (SplitWallTop(element, baseZ) is { } split) (top, above) = split;
                        else reason = data.ProfileEdited ? "Wall with edited profile" : "Wall attached to a roof or floor";
                    }
                    if (reason is null) reason = PlaceWall(element, openings, level, top, above);
                    if (reason is null) return;
                }
            }
            _meshWalls.Add(element.Id);
            Component(element, reason);
            foreach (var opening in openings) Component(opening, "Opening in a modeled wall");
        }

        /// <summary>Adds the native wall with its openings; returns why it could not be placed, or null.</summary>
        private string? PlaceWall(ExchangeElement element, List<ExchangeElement> openings, Level level, double top, TriSoup? above)
        {
            var data = element.Wall!;
            var wall = new Wall(Guid.NewGuid(), level.Id, Plan(data.Start!), Plan(data.End!), Math.Round(data.Width, 4),
                Math.Round(top - level.Elevation, 4))
            {
                WallTypeId = WallTypeFor(element).Id
            };
            if (above is null && data.TopLevel is { } topId && _levels.TryGetValue(topId, out var topLevel) &&
                Math.Abs(topLevel.Elevation + data.TopOffset - top) < 0.02 && topLevel.Elevation > level.Elevation)
            {
                // The height must equal what the constraint implies, to the micrometer.
                var offset = Math.Round(data.TopOffset, 4);
                wall = wall with { TopLevelId = topLevel.Id, TopOffset = offset, Height = topLevel.Elevation + offset - level.Elevation };
            }
            if (wall.Length < 0.05 || wall.Height is < 0.1 or > 1000) return "Wall too short";
            var hosted = HostOpenings(wall, level.Elevation, openings);
            if (hosted is null) return "Wall with openings that do not fit";
            _document.Walls.Add(wall);
            _document.Openings.AddRange(hosted);
            _walls[element.Id] = (wall, level.Elevation);
            _report.Walls++;
            _report.Doors += hosted.Count(opening => opening.Kind == OpeningKind.Door);
            _report.Windows += hosted.Count(opening => opening.Kind == OpeningKind.Window);
            if (above is not null) AddSoup(element, above, "Remate de muro", SurfaceKind.Wall);
            return null;
        }

        /// <summary>
        /// The lowest top of a wall along its length and its geometry above that height. The top is sampled every
        /// 5 cm from end to end as the highest point of the wall's triangles there, so window sills and door heads
        /// below the top do not count.
        /// </summary>
        private (double Top, TriSoup Above)? SplitWallTop(ExchangeElement element, double baseZ)
        {
            var data = element.Wall!;
            if (World(element) is not { } soup) return null;
            var start = new Vector2((float)data.Start![0], (float)data.Start[1]);
            var axis = new Vector2((float)data.End![0], (float)data.End[1]) - start;
            var length = axis.Length();
            if (length < 0.3f) return null;
            axis /= length;
            var lowest = double.MaxValue;
            var step = (length - 0.01f) / MathF.Max(1, MathF.Ceiling((length - 0.01f) / 0.05f));
            for (var t = 0.005f; t <= length - 0.005f + 1e-4f; t += step)
            {
                var highest = double.MinValue;
                for (var index = 0; index + 2 < soup.Points.Count; index += 3)
                    for (var edge = 0; edge < 3; edge++)
                    {
                        var (a, b) = (soup.Points[index + edge], soup.Points[index + (edge + 1) % 3]);
                        var ta = Vector2.Dot(new Vector2(a.X, a.Y) - start, axis);
                        var tb = Vector2.Dot(new Vector2(b.X, b.Y) - start, axis);
                        if (t < Math.Min(ta, tb) || t > Math.Max(ta, tb)) continue;
                        var z = Math.Abs(tb - ta) < 1e-6f ? Math.Max(a.Z, b.Z) : a.Z + (b.Z - a.Z) * (t - ta) / (tb - ta);
                        highest = Math.Max(highest, z);
                    }
                if (highest > double.MinValue) lowest = Math.Min(lowest, highest);
            }
            if (lowest == double.MaxValue || lowest - baseZ < 0.5) return null;
            lowest = Math.Round(lowest, 4);
            return (lowest, MeshClipper.Clip(soup, Vector3.UnitZ, (float)lowest, keepPositive: true));
        }

        /// <summary>An element's triangles in model coordinates, before the plan shift.</summary>
        private TriSoup? World(ExchangeElement element)
        {
            if (element.Mesh is not { } index || index >= file.Meshes.Count || element.Origin is not { Length: 3 } origin) return null;
            var mesh = file.Meshes[index].Decode();
            var (cos, sin) = ((float)Math.Cos(element.Rotation), (float)Math.Sin(element.Rotation));
            var offset = new Vector3((float)origin[0], (float)origin[1], (float)origin[2]);
            Vector3 Place(Vector3 p) => offset + new Vector3(p.X * cos - p.Y * sin, p.X * sin + p.Y * cos, p.Z);
            var soup = new TriSoup();
            for (var i = 0; i + 2 < mesh.Indices.Length; i += 3)
                soup.Add(Place(mesh.Positions[mesh.Indices[i]]), Place(mesh.Positions[mesh.Indices[i + 1]]),
                    Place(mesh.Positions[mesh.Indices[i + 2]]));
            return soup;
        }

        /// <summary>Places model-space triangles of one surface as a family of their own, standing where they are.</summary>
        private void AddSoup(ExchangeElement element, TriSoup soup, string name, SurfaceKind kind)
        {
            if (soup.TriangleCount == 0) return;
            var (min, max) = soup.Bounds();
            var size = max - min;
            if (size.X <= 0.0015f && size.Y <= 0.0015f || size.Z <= 0.0015f) return;
            var center = new Vector3((min.X + max.X) / 2, (min.Y + max.Y) / 2, min.Z);
            var positions = soup.Points.Select(point => point - center).ToArray();
            var family = new FamilyDocument
            {
                Name = UniqueFamilyName($"{name} - {(string.IsNullOrWhiteSpace(element.Type) ? "Muro" : element.Type)}"),
                Category = FamilyCategory.FacadeComponents,
                Types = [new FamilyTypeDefinition(Guid.NewGuid(), "Estándar", [])],
                Mesh = FamilyMesh.Create(positions, Enumerable.Range(0, positions.Length).ToArray(),
                    Enumerable.Repeat(kind, soup.TriangleCount).ToArray()),
                Metadata = new Dictionary<string, string>
                {
                    ["Source"] = "Revit", ["RevitCategory"] = element.Category, ["RevitType"] = Trim(element.Type, 4000)
                }
            };
            _document.ComponentFamilies.Add(family);
            _report.Triangles += soup.TriangleCount;
            AddInstance(element, family, new Point2(center.X - _shift.X, center.Y - _shift.Y), min.Z, 0, size, family.Name);
        }

        private static string? WallFallbackReason(ExchangeWall data)
        {
            if (data.WallKind != "Basic") return data.WallKind == "Curtain" ? "Curtain wall" : "Stacked wall";
            if (data.Curved) return "Curved wall";
            if (data.Start is null || data.End is null) return "Wall without location";
            if (data.BaseAttached) return "Wall attached to a roof or floor";
            if (data.Width is < 0.05 or > 5) return "Wall thickness out of range";
            return null;
        }

        private WallType WallTypeFor(ExchangeElement element)
        {
            var data = element.Wall!;
            var key = element.Type + "|" + data.Width.ToString("0.####", CultureInfo.InvariantCulture);
            if (_wallTypes.TryGetValue(key, out var existing)) return existing;
            var name = Trim(string.IsNullOrWhiteSpace(element.Type) ? "Muro Revit" : element.Type, 110);
            if (_document.WallTypes.Any(type => type.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                name = Trim(name, 100) + $" {data.Width * 1000:0} mm";
            var material = data.Layers.FirstOrDefault(layer => layer.Function == "Structure" && layer.Material.Length > 0)?.Material
                           ?? data.Layers.FirstOrDefault(layer => layer.Material.Length > 0)?.Material ?? "Revit";
            var function = data.Function switch
            {
                "Exterior" => WallFunction.Exterior,
                "Foundation" => WallFunction.Foundation,
                "Retaining" => WallFunction.Retaining,
                _ => WallFunction.Interior
            };
            var type = new WallType(Guid.NewGuid(), name, function, Math.Round(data.Width, 4), Trim(material, 120))
            {
                Construction = ExchangeMaterials.ConstructionFor(data)
            };
            _document.WallTypes.Add(type);
            return _wallTypes[key] = type;
        }

        /// <summary>
        /// The wall's doors and windows as native openings, or null when one of them cannot be hosted (it overlaps
        /// another, runs past the wall end or above its top): then the wall keeps Revit's own cut geometry.
        /// </summary>
        private List<WallOpening>? HostOpenings(Wall wall, double baseZ, List<ExchangeElement> openings)
        {
            var direction = new Vector2((float)(wall.End.X - wall.Start.X), (float)(wall.End.Y - wall.Start.Y));
            var length = wall.Length;
            direction /= (float)length;
            var left = new Vector2(-direction.Y, direction.X);
            var result = new List<WallOpening>();
            foreach (var element in openings)
            {
                var data = element.Opening!;
                var kind = element.Kind == "door" ? OpeningKind.Door : OpeningKind.Window;
                var point = Plan(data.Point);
                var offset = (point.X - wall.Start.X) * direction.X + (point.Y - wall.Start.Y) * direction.Y;
                var (meshWidth, meshHeight) = MeshOpeningSize(element);
                var width = Math.Round(data.Width ?? data.RoughWidth ?? meshWidth, 4);
                var height = Math.Round(data.Height ?? data.RoughHeight ?? meshHeight, 4);
                var sill = kind == OpeningKind.Door ? 0 : Math.Round(Math.Max(0, (data.SillZ ?? data.Point[2]) - baseZ), 4);
                // Trim small overruns at the wall ends and top, which Revit tolerates and Guanacaste does not.
                var overrun = Math.Max(0.05 - (offset - width / 2), (offset + width / 2) - (length - 0.05));
                if (overrun > 0 && overrun < 0.12) width -= 2 * overrun;
                var over = sill + height - (wall.Height - 0.05);
                if (over > 0 && over < 0.12) height -= over;
                if (width < 0.3 || height < 0.3 || offset - width / 2 < 0.05 - 1e-6 || offset + width / 2 > length - 0.05 + 1e-6 ||
                    sill + height > wall.Height - 0.05 + 1e-6)
                    return null;
                var type = OpeningTypeFor(element, kind, width, height, sill);
                var facing = data.Facing is { Length: 2 } f ? new Vector2((float)f[0], (float)f[1]) : left;
                var hand = data.Hand is { Length: 2 } h ? new Vector2((float)h[0], (float)h[1]) : direction;
                result.Add(new WallOpening(Guid.NewGuid(), wall.Id, kind, Math.Round(offset, 4), type.Width, sill, type.Height)
                {
                    OpeningTypeId = type.Id,
                    FlipFacing = Vector2.Dot(facing, left) < 0,
                    FlipHand = Vector2.Dot(hand, direction) < 0
                });
            }
            var sorted = result.OrderBy(opening => opening.Begin).ToList();
            for (var index = 1; index < sorted.Count; index++)
                if (sorted[index - 1].End + 0.05 > sorted[index].Begin) return null;
            return result;
        }

        /// <summary>Width and height of an opening's own geometry, for families without size parameters.</summary>
        private (double Width, double Height) MeshOpeningSize(ExchangeElement element)
        {
            if (element.Mesh is not { } index || index >= file.Meshes.Count) return (0.9, 2.1);
            var positions = file.Meshes[index].Decode().Positions;
            if (positions.Length == 0) return (0.9, 2.1);
            var min = positions.Aggregate(Vector3.Min);
            var max = positions.Aggregate(Vector3.Max);
            return (max.X - min.X, max.Z - min.Z);
        }

        private OpeningType OpeningTypeFor(ExchangeElement element, OpeningKind kind, double width, double height, double sill)
        {
            var family = Trim(string.IsNullOrWhiteSpace(element.Family) ? (kind == OpeningKind.Door ? "Puerta Revit" : "Ventana Revit") : element.Family, 120);
            var typeName = Trim(string.IsNullOrWhiteSpace(element.Type) ? "Estándar" : element.Type, 100);
            var key = string.Create(CultureInfo.InvariantCulture, $"{kind}|{family}|{typeName}|{width:0.####}|{height:0.####}");
            if (_openingTypes.TryGetValue(key, out var existing)) return existing;
            var name = typeName;
            if (_document.OpeningTypes.Any(type => type.FamilyName.Equals(family, StringComparison.OrdinalIgnoreCase) &&
                                                   type.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                name = string.Create(CultureInfo.InvariantCulture, $"{typeName} {width * 1000:0} × {height * 1000:0} mm");
            for (var suffix = 2; _document.OpeningTypes.Any(type => type.FamilyName.Equals(family, StringComparison.OrdinalIgnoreCase) &&
                                                                    type.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); suffix++)
                name = $"{typeName} ({suffix})";
            var text = (family + " " + typeName).ToLowerInvariant();
            var operation = kind == OpeningKind.Door
                ? Has(text, "corrediza", "sliding", "corredera") ? OpeningOperation.Sliding
                : Has(text, "plegable", "folding", "acordeón", "acordeon") ? OpeningOperation.Folding
                : Has(text, "garage", "garaje", "cochera", "seccional", "enrollable", "overhead") ? OpeningOperation.Overhead
                : Has(text, "pocket", "empotrada", "embutida") ? OpeningOperation.Pocket
                : OpeningOperation.Standard
                : Has(text, "corrediza", "sliding", "corredera") ? OpeningOperation.Sliding
                : Has(text, "celosía", "celosia", "louver", "paleta", "persiana") ? OpeningOperation.Louvered
                : Has(text, "guillotina", "hung") ? OpeningOperation.Hung
                : Has(text, "proyectante", "awning") ? OpeningOperation.Awning
                : Has(text, "abatible", "casement", "batiente") ? OpeningOperation.Casement
                : OpeningOperation.Standard;
            var type = new OpeningType(Guid.NewGuid(), kind, family, name, width, height, kind == OpeningKind.Door ? 0 : sill)
            {
                Depth = kind == OpeningKind.Door ? 0.04 : 0,
                Operation = operation,
                GlazedLeaf = kind == OpeningKind.Door && Has(text, "vidrio", "glass", "cristal", "vidriada"),
                LeafStyle = kind == OpeningKind.Door && Has(text, "metal", "seguridad", "security", "hierro") ? DoorLeafStyle.Security
                    : kind == OpeningKind.Door && Has(text, "lisa", "flush", "plywood", "tambor") ? DoorLeafStyle.Flush
                    : DoorLeafStyle.Panelled,
                GlazingLightCount = kind == OpeningKind.Window && operation == OpeningOperation.Sliding && width / 2 >= 0.18 ? 2 : 0,
                MaterialName = "Revit"
            };
            _document.OpeningTypes.Add(type);
            return _openingTypes[key] = type;
        }

        // ---------------------------------------------------------------- floors, ceilings, roofs and rooms

        private void AddSlab(ExchangeElement element)
        {
            var data = element.Slab;
            var kind = element.Kind switch { "ceiling" => FloorKind.Ceiling, "roof" => FloorKind.Roof, _ => FloorKind.Floor };
            if (data is not { Flat: true, Loops.Count: > 0, Z: { } z })
            {
                Component(element, kind == FloorKind.Roof ? "Sloped roof" : "Sloped or shaped slab");
                return;
            }
            var loops = data.Loops.Select(loop => Simplify(loop.Select(Plan).ToList())).Where(loop => loop.Count >= 3).ToList();
            if (loops.Count == 0)
            {
                Component(element, "Slab without outline");
                return;
            }
            // Outlines inside another one are its holes; the others are separate pieces of the same element.
            var outers = loops.Where(loop => !loops.Any(other => !ReferenceEquals(other, loop) &&
                Math.Abs(SignedArea(other)) > Math.Abs(SignedArea(loop)) && Inside(loop[0], other))).ToList();
            var pieces = outers.Select(outer => (Outer: outer, Holes: loops.Where(loop => !outers.Contains(loop) && Inside(loop[0], outer) &&
                // Holes smaller than this are cut-outs for lights and pipes; the fixture covers them.
                Math.Abs(SignedArea(loop)) >= 0.05).ToList())).ToList();
            if (pieces.Any(piece => piece.Outer.Count > 500 || piece.Holes.Any(hole => AxisRectangle(hole) is null)))
            {
                Component(element, pieces.Any(piece => piece.Holes.Count > 0) ? "Slab with shaped openings" : "Slab outline too complex");
                return;
            }
            var thickness = Math.Clamp(data.Thickness ?? (kind == FloorKind.Ceiling ? 0.03 : 0.15), 0.03, 3);
            var top = kind == FloorKind.Ceiling ? z + thickness : z;
            var level = LevelFor(element.Level, z - 0.01);
            var offset = Math.Round(top - level.Elevation, 4);
            if (offset is < -10 or > 1000)
            {
                level = LevelFor(null, top);
                offset = Math.Round(top - level.Elevation, 4);
            }
            var name = Trim(string.IsNullOrWhiteSpace(element.Type) ? element.CategoryName : element.Type, 120);
            foreach (var (outer, holes) in pieces.Where(piece => Math.Abs(SignedArea(piece.Outer)) >= 0.1))
            {
                if (SignedArea(outer) < 0) outer.Reverse();
                _document.Floors.Add(new FloorSlab(Guid.NewGuid(), level.Id, string.IsNullOrWhiteSpace(name) ? "Losa" : name, outer,
                    Math.Round(thickness, 4))
                {
                    Kind = kind,
                    ElevationOffset = offset
                });
                foreach (var (low, high) in holes.Select(hole => AxisRectangle(hole)!.Value))
                {
                    _document.StructuralElements.Add(new StructuralElement(Guid.NewGuid(), level.Id, StructuralElementKind.Opening,
                        "Hueco de losa", low, high, 0.1, Math.Max(0.1, thickness), offset - thickness));
                    _report.SlabOpenings++;
                }
                switch (kind)
                {
                    case FloorKind.Ceiling: _report.Ceilings++; break;
                    case FloorKind.Roof: _report.Roofs++; break;
                    default: _report.Floors++; break;
                }
            }
        }

        /// <summary>Whether a point lies inside a polygon (even-odd rule).</summary>
        private static bool Inside(Point2 point, IReadOnlyList<Point2> polygon)
        {
            var inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
                if ((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y) &&
                    point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X)
                    inside = !inside;
            return inside;
        }

        private void AddRoom(ExchangeElement element)
        {
            if (element.Loops is not { Count: 1 } loops)
            {
                _report.Skip("Room with several boundaries");
                return;
            }
            var outline = Simplify(loops[0].Select(Plan).ToList());
            if (AxisRectangle(outline) is not var (low, high))
            {
                _report.Skip("Room that is not rectangular");
                return;
            }
            var number = Trim(string.IsNullOrWhiteSpace(element.Number) ? (_document.Rooms.Count + 1).ToString(CultureInfo.InvariantCulture) : element.Number, 32);
            for (var suffix = 2; _document.Rooms.Any(room => room.Number.Equals(number, StringComparison.OrdinalIgnoreCase)); suffix++)
                number = Trim(element.Number ?? "", 28) + "-" + suffix;
            var level = LevelFor(element.Level, 0);
            var name = Trim(string.IsNullOrWhiteSpace(element.Name) ? "Habitación" : element.Name, 120);
            if ((high.X - low.X) * (high.Y - low.Y) < 0.1) return;
            _document.Rooms.Add(new Room(Guid.NewGuid(), level.Id, number, name, low, high));
            _report.Rooms++;
        }

        // ---------------------------------------------------------------- mesh component families

        private void Component(ExchangeElement element, string? fallbackReason)
        {
            if (element.Mesh is not { } index || index < 0 || index >= file.Meshes.Count || element.Origin is not { Length: 3 })
            {
                _report.Skip("Element without geometry");
                return;
            }
            if (fallbackReason is not null) _report.Fallback(fallbackReason);
            _components.Add(element);
        }

        private void AddComponents()
        {
            var meshes = _components.GroupBy(element => element.Mesh!.Value).ToList();
            // Keep the families within the project limit: the meshes used the fewest times are merged per category and level.
            var merged = new Dictionary<(string, long?), List<ExchangeElement>>();
            if (meshes.Count > FamilyBudget)
            {
                var keep = meshes.OrderByDescending(group => group.Count()).Take(FamilyBudget - 60).Select(group => group.Key).ToHashSet();
                foreach (var group in meshes.Where(group => !keep.Contains(group.Key)))
                    foreach (var element in group)
                    {
                        var key = (element.Category, element.Level);
                        if (!merged.TryGetValue(key, out var list)) merged[key] = list = [];
                        list.Add(element);
                    }
                meshes = meshes.Where(group => keep.Contains(group.Key)).ToList();
            }
            foreach (var group in meshes)
                foreach (var element in group) PlaceShared(element);
            foreach (var ((category, _), elements) in merged) PlaceMerged(category, elements);
        }

        /// <summary>Places an instance of the family made from the element's mesh, creating the family on first use.</summary>
        private void PlaceShared(ExchangeElement element)
        {
            var mesh = file.Meshes[element.Mesh!.Value].Decode();
            var (min, max) = Bounds(mesh.Positions);
            if (!_families.TryGetValue(element.Mesh.Value, out var family))
            {
                var name = FamilyName(element);
                family = CreateFamily(name, element.Category, mesh.Positions, mesh.Indices, mesh.Materials, element.Category,
                    new Vector3((min.X + max.X) / 2, (min.Y + max.Y) / 2, min.Z), element);
                if (family is null)
                {
                    _report.Skip("Flat or empty geometry");
                    return;
                }
                _families[element.Mesh.Value] = family;
                _document.ComponentFamilies.Add(family);
            }
            var origin = element.Origin!;
            var (cos, sin) = (Math.Cos(element.Rotation), Math.Sin(element.Rotation));
            var (cx, cy) = ((min.X + max.X) / 2.0, (min.Y + max.Y) / 2.0);
            var at = new Point2(origin[0] - _shift.X + cx * cos - cy * sin, origin[1] - _shift.Y + cx * sin + cy * cos);
            AddInstance(element, family, at, origin[2] + min.Z, element.Rotation, max - min);
        }

        /// <summary>Joins several single-use meshes of one category and level into one family placed where they stand.</summary>
        private void PlaceMerged(string category, List<ExchangeElement> elements)
        {
            var positions = new List<Vector3>();
            var indices = new List<int>();
            var materials = new List<int>();
            foreach (var element in elements)
            {
                var mesh = file.Meshes[element.Mesh!.Value].Decode();
                var origin = element.Origin!;
                var (cos, sin) = ((float)Math.Cos(element.Rotation), (float)Math.Sin(element.Rotation));
                var start = positions.Count;
                foreach (var p in mesh.Positions)
                    positions.Add(new Vector3((float)origin[0] + p.X * cos - p.Y * sin, (float)origin[1] + p.X * sin + p.Y * cos,
                        (float)origin[2] + p.Z));
                indices.AddRange(mesh.Indices.Select(index => index + start));
                materials.AddRange(mesh.Materials);
            }
            var (min, max) = Bounds(positions);
            var first = elements[0];
            var level = LevelFor(first.Level, min.Z);
            var name = Trim($"{(string.IsNullOrWhiteSpace(first.CategoryName) ? category : first.CategoryName)} - {level.Name}", 150);
            var family = CreateFamily(name, category, [.. positions], [.. indices], [.. materials], category,
                new Vector3((min.X + max.X) / 2, (min.Y + max.Y) / 2, min.Z), first);
            if (family is null) return;
            _document.ComponentFamilies.Add(family);
            var at = new Point2((min.X + max.X) / 2.0 - _shift.X, (min.Y + max.Y) / 2.0 - _shift.Y);
            AddInstance(first, family, at, min.Z, 0, max - min, name);
        }

        private void AddInstance(ExchangeElement element, FamilyDocument family, Point2 at, double baseZ, double rotation,
            Vector3 size, string? name = null)
        {
            if (size.X > 50 || size.Y > 50 || size.Z > 100)
            {
                _report.Skip("Element larger than 50 m");
                return;
            }
            var level = LevelFor(element.Level, baseZ);
            var offset = baseZ - level.Elevation;
            if (offset is < -100 or > 1000)
            {
                level = LevelFor(null, baseZ);
                offset = baseZ - level.Elevation;
            }
            var turn = rotation % (2 * Math.PI);
            if (turn < 0) turn += 2 * Math.PI;
            _document.StructuralElements.Add(new StructuralElement(Guid.NewGuid(), level.Id, StructuralElementKind.Component,
                Trim(name ?? InstanceName(element), 120), at, at, Math.Max(0.005, size.X), Math.Max(0.001, size.Z), Math.Round(offset, 4))
            {
                Depth = Math.Max(0.005, size.Y),
                Rotation = turn >= 2 * Math.PI - 1e-9 ? 0 : turn,
                FamilyId = family.Id
            });
            _report.Components++;
        }

        private FamilyDocument? CreateFamily(string name, string category, IReadOnlyList<Vector3> positions, int[] indices,
            int[] materials, string surfaceCategory, Vector3 baseCenter, ExchangeElement source)
        {
            var kinds = new SurfaceKind[materials.Length];
            for (var triangle = 0; triangle < materials.Length; triangle++)
                kinds[triangle] = ExchangeMaterials.SurfaceFor(
                    materials[triangle] >= 0 && materials[triangle] < file.Materials.Count ? file.Materials[materials[triangle]] : null,
                    surfaceCategory);
            var moved = positions.Select(position => position - baseCenter).ToArray();
            var (min, max) = Bounds(moved);
            if (moved.Length == 0 || max.X - min.X <= 0.0015f || max.Y - min.Y <= 0.0015f || max.Z - min.Z <= 0.0015f) return null;
            // Shrink to the family triangle limit by dropping the smallest triangles of very dense geometry.
            if (kinds.Length > FamilyMesh.MaximumTriangles) return null;
            var family = new FamilyDocument
            {
                Name = UniqueFamilyName(name),
                Category = CategoryFor(category),
                Types = [new FamilyTypeDefinition(Guid.NewGuid(), Trim(string.IsNullOrWhiteSpace(source.Type) ? "Estándar" : source.Type, 120), [])],
                Mesh = FamilyMesh.Create(moved, indices, kinds, SmoothNormals(moved, indices)),
                Metadata = new Dictionary<string, string>
                {
                    ["Source"] = "Revit",
                    ["RevitCategory"] = Trim(category, 4000),
                    ["RevitFamily"] = Trim(source.Family, 4000),
                    ["RevitType"] = Trim(source.Type, 4000)
                }
            };
            _report.Triangles += kinds.Length;
            return family;
        }

        private string FamilyName(ExchangeElement element)
        {
            if (!string.IsNullOrWhiteSpace(element.Family) && !string.IsNullOrWhiteSpace(element.Type))
                return element.Family == element.Type ? element.Family : $"{element.Family} - {element.Type}";
            if (!string.IsNullOrWhiteSpace(element.Type)) return $"{element.CategoryName} - {element.Type}";
            return string.IsNullOrWhiteSpace(element.CategoryName) ? "Elemento Revit" : element.CategoryName;
        }

        private static string InstanceName(ExchangeElement element) =>
            !string.IsNullOrWhiteSpace(element.Type) ? element.Type
            : !string.IsNullOrWhiteSpace(element.Name) ? element.Name
            : string.IsNullOrWhiteSpace(element.CategoryName) ? "Elemento Revit" : element.CategoryName;

        private string UniqueFamilyName(string name)
        {
            var candidate = Trim(name, 150);
            for (var suffix = 2; !_familyNames.Add(candidate); suffix++) candidate = Trim(name, 144) + $" ({suffix})";
            return candidate;
        }

        private static FamilyCategory CategoryFor(string category) => category switch
        {
            "OST_Doors" => FamilyCategory.Doors,
            "OST_Windows" => FamilyCategory.Windows,
            "OST_Furniture" or "OST_FurnitureSystems" => FamilyCategory.Furniture,
            "OST_Casework" => FamilyCategory.Casework,
            "OST_PlumbingFixtures" => FamilyCategory.PlumbingFixtures,
            "OST_LightingFixtures" or "OST_LightingDevices" => FamilyCategory.LightingFixtures,
            "OST_ElectricalEquipment" or "OST_ElectricalFixtures" => FamilyCategory.ElectricalEquipment,
            "OST_MechanicalEquipment" => FamilyCategory.MechanicalEquipment,
            "OST_StructuralColumns" or "OST_StructuralFraming" or "OST_StructuralFoundation" or "OST_Columns" => FamilyCategory.StructuralElements,
            "OST_Roofs" or "OST_Gutter" or "OST_Fascia" or "OST_RoofSoffit" => FamilyCategory.RoofComponents,
            "OST_Walls" or "OST_CurtainWallPanels" or "OST_CurtainWallMullions" or "OST_Cornices" => FamilyCategory.FacadeComponents,
            "OST_Planting" => FamilyCategory.Planting,
            "OST_Entourage" => FamilyCategory.Entourage,
            "OST_SpecialityEquipment" => FamilyCategory.DecorativeComponents,
            _ => FamilyCategory.GenericModels
        };

        // ---------------------------------------------------------------- geometry helpers

        private static (Vector3 Min, Vector3 Max) Bounds(IReadOnlyCollection<Vector3> positions) => positions.Count == 0
            ? (Vector3.Zero, Vector3.Zero)
            : (positions.Aggregate(Vector3.Min), positions.Aggregate(Vector3.Max));

        /// <summary>Area-weighted vertex normals; Revit faces do not share vertices, so edges between faces stay sharp.</summary>
        private static Vector3[] SmoothNormals(Vector3[] positions, int[] indices)
        {
            var normals = new Vector3[positions.Length];
            for (var index = 0; index + 2 < indices.Length; index += 3)
            {
                var (a, b, c) = (indices[index], indices[index + 1], indices[index + 2]);
                var normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                normals[a] += normal;
                normals[b] += normal;
                normals[c] += normal;
            }
            for (var index = 0; index < normals.Length; index++)
                normals[index] = normals[index].LengthSquared() > 1e-20f ? Vector3.Normalize(normals[index]) : Vector3.UnitZ;
            return normals;
        }

        /// <summary>Drops repeated and collinear points (within a millimeter) from a closed outline.</summary>
        private static List<Point2> Simplify(List<Point2> points)
        {
            var result = new List<Point2>();
            foreach (var point in points)
                if (result.Count == 0 || result[^1].DistanceTo(point) > 0.001) result.Add(point);
            if (result.Count > 1 && result[0].DistanceTo(result[^1]) <= 0.001) result.RemoveAt(result.Count - 1);
            var changed = true;
            while (changed && result.Count > 3)
            {
                changed = false;
                for (var index = 0; index < result.Count && result.Count > 3; index++)
                {
                    var (a, b, c) = (result[(index + result.Count - 1) % result.Count], result[index], result[(index + 1) % result.Count]);
                    var cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                    if (Math.Abs(cross) / Math.Max(1e-9, a.DistanceTo(c)) < 0.001)
                    {
                        result.RemoveAt(index);
                        changed = true;
                        index--;
                    }
                }
            }
            return result.Select(point => new Point2(Math.Round(point.X, 4), Math.Round(point.Y, 4))).ToList();
        }

        private static double SignedArea(IReadOnlyList<Point2> loop)
        {
            var area = 0.0;
            for (var index = 0; index < loop.Count; index++)
            {
                var (a, b) = (loop[index], loop[(index + 1) % loop.Count]);
                area += a.X * b.Y - b.X * a.Y;
            }
            return area / 2;
        }

        /// <summary>The corners of an axis-aligned rectangle, or null for any other outline.</summary>
        private static (Point2 Low, Point2 High)? AxisRectangle(List<Point2> loop)
        {
            if (loop.Count != 4) return null;
            for (var index = 0; index < 4; index++)
            {
                var (a, b) = (loop[index], loop[(index + 1) % 4]);
                if (Math.Abs(a.X - b.X) > 0.002 && Math.Abs(a.Y - b.Y) > 0.002) return null;
            }
            return (new Point2(loop.Min(p => p.X), loop.Min(p => p.Y)), new Point2(loop.Max(p => p.X), loop.Max(p => p.Y)));
        }

        private static bool Has(string text, params string[] words) => words.Any(text.Contains);

        private static string Trim(string text, int length)
        {
            text = (text ?? "").Trim();
            return text.Length <= length ? text : text[..length].Trim();
        }
    }
}
