using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Guanacaste3D.Agent;

/// <summary>
/// Chat Completions wire format, used for OpenAI and for any compatible server such as Ollama or LM Studio.
/// The API key is optional because local servers do not need one.
/// </summary>
public sealed class OpenAiCompatibleProvider(HttpClient http, string? apiKey, string model, string baseUrl,
    string name = "OpenAI", bool useMaxCompletionTokens = true) : IAiProvider
{
    public const string OpenAiBaseUrl = "https://api.openai.com/v1";
    public const string OllamaBaseUrl = "http://localhost:11434/v1";

    private readonly Uri _endpoint = new(baseUrl.TrimEnd('/') + "/chat/completions");

    public string Name => name;

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellation = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(BuildBody(request).ToJsonString(), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(apiKey)) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await http.SendAsync(message, cancellation);
        var body = await response.Content.ReadAsStringAsync(cancellation);
        if (!response.IsSuccessStatusCode)
            throw new AiProviderException($"{name} returned {(int)response.StatusCode}: {Truncate(body)}", (int)response.StatusCode);
        return ParseResponse(body);
    }

    internal JsonObject BuildBody(ChatRequest request)
    {
        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = request.System } };
        foreach (var chat in request.Messages)
        {
            if (chat.ToolResults is { Count: > 0 })
            {
                foreach (var result in chat.ToolResults)
                    messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = result.CallId, ["content"] = result.Content });
                if (string.IsNullOrEmpty(chat.Text)) continue;
            }
            var entry = new JsonObject
            {
                ["role"] = chat.Role == ChatRole.User ? "user" : "assistant",
                ["content"] = string.IsNullOrEmpty(chat.Text) ? null : chat.Text
            };
            if (chat.ToolCalls is { Count: > 0 })
            {
                var calls = new JsonArray();
                foreach (var call in chat.ToolCalls)
                    calls.Add(new JsonObject
                    {
                        ["id"] = call.Id,
                        ["type"] = "function",
                        ["function"] = new JsonObject { ["name"] = call.Name, ["arguments"] = call.Arguments.ToJsonString() }
                    });
                entry["tool_calls"] = calls;
            }
            messages.Add(entry);
        }
        var tools = new JsonArray();
        foreach (var tool in request.Tools)
            tools.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = tool.InputSchema.DeepClone()
                }
            });
        var body = new JsonObject { ["model"] = model, ["messages"] = messages };
        body[useMaxCompletionTokens ? "max_completion_tokens" : "max_tokens"] = request.MaxTokens;
        if (tools.Count > 0) body["tools"] = tools;
        return body;
    }

    internal static ChatResponse ParseResponse(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new AiProviderException("The reply is not a JSON object.");
        var choice = (root["choices"] as JsonArray)?.FirstOrDefault() as JsonObject
            ?? throw new AiProviderException("The reply has no choices.");
        var message = choice["message"] as JsonObject ?? throw new AiProviderException("The reply has no message.");
        var calls = new List<ToolCall>();
        foreach (var call in message["tool_calls"] as JsonArray ?? [])
        {
            if (call?["function"] is not JsonObject function) continue;
            var arguments = function["arguments"]?.GetValue<string>();
            JsonObject parsed;
            try { parsed = string.IsNullOrWhiteSpace(arguments) ? new JsonObject() : (JsonNode.Parse(arguments) as JsonObject ?? new JsonObject()); }
            catch (System.Text.Json.JsonException) { parsed = new JsonObject(); }
            calls.Add(new ToolCall(call["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
                function["name"]!.GetValue<string>(), parsed));
        }
        var stop = choice["finish_reason"]?.GetValue<string>() == "length" ? StopReason.MaxTokens : StopReason.EndTurn;
        return new ChatResponse(message["content"]?.GetValue<string>() ?? string.Empty, calls,
            calls.Count > 0 ? StopReason.ToolUse : stop);
    }

    private static string Truncate(string value) => value.Length <= 400 ? value : value[..400] + "...";
}
