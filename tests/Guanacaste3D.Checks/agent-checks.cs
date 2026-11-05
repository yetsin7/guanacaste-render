using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Guanacaste3D.Agent;
using Guanacaste3D.Core;

/// <summary>Checks for the AI assistant: provider wire formats, the tool loop, undo grouping, confirmation and budget.</summary>
static class AgentChecks
{
    public static void Run(Action<bool, string> check)
    {
        CheckWireFormats(check);
        CheckBudget(check);
        CheckAgentLoop(check).GetAwaiter().GetResult();
        CheckConfirmationAndErrors(check).GetAwaiter().GetResult();
        CheckHttpProviders(check).GetAwaiter().GetResult();
    }

    private sealed class ScriptedProvider(params ChatResponse[] replies) : IAiProvider
    {
        private int _next;
        public string Name => "Scripted";
        public List<ChatRequest> Requests { get; } = [];

        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellation = default)
        {
            Requests.Add(request);
            return Task.FromResult(replies[Math.Min(_next++, replies.Length - 1)]);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, string, (HttpStatusCode, string)> reply) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellation);
            var (status, text) = reply(request, body);
            return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }

    private static ToolCall Call(string id, string name, string json) => new(id, name, JsonNode.Parse(json)!.AsObject());

    private static ChatResponse Tools(params ToolCall[] calls) => new(string.Empty, calls, StopReason.ToolUse);

    private static ChatResponse Say(string text) => new(text, [], StopReason.EndTurn);

    private static void CheckWireFormats(Action<bool, string> check)
    {
        var anthropic = AnthropicProvider.ParseResponse("""
            {"stop_reason":"tool_use","content":[{"type":"text","text":"Voy."},
             {"type":"tool_use","id":"toolu_1","name":"add_wall","input":{"level":"A","x1":0}}]}
            """);
        check(anthropic.Text == "Voy." && anthropic.ToolCalls.Single().Id == "toolu_1" &&
            anthropic.StopReason == StopReason.ToolUse, "Anthropic reply parses text and tool_use blocks");

        var openai = OpenAiCompatibleProvider.ParseResponse("""
            {"choices":[{"finish_reason":"tool_calls","message":{"content":null,"tool_calls":[
             {"id":"call_1","type":"function","function":{"name":"undo","arguments":"{}"}}]}}]}
            """);
        check(openai.ToolCalls.Single().Name == "undo" && openai.StopReason == StopReason.ToolUse,
            "OpenAI-compatible reply parses function calls with string arguments");

        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "hola"),
            new(ChatRole.Assistant, string.Empty, [Call("c1", "undo", "{}")]),
            new(ChatRole.User, string.Empty, ToolResults: [new("c1", "Undone.", false)])
        };
        var request = new ChatRequest("sys", history, ToolRegistry.CreateDefault().Definitions);
        var a = new AnthropicProvider(new HttpClient(), "k", "m").BuildBody(request);
        check(a["messages"]![2]!["content"]![0]!["type"]!.GetValue<string>() == "tool_result" &&
            a["tools"]![0]!["input_schema"] is not null, "Anthropic request carries tool results and input schemas");
        var o = new OpenAiCompatibleProvider(new HttpClient(), "k", "m", "http://x/v1").BuildBody(request);
        check(o["messages"]![3]!["role"]!.GetValue<string>() == "tool" && o["max_completion_tokens"] is not null,
            "OpenAI request expands tool results into tool messages");
        var local = new OpenAiCompatibleProvider(new HttpClient(), null, "m", "http://x/v1", "Local", false).BuildBody(request);
        check(local["max_tokens"] is not null && local["max_completion_tokens"] is null, "Local servers get max_tokens");
    }

    private static void CheckBudget(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create());
        var level = session.Document.Levels[0].Id;
        session.AddWallsAroundBoundary(level, [new(0, 0), new(10, 0), new(10, 5), new(0, 5)], BuiltInWallTypes.Basic200Id, 3);
        session.AddFloor(level, [new(0, 0), new(10, 0), new(10, 5), new(0, 5)]);
        var wall = session.Document.Walls[0].Id;
        session.AddOpening(wall, OpeningKind.Door, offset: 2.0);
        var budget = BudgetCalculator.Build(session.Document, UnitPriceList.Default(), 0.10m);
        var wallLine = budget.Lines.Single(line => line.Code == UnitPriceList.WallArea);
        var openingArea = session.Document.Openings.Single().Width * session.Document.Openings.Single().Height;
        check(Math.Abs(wallLine.Quantity - (30 + 30 + 15 + 15 - openingArea)) < 0.001, "Budget wall area subtracts openings");
        check(budget.Lines.Single(line => line.Code == UnitPriceList.FloorArea).Quantity == 50, "Budget floor area");
        check(budget.Total == budget.Subtotal + budget.Contingency && budget.Contingency == Math.Round(budget.Subtotal * 0.10m, 2),
            "Budget applies contingency to the subtotal");
        check(budget.ToCsv().Contains("TOTAL_USD"), "Budget exports CSV with a currency total");
    }

    private static async Task CheckAgentLoop(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create());
        var context = new AgentContext(session);
        var level = session.Document.Levels[0];
        var wallTypesBefore = session.Document.WallTypes.Count;

        // The level name is localized, so resolve it the way the model would: from the summary.
        var provider = new ScriptedProvider(
            Tools(Call("1", "get_project_summary", "{}")),
            Tools(
                Call("2", "add_rectangular_walls", $$"""{"level":"{{level.Name}}","x":0,"y":0,"width":6,"depth":4}"""),
                Call("3", "add_slab", $$"""{"level":"{{level.Name}}","x":0,"y":0,"width":6,"depth":4}""")),
            Say("Listo."));
        var runner = new AgentRunner(provider, context, ToolRegistry.CreateDefault(), (_, _) => Task.FromResult(true));
        var answer = await runner.SendAsync("Hazme una caja de 6x4");

        check(answer == "Listo." && session.Document.Walls.Count == 4 && session.Document.Floors.Count == 1,
            "Agent loop executes tool calls against the session and returns the final text");
        check(session.Document.WallTypes.Count == wallTypesBefore, "Agent edits do not disturb unrelated project data");
        session.Undo();
        check(session.Document.Walls.Count == 0 && session.Document.Floors.Count == 0,
            "One assistant batch is a single undo step");
        check(provider.Requests.Count == 3 && runner.History.Count == 6 && runner.History[2].ToolResults!.Count == 1 && runner.History[4].ToolResults!.Count == 2,
            "Tool results are fed back to the model in order");
    }

    private static async Task CheckConfirmationAndErrors(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create());
        var context = new AgentContext(session);
        var path = Path.Combine(Path.GetTempPath(), $"g3-agent-{Guid.NewGuid():N}.ifc");
        var asked = 0;
        var provider = new ScriptedProvider(
            Tools(Call("1", "export_ifc", $$"""{"path":{{JsonValueText(path)}}}""")), Say("No exporté."));
        var runner = new AgentRunner(provider, context, ToolRegistry.CreateDefault(), (_, _) => { asked++; return Task.FromResult(false); });
        await runner.SendAsync("exporta");
        check(asked == 1 && !File.Exists(path), "Sensitive tools ask first and do nothing when declined");

        provider = new ScriptedProvider(
            Tools(Call("1", "export_ifc", $$"""{"path":{{JsonValueText(path)}}}""")), Say("Exportado."));
        runner = new AgentRunner(provider, context, ToolRegistry.CreateDefault(), (_, _) => Task.FromResult(true));
        await runner.SendAsync("exporta");
        try { check(File.Exists(path) && File.ReadAllText(path).StartsWith("ISO-10303-21"), "Approved export writes a valid IFC file"); }
        finally { if (File.Exists(path)) File.Delete(path); }

        provider = new ScriptedProvider(
            Tools(Call("1", "add_wall", """{"level":"nope","x1":0,"y1":0,"x2":1,"y2":0}"""),
                Call("2", "not_a_tool", "{}"),
                Call("3", "export_ifc", """{"path":"relative.ifc"}""")),
            Say("Hubo errores."));
        runner = new AgentRunner(provider, context, ToolRegistry.CreateDefault(), (_, _) => Task.FromResult(true));
        await runner.SendAsync("prueba errores");
        var results = runner.History[2].ToolResults!;
        check(results.Count == 3 && results.All(result => result.IsError) && session.Document.Walls.Count == 0,
            "Bad arguments, unknown tools and relative paths come back as errors without changing the model");
    }

    private static async Task CheckHttpProviders(Action<bool, string> check)
    {
        HttpRequestMessage? seen = null;
        var handler = new StubHandler((request, _) =>
        {
            seen = request;
            return (HttpStatusCode.OK, """{"stop_reason":"end_turn","content":[{"type":"text","text":"ok"}]}""");
        });
        var provider = AiProviderFactory.Create(new AiProviderSettings(AiProviderKind.Anthropic, "claude-sonnet-5-5", "secret"),
            new HttpClient(handler));
        var reply = await provider.CompleteAsync(new ChatRequest("s", [new(ChatRole.User, "hi")], []));
        check(reply.Text == "ok" && seen!.Headers.GetValues("x-api-key").Single() == "secret" &&
            seen.RequestUri!.AbsolutePath == "/v1/messages", "Anthropic provider sends the key header to /v1/messages");

        var failing = AiProviderFactory.Create(new AiProviderSettings(AiProviderKind.OpenAi, "m", "secret"),
            new HttpClient(new StubHandler((_, _) => (HttpStatusCode.Unauthorized, """{"error":"bad key"}"""))));
        var failed = false;
        try { await failing.CompleteAsync(new ChatRequest("s", [new(ChatRole.User, "hi")], [])); }
        catch (AiProviderException ex) { failed = ex.StatusCode == 401 && !ex.Message.Contains("secret"); }
        check(failed, "HTTP failures surface as provider exceptions without leaking the key");
        check(!AiProviderSettings.DefaultFor(AiProviderKind.OpenAi).IsUsable && AiProviderSettings.DefaultFor(AiProviderKind.Local).IsUsable,
            "Cloud providers need a key; local servers do not");
    }

    private static string JsonValueText(string value) => JsonValue.Create(value)!.ToJsonString();
}
