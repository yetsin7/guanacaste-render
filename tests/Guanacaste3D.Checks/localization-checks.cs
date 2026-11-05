using System.Text.RegularExpressions;

/// <summary>
/// Static checks over the WPF resource dictionaries and Core sources: no key
/// may be defined twice (WPF throws at startup), and every transaction name
/// and Core validation message must have a Spanish resource so no English text
/// reaches the user. Skipped with a notice when the sources are not present.
/// </summary>
static class LocalizationChecks
{
    /// <summary>Runs the localization checks against the repository sources.</summary>
    public static void Run(Action<bool, string> check)
    {
        var root = FindRepositoryRoot();
        if (root is null)
        {
            Console.WriteLine("INFO: repository sources not found; localization checks skipped.");
            return;
        }
        var resources = Path.Combine(root, "src", "Guanacaste3D.Desktop", "resources");
        var keyPattern = new Regex("x:Key=\"([^\"]+)\"");
        var perFile = Directory.GetFiles(resources, "localization*.xaml")
            .ToDictionary(file => Path.GetFileName(file)!, file => keyPattern.Matches(File.ReadAllText(file))
                .Select(match => System.Net.WebUtility.HtmlDecode(match.Groups[1].Value)).ToList());
        var duplicates = perFile.SelectMany(pair => pair.Value).GroupBy(key => key).Where(group => group.Count() > 1)
            .Select(group => group.Key).ToArray();
        check(duplicates.Length == 0, "Localization keys are unique across all dictionaries" +
            (duplicates.Length == 0 ? "" : ": " + string.Join(", ", duplicates)));

        var keys = perFile.SelectMany(pair => pair.Value).ToHashSet(StringComparer.Ordinal);
        var core = Directory.GetFiles(Path.Combine(root, "src", "Guanacaste3D.Core"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText).ToArray();
        var transactions = core.SelectMany(source => Regex.Matches(source, "\"(Transaction\\.[A-Za-z]+)\"")
            .Select(match => match.Groups[1].Value)).Distinct().ToArray();
        var missingTransactions = transactions.Where(key => !keys.Contains(key)).ToArray();
        check(transactions.Length > 20 && missingTransactions.Length == 0,
            "Every transaction name has a Spanish label" +
            (missingTransactions.Length == 0 ? "" : ": " + string.Join(", ", missingTransactions)));

        var messages = core.SelectMany(source => Regex.Matches(source, "InvalidDataException\\(\"([^\"{]+)\"\\)")
            .Select(match => match.Groups[1].Value)).Distinct()
            .Where(message => !message.StartsWith("Formula error", StringComparison.Ordinal)).ToArray();
        var missingMessages = messages.Where(message => !keys.Contains("Error." + message)).ToArray();
        check(missingMessages.Length == 0, "Every Core validation message has a Spanish translation" +
            (missingMessages.Length == 0 ? "" : ": " + string.Join(" | ", missingMessages)));
    }

    /// <summary>Walks up from the test binary to the folder that contains Guanacaste3D.slnx.</summary>
    private static string? FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Guanacaste3D.slnx"))) return directory.FullName;
        return null;
    }
}
