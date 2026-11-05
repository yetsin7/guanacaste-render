using System.Globalization;
using System.Numerics;
using System.Text;

namespace Guanacaste3D.Core.Import;

/// <summary>A reference to another entity (<c>#123</c>).</summary>
internal readonly record struct StepRef(int Id);

/// <summary>An enumeration or boolean literal such as <c>.T.</c> or <c>.LENGTHUNIT.</c>, without the dots.</summary>
internal readonly record struct StepEnum(string Name);

/// <summary>A typed value such as <c>IFCLENGTHMEASURE(0.3)</c>.</summary>
internal sealed record StepTyped(string Type, object? Value);

/// <summary>One parsed entity instance; <see cref="Args"/> hold refs, strings, numbers, enums, lists and nulls.</summary>
internal sealed record StepEntity(int Id, string Type, List<object?> Args)
{
    public StepRef? Ref(int index) => index < Args.Count ? Args[index] as StepRef? : null;
    public double Number(int index, double fallback = 0) =>
        index < Args.Count ? StepFile.AsNumber(Args[index]) ?? fallback : fallback;
    public string? Text(int index) => index < Args.Count ? Args[index] as string : null;
    public List<object?>? List(int index) => index < Args.Count ? Args[index] as List<object?> : null;
    public bool Flag(int index) => index < Args.Count && Args[index] is StepEnum { Name: "T" };
    public string? Enum(int index) => index < Args.Count && Args[index] is StepEnum e ? e.Name : null;
}

/// <summary>
/// Random-access reader for ISO 10303-21 ("STEP physical file") data, the syntax of IFC. The file is held as bytes
/// and indexed by entity number in one pass; entities are parsed on demand from the bytes, so a 500 MB model does not
/// become millions of strings. Handles quoted strings with escaped quotes, comments and multi-line statements.
/// </summary>
internal sealed class StepFile
{
    private readonly byte[] _data;
    private readonly int[] _offsets;
    private readonly Dictionary<int, StepEntity> _cache = [];
    private readonly Dictionary<int, Vector3> _points = [];
    private readonly List<int> _order = [];

    private StepFile(byte[] data, int[] offsets, List<int> order)
    {
        _data = data;
        _offsets = offsets;
        _order = order;
    }

    /// <summary>Entity numbers in the order they appear in the file.</summary>
    public IReadOnlyList<int> Ids => _order;

    public static StepFile Load(string path, CancellationToken cancellation)
    {
        var info = new FileInfo(path);
        if (info.Length > 2_000_000_000L) throw new InvalidDataException("The model file is too large to import.");
        var data = File.ReadAllBytes(path);
        var start = IndexOf(data, "DATA;"u8, 0);
        if (start < 0) throw new InvalidDataException("The IFC file is not valid.");
        start += 5;

        var offsets = new List<(int Id, int Offset)>(data.Length / 80);
        var i = start;
        var count = 0;
        while (i < data.Length)
        {
            if ((++count & 0xFFFFF) == 0) cancellation.ThrowIfCancellationRequested();
            // Skip whitespace and comments between statements.
            while (i < data.Length && (data[i] is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t')) i++;
            if (i + 1 < data.Length && data[i] == '/' && data[i + 1] == '*')
            {
                var close = IndexOf(data, "*/"u8, i + 2);
                i = close < 0 ? data.Length : close + 2;
                continue;
            }
            if (i >= data.Length) break;
            if (data[i] == '#')
            {
                var id = 0;
                var j = i + 1;
                while (j < data.Length && data[j] >= '0' && data[j] <= '9') id = id * 10 + (data[j++] - '0');
                offsets.Add((id, i));
            }
            else if (i + 6 <= data.Length && data.AsSpan(i, 6).SequenceEqual("ENDSEC"u8))
            {
                break;
            }
            i = EndOfStatement(data, i);
        }

        var max = offsets.Count == 0 ? 0 : offsets.Max(entry => entry.Id);
        var table = new int[max + 1];
        Array.Fill(table, -1);
        var order = new List<int>(offsets.Count);
        foreach (var (id, offset) in offsets)
        {
            table[id] = offset;
            order.Add(id);
        }
        return new StepFile(data, table, order);
    }

    /// <summary>Index just past the semicolon that ends the statement beginning at <paramref name="from"/>.</summary>
    private static int EndOfStatement(byte[] data, int from)
    {
        var i = from;
        var inString = false;
        while (i < data.Length)
        {
            var b = data[i];
            if (inString)
            {
                if (b == '\'')
                {
                    if (i + 1 < data.Length && data[i + 1] == '\'') i++;
                    else inString = false;
                }
            }
            else if (b == '\'') inString = true;
            else if (b == ';') return i + 1;
            i++;
        }
        return data.Length;
    }

    private static int IndexOf(byte[] data, ReadOnlySpan<byte> needle, int from) =>
        from >= data.Length ? -1 : data.AsSpan(from).IndexOf(needle) is var found and >= 0 ? from + found : -1;

    /// <summary>Returns the parsed entity, or null for an unknown id. Parsed entities are cached.</summary>
    public StepEntity? Get(int id) => Get(id, cache: true);

    public StepEntity? Get(int id, bool cache)
    {
        if ((uint)id >= (uint)_offsets.Length || _offsets[id] < 0) return null;
        if (cache && _cache.TryGetValue(id, out var cached)) return cached;
        var entity = Parse(id, _offsets[id]);
        if (cache && entity is not null) _cache[id] = entity;
        return entity;
    }

    /// <summary>Returns the entity type name (uppercase) without parsing its arguments.</summary>
    public string? TypeOf(int id)
    {
        if ((uint)id >= (uint)_offsets.Length || _offsets[id] < 0) return null;
        var i = _offsets[id];
        while (i < _data.Length && _data[i] != '=') i++;
        i++;
        while (i < _data.Length && _data[i] == ' ') i++;
        var start = i;
        while (i < _data.Length && _data[i] != '(' && _data[i] != ' ' && _data[i] != ';') i++;
        return Encoding.ASCII.GetString(_data, start, i - start).ToUpperInvariant();
    }

    /// <summary>Reads a Cartesian point's coordinates directly, which avoids building entities for the most repeated type.</summary>
    public Vector3 Point(int id)
    {
        if (_points.TryGetValue(id, out var cached)) return cached;
        var entity = Get(id, cache: false);
        var value = Vector3.Zero;
        if (entity is { Args.Count: > 0 } && entity.Args[0] is List<object?> coordinates)
        {
            value = new Vector3((float)(AsNumber(At(coordinates, 0)) ?? 0), (float)(AsNumber(At(coordinates, 1)) ?? 0),
                (float)(AsNumber(At(coordinates, 2)) ?? 0));
        }
        _points[id] = value;
        return value;
    }

    private static object? At(List<object?> list, int index) => index < list.Count ? list[index] : null;

    /// <summary>Converts a number or typed number (<c>IFCLENGTHMEASURE(1.5)</c>) to a double.</summary>
    public static double? AsNumber(object? value) => value switch
    {
        double d => d,
        StepTyped { Value: double d } => d,
        _ => null
    };

    private StepEntity? Parse(int id, int offset)
    {
        var i = offset;
        while (i < _data.Length && _data[i] != '=') i++;
        i++;
        while (i < _data.Length && _data[i] == ' ') i++;
        if (i >= _data.Length) return null;
        if (_data[i] == '(') return new StepEntity(id, "", []); // complex entity instances are not used for geometry
        var typeStart = i;
        while (i < _data.Length && _data[i] != '(' && _data[i] != ';' && _data[i] != ' ') i++;
        var type = Encoding.ASCII.GetString(_data, typeStart, i - typeStart).ToUpperInvariant();
        while (i < _data.Length && _data[i] == ' ') i++;
        if (i >= _data.Length || _data[i] != '(') return new StepEntity(id, type, []);
        var args = ParseList(ref i);
        return new StepEntity(id, type, args);
    }

    /// <summary>Parses a parenthesized list starting at the "(" under <paramref name="i"/>.</summary>
    private List<object?> ParseList(ref int i)
    {
        var list = new List<object?>();
        i++; // consume '('
        while (i < _data.Length)
        {
            SkipSpace(ref i);
            if (i >= _data.Length) break;
            var b = _data[i];
            if (b == ')')
            {
                i++;
                return list;
            }
            if (b == ',')
            {
                i++;
                continue;
            }
            list.Add(ParseValue(ref i));
        }
        return list;
    }

    private object? ParseValue(ref int i)
    {
        var b = _data[i];
        switch (b)
        {
            case (byte)'#':
            {
                i++;
                var id = 0;
                while (i < _data.Length && _data[i] >= '0' && _data[i] <= '9') id = id * 10 + (_data[i++] - '0');
                return new StepRef(id);
            }
            case (byte)'\'':
            {
                var builder = new StringBuilder();
                i++;
                while (i < _data.Length)
                {
                    if (_data[i] == '\'')
                    {
                        if (i + 1 < _data.Length && _data[i + 1] == '\'')
                        {
                            builder.Append('\'');
                            i += 2;
                            continue;
                        }
                        i++;
                        break;
                    }
                    builder.Append((char)_data[i++]);
                }
                return builder.ToString();
            }
            case (byte)'.':
            {
                var start = ++i;
                while (i < _data.Length && _data[i] != '.') i++;
                var name = Encoding.ASCII.GetString(_data, start, i - start);
                i++;
                return new StepEnum(name);
            }
            case (byte)'$':
            case (byte)'*':
                i++;
                return null;
            case (byte)'(':
                return ParseList(ref i);
            default:
                if (b == '-' || b == '+' || b == '.' || (b >= '0' && b <= '9')) return ParseNumber(ref i);
                // A typed value: NAME(value[, value…]).
                var typeStart = i;
                while (i < _data.Length && _data[i] != '(' && _data[i] != ',' && _data[i] != ')') i++;
                var type = Encoding.ASCII.GetString(_data, typeStart, i - typeStart).Trim().ToUpperInvariant();
                if (i < _data.Length && _data[i] == '(')
                {
                    var inner = ParseList(ref i);
                    return new StepTyped(type, inner.Count == 1 ? inner[0] : inner);
                }
                return new StepTyped(type, null);
        }
    }

    private double ParseNumber(ref int i)
    {
        var start = i;
        while (i < _data.Length && (_data[i] is (byte)'-' or (byte)'+' or (byte)'.' or (byte)'E' or (byte)'e' ||
                                   (_data[i] >= '0' && _data[i] <= '9')))
            i++;
        return double.TryParse(Encoding.ASCII.GetString(_data, start, i - start), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private void SkipSpace(ref int i)
    {
        while (i < _data.Length && (_data[i] is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t')) i++;
    }
}
