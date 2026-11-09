using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Translates Core validation messages, which are English by design so the
/// core stays UI-independent, into the active UI language. Fixed messages are
/// looked up as "Error." + message. Formula messages carry names and types, so
/// they are matched by pattern and their captured parts are re-inserted into a
/// localized template. Unknown messages pass through unchanged.
/// </summary>
public static partial class LocalizedErrors
{
    /// <summary>Formula message patterns and the resource key of their localized template.</summary>
    private static readonly (Regex Pattern, string Key)[] FormulaPatterns =
    [
        (new(@"^Formula error: Unknown name '(.+)'\.$"), "Error.Formula.UnknownName"),
        (new(@"^Formula error: Unknown function '(.+)'\.$"), "Error.Formula.UnknownFunction"),
        (new(@"^Formula error: Inconsistent units in '(.+)': (.+) and (.+)\.$"), "Error.Formula.InconsistentUnits"),
        (new(@"^Formula error: '(.+)' needs a number, not (.+)\.$"), "Error.Formula.NeedsNumber"),
        (new(@"^Formula error: '(.+)' needs (.+), not (.+)\.$"), "Error.Formula.NeedsKind"),
        (new(@"^Formula error: Both results of if\(\) must have the same type \((.+) vs (.+)\)\.$"), "Error.Formula.IfTypes"),
        (new(@"^Formula error: Cannot compare (.+) with (.+)\.$"), "Error.Formula.Compare"),
        (new(@"^Formula error: '(.+)' produces (.+) but the parameter is (.+)\.$"), "Error.Formula.ResultType"),
        (new(@"^Formula error: '(.+)' depends on itself through other formulas\.$"), "Error.Formula.Cycle"),
        (new(@"^Formula error: (\w+)\(\) takes (\d+) argument\(s\)\.$"), "Error.Formula.ArgumentCount"),
        (new(@"^Formula error: (\w+)\(\) .+\.$"), "Error.Formula.Arguments"),
        (new(@"^Formula error: An exponent must be a plain number\.$"), "Error.Formula.Exponent"),
        (new(@"^Formula error: A length can only be raised to a whole-number literal\.$"), "Error.Formula.LengthPower"),
        (new(@"^(Unexpected text|Unexpected end of formula|Missing '\]'|Empty reference|Invalid number|Missing closing quote|The formula is nested too deeply) at position (\d+) of the formula\.$"),
            "Error.Formula.Syntax"),
        (new(@"^(Unexpected character '.+'|Expected '.+') at position (\d+) of the formula\.$"), "Error.Formula.Syntax")
    ];

    /// <summary>Returns the localized text for a Core exception message.</summary>
    public static string Translate(string message)
    {
        if (Lookup("Error." + message) is { } direct) return direct;
        foreach (var (pattern, key) in FormulaPatterns)
        {
            var match = pattern.Match(message);
            if (!match.Success || Lookup(key) is not { } template) continue;
            var parts = match.Groups.Values.Skip(1).Select(group => (object)TranslatePart(group.Value)).ToArray();
            return string.Format(CultureInfo.CurrentCulture, template, parts);
        }
        return message;
    }

    /// <summary>Translates type words and parser reasons that appear inside formula messages.</summary>
    private static string TranslatePart(string part)
    {
        if (Lookup("Error.FormulaWord." + part) is { } word) return word;
        var quoted = QuotedReason().Match(part);
        return quoted.Success && Lookup("Error.FormulaWord." + quoted.Groups[1].Value) is { } template
            ? string.Format(CultureInfo.CurrentCulture, template, quoted.Groups[2].Value)
            : part;
    }

    /// <summary>Matches parser reasons with a quoted symbol, such as "Expected ')'".</summary>
    [GeneratedRegex(@"^(Expected|Unexpected character) '(.+)'$")]
    private static partial Regex QuotedReason();

    /// <summary>Finds a string resource in the merged application dictionaries.</summary>
    private static string? Lookup(string key) => Application.Current?.TryFindResource(key) as string;
}
