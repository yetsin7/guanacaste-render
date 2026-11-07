namespace Guanacaste3D.Agent;

/// <summary>The backends the assistant can talk to.</summary>
public enum AiProviderKind { Anthropic, OpenAi, Local }

/// <summary>User-chosen connection details for one backend. The key is secret and must never be logged.</summary>
public sealed record AiProviderSettings(AiProviderKind Kind, string Model, string ApiKey = "", string BaseUrl = "")
{
    /// <summary>Sensible starting values for a backend.</summary>
    public static AiProviderSettings DefaultFor(AiProviderKind kind) => kind switch
    {
        AiProviderKind.Anthropic => new(kind, AnthropicProvider.DefaultModel),
        AiProviderKind.OpenAi => new(kind, "gpt-5"),
        _ => new(kind, "qwen3:14b", BaseUrl: OpenAiCompatibleProvider.OllamaBaseUrl)
    };

    /// <summary>True when the settings are complete enough to attempt a request.</summary>
    public bool IsUsable => !string.IsNullOrWhiteSpace(Model) &&
        (Kind == AiProviderKind.Local ? !string.IsNullOrWhiteSpace(BaseUrl) : !string.IsNullOrWhiteSpace(ApiKey));
}

/// <summary>Creates the provider matching a settings record.</summary>
public static class AiProviderFactory
{
    public static IAiProvider Create(AiProviderSettings settings, HttpClient http)
    {
        if (!settings.IsUsable) throw new InvalidOperationException("The AI provider settings are incomplete.");
        return settings.Kind switch
        {
            AiProviderKind.Anthropic => new AnthropicProvider(http, settings.ApiKey, settings.Model,
                string.IsNullOrWhiteSpace(settings.BaseUrl) ? null : settings.BaseUrl),
            AiProviderKind.OpenAi => new OpenAiCompatibleProvider(http, settings.ApiKey, settings.Model,
                string.IsNullOrWhiteSpace(settings.BaseUrl) ? OpenAiCompatibleProvider.OpenAiBaseUrl : settings.BaseUrl),
            _ => new OpenAiCompatibleProvider(http, settings.ApiKey, settings.Model, settings.BaseUrl,
                "Local", useMaxCompletionTokens: false)
        };
    }
}
