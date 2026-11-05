using System.IO;
using System.Text.Json;

namespace Guanacaste3D.Desktop;

/// <summary>Per-user application options; never saved inside a BIM model.</summary>
public sealed class DesktopOptions
{
    public int SaveReminderMinutes { get; set; } = 30;
    public bool ShowRecentFiles { get; set; } = true;
    public bool ShowContextualTab { get; set; } = true;
    public string UiTheme { get; set; } = "Claro";
    public string CanvasColor { get; set; } = "#FFFFFF";
    public string SelectionColor { get; set; } = "#0078D7";
    public string WarningColor { get; set; } = "#FF8000";
    public bool AntiAliasing { get; set; } = true;
    public bool SimplifyDuringNavigation { get; set; } = true;
    public bool EnableHardwareAcceleration { get; set; } = true;
    public bool ShowViewCube { get; set; } = true;
    public string ViewCubePosition { get; set; } = "Superior derecha";
    public int ViewCubeOpacity { get; set; } = 50;
    public bool AnimateViewCube { get; set; } = true;
    public bool KeepSceneVertical { get; set; } = true;
    public string DefaultProjectFolder { get; set; } =
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    public string DefaultFamilyFolder { get; set; } =
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    /// <summary>The folder of the shared family library; empty uses a default under Documents.</summary>
    public string CloudLibraryFolder { get; set; } = string.Empty;
    public bool CheckSpelling { get; set; } = true;
    public string DictionaryLanguage { get; set; } = "Español";
    public bool ShowNavigationWheelText { get; set; } = true;
    public double WalkSpeed { get; set; } = 3;
    public bool SaveNavigationHistory { get; set; } = true;
    public string DefaultViewDiscipline { get; set; } = "Coordinación";
    public Dictionary<string, string> Shortcuts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Guanacaste3D", "desktop-options.json");

    public static DesktopOptions Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<DesktopOptions>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return new DesktopOptions();
    }

    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, FilePath, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
