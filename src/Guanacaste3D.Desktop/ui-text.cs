using System.Globalization;
using System.Windows;

namespace Guanacaste3D.Desktop;

/// <summary>Resolves localized strings for windows and controls that are not part of <c>MainWindow</c>.</summary>
internal static class UiText
{
    public static string Get(string key) => Application.Current?.TryFindResource(key) as string ?? key;

    public static string Format(string key, params object[] values) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), values);

    /// <summary>Formats a byte count as megabytes or gigabytes.</summary>
    public static string Bytes(long bytes) => bytes >= 1L << 30
        ? (bytes / 1073741824.0).ToString("0.0", CultureInfo.CurrentCulture) + " GB"
        : (bytes / 1048576.0).ToString("0", CultureInfo.CurrentCulture) + " MB";

    /// <summary>Formats a duration as seconds or minutes and seconds.</summary>
    public static string Duration(TimeSpan span) => span.TotalMinutes >= 1
        ? Format("RenderPanel.Minutes", (int)span.TotalMinutes, span.Seconds)
        : Format("RenderPanel.Seconds", Math.Max(0, span.TotalSeconds));
}
