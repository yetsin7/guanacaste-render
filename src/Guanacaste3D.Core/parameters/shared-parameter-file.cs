using System.Text;

namespace Guanacaste3D.Core;

/// <summary>A parameter definition stored in a shared parameter file, identified by a GUID that every project that uses it shares.</summary>
public sealed record SharedParameter(Guid Id, string Name, ParameterSpec Spec, string Group, string Description);

/// <summary>What reading a shared parameter file found: the parameters it could use and how many lines it had to leave out.</summary>
public sealed record SharedParameterRead(IReadOnlyList<SharedParameter> Parameters, int Skipped);

/// <summary>
/// The shared parameter file, in the tab-separated text format that Revit reads and writes (*META, *GROUP and *PARAM
/// lines), so definitions travel between projects, and between this program and Revit. Only the data types this program
/// has are read; a parameter of any other type is counted as skipped.
/// </summary>
public static class SharedParameterFile
{
    private static readonly (string Type, ParameterSpec Spec)[] Types =
    [
        ("TEXT", ParameterSpec.Text), ("INTEGER", ParameterSpec.Integer), ("NUMBER", ParameterSpec.Number),
        ("LENGTH", ParameterSpec.Length), ("AREA", ParameterSpec.Area), ("VOLUME", ParameterSpec.Volume),
        ("ANGLE", ParameterSpec.Angle), ("YESNO", ParameterSpec.YesNo), ("CURRENCY", ParameterSpec.Currency)
    ];

    public const string DefaultGroup = "General";

    private static string Clean(string text) => text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();

    /// <summary>Writes parameter definitions as a shared parameter file. Formulas are not part of the format and are left out.</summary>
    public static string Write(IEnumerable<ParameterDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var list = definitions.ToList();
        var groups = list.Select(definition => string.IsNullOrWhiteSpace(definition.Group) ? DefaultGroup : Clean(definition.Group))
            .Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
        var text = new StringBuilder();
        text.Append("# This is a Revit shared parameter file.\n# Do not edit manually.\n");
        text.Append("*META\tVERSION\tMINVERSION\nMETA\t2\t1\n");
        text.Append("*GROUP\tID\tNAME\n");
        for (var index = 0; index < groups.Count; index++) text.Append("GROUP\t").Append(index + 1).Append('\t').Append(groups[index]).Append('\n');
        text.Append("*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE\n");
        foreach (var definition in list)
        {
            var group = string.IsNullOrWhiteSpace(definition.Group) ? DefaultGroup : Clean(definition.Group);
            var groupId = groups.FindIndex(name => name.Equals(group, StringComparison.CurrentCultureIgnoreCase)) + 1;
            text.Append("PARAM\t").Append(definition.Id.ToString("D")).Append('\t').Append(Clean(definition.Name)).Append('\t')
                .Append(Types.First(entry => entry.Spec == definition.Spec).Type).Append("\t\t").Append(groupId)
                .Append("\t1\t").Append(Clean(definition.Description)).Append("\t1\t0\n");
        }
        return text.ToString();
    }

    /// <summary>
    /// Reads a shared parameter file. Lines it cannot use (an unknown data type, a bad GUID, an empty or repeated name,
    /// a repeated GUID) are counted and left out; a file with no *PARAM section at all is not a shared parameter file.
    /// </summary>
    public static SharedParameterRead Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.Contains("*PARAM", StringComparison.Ordinal))
            throw new InvalidDataException("The file is not a shared parameter file.");
        var groups = new Dictionary<string, string>();
        var parameters = new List<SharedParameter>();
        var skipped = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] is '#' or '*') continue;
            var fields = line.Split('\t');
            if (fields[0] == "GROUP" && fields.Length >= 3) groups[fields[1].Trim()] = fields[2].Trim();
            else if (fields[0] == "PARAM")
            {
                var name = fields.Length > 2 ? fields[2].Trim() : string.Empty;
                var spec = Types.FirstOrDefault(entry => fields.Length > 3 && entry.Type.Equals(fields[3].Trim(), StringComparison.OrdinalIgnoreCase));
                if (fields.Length < 4 || spec.Type is null || !Guid.TryParse(fields[1].Trim(), out var id) || id == Guid.Empty ||
                    name.Length == 0 || name.Length > 120 ||
                    parameters.Any(item => item.Id == id || item.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)))
                {
                    skipped++;
                    continue;
                }
                var group = fields.Length > 5 && groups.TryGetValue(fields[5].Trim(), out var named) ? named : string.Empty;
                var description = fields.Length > 7 ? fields[7].Trim() : string.Empty;
                parameters.Add(new SharedParameter(id, name, spec.Spec, group.Length > 120 ? group[..120] : group, description.Length > 400 ? description[..400] : description));
            }
        }
        return new SharedParameterRead(parameters, skipped);
    }
}

public sealed partial class DocumentSession
{
    /// <summary>
    /// Adds shared parameters to the project, bound to the given categories, keeping their GUIDs so the same parameter is
    /// recognised in every project. A parameter whose GUID or name the project already has is left alone.
    /// Returns how many were added.
    /// </summary>
    public int ImportSharedParameters(IReadOnlyCollection<SharedParameter> parameters, IEnumerable<ElementCategory> categories, ParameterBinding binding)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var chosen = categories.Distinct().ToList();
        if (chosen.Count == 0) throw new InvalidDataException("Choose at least one category for the shared parameters.");
        var fresh = parameters.Where(item => Document.ParameterDefinitions.All(existing =>
            existing.Id != item.Id && !existing.Name.Equals(item.Name, StringComparison.CurrentCultureIgnoreCase))).ToList();
        if (fresh.Count == 0) return 0;
        Commit("Transaction.ImportSharedParameters", document =>
        {
            foreach (var item in fresh)
                document.ParameterDefinitions.Add(new ParameterDefinition(item.Id, item.Name, item.Spec, binding, [.. chosen], item.Group)
                { Description = item.Description });
        });
        return fresh.Count;
    }
}
