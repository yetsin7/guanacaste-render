using System.Text.Json;
using System.Text.Json.Serialization;

namespace Guanacaste3D.Core.Import;

/// <summary>
/// A reference from a project to an external model file, like a Revit link: the geometry stays in its own file and is
/// read when needed, so the project stays small, and updating the source updates the project on the next reload.
/// </summary>
public sealed class ModelLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Absolute path of the model file when it was linked.</summary>
    public string Path { get; set; } = "";
    /// <summary>Path relative to the project file, tried when the absolute path no longer exists (a moved folder).</summary>
    public string? RelativePath { get; set; }
    public string Name { get; set; } = "";
    public bool Visible { get; set; } = true;
    public ModelPlacement Placement { get; set; } = ModelPlacement.Identity;
    public UpAxis UpAxis { get; set; } = UpAxis.Auto;
    public double? MetersPerUnit { get; set; }
    public bool CenterHorizontally { get; set; }
    public bool PlaceOnGround { get; set; }

    /// <summary>The import options that reproduce this link's geometry.</summary>
    public ModelImportOptions ToOptions() => new()
    {
        UpAxis = UpAxis, MetersPerUnit = MetersPerUnit, CenterHorizontally = CenterHorizontally, PlaceOnGround = PlaceOnGround
    };
}

/// <summary>Reads and writes a project's model links in a small JSON file beside the project.</summary>
public static class ModelLinkFile
{
    public const int MaximumLinks = 200;
    private const string Suffix = ".links.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>The file that holds the links of the project saved at <paramref name="projectPath"/>.</summary>
    public static string SidecarPath(string projectPath) => projectPath + Suffix;

    /// <summary>Loads the links of a project. A missing file is an empty list; a damaged one is reported.</summary>
    public static List<ModelLink> Load(string projectPath)
    {
        var path = SidecarPath(projectPath);
        if (!File.Exists(path)) return [];
        List<ModelLink>? links;
        try
        {
            links = JsonSerializer.Deserialize<List<ModelLink>>(File.ReadAllText(path), Options);
        }
        catch (JsonException)
        {
            throw new InvalidDataException("The model links file is not valid.");
        }
        if (links is null) return [];
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(projectPath)) ?? ".";
        var result = new List<ModelLink>();
        foreach (var link in links.Take(MaximumLinks))
        {
            if (string.IsNullOrWhiteSpace(link.Path) && string.IsNullOrWhiteSpace(link.RelativePath)) continue;
            // A project moved together with its models keeps working through the relative path.
            if (!File.Exists(link.Path) && !string.IsNullOrWhiteSpace(link.RelativePath))
            {
                var candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, link.RelativePath));
                if (File.Exists(candidate)) link.Path = candidate;
            }
            if (!link.Placement.IsValid) link.Placement = ModelPlacement.Identity;
            if (link.Id == Guid.Empty) link.Id = Guid.NewGuid();
            if (string.IsNullOrWhiteSpace(link.Name)) link.Name = System.IO.Path.GetFileNameWithoutExtension(link.Path);
            result.Add(link);
        }
        return result;
    }

    /// <summary>Writes the links atomically, or removes the file when there are none.</summary>
    public static void Save(string projectPath, IEnumerable<ModelLink> links)
    {
        var list = links.Take(MaximumLinks).ToList();
        var path = SidecarPath(projectPath);
        if (list.Count == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(projectPath)) ?? ".";
        foreach (var link in list)
            link.RelativePath = System.IO.Path.GetRelativePath(directory, link.Path);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(list, Options));
        File.Move(temporary, path, overwrite: true);
    }
}
