using System.Globalization;
using System.Text;

namespace Guanacaste3D.Core;

/// <summary>
/// Exports metric DXF R12 (AC1009) floor-plan geometry without external CAD dependencies.
/// It is a one-way drawing interchange, not a round-trip BIM serialization.
/// </summary>
public static partial class DxfExporter
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly (string Suffix, int Color)[] LayerKinds =
    [
        ("WALL", 7),
        ("DOOR", 1),
        ("WINDOW", 5),
        ("SLAB", 3),
        ("ROOM", 4),
        ("DIMENSION", 2)
    ];

    /// <summary>Builds an ASCII DXF document with one layer group per building level.</summary>
    public static string ToAscii(BuildingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Validate();

        var output = new StringBuilder();
        Pair(output, 0, "SECTION");
        Pair(output, 2, "HEADER");
        Pair(output, 9, "$ACADVER");
        Pair(output, 1, "AC1009");
        Pair(output, 0, "ENDSEC");

        Pair(output, 0, "SECTION");
        Pair(output, 2, "TABLES");
        // Several R12 readers require the line-type table before LAYER.
        Pair(output, 0, "TABLE");
        Pair(output, 2, "LTYPE");
        Pair(output, 70, "1");
        Pair(output, 0, "LTYPE");
        Pair(output, 2, "CONTINUOUS");
        Pair(output, 70, "0");
        Pair(output, 3, "Solid line");
        Pair(output, 72, "65");
        Pair(output, 73, "0");
        Pair(output, 40, "0");
        Pair(output, 0, "ENDTAB");

        var levels = document.Levels.OrderBy(level => level.Elevation).ToArray();
        Pair(output, 0, "TABLE");
        Pair(output, 2, "LAYER");
        Pair(output, 70, (levels.Length * LayerKinds.Length + 1).ToString(Invariant));
        WriteLayer(output, "0", 7);
        for (var index = 0; index < levels.Length; index++)
            foreach (var (suffix, color) in LayerKinds)
                WriteLayer(output, LayerName(index, suffix), color);
        Pair(output, 0, "ENDTAB");
        Pair(output, 0, "ENDSEC");

        Pair(output, 0, "SECTION");
        Pair(output, 2, "BLOCKS");
        Pair(output, 0, "ENDSEC");

        Pair(output, 0, "SECTION");
        Pair(output, 2, "ENTITIES");
        for (var index = 0; index < levels.Length; index++)
            WriteLevel(output, document, levels[index], index);
        Pair(output, 0, "ENDSEC");
        Pair(output, 0, "EOF");
        return output.ToString();
    }

    /// <summary>Writes slabs, wall outlines, opening chords, and room labels of one level.</summary>
    private static void WriteLevel(StringBuilder output, BuildingDocument document, Level level, int index)
    {
        var z = level.Elevation;
        foreach (var floor in document.Floors.Where(f => f.LevelId == level.Id).OrderBy(f => f.Id))
        {
            for (var i = 0; i < floor.Boundary.Count; i++)
                WriteLine(output, floor.Boundary[i], floor.Boundary[(i + 1) % floor.Boundary.Count],
                    z, LayerName(index, "SLAB"));
        }

        foreach (var wall in document.Walls.Where(w => w.LevelId == level.Id).OrderBy(w => w.Id))
        {
            var openings = document.Openings.Where(o => o.WallId == wall.Id)
                .OrderBy(o => o.Begin).ThenBy(o => o.Id).ToArray();
            var previous = 0d;
            foreach (var opening in openings)
            {
                if (opening.Begin > previous)
                    WriteWallRectangle(output, wall, previous, opening.Begin, z, LayerName(index, "WALL"));
                WriteLine(output, Along(wall, opening.Begin, 0), Along(wall, opening.End, 0), z,
                    LayerName(index, opening.Kind == OpeningKind.Door ? "DOOR" : "WINDOW"));
                previous = opening.End;
            }
            if (previous < wall.Length)
                WriteWallRectangle(output, wall, previous, wall.Length, z, LayerName(index, "WALL"));
        }

        WriteDimensions(output, document, level, index);

        foreach (var room in document.Rooms.Where(r => r.LevelId == level.Id).OrderBy(r => r.Number))
        {
            var layer = LayerName(index, "ROOM");
            var first = room.FirstCorner;
            var second = room.SecondCorner;
            Point2[] corners = [first, new(second.X, first.Y), second, new(first.X, second.Y)];
            for (var i = 0; i < corners.Length; i++)
                WriteLine(output, corners[i], corners[(i + 1) % corners.Length], z, layer);
            WriteText(output, room.Center, z, layer, $"{room.Number} {room.Name}");
        }
    }

    /// <summary>Returns a deterministic layer name ordered by level elevation.</summary>
    public static string LayerName(int levelIndex, string kind) => $"G3D-L{levelIndex + 1:D3}-{kind}";

    /// <summary>Writes one layer-table record.</summary>
    private static void WriteLayer(StringBuilder output, string name, int color)
    {
        Pair(output, 0, "LAYER");
        Pair(output, 2, name);
        Pair(output, 70, "0");
        Pair(output, 62, color.ToString(Invariant));
        Pair(output, 6, "CONTINUOUS");
    }

    /// <summary>Returns a point at a distance along the wall axis, offset by a fraction of thickness.</summary>
    private static Point2 Along(Wall wall, double distance, double thicknessFraction)
    {
        var dx = (wall.End.X - wall.Start.X) / wall.Length;
        var dy = (wall.End.Y - wall.Start.Y) / wall.Length;
        var offset = thicknessFraction * wall.Thickness;
        return new Point2(
            wall.Start.X + dx * distance - dy * offset,
            wall.Start.Y + dy * distance + dx * offset);
    }

    /// <summary>Writes the closed outline of a solid wall segment.</summary>
    private static void WriteWallRectangle(StringBuilder output, Wall wall, double from, double to,
        double z, string layer)
    {
        var a = Along(wall, from, 0.5);
        var b = Along(wall, to, 0.5);
        var c = Along(wall, to, -0.5);
        var d = Along(wall, from, -0.5);
        WriteLine(output, a, b, z, layer);
        WriteLine(output, b, c, z, layer);
        WriteLine(output, c, d, z, layer);
        WriteLine(output, d, a, z, layer);
    }

    /// <summary>Writes a 3D LINE entity at a constant elevation.</summary>
    private static void WriteLine(StringBuilder output, Point2 start, Point2 end, double z, string layer)
    {
        Pair(output, 0, "LINE");
        Pair(output, 8, layer);
        Pair(output, 10, Number(start.X));
        Pair(output, 20, Number(start.Y));
        Pair(output, 30, Number(z));
        Pair(output, 11, Number(end.X));
        Pair(output, 21, Number(end.Y));
        Pair(output, 31, Number(z));
    }

    /// <summary>Writes a centered single-line TEXT entity with a 0.25 m glyph height.</summary>
    private static void WriteText(StringBuilder output, Point2 position, double z, string layer, string text)
    {
        Pair(output, 0, "TEXT");
        Pair(output, 8, layer);
        Pair(output, 10, Number(position.X));
        Pair(output, 20, Number(position.Y));
        Pair(output, 30, Number(z));
        Pair(output, 40, "0.25");
        Pair(output, 1, SanitizeText(text));
        Pair(output, 72, "1");
        Pair(output, 11, Number(position.X));
        Pair(output, 21, Number(position.Y));
        Pair(output, 31, Number(z));
    }

    /// <summary>Removes line breaks and escapes that would corrupt the group-code stream.</summary>
    private static string SanitizeText(string text) =>
        new(text.Select(character => char.IsControl(character) ? ' ' : character).ToArray());

    /// <summary>Formats metric coordinates independently of the current user locale.</summary>
    private static string Number(double value) => value.ToString("0.########", Invariant);

    /// <summary>Appends a group-code pair using the DXF ASCII line convention.</summary>
    private static void Pair(StringBuilder output, int code, string value) =>
        output.Append(code.ToString(Invariant)).Append("\r\n").Append(value).Append("\r\n");
}
