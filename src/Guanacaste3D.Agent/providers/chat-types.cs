using System.Text.Json.Nodes;

namespace Guanacaste3D.Agent;

/// <summary>Who wrote a chat message. Tool results travel as user messages, as the providers expect.</summary>
public enum ChatRole { User, Assistant }

/// <summary>A tool invocation requested by the model.</summary>
public sealed record ToolCall(string Id, string Name, JsonObject Arguments);

/// <summary>The outcome of one tool invocation, returned to the model.</summary>
public sealed record ToolResultMessage(string CallId, string Content, bool IsError);

/// <summary>One turn of the conversation, kept provider-neutral.</summary>
public sealed record ChatMessage(ChatRole Role, string Text,
    IReadOnlyList<ToolCall>? ToolCalls = null, IReadOnlyList<ToolResultMessage>? ToolResults = null);

/// <summary>A tool the model may call, described by a JSON schema for its arguments.</summary>
public sealed record ToolDefinition(string Name, string Description, JsonObject InputSchema);

/// <summary>Everything a provider needs for one completion.</summary>
public sealed record ChatRequest(string System, IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<ToolDefinition> Tools, int MaxTokens = 4096);

/// <summary>Why the model stopped generating.</summary>
public enum StopReason { EndTurn, ToolUse, MaxTokens }

/// <summary>A model reply: text, requested tool calls, and the stop reason.</summary>
public sealed record ChatResponse(string Text, IReadOnlyList<ToolCall> ToolCalls, StopReason StopReason);

/// <summary>Raised when a provider rejects a request or returns an unusable reply.</summary>
public sealed class AiProviderException(string message, int? statusCode = null) : Exception(message)
{
    public int? StatusCode { get; } = statusCode;
}

/// <summary>A language-model backend. Implementations translate the neutral types to one vendor's wire format.</summary>
public interface IAiProvider
{
    string Name { get; }
    Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellation = default);
}
