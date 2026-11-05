using System.Globalization;
using System.Text.Json.Nodes;
using Guanacaste3D.Core;

namespace Guanacaste3D.Agent;

/// <summary>How much trust a tool needs: reads and model edits run freely (edits are undoable), file writes ask first.</summary>
public enum ToolRisk { Read, Modify, Sensitive }

/// <summary>State shared by the tools of one assistant conversation.</summary>
public sealed class AgentContext(DocumentSession session)
{
    public DocumentSession Session { get; } = session;
    public UnitPriceList Prices { get; set; } = UnitPriceList.Default();
}

/// <summary>A capability exposed to the model: a schema for its arguments and the code that carries it out.</summary>
public sealed record AgentTool(string Name, string Description, JsonObject InputSchema, ToolRisk Risk,
    Func<AgentContext, ToolArguments, string> Run)
{
    public ToolDefinition Definition => new(Name, Description, InputSchema);
}

/// <summary>Typed, defensive access to model-supplied arguments. Failures become messages the model can act on.</summary>
public sealed class ToolArguments(JsonObject raw, DocumentSession session)
{
    public JsonObject Raw { get; } = raw;

    public string Text(string name, string? fallback = null)
    {
        var value = Raw[name]?.ToString();
        if (!string.IsNullOrWhiteSpace(value)) return value;
        return fallback ?? throw new InvalidDataException($"Missing required argument '{name}'.");
    }

    public double Number(string name)
    {
        var node = Raw[name] ?? throw new InvalidDataException($"Missing required argument '{name}'.");
        return ToDouble(node, name);
    }

    public double Number(string name, double fallback) => Raw[name] is { } node ? ToDouble(node, name) : fallback;

    public double? OptionalNumber(string name) => Raw[name] is { } node ? ToDouble(node, name) : null;

    public Guid Id(string name)
    {
        var value = Text(name);
        return Guid.TryParse(value, out var id) ? id : throw new InvalidDataException($"'{name}' must be an id returned by a tool.");
    }

    /// <summary>Accepts a level id, an exact name, or a name ignoring case.</summary>
    public Guid Level(string name = "level")
    {
        var value = Text(name);
        var levels = session.Document.Levels;
        if (Guid.TryParse(value, out var id) && levels.Any(level => level.Id == id)) return id;
        var match = levels.FirstOrDefault(level => string.Equals(level.Name, value, StringComparison.OrdinalIgnoreCase));
        return match?.Id ?? throw new InvalidDataException(
            $"No level matches '{value}'. Known levels: {string.Join(", ", levels.Select(l => l.Name))}.");
    }

    private static double ToDouble(JsonNode node, string name)
    {
        try
        {
            var value = node.GetValueKind() == System.Text.Json.JsonValueKind.String
                ? double.Parse(node.GetValue<string>(), CultureInfo.InvariantCulture)
                : node.GetValue<double>();
            return double.IsFinite(value) ? value : throw new InvalidDataException($"'{name}' must be a finite number.");
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            throw new InvalidDataException($"'{name}' must be a number in meters or the unit the tool states.");
        }
    }
}

/// <summary>Small helpers to declare JSON schemas without repeating boilerplate.</summary>
public static class Schema
{
    public static JsonObject Object(params (string Name, JsonObject Type, bool Required)[] properties)
    {
        var props = new JsonObject();
        var required = new JsonArray();
        foreach (var (name, type, isRequired) in properties)
        {
            props[name] = type;
            if (isRequired) required.Add(name);
        }
        var schema = new JsonObject { ["type"] = "object", ["properties"] = props };
        if (required.Count > 0) schema["required"] = required;
        return schema;
    }

    public static JsonObject Str(string description, params string[] allowed)
    {
        var node = new JsonObject { ["type"] = "string", ["description"] = description };
        if (allowed.Length > 0) node["enum"] = new JsonArray(allowed.Select(value => (JsonNode)value).ToArray());
        return node;
    }

    public static JsonObject Num(string description) => new() { ["type"] = "number", ["description"] = description };
}
