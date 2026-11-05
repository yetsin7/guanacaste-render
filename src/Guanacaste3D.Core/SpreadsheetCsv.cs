using System.Globalization;

namespace Guanacaste3D.Core;

/// <summary>Consistent RFC-style CSV quoting and spreadsheet formula-prefix mitigation.</summary>
internal static class SpreadsheetCsv
{
    /// <summary>
    /// Quotes a user-controlled cell and prefixes dangerous formula starters with an apostrophe.
    /// Preserve numeric values only when exporting typed schedule cells, not authored labels.
    /// </summary>
    public static string Quote(string value, bool preserveNumeric = false)
    {
        ArgumentNullException.ThrowIfNull(value);
        var trimmed = value.AsSpan().TrimStart();
        var formula = !trimmed.IsEmpty && trimmed[0] is '=' or '+' or '-' or '@';
        var numeric = preserveNumeric && double.TryParse(value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out _);
        var safe = formula && !numeric ? "'" + value : value;
        return "\"" + safe.Replace("\"", "\"\"") + "\"";
    }
}
