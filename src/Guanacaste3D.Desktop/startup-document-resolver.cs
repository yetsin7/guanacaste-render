namespace Guanacaste3D.Desktop;

/// <summary>Filters process arguments so only registered Guanacaste documents reach the workspace loader.</summary>
internal static class StartupDocumentResolver
{
    private static readonly string[] SupportedExtensions = [".g3d.json", ".g3d", ".g3df", ".g3dt", ".g3", ".g3dx", ".rvt"];

    /// <summary>
    /// Returns the first supported document path, ignoring executable paths, switches and unrelated shell arguments.
    /// A supported path is returned even when it no longer exists so the normal open error can explain that condition.
    /// </summary>
    public static string? FindSupportedPath(IEnumerable<string> arguments)
    {
        foreach (string argument in arguments)
        {
            string candidate = argument.Trim();
            if (candidate.Length == 0) continue;
            if (SupportedExtensions.Any(extension => candidate.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
        return null;
    }
}
