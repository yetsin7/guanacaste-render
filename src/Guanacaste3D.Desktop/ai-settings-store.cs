using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Guanacaste3D.Agent;

namespace Guanacaste3D.Desktop;

/// <summary>The saved choice of provider plus the settings of every provider, with API keys encrypted per Windows user.</summary>
internal sealed record AiStoredSettings(AiProviderKind Selected, IReadOnlyDictionary<AiProviderKind, AiProviderSettings> Providers)
{
    public AiProviderSettings Current => Providers.TryGetValue(Selected, out var settings)
        ? settings : AiProviderSettings.DefaultFor(Selected);
}

/// <summary>Persists assistant settings under the user's local application data. Keys are protected with DPAPI.</summary>
internal sealed class AiSettingsStore(string? path = null)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Guanacaste3D", "ai-settings.json");

    private sealed record Entry(string Model = "", string BaseUrl = "", string ProtectedKey = "");

    private sealed record FileModel(AiProviderKind Selected = AiProviderKind.Anthropic, Dictionary<AiProviderKind, Entry>? Providers = null);

    public AiStoredSettings Load()
    {
        var providers = Enum.GetValues<AiProviderKind>().ToDictionary(kind => kind, AiProviderSettings.DefaultFor);
        var selected = AiProviderKind.Anthropic;
        try
        {
            if (File.Exists(_path))
            {
                var model = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(_path), Options);
                if (model is not null)
                {
                    selected = model.Selected;
                    foreach (var (kind, entry) in model.Providers ?? [])
                    {
                        var fallback = AiProviderSettings.DefaultFor(kind);
                        providers[kind] = new AiProviderSettings(kind,
                            string.IsNullOrWhiteSpace(entry.Model) ? fallback.Model : entry.Model,
                            Unprotect(entry.ProtectedKey), entry.BaseUrl);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or CryptographicException)
        {
            // A damaged settings file must never stop the application: fall back to defaults.
        }
        return new AiStoredSettings(selected, providers);
    }

    public void Save(AiStoredSettings settings)
    {
        var model = new FileModel(settings.Selected, settings.Providers.ToDictionary(pair => pair.Key,
            pair => new Entry(pair.Value.Model, pair.Value.BaseUrl, Protect(pair.Value.ApiKey))));
        var folder = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("The settings folder could not be resolved.");
        Directory.CreateDirectory(folder);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(model, Options));
        File.Move(temporary, _path, true);
    }

    private static string Protect(string secret) => string.IsNullOrEmpty(secret) ? string.Empty
        : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser));

    private static string Unprotect(string stored) => string.IsNullOrEmpty(stored) ? string.Empty
        : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), null, DataProtectionScope.CurrentUser));
}
