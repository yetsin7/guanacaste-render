using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Guanacaste3D.Core.Import.Revit;

/// <summary>
/// A Guanacaste exchange file (.g3dx) written by the Revit bridge add-in: levels, materials, shared meshes and the
/// model elements with the BIM data needed to rebuild them as native walls, openings, slabs and families. Lengths are
/// meters, Z up. See docs/revit-import.md.
/// </summary>
public sealed class ExchangeFile
{
    public const string Extension = ".g3dx";

    public string Format { get; set; } = "";
    public int Version { get; set; }
    public string Source { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public List<ExchangeLevel> Levels { get; set; } = [];
    public List<ExchangeMaterial> Materials { get; set; } = [];
    public List<ExchangeMesh> Meshes { get; set; } = [];
    public List<ExchangeElement> Elements { get; set; } = [];
    public Dictionary<string, int> Skipped { get; set; } = [];

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Reads and checks an exchange file.</summary>
    public static ExchangeFile Read(string path)
    {
        ExchangeFile? file;
        try
        {
            using var stream = File.OpenRead(path);
            file = JsonSerializer.Deserialize<ExchangeFile>(stream, Options);
        }
        catch (JsonException)
        {
            throw new InvalidDataException("The exchange file is damaged.");
        }
        if (file is null || file.Format != "guanacaste-exchange" || file.Version != 1)
            throw new InvalidDataException("The file is not a supported Guanacaste exchange file.");
        return file;
    }

    /// <summary>Whether a path names an exchange file.</summary>
    public static bool IsExchangePath(string path) =>
        Path.GetExtension(path).Equals(Extension, StringComparison.OrdinalIgnoreCase);
}

public sealed class ExchangeLevel
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public double Elevation { get; set; }
}

public sealed class ExchangeMaterial
{
    public string Name { get; set; } = "";
    public string Class { get; set; } = "";
    public string Category { get; set; } = "";
    public string Color { get; set; } = "#B4B4B4";
    public double Transparency { get; set; }
    public double Shininess { get; set; }
    public string OwnerCategory { get; set; } = "";
}

/// <summary>Triangles in an element's own frame; arrays are base64 little-endian float32/int32.</summary>
public sealed class ExchangeMesh
{
    public string Positions { get; set; } = "";
    public string Indices { get; set; } = "";
    public string Materials { get; set; } = "";

    private (Vector3[] Positions, int[] Indices, int[] Materials)? _decoded;

    public (Vector3[] Positions, int[] Indices, int[] Materials) Decode()
    {
        if (_decoded is { } cached) return cached;
        try
        {
            var positions = MemoryMarshal.Cast<byte, Vector3>(Convert.FromBase64String(Positions)).ToArray();
            var indices = MemoryMarshal.Cast<byte, int>(Convert.FromBase64String(Indices)).ToArray();
            var materials = MemoryMarshal.Cast<byte, int>(Convert.FromBase64String(Materials)).ToArray();
            if (indices.Length != materials.Length * 3 || indices.Any(index => (uint)index >= (uint)positions.Length))
                throw new InvalidDataException("An exchange mesh is damaged.");
            return (_decoded = (positions, indices, materials)).Value;
        }
        catch (FormatException)
        {
            throw new InvalidDataException("An exchange mesh is damaged.");
        }
    }
}

public sealed class ExchangeElement
{
    public long Id { get; set; }
    public string UniqueId { get; set; } = "";
    /// <summary>Revit BuiltInCategory name, such as OST_Walls.</summary>
    public string Category { get; set; } = "";
    /// <summary>Category name in the language Revit ran in.</summary>
    public string CategoryName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Family { get; set; } = "";
    public string Type { get; set; } = "";
    public string Kind { get; set; } = "";
    public long? Level { get; set; }
    public string? Mark { get; set; }
    public int? Mesh { get; set; }
    public double[]? Origin { get; set; }
    public double Rotation { get; set; }
    public ExchangeWall? Wall { get; set; }
    public ExchangeOpening? Opening { get; set; }
    public ExchangeSlab? Slab { get; set; }
    // Rooms
    public string? Number { get; set; }
    public double Area { get; set; }
    public List<List<double[]>>? Loops { get; set; }
}

public sealed class ExchangeWall
{
    public string WallKind { get; set; } = "Basic";
    public double Width { get; set; }
    public string Function { get; set; } = "";
    public double[]? Start { get; set; }
    public double[]? End { get; set; }
    public bool Curved { get; set; }
    public double[]? Orientation { get; set; }
    public long? BaseLevel { get; set; }
    public double? BaseZ { get; set; }
    public double Height { get; set; }
    public double? BottomZ { get; set; }
    public double? TopZ { get; set; }
    public long? TopLevel { get; set; }
    public double TopOffset { get; set; }
    public bool ProfileEdited { get; set; }
    public bool TopAttached { get; set; }
    public bool BaseAttached { get; set; }
    public List<ExchangeLayer> Layers { get; set; } = [];
}

public sealed class ExchangeLayer
{
    public string Function { get; set; } = "";
    public double Width { get; set; }
    public string Material { get; set; } = "";
    public string Class { get; set; } = "";
}

public sealed class ExchangeOpening
{
    public long Host { get; set; }
    public double[] Point { get; set; } = [0, 0, 0];
    public double[]? Facing { get; set; }
    public double[]? Hand { get; set; }
    public bool FacingFlipped { get; set; }
    public bool HandFlipped { get; set; }
    public bool Mirrored { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public double? RoughWidth { get; set; }
    public double? RoughHeight { get; set; }
    public double? SillZ { get; set; }
}

public sealed class ExchangeSlab
{
    public bool Flat { get; set; }
    public List<List<double[]>>? Loops { get; set; }
    public double? Z { get; set; }
    public double? Thickness { get; set; }
    public string? RoofClass { get; set; }
}
