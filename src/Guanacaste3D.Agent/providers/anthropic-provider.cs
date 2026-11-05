using System.Text;
using System.Text.Json.Nodes;

namespace Guanacaste3D.Agent;

/// <summary>Claude through the Messages API, authenticated with the user's own API key.</summary>
public sealed class AnthropicProvider(HttpClient http, string apiKey, string model, string? baseUrl = null) : IAiProvider
{
    public const string DefaultBaseUrl = "https://api.anthropic.com";
    public const string DefaultModel = "claude-sonnet-5-5";

    private readonly Uri _endpoint = new((baseUrl ?? DefaultBaseUrl).TrimEnd('/') + "/v1/messages");

    public string Name => "Anthropic";

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellation = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(BuildBody(request).ToJsonString(), Encoding.UTF8, "application/json")
        };
        message.Headers.Add("x-api-key", apiKey);
        message.Headers.Add("anthropic-version", "2023-06-01");
        using var response = await http.SendAsync(message, cancellation);
        var body = await response.Content.ReadAsStringAsync(cancellation);
        if (!response.IsSuccessStatusCode)
            throw new AiProviderException($"Anthropic returned {(int)response.StatusCode}: {Truncate(body)}", (int)response.StatusCode);
        return ParseResponse(body);
    }

    internal JsonObject BuildBody(ChatRequest request)
    {
        var messages = new JsonArray();
        foreach (var chat in request.Messages)
        {
            var content = new JsonArray();
            if (chat.ToolResults is { Count: > 0 })
                foreach (var result in chat.ToolResults)
                    content.Add(new JsonObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = result.CallId,
                        ["content"] = result.Content,
                        ["is_error"] = result.IsError
                    });
            if (!string.IsNullOrEmpty(chat.Text)) content.Add(new JsonObject { ["type"] = "text", ["text"] = chat.Text });
            if (chat.ToolCalls is { Count: > 0 })
                foreach (var call in chat.ToolCalls)
                    content.Add(new JsonObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = call.Id,
                        ["name"] = call.Name,
                        ["input"] = call.Arguments.DeepClone()
                    });
            messages.Add(new JsonObject { ["role"] = chat.Role == ChatRole.User ? "user" : "assistant", ["content"] = content });
        }
        var tools = new JsonArray();
        foreach (var tool in request.Tools)
            tools.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["input_schema"] = tool.InputSchema.DeepClone()
            });
        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = request.MaxTokens,
            ["system"] = request.System,
            ["messages"] = messages
        };
        if (tools.Count > 0) body["tools"] = tools;
        return body;
    }

    internal static ChatResponse ParseResponse(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new AiProviderException("The reply is not a JSON object.");
        var text = new StringBuilder();
        var calls = new List<ToolCall>();
        foreach (var block in root["content"] as JsonArray ?? [])
        {
            switch (block?["type"]?.GetValue<string>())
            {
                case "text": text.Append(block["text"]?.GetValue<string>()); break;
                case "tool_use":
                    calls.Add(new ToolCall(block["id"]!.GetValue<string>(), block["name"]!.GetValue<string>(),
                        block["input"] as JsonObject ?? new JsonObject()));
                    break;
            }
        }
        var stop = root["stop_reason"]?.GetValue<string>() == "max_tokens" ? StopReason.MaxTokens : StopReason.EndTurn;
        return new ChatResponse(text.ToString(), calls, calls.Count > 0 ? StopReason.ToolUse : stop);
    }

    private static string Truncate(string value) => value.Length <= 400 ? value : value[..400] + "...";
}
