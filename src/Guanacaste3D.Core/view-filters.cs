using System.Globalization;

namespace Guanacaste3D.Core;

/// <summary>The property of a wall that a view filter looks at.</summary>
public enum ViewFilterProperty { WallType, Thickness, Height, Length }

/// <summary>How a view filter compares a wall's property with its value.</summary>
public enum ViewFilterOperator { Equals, Contains, Greater, Less }

/// <summary>
/// A rule that hides, in one view, every wall it matches: the wall type is or contains some text, or the thickness,
/// height or length (in meters) is equal to, greater than or less than a number. Filters never change the walls
/// themselves, and other views keep showing them.
/// </summary>
public sealed record ViewFilter(string Name, ViewFilterProperty Property, ViewFilterOperator Operator, string Value)
{
    /// <summary>Whether two lengths are the same wall dimension; a 5 mm difference is not one a drawing can show.</summary>
    private const double EqualTolerance = 0.005;

    private bool IsText => Property == ViewFilterProperty.WallType;

    private bool TryNumber(out double number) =>
        double.TryParse(Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);

    /// <summary>A named rule whose operator suits its property and whose value is a number when the property is one.</summary>
    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && Name.Length <= 120 && !string.IsNullOrWhiteSpace(Value) && Value.Length <= 120 &&
        Enum.IsDefined(Property) && Enum.IsDefined(Operator) &&
        (IsText ? Operator is ViewFilterOperator.Equals or ViewFilterOperator.Contains
            : Operator != ViewFilterOperator.Contains && TryNumber(out _));

    /// <summary>Whether the filter hides this wall.</summary>
    public bool Matches(BuildingDocument document, Wall wall)
    {
        if (!IsValid) return false;
        if (IsText)
        {
            var typeName = document.WallTypes.FirstOrDefault(type => type.Id == wall.WallTypeId)?.Name ?? string.Empty;
            return Operator == ViewFilterOperator.Equals
                ? typeName.Equals(Value.Trim(), StringComparison.CurrentCultureIgnoreCase)
                : typeName.Contains(Value.Trim(), StringComparison.CurrentCultureIgnoreCase);
        }
        TryNumber(out var number);
        var actual = Property switch
        {
            ViewFilterProperty.Thickness => wall.Thickness,
            ViewFilterProperty.Height => wall.Height,
            _ => wall.Length
        };
        return Operator switch
        {
            ViewFilterOperator.Greater => actual > number,
            ViewFilterOperator.Less => actual < number,
            _ => Math.Abs(actual - number) < EqualTolerance
        };
    }

    /// <summary>The rule as it is typed: "type contains cobogó", "thickness &gt; 0.25".</summary>
    public override string ToString() => $"{Name}: {Property} {Operator} {Value}";
}

/// <summary>Reads a filter from the short text a person types, in Spanish or English.</summary>
public static class ViewFilterParser
{
    private static readonly (string Word, ViewFilterProperty Property)[] Properties =
    [
        ("tipo", ViewFilterProperty.WallType), ("type", ViewFilterProperty.WallType),
        ("espesor", ViewFilterProperty.Thickness), ("grosor", ViewFilterProperty.Thickness), ("thickness", ViewFilterProperty.Thickness),
        ("altura", ViewFilterProperty.Height), ("alto", ViewFilterProperty.Height), ("height", ViewFilterProperty.Height),
        ("largo", ViewFilterProperty.Length), ("longitud", ViewFilterProperty.Length), ("length", ViewFilterProperty.Length)
    ];

    private static readonly (string Word, ViewFilterOperator Operator)[] Operators =
    [
        ("contiene", ViewFilterOperator.Contains), ("contains", ViewFilterOperator.Contains),
        ("es", ViewFilterOperator.Equals), ("igual", ViewFilterOperator.Equals), ("equals", ViewFilterOperator.Equals), ("=", ViewFilterOperator.Equals),
        (">", ViewFilterOperator.Greater), ("mayor", ViewFilterOperator.Greater), ("greater", ViewFilterOperator.Greater),
        ("<", ViewFilterOperator.Less), ("menor", ViewFilterOperator.Less), ("less", ViewFilterOperator.Less)
    ];

    /// <summary>
    /// Parses "property operator value", for example "tipo contiene cobogó", "espesor &gt; 0.25" or "altura &lt; 2".
    /// The filter takes the typed text as its name. Null when the text is not a rule that makes sense.
    /// </summary>
    public static ViewFilter? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Trim().Split((char)32, 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;
        var property = Properties.FirstOrDefault(entry => entry.Word.Equals(parts[0], StringComparison.CurrentCultureIgnoreCase));
        var comparison = Operators.FirstOrDefault(entry => entry.Word.Equals(parts[1], StringComparison.CurrentCultureIgnoreCase));
        if (property.Word is null || comparison.Word is null) return null;
        var filter = new ViewFilter(text.Trim(), property.Property, comparison.Operator, parts[2].Trim());
        return filter.IsValid ? filter : null;
    }
}

public sealed partial class DocumentSession
{
    /// <summary>The most filters one view can hold.</summary>
    public const int MaximumViewFilters = 50;

    /// <summary>Replaces the filters of a view, as one undoable step.</summary>
    public void SetViewFilters(Guid viewId, IReadOnlyCollection<ViewFilter> filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        if (filters.Count > MaximumViewFilters || filters.Any(filter => filter is null || !filter.IsValid))
            throw new InvalidDataException("The view filter is invalid.");
        if (Document.Views.All(view => view.Id != viewId)) return;
        var kept = filters.ToList();
        Commit("Transaction.SetViewFilters", document =>
        {
            var index = document.Views.FindIndex(view => view.Id == viewId);
            document.Views[index] = document.Views[index] with { Filters = kept };
        });
    }
}
