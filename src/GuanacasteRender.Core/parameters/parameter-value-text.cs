using System.Globalization;

namespace Guanacaste3D.Core;

/// <summary>
/// Converts between stored parameter values and editable text. Parsing accepts
/// the caller's culture first and invariant notation second, so "2,5" and
/// "2.5" both work in a Spanish UI while files stay culture-independent.
/// </summary>
public static class ParameterValueText
{
    private static readonly string[] YesTokens = ["1", "true", "yes", "y", "sí", "si", "s"];
    private static readonly string[] NoTokens = ["0", "false", "no", "n"];

    /// <summary>Returns the unit suffix displayed after numeric values of a spec.</summary>
    public static string UnitSuffix(ParameterSpec spec) => spec switch
    {
        ParameterSpec.Length => "m",
        ParameterSpec.Area => "m²",
        ParameterSpec.Volume => "m³",
        ParameterSpec.Angle => "°",
        _ => string.Empty
    };

    /// <summary>Formats a stored value for display; yes/no labels are supplied by the caller's localization.</summary>
    public static string Format(ParameterSpec spec, ParameterValue? value, CultureInfo culture,
        string yesLabel = "Yes", string noLabel = "No")
    {
        if (value is null) return string.Empty;
        if (spec == ParameterSpec.Text) return value.Text ?? string.Empty;
        if (value.Number is not double number) return string.Empty;
        return spec switch
        {
            ParameterSpec.YesNo => number == 1 ? yesLabel : noLabel,
            ParameterSpec.Integer => number.ToString("0", culture),
            ParameterSpec.Currency => number.ToString("N2", culture),
            _ => number.ToString("0.###", culture)
        };
    }

    /// <summary>
    /// Parses user input for a spec. Empty input means "clear the value" and
    /// returns true with both outputs null. Unit suffixes such as "m" are ignored.
    /// </summary>
    public static bool TryParse(ParameterSpec spec, string? input, CultureInfo culture,
        out double? number, out string? text)
    {
        number = null;
        text = null;
        var trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length == 0) return true;
        if (spec == ParameterSpec.Text)
        {
            text = trimmed;
            return ParameterRules.IsValueValid(spec, null, text);
        }
        if (spec == ParameterSpec.YesNo)
        {
            var token = trimmed.ToLowerInvariant();
            if (YesTokens.Contains(token)) number = 1;
            else if (NoTokens.Contains(token)) number = 0;
            return number is not null;
        }
        var suffix = UnitSuffix(spec);
        if (suffix.Length > 0 && trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^suffix.Length].TrimEnd();
        // Thousands separators are rejected on purpose: in es-ES "2.5" would
        // otherwise parse as 25 instead of falling back to invariant 2.5.
        const NumberStyles styles = NumberStyles.Float;
        // A lone comma is a decimal comma even when the OS culture uses a
        // point: Spanish-speaking users type "3,5" regardless of locale.
        var decimalComma = trimmed.Contains(',') && !trimmed.Contains('.') ? trimmed.Replace(',', '.') : null;
        if (!double.TryParse(trimmed, styles, culture, out var parsed) &&
            !double.TryParse(trimmed, styles, CultureInfo.InvariantCulture, out parsed) &&
            !(decimalComma is not null &&
              double.TryParse(decimalComma, styles, CultureInfo.InvariantCulture, out parsed)))
            return false;
        number = parsed;
        return ParameterRules.IsValueValid(spec, number, null);
    }
}
