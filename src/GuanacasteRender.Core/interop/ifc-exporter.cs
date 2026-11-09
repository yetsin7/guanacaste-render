using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core.Interop;

/// <summary>
/// Writes an IFC4 (ISO 16739-1) STEP physical file with the project's spatial structure,
/// walls with hosted doors and windows (voids and fills), slabs, and spaces as swept solids.
/// Output is deterministic: identical documents produce byte-identical files.
/// </summary>
public static class IfcExporter
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private const string Base64Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

    /// <summary>Serializes the validated document; <paramref name="timestamp"/> fixes the header time.</summary>
    public static string ToStep(BuildingDocument document, DateTimeOffset timestamp, string applicationVersion = "0.4")
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Validate();
        var writer = new StepWriter();

        // Units, contexts and shared placement primitives.
        var length = writer.Add("IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.)");
        var area = writer.Add("IFCSIUNIT(*,.AREAUNIT.,$,.SQUARE_METRE.)");
        var volume = writer.Add("IFCSIUNIT(*,.VOLUMEUNIT.,$,.CUBIC_METRE.)");
        var angle = writer.Add("IFCSIUNIT(*,.PLANEANGLEUNIT.,$,.RADIAN.)");
        var units = writer.Add($"IFCUNITASSIGNMENT(({Refs(length, area, volume, angle)}))");
        var origin = writer.Add("IFCCARTESIANPOINT((0.,0.,0.))");
        var zAxis = writer.Add("IFCDIRECTION((0.,0.,1.))");
        var xAxis = writer.Add("IFCDIRECTION((1.,0.,0.))");
        var worldPlacement = writer.Add($"IFCAXIS2PLACEMENT3D({Ref(origin)},{Ref(zAxis)},{Ref(xAxis)})");
        var trueNorth = writer.Add("IFCDIRECTION((0.,1.))");
        var context = writer.Add($"IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-05,{Ref(worldPlacement)},{Ref(trueNorth)})");
        var body = writer.Add($"IFCGEOMETRICREPRESENTATIONSUBCONTEXT('Body','Model',*,*,*,*,{Ref(context)},$,.MODEL_VIEW.,$)");
        var origin2D = writer.Add("IFCCARTESIANPOINT((0.,0.))");
        var xAxis2D = writer.Add("IFCDIRECTION((1.,0.))");

        var project = writer.Add($"IFCPROJECT({Id(document.Id)},$,{Text(document.Name)},$,$,$,$,({Ref(context)}),{Ref(units)})");

        // Spatial structure: site → building → storeys, each with a placement relative to its parent.
        var sitePlacement = writer.Add($"IFCLOCALPLACEMENT($,{Ref(worldPlacement)})");
        var site = writer.Add($"IFCSITE({Id(document.Id, "site")},$,'Site',$,$,{Ref(sitePlacement)},$,$,.ELEMENT.,$,$,$,$,$)");
        var buildingPlacement = writer.Add($"IFCLOCALPLACEMENT({Ref(sitePlacement)},{Ref(worldPlacement)})");
        var building = writer.Add($"IFCBUILDING({Id(document.Id, "building")},$,{Text(document.Name)},$,$,{Ref(buildingPlacement)},$,$,.ELEMENT.,$,$,$)");
        writer.Add($"IFCRELAGGREGATES({Id(document.Id, "project-site")},$,$,$,{Ref(project)},({Ref(site)}))");
        writer.Add($"IFCRELAGGREGATES({Id(document.Id, "site-building")},$,$,$,{Ref(site)},({Ref(building)}))");

        var storeys = new Dictionary<Guid, (int Entity, int Placement)>();
        foreach (var level in document.Levels.OrderBy(level => level.Elevation))
        {
            var point = writer.Add($"IFCCARTESIANPOINT((0.,0.,{Real(level.Elevation)}))");
            var axes = writer.Add($"IFCAXIS2PLACEMENT3D({Ref(point)},$,$)");
            var placement = writer.Add($"IFCLOCALPLACEMENT({Ref(buildingPlacement)},{Ref(axes)})");
            var storey = writer.Add($"IFCBUILDINGSTOREY({Id(level.Id)},$,{Text(level.Name)},$,$,{Ref(placement)},$,$,.ELEMENT.,{Real(level.Elevation)})");
            storeys[level.Id] = (storey, placement);
        }
        writer.Add($"IFCRELAGGREGATES({Id(document.Id, "building-storeys")},$,$,$,{Ref(building)},({Refs([.. storeys.Values.Select(s => s.Entity)])}))");

        var contained = storeys.Keys.ToDictionary(id => id, _ => new List<int>());
        var wallTypes = document.WallTypes.ToDictionary(type => type.Id);
        var openingTypes = document.OpeningTypes.ToDictionary(type => type.Id);
        var materials = new Dictionary<string, (int Material, List<int> Elements)>(StringComparer.Ordinal);

        foreach (var wall in document.Walls.OrderBy(wall => wall.Id))
        {
            var storey = storeys[wall.LevelId];
            var type = wallTypes[wall.WallTypeId];
            var dx = (wall.End.X - wall.Start.X) / wall.Length;
            var dy = (wall.End.Y - wall.Start.Y) / wall.Length;
            var start = writer.Add($"IFCCARTESIANPOINT(({Real(wall.Start.X)},{Real(wall.Start.Y)},0.))");
            var direction = writer.Add($"IFCDIRECTION(({Real(dx)},{Real(dy)},0.))");
            var axes = writer.Add($"IFCAXIS2PLACEMENT3D({Ref(start)},{Ref(zAxis)},{Ref(direction)})");
            var placement = writer.Add($"IFCLOCALPLACEMENT({Ref(storey.Placement)},{Ref(axes)})");
            var shape = wall.TopProfile is { Count: > 1 }
                ? ProfiledWallBody(writer, body, zAxis, wall)
                : Box(writer, body, zAxis, xAxis2D, wall.Length / 2, 0, wall.BaseOffset, wall.Length, wall.Thickness, wall.NetHeight);
            var entity = writer.Add($"IFCWALL({Id(wall.Id)},$,{Text(type.Name)},$,$,{Ref(placement)},{Ref(shape)},{Text(wall.Id.ToString("N")[..8])},.STANDARD.)");
            contained[wall.LevelId].Add(entity);
            if (!materials.TryGetValue(type.MaterialName, out var material))
                materials[type.MaterialName] = material = (writer.Add($"IFCMATERIAL({Text(type.MaterialName)},$,$)"), []);
            material.Elements.Add(entity);

            foreach (var opening in document.Openings.Where(o => o.WallId == wall.Id).OrderBy(o => o.Offset))
            {
                // The void is slightly deeper than the wall so boolean subtraction is unambiguous.
                var voidPoint = writer.Add($"IFCCARTESIANPOINT(({Real(opening.Offset)},0.,{Real(opening.Sill)}))");
                var voidAxes = writer.Add($"IFCAXIS2PLACEMENT3D({Ref(voidPoint)},$,$)");
                var voidPlacement = writer.Add($"IFCLOCALPLACEMENT({Ref(placement)},{Ref(voidAxes)})");
                var voidShape = Box(writer, body, zAxis, xAxis2D, 0, 0, 0, opening.Width, wall.Thickness + 0.1, opening.Height);
                var voidEntity = writer.Add($"IFCOPENINGELEMENT({Id(opening.Id, "void")},$,'Opening',$,$,{Ref(voidPlacement)},{Ref(voidShape)},$,.OPENING.)");
                writer.Add($"IFCRELVOIDSELEMENT({Id(opening.Id, "voids")},$,$,$,{Ref(entity)},{Ref(voidEntity)})");

                var fillShape = Box(writer, body, zAxis, xAxis2D, 0, 0, 0, opening.Width,
                    opening.Kind == OpeningKind.Door ? 0.05 : 0.02, opening.Height);
                var openingType = openingTypes[opening.OpeningTypeId];
                var fill = opening.Kind == OpeningKind.Door
                    ? writer.Add($"IFCDOOR({Id(opening.Id)},$,'Door',$,$,{Ref(voidPlacement)},{Ref(fillShape)},$,{Real(opening.Height)},{Real(opening.Width)},.DOOR.,{DoorOperation(openingType, opening)},$)")
                    : writer.Add($"IFCWINDOW({Id(opening.Id)},$,'Window',$,$,{Ref(voidPlacement)},{Ref(fillShape)},$,{Real(opening.Height)},{Real(opening.Width)},.WINDOW.,{WindowPartition(openingType, opening)},$)");
                writer.Add($"IFCRELFILLSELEMENT({Id(opening.Id, "fills")},$,$,$,{Ref(voidEntity)},{Ref(fill)})");
                contained[wall.LevelId].Add(fill);
            }
        }

        foreach (var floor in document.Floors.OrderBy(floor => floor.Id))
        {
            var storey = storeys[floor.LevelId];
            // The model defines ElevationOffset at the slab top, relative to its storey.
            // IFC extrusion grows upward from the underside, so subtract thickness.
            var below = writer.Add($"IFCCARTESIANPOINT((0.,0.,{Real(floor.ElevationOffset - floor.Thickness)}))");
            var axes = writer.Add($"IFCAXIS2PLACEMENT3D({Ref(below)},$,$)");
            var placement = writer.Add($"IFCLOCALPLACEMENT({Ref(storey.Placement)},{Ref(axes)})");
            // IFC swept profiles require a consistently counter-clockwise outer ring.
            // User-drawn slabs may be clockwise; never invert their solid when exporting.
            var shape = Extrusion(writer, body, zAxis,
                Polyline(writer, OrientRing(floor.Boundary, counterClockwise: true)), floor.Thickness);
            // IFC4 distinguishes floor and roof slabs. Other Guanacaste floor kinds do not
            // have a matching IfcSlab predefined type; do not mislabel them as floors.
            // Pitched roofs and surveyed terrain still need dedicated geometry exporters.
            var slabType = floor.Kind switch
            {
                FloorKind.Floor => ".FLOOR.",
                FloorKind.Roof => ".ROOF.",
                _ => ".NOTDEFINED."
            };
            var entity = writer.Add($"IFCSLAB({Id(floor.Id)},$,{Text(floor.Name)},$,$,{Ref(placement)},{Ref(shape)},$,{slabType})");
            contained[floor.LevelId].Add(entity);
        }

        foreach (var group in document.Rooms.GroupBy(room => room.LevelId))
        {
            var storey = storeys[group.Key];
            var height = document.Walls.Where(wall => wall.LevelId == group.Key).Select(wall => wall.Height)
                .DefaultIfEmpty(3).Max();
            var spaces = new List<int>();
            foreach (var room in group.OrderBy(room => room.Number, StringComparer.Ordinal))
            {
                var placement = writer.Add($"IFCLOCALPLACEMENT({Ref(storey.Placement)},{Ref(worldPlacement)})");
                // Export the actual room footprint, not its bounding rectangle. Automatic
                // rooms may be concave and may surround courtyards that must remain void.
                IReadOnlyList<Point2> outline = room.Boundary is { Count: >= 3 } boundary
                    ? boundary
                    : [room.FirstCorner, new(room.SecondCorner.X, room.FirstCorner.Y),
                       room.SecondCorner, new(room.FirstCorner.X, room.SecondCorner.Y)];
                var outer = Polyline(writer, OrientRing(outline, counterClockwise: true));
                var holes = room.Holes?.Select(hole =>
                    Polyline(writer, OrientRing(hole, counterClockwise: false))).ToArray() ?? [];
                var shape = Extrusion(writer, body, zAxis, outer, height, holes);
                spaces.Add(writer.Add($"IFCSPACE({Id(room.Id)},$,{Text(room.Number)},$,$,{Ref(placement)},{Ref(shape)},{Text(room.Name)},.ELEMENT.,.INTERNAL.,$)"));
            }
            writer.Add($"IFCRELAGGREGATES({Id(group.Key, "spaces")},$,$,$,{Ref(storey.Entity)},({Refs([.. spaces])}))");
        }

        foreach (var (levelId, elements) in contained.Where(pair => pair.Value.Count > 0))
            writer.Add($"IFCRELCONTAINEDINSPATIALSTRUCTURE({Id(levelId, "contains")},$,$,$,({Refs([.. elements])}),{Ref(storeys[levelId].Entity)})");
        foreach (var (name, (material, elements)) in materials.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            writer.Add($"IFCRELASSOCIATESMATERIAL({Id(document.Id, "material:" + name)},$,$,$,({Refs([.. elements])}),{Ref(material)})");

        var header = new StringBuilder()
            .Append("ISO-10303-21;\n")
            .Append("HEADER;\n")
            .Append("FILE_DESCRIPTION(('ViewDefinition [ReferenceView_V1.2]'),'2;1');\n")
            .Append($"FILE_NAME({Text(document.Name + ".ifc")},'{timestamp.UtcDateTime:yyyy-MM-ddTHH:mm:ss}',(''),(''),")
            .Append($"{Text("Guanacaste 3D " + applicationVersion)},'Guanacaste 3D','');\n")
            .Append("FILE_SCHEMA(('IFC4'));\n")
            .Append("ENDSEC;\n");
        return header + writer.ToDataSection() + "END-ISO-10303-21;\n";
    }

    /// <summary>IFC4 door operation semantics must follow the modeled family, not a fixed swing direction.</summary>
    private static string DoorOperation(OpeningType type, WallOpening opening) => type.Operation switch
    {
        // The BIM mesher uses two leaves for standard doors wider than 1.2 m.
        // Export the same topology instead of mislabeling a double door as a single swing.
        OpeningOperation.Standard when opening.Width > 1.2 => ".DOUBLE_DOOR_SINGLE_SWING.",
        OpeningOperation.Standard => opening.FlipHand ? ".SINGLE_SWING_RIGHT." : ".SINGLE_SWING_LEFT.",
        OpeningOperation.Sliding => ".DOUBLE_DOOR_SLIDING.",
        OpeningOperation.Folding => ".DOUBLE_DOOR_FOLDING.",
        OpeningOperation.Pocket => opening.FlipHand ? ".SLIDING_TO_RIGHT." : ".SLIDING_TO_LEFT.",
        OpeningOperation.Overhead => ".ROLLINGUP.",
        OpeningOperation.Revolving => ".REVOLVING.",
        _ => ".NOTDEFINED."
    };

    /// <summary>Match IFC4 window panel partitions to the panes generated by the BIM mesher.</summary>
    private static string WindowPartition(OpeningType type, WallOpening opening)
    {
        if (type.Operation == OpeningOperation.Void) return ".NOTDEFINED.";
        if (type.Operation == OpeningOperation.Hung) return ".DOUBLE_PANEL_HORIZONTAL.";

        var panels = type.Operation switch
        {
            OpeningOperation.Sliding => Math.Max(2, type.GlazingLightCount),
            OpeningOperation.Louvered => 1,
            _ => type.GlazingLightCount > 0 ? type.GlazingLightCount : opening.Width > 1 ? 2 : 1
        };
        return panels switch
        {
            1 => ".SINGLE_PANEL.",
            2 => ".DOUBLE_PANEL_VERTICAL.",
            3 => ".TRIPLE_PANEL_VERTICAL.",
            // IFC4 has no four-side-by-side-panel partition enumerator.
            _ => ".NOTDEFINED."
        };
    }

    /// <summary>Compresses a GUID into the 22-character IFC base-64 GlobalId.</summary>
    public static string ToGlobalId(Guid guid)
    {
        // RFC 4122 byte order (big-endian), read as one 128-bit unsigned integer.
        var bytes = guid.ToByteArray(bigEndian: true);
        var value = new UInt128(
            System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(0, 8)),
            System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(8, 8)));
        Span<char> result = stackalloc char[22];
        // 22 digits × 6 bits = 132 bits; the leading digit carries only the top 2 bits.
        for (var index = 21; index >= 0; index--)
        {
            result[index] = Base64Alphabet[(int)(value & 63)];
            value >>= 6;
        }
        return new string(result);
    }

    /// <summary>Returns a quoted GlobalId for an element, or for an entity derived from it.</summary>
    private static string Id(Guid source, string? derivation = null)
    {
        if (derivation is null) return $"'{ToGlobalId(source)}'";
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(source.ToString("N") + "/" + derivation));
        return $"'{ToGlobalId(new Guid(bytes))}'";
    }

    /// <summary>
    /// A wall whose top follows a profile: its outline in elevation (base, then the top from the far end back to the start) extruded
    /// across the thickness. The outline sits in the wall's own X and Z axes and is extruded toward -Y from the wall's face.
    /// </summary>
    private static int ProfiledWallBody(StepWriter writer, int body, int zAxis, Wall wall)
    {
        var stations = wall.TopProfile!.Select(point => point.X).Where(x => x > 1e-6 && x < wall.Length - 1e-6).Append(0).Append(wall.Length).Distinct().Order().ToList();
        var outline = new List<Point2> { new(0, wall.BaseOffset), new(wall.Length, wall.BaseOffset) };
        for (var index = stations.Count - 1; index >= 0; index--) outline.Add(new Point2(stations[index], wall.TopHeightAt(stations[index])));
        var polyline = Polyline(writer, outline);
        var profile = writer.Add($"IFCARBITRARYCLOSEDPROFILEDEF(.AREA.,$,{Ref(polyline)})");
        var origin = writer.Add($"IFCCARTESIANPOINT((0.,{Real(wall.Thickness / 2)},0.))");
        var normal = writer.Add("IFCDIRECTION((0.,-1.,0.))");
        var across = writer.Add("IFCDIRECTION((1.,0.,0.))");
        var position = writer.Add($"IFCAXIS2PLACEMENT3D({Ref(origin)},{Ref(normal)},{Ref(across)})");
        var solid = writer.Add($"IFCEXTRUDEDAREASOLID({Ref(profile)},{Ref(position)},{Ref(zAxis)},{Real(wall.Thickness)})");
        return Representation(writer, body, solid);
    }

    /// <summary>Adds a rectangle profile centred at (x, y) and extruded upward from z.</summary>
    private static int Box(StepWriter writer, int body, int zAxis, int xAxis2D,
        double x, double y, double z, double sizeX, double sizeY, double height)
    {
        var center = writer.Add($"IFCCARTESIANPOINT(({Real(x)},{Real(y)}))");
        var position = writer.Add($"IFCAXIS2PLACEMENT2D({Ref(center)},{Ref(xAxis2D)})");
        var profile = writer.Add($"IFCRECTANGLEPROFILEDEF(.AREA.,$,{Ref(position)},{Real(sizeX)},{Real(sizeY)})");
        var basePoint = writer.Add($"IFCCARTESIANPOINT((0.,0.,{Real(z)}))");
        var solidPosition = writer.Add($"IFCAXIS2PLACEMENT3D({Ref(basePoint)},$,$)");
        var solid = writer.Add($"IFCEXTRUDEDAREASOLID({Ref(profile)},{Ref(solidPosition)},{Ref(zAxis)},{Real(height)})");
        return Representation(writer, body, solid);
    }

    /// <summary>Extrudes a closed polyline profile upward by a depth.</summary>
    private static int Extrusion(StepWriter writer, int body, int zAxis, int polyline, double depth,
        IReadOnlyList<int>? innerCurves = null)
    {
        // IFC4 requires a profile with voids for rooms surrounding courtyards.
        var profile = innerCurves is { Count: > 0 }
            ? writer.Add($"IFCARBITRARYPROFILEDEFWITHVOIDS(.AREA.,$,{Ref(polyline)},({Refs([.. innerCurves])}))")
            : writer.Add($"IFCARBITRARYCLOSEDPROFILEDEF(.AREA.,$,{Ref(polyline)})");
        var basePoint = writer.Add("IFCCARTESIANPOINT((0.,0.,0.))");
        var position = writer.Add($"IFCAXIS2PLACEMENT3D({Ref(basePoint)},$,$)");
        var solid = writer.Add($"IFCEXTRUDEDAREASOLID({Ref(profile)},{Ref(position)},{Ref(zAxis)},{Real(depth)})");
        return Representation(writer, body, solid);
    }

    private static int Representation(StepWriter writer, int body, int solid)
    {
        var shape = writer.Add($"IFCSHAPEREPRESENTATION({Ref(body)},'Body','SweptSolid',({Ref(solid)}))");
        return writer.Add($"IFCPRODUCTDEFINITIONSHAPE($,$,({Ref(shape)}))");
    }

    /// <summary>Writes a closed 2D polyline (IFC requires the first point repeated at the end).</summary>
    /// <summary>IFC swept profiles use opposite winding for outer boundaries and holes.</summary>
    private static IReadOnlyList<Point2> OrientRing(IReadOnlyList<Point2> ring, bool counterClockwise) =>
        (PolygonTriangulator.SignedDoubleArea(ring) > 0) == counterClockwise
            ? ring
            : ring.Reverse().ToArray();

    private static int Polyline(StepWriter writer, IReadOnlyList<Point2> points)
    {
        var references = points.Select(point =>
            writer.Add($"IFCCARTESIANPOINT(({Real(point.X)},{Real(point.Y)}))")).ToList();
        references.Add(references[0]);
        return writer.Add($"IFCPOLYLINE(({Refs([.. references])}))");
    }

    private static string Ref(int entity) => "#" + entity.ToString(Invariant);

    private static string Refs(params int[] entities) => string.Join(",", entities.Select(Ref));

    /// <summary>Formats a STEP REAL with round-trip precision and a decimal point in its mantissa.</summary>
    public static string Real(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        if (value == 0) return "0.";

        // Fixed 10-place formatting silently rounded small nonzero coordinates to zero and
        // truncated more precise measurements. "R" preserves the original double value.
        var text = value.ToString("R", Invariant);
        var exponent = text.IndexOf('E');
        if (exponent >= 0)
        {
            // STEP REAL syntax requires a decimal point even in scientific notation.
            var mantissa = text[..exponent];
            return (mantissa.Contains('.') ? mantissa : mantissa + ".") + text[exponent..];
        }
        return text.Contains('.') ? text : text + ".";
    }

    /// <summary>Encodes STEP strings using Unicode scalar values (X2 for BMP, X4 for supplementary planes).</summary>
    public static string Text(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder("'");
        for (var index = 0; index < value.Length;)
        {
            // Iterating char-by-char would emit isolated UTF-16 surrogate halves for emoji and
            // other supplementary characters. ISO 10303-21 expects a single X4 code point.
            if (!Rune.TryGetRuneAt(value, index, out var rune))
                throw new ArgumentException("STEP strings must contain valid UTF-16.", nameof(value));

            var scalar = rune.Value;
            if (scalar == '\'') builder.Append("''");
            else if (scalar == '\\') builder.Append("\\\\");
            else if (scalar is >= ' ' and <= '~') builder.Append((char)scalar);
            else if (scalar <= 0xFFFF)
                builder.Append("\\X2\\").Append(scalar.ToString("X4", Invariant)).Append("\\X0\\");
            else
                builder.Append("\\X4\\").Append(scalar.ToString("X8", Invariant)).Append("\\X0\\");
            index += rune.Utf16SequenceLength;
        }
        return builder.Append('\'').ToString();
    }

    /// <summary>Numbers entity instances sequentially and renders the DATA section.</summary>
    private sealed class StepWriter
    {
        private readonly StringBuilder _data = new();
        private int _next = 1;

        public int Add(string entity)
        {
            var id = _next++;
            _data.Append('#').Append(id.ToString(Invariant)).Append('=').Append(entity).Append(";\n");
            return id;
        }

        public string ToDataSection() => "DATA;\n" + _data + "ENDSEC;\n";
    }
}
