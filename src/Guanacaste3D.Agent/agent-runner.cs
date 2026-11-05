using System.Text.Json.Nodes;
using Guanacaste3D.Core;

namespace Guanacaste3D.Agent;

/// <summary>The catalog of tools the assistant may call.</summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, AgentTool> _tools;

    public ToolRegistry(IEnumerable<AgentTool> tools) => _tools = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);

    public IReadOnlyCollection<AgentTool> All => _tools.Values;

    public AgentTool? Find(string name) => _tools.GetValueOrDefault(name);

    public IReadOnlyList<ToolDefinition> Definitions => [.. _tools.Values.Select(tool => tool.Definition)];

    /// <summary>The built-in tools: project queries, modeling, budget and file export.</summary>
    public static ToolRegistry CreateDefault() =>
        new([.. ReadTools.Create(), .. ModelingTools.Create(), .. FileTools.Create()]);
}

/// <summary>What happened during a turn, for the chat transcript.</summary>
public enum AgentEventKind { AssistantText, ToolStarted, ToolFinished, ToolDeclined, Error }

public sealed record AgentEvent(AgentEventKind Kind, string Text, string? ToolName = null, bool IsError = false);

/// <summary>
/// Runs the conversation loop: send the history, execute requested tools against the session, return the results,
/// and repeat until the model answers in plain text. Must be driven from the thread that owns the session.
/// </summary>
public sealed class AgentRunner(IAiProvider provider, AgentContext context, ToolRegistry tools,
    Func<ToolCall, AgentTool, Task<bool>> confirm)
{
    public const string TransactionName = "Transaction.AiAssistant";
    public const int MaxSteps = 14;

    private readonly List<ChatMessage> _history = [];

    public IReadOnlyList<ChatMessage> History => _history;

    public event Action<AgentEvent>? Event;

    /// <summary>Forgets the conversation. The project is untouched.</summary>
    public void Reset() => _history.Clear();

    public async Task<string> SendAsync(string userText, CancellationToken cancellation = default)
    {
        _history.Add(new ChatMessage(ChatRole.User, userText));
        for (var step = 0; step < MaxSteps; step++)
        {
            var response = await provider.CompleteAsync(
                new ChatRequest(SystemPrompt.Text, _history, tools.Definitions), cancellation);
            _history.Add(new ChatMessage(ChatRole.Assistant, response.Text, response.ToolCalls));
            if (!string.IsNullOrWhiteSpace(response.Text)) Event?.Invoke(new(AgentEventKind.AssistantText, response.Text));
            if (response.ToolCalls.Count == 0)
                return response.StopReason == StopReason.MaxTokens
                    ? response.Text + "\n\n[The reply was cut short by the length limit.]" : response.Text;

            var results = await ExecuteAsync(response.ToolCalls);
            _history.Add(new ChatMessage(ChatRole.User, string.Empty, ToolResults: results));
            cancellation.ThrowIfCancellationRequested();
        }
        const string stopped = "I stopped after too many steps. Tell me how to continue.";
        Event?.Invoke(new(AgentEventKind.Error, stopped));
        _history.Add(new ChatMessage(ChatRole.Assistant, stopped));
        return stopped;
    }

    /// <summary>
    /// Runs one batch of tool calls inside a single transaction group, so the whole batch is one undo step.
    /// Confirmations are awaited before the group opens, so the user never edits mid-group.
    /// </summary>
    private async Task<IReadOnlyList<ToolResultMessage>> ExecuteAsync(IReadOnlyList<ToolCall> calls)
    {
        var approvals = new Dictionary<string, bool>();
        foreach (var call in calls)
        {
            var tool = tools.Find(call.Name);
            if (tool is { Risk: ToolRisk.Sensitive }) approvals[call.Id] = await confirm(call, tool);
        }

        var results = new List<ToolResultMessage>();
        using var group = context.Session.BeginGroup(TransactionName);
        foreach (var call in calls)
        {
            var tool = tools.Find(call.Name);
            if (tool is null)
            {
                results.Add(new(call.Id, $"Unknown tool '{call.Name}'.", true));
                continue;
            }
            if (approvals.TryGetValue(call.Id, out var allowed) && !allowed)
            {
                Event?.Invoke(new(AgentEventKind.ToolDeclined, "Declined by the user.", tool.Name));
                results.Add(new(call.Id, "The user declined this action.", true));
                continue;
            }
            Event?.Invoke(new(AgentEventKind.ToolStarted, call.Arguments.ToJsonString(), tool.Name));
            try
            {
                var output = tool.Run(context, new ToolArguments(call.Arguments, context.Session));
                Event?.Invoke(new(AgentEventKind.ToolFinished, Shorten(output), tool.Name));
                results.Add(new(call.Id, output, false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Session commits are atomic, so a rejected edit leaves the model unchanged; the model gets the reason.
                Event?.Invoke(new(AgentEventKind.ToolFinished, ex.Message, tool.Name, IsError: true));
                results.Add(new(call.Id, ex.Message, true));
            }
        }
        group.Assimilate();
        return results;
    }

    private static string Shorten(string value) => value.Length <= 300 ? value : value[..300] + "...";
}

/// <summary>The standing instructions given to the model on every request.</summary>
public static class SystemPrompt
{
    public const string Text =
        "You are the built-in assistant of Guanacaste 3D, a BIM program for architects and engineers. " +
        "You design by calling tools that edit the real project: levels, walls, doors, windows, rooms, slabs, columns, " +
        "quantities, budget and exports. Reply in the user's language, concisely.\n" +
        "Rules: all lengths are meters, Z is up and +Y is project north. Call get_project_summary before editing, " +
        "because ids and level names come from it; never invent ids. Plan multi-step work, then execute it with tools " +
        "and verify with a query. Every model edit can be undone. Exports write files and ask the user first. " +
        "Budget prices are placeholders until the user supplies local prices, so say so when reporting totals. " +
        "If a tool returns an error, read it, fix the arguments and retry once before asking the user.";
}
