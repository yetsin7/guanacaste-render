using System.IO;
using System.Text.Json;

namespace Guanacaste3D.Desktop.Customization;

/// <summary>
/// Stores user-specific quick access and keyboard sequence preferences outside project files.
/// </summary>
public sealed class CommandPreferenceService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _preferencePath;

    /// <summary>Creates a preference store in the current user's local application folder.</summary>
    public CommandPreferenceService()
    {
        string applicationFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Guanacaste3D");
        _preferencePath = Path.Combine(applicationFolder, "command-preferences.json");
    }

    /// <summary>Loads saved preferences or returns safe defaults when the file is absent or invalid.</summary>
    public CommandPreferences Load()
    {
        try
        {
            if (!File.Exists(_preferencePath)) return CreateDefaults();
            CommandPreferences? preferences = JsonSerializer.Deserialize<CommandPreferences>(
                File.ReadAllText(_preferencePath),
                SerializerOptions);
            return preferences is null ? CreateDefaults() : Normalize(preferences);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return CreateDefaults();
        }
    }

    /// <summary>Persists validated command preferences atomically for future application sessions.</summary>
    public void Save(CommandPreferences preferences)
    {
        string? folder = Path.GetDirectoryName(_preferencePath);
        if (folder is null) throw new InvalidOperationException("The command preference folder could not be resolved.");
        Directory.CreateDirectory(folder);
        string temporaryPath = _preferencePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Normalize(preferences), SerializerOptions));
        File.Move(temporaryPath, _preferencePath, true);
    }

    /// <summary>Returns the initial quick access tools and familiar two-key command sequences.</summary>
    public static CommandPreferences CreateDefaults() => new(
        ["Save", "Undo", "Redo", "Wall", "Model3D", "RenderView", "AiAssistant"],
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Wall"] = "WA",
            ["Door"] = "DR",
            ["Window"] = "WN",
            ["Model3D"] = "3D",
            ["RenderView"] = "RN",
            ["AiAssistant"] = "AI",
            ["VisibilityGraphics"] = "VG"
        }, CurrentVersion);

    /// <summary>The layout of the defaults: version 2 added the Render view button beside the 3D view button; version 3 added the AI assistant button.</summary>
    private const int CurrentVersion = 3;

    /// <summary>Removes duplicate identifiers and canonicalizes keyboard sequences.</summary>
    private static CommandPreferences Normalize(CommandPreferences preferences)
    {
        List<string> quickAccess = preferences.QuickAccessCommandIds
            .Where(identifier => !string.IsNullOrWhiteSpace(identifier))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Dictionary<string, string> shortcuts = preferences.Shortcuts
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Trim().ToUpperInvariant(),
                StringComparer.OrdinalIgnoreCase);
        // Preferences saved before version 2 get the Render view button right after the 3D view button, once. After that
        // the user's own choice, including removing it, is kept.
        if (preferences.Version < 2)
        {
            var model = quickAccess.IndexOf("Model3D");
            if (model >= 0 && !quickAccess.Contains("RenderView")) quickAccess.Insert(model + 1, "RenderView");
            shortcuts.TryAdd("RenderView", "RN");
        }
        if (preferences.Version < 3)
        {
            var render = quickAccess.IndexOf("RenderView");
            if (!quickAccess.Contains("AiAssistant")) quickAccess.Insert(render >= 0 ? render + 1 : quickAccess.Count, "AiAssistant");
            shortcuts.TryAdd("AiAssistant", "AI");
        }
        return new CommandPreferences(quickAccess, shortcuts, CurrentVersion);
    }
}

/// <summary>
/// Captures user-selected quick access commands and sequential keyboard shortcuts.
/// </summary>
public sealed record CommandPreferences(
    IReadOnlyList<string> QuickAccessCommandIds,
    IReadOnlyDictionary<string, string> Shortcuts,
    int Version = 0);
