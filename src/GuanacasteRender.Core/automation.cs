using System.Text;
using System.Text.RegularExpressions;

namespace Guanacaste3D.Core;

/// <summary>One command of a script and the answers it gives to the questions that command asks, in order.</summary>
public sealed record AutomationStep(string CommandId, List<string> Answers);

/// <summary>A saved sequence of commands that can be played again.</summary>
public sealed record AutomationScript(Guid Id, string Name, List<AutomationStep> Steps);

/// <summary>
/// Scripts are written one step per line: the command, then its answers separated by bars ("Rebar | 3 0.016 abajo"). Blank lines and
/// lines starting with # are ignored. The commands themselves are checked where they run, because only the application knows them.
/// </summary>
public static partial class AutomationScripts
{
    public const int MaximumScripts = 100;
    public const int MaximumSteps = 300;
    public const int MaximumAnswers = 20;
    public const int MaximumAnswerLength = 500;

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.]{0,63}$")]
    private static partial Regex CommandPattern();

    public static bool IsValidStep(AutomationStep? step) =>
        step is not null && step.CommandId is not null && CommandPattern().IsMatch(step.CommandId) && step.Answers is not null &&
        step.Answers.Count <= MaximumAnswers && step.Answers.All(answer => !string.IsNullOrWhiteSpace(answer) && answer.Length <= MaximumAnswerLength && !answer.Contains('|') && !answer.Contains('\n'));

    public static bool IsValid(AutomationScript? script) =>
        script is not null && script.Id != Guid.Empty && !string.IsNullOrWhiteSpace(script.Name) && script.Name.Length <= 120 && script.Steps is not null &&
        script.Steps.Count is >= 1 and <= MaximumSteps && script.Steps.All(IsValidStep);

    /// <summary>Reads a script written as text; a line that is not a command followed by answers is an error that names the line.</summary>
    public static List<AutomationStep> Parse(string? text)
    {
        var steps = new List<AutomationStep>();
        foreach (var raw in (text ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var parts = line.Split('|', StringSplitOptions.TrimEntries);
            var step = new AutomationStep(parts[0], [.. parts.Skip(1).Where(answer => answer.Length > 0)]);
            if (!IsValidStep(step)) throw new InvalidDataException("The script has an invalid line.");
            steps.Add(step);
            if (steps.Count > MaximumSteps) throw new InvalidDataException("The script has too many steps.");
        }
        if (steps.Count == 0) throw new InvalidDataException("The script has no steps.");
        return steps;
    }

    public static string ToText(IEnumerable<AutomationStep> steps)
    {
        var text = new StringBuilder();
        foreach (var step in steps) text.AppendLine(step.Answers.Count == 0 ? step.CommandId : $"{step.CommandId} | {string.Join(" | ", step.Answers)}");
        return text.ToString();
    }
}

public sealed partial class DocumentSession
{
    /// <summary>Saves a script under a name, replacing the one with that name if there is one. Returns its identity.</summary>
    public Guid SaveAutomation(string name, string text)
    {
        var clean = name?.Trim() ?? string.Empty;
        if (clean.Length == 0 || clean.Length > 120) throw new InvalidDataException("The name is empty or too long.");
        var steps = AutomationScripts.Parse(text);
        var existing = Document.Automations.FirstOrDefault(item => item.Name.Equals(clean, StringComparison.CurrentCultureIgnoreCase));
        if (existing is null && Document.Automations.Count >= AutomationScripts.MaximumScripts) throw new InvalidDataException("The project has too many scripts.");
        var script = new AutomationScript(existing?.Id ?? Guid.NewGuid(), clean, steps);
        Commit("Transaction.SaveAutomation", document =>
        {
            var index = document.Automations.FindIndex(item => item.Id == script.Id);
            if (index < 0) document.Automations.Add(script);
            else document.Automations[index] = script;
        });
        return script.Id;
    }

    public void RemoveAutomation(Guid id)
    {
        if (Document.Automations.All(item => item.Id != id)) throw new InvalidDataException("The script does not exist.");
        Commit("Transaction.RemoveAutomation", document => document.Automations.RemoveAll(item => item.Id == id));
    }
}
