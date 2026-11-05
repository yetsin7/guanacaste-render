using System.Globalization;
using System.Reflection;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>
/// The version of this build and the date it was released, both written once in Guanacaste3D.Desktop.csproj
/// (<c>Version</c> and <c>ReleaseDate</c>) and shown in the title bar.
/// </summary>
public static class AppInfo
{
    public static AppVersion Version { get; } = ReadVersion();

    public static DateOnly? ReleaseDate { get; } = ReadReleaseDate();

    /// <summary>The text beside the icons of the title bar, such as <c>v1.0.0 · 02/11/2026</c>.</summary>
    public static string Label => ReleaseDate is { } date
        ? $"v{Version} · {date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}"
        : $"v{Version}";

    private static AppVersion ReadVersion()
    {
        var assembly = typeof(AppInfo).Assembly;
        var text = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return AppVersion.TryParse(text, out var version) ? version : default;
    }

    private static DateOnly? ReadReleaseDate()
    {
        var text = typeof(AppInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(item => item.Key == "ReleaseDate")?.Value;
        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }
}
