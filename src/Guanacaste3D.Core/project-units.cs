using System.Globalization;

namespace Guanacaste3D.Core;

/// <summary>The unit lengths are shown and typed in. The model itself always keeps meters.</summary>
public enum LengthUnit { Meters, Centimeters, Millimeters, Feet, Inches }

/// <summary>
/// How the project shows and reads measurements: lengths in one unit with a number of decimals, areas and volumes in
/// that unit's square and cube (feet for the imperial units, meters for the rest). Nothing here changes the model.
/// </summary>
public sealed record ProjectUnits(LengthUnit Length, int Decimals)
{
    public const int MaximumDecimals = 6;
    private const double MetersPerFoot = 0.3048;
    private const double MetersPerInch = 0.0254;

    public static ProjectUnits Default { get; } = new(LengthUnit.Meters, 2);

    public bool IsValid => Enum.IsDefined(Length) && Decimals is >= 0 and <= MaximumDecimals;

    private bool Imperial => Length is LengthUnit.Feet or LengthUnit.Inches;

    /// <summary>How many meters one unit of the project's length unit is.</summary>
    public double MetersPerUnit => MetersFor(Length);

    private static double MetersFor(LengthUnit unit) => unit switch
    {
        LengthUnit.Centimeters => 0.01,
        LengthUnit.Millimeters => 0.001,
        LengthUnit.Feet => MetersPerFoot,
        LengthUnit.Inches => MetersPerInch,
        _ => 1
    };

    /// <summary>The short name of the length unit: m, cm, mm, ft or in.</summary>
    public string LengthSymbol => Length switch
    {
        LengthUnit.Centimeters => "cm",
        LengthUnit.Millimeters => "mm",
        LengthUnit.Feet => "ft",
        LengthUnit.Inches => "in",
        _ => "m"
    };

    public string AreaSymbol => Imperial ? "ft²" : "m²";
    public string VolumeSymbol => Imperial ? "ft³" : "m³";

    private string Pattern => "0." + new string('0', Decimals);

    private string Number(double value) => (Decimals == 0 ? value.ToString("0", CultureInfo.CurrentCulture) : value.ToString(Pattern, CultureInfo.CurrentCulture));

    /// <summary>A length in meters as the number the project shows, without its unit.</summary>
    public string LengthNumber(double meters) => Number(meters / MetersPerUnit);

    /// <summary>A length in meters as the project shows it, with its unit: "1.20 m", "120 cm", "3.94 ft".</summary>
    public string FormatLength(double meters) => LengthNumber(meters) + " " + LengthSymbol;

    /// <summary>An area in square meters as the number the project shows (square meters or square feet).</summary>
    public string AreaNumber(double squareMeters) => Number(Imperial ? squareMeters / (MetersPerFoot * MetersPerFoot) : squareMeters);

    /// <summary>A volume in cubic meters as the number the project shows (cubic meters or cubic feet).</summary>
    public string VolumeNumber(double cubicMeters) => Number(Imperial ? cubicMeters / (MetersPerFoot * MetersPerFoot * MetersPerFoot) : cubicMeters);

    /// <summary>The unit a metric unit ("m", "m²", "m³") is shown in; anything else is kept.</summary>
    public string Symbol(string metricUnit) => metricUnit switch { "m" => LengthSymbol, "m²" => AreaSymbol, "m³" => VolumeSymbol, _ => metricUnit };

    /// <summary>A quantity in a metric unit ("m", "m²", "m³") as the number the project shows; null for any other unit.</summary>
    public string? NumberFor(string metricUnit, double value) => metricUnit switch
    {
        "m" => LengthNumber(value),
        "m²" => AreaNumber(value),
        "m³" => VolumeNumber(value),
        _ => null
    };

    public string FormatArea(double squareMeters) => AreaNumber(squareMeters) + " " + AreaSymbol;

    public string FormatVolume(double cubicMeters) =>
        Number(Imperial ? cubicMeters / (MetersPerFoot * MetersPerFoot * MetersPerFoot) : cubicMeters) + " " + VolumeSymbol;

    /// <summary>
    /// Reads a length typed by a person into meters. A plain number is in the project's unit; "m", "cm", "mm", "ft", "in",
    /// and the marks ' and " after the number say otherwise. Decimals may use a point or the culture's separator.
    /// </summary>
    public bool TryParseLength(string? text, out double meters)
    {
        meters = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim();
        var unit = Length;
        foreach (var (suffix, suffixUnit) in new (string, LengthUnit)[]
                 {
                     ("mm", LengthUnit.Millimeters), ("cm", LengthUnit.Centimeters), ("ft", LengthUnit.Feet), ("in", LengthUnit.Inches),
                     ("m", LengthUnit.Meters), ("'", LengthUnit.Feet), ("\"", LengthUnit.Inches)
                 })
        {
            if (!trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            trimmed = trimmed[..^suffix.Length].Trim();
            unit = suffixUnit;
            break;
        }
        if (!TryNumber(trimmed, out var number)) return false;
        meters = number * MetersFor(unit);
        return double.IsFinite(meters);
    }

    /// <summary>Reads an area typed by a person (in square meters or square feet, as the project shows them) into square meters.</summary>
    public bool TryParseArea(string? text, out double squareMeters)
    {
        squareMeters = 0;
        if (string.IsNullOrWhiteSpace(text) || !TryNumber(text.Trim(), out var number)) return false;
        squareMeters = Imperial ? number * MetersPerFoot * MetersPerFoot : number;
        return double.IsFinite(squareMeters);
    }

    private static bool TryNumber(string text, out double number) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out number) && double.IsFinite(number) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);
}

public sealed partial class DocumentSession
{
    /// <summary>Changes how the project shows and reads measurements, as one undoable step. The model is not touched.</summary>
    public void SetProjectUnits(ProjectUnits units)
    {
        ArgumentNullException.ThrowIfNull(units);
        if (!units.IsValid) throw new InvalidDataException("The project units are invalid.");
        if (units == Document.Units) return;
        Commit("Transaction.SetProjectUnits", document => document.Units = units);
    }
}
