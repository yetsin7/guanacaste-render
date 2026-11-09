namespace Guanacaste3D.Core;

/// <summary>
/// Data kind of a parameter value. The spec decides storage (number or text),
/// validation rules, and the unit used for display: lengths are meters, areas
/// square meters, volumes cubic meters, and angles degrees.
/// </summary>
public enum ParameterSpec
{
    Text,
    Integer,
    Number,
    Length,
    Area,
    Volume,
    Angle,
    YesNo,
    Currency
}

/// <summary>Element categories that user-defined parameters can be bound to.</summary>
public enum ElementCategory
{
    Walls,
    Doors,
    Windows,
    Rooms,
    Floors,
    Levels,
    Sheets
}

/// <summary>
/// Selects where a parameter value lives: on each element instance, or once on
/// the shared type so every instance of that type reports the same value.
/// </summary>
public enum ParameterBinding
{
    Instance,
    Type
}

/// <summary>
/// A project parameter definition. The identifier is stable across renames so
/// values, schedules, and future shared-definition files can reference it.
/// </summary>
public sealed record ParameterDefinition(
    Guid Id,
    string Name,
    ParameterSpec Spec,
    ParameterBinding Binding,
    List<ElementCategory> Categories,
    string Group)
{
    /// <summary>Optional tooltip-style guidance shown next to the value.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Optional formula. When set, the parameter is calculated for each element
    /// from other parameters and model quantities, and is never stored or edited.
    /// </summary>
    public string Formula { get; init; } = string.Empty;

    /// <summary>Returns whether values of this definition are stored as text.</summary>
    public bool StoresText => Spec == ParameterSpec.Text;
}

/// <summary>
/// One stored parameter value. <see cref="OwnerId"/> is the element instance for
/// instance-bound definitions, or the element type (for example a wall type) for
/// type-bound definitions. Exactly one of <see cref="Number"/> or
/// <see cref="Text"/> is set, according to the definition's spec.
/// </summary>
public sealed record ParameterValue(Guid DefinitionId, Guid OwnerId, double? Number, string? Text);

/// <summary>Lists which categories currently support type-bound parameters.</summary>
public static class ParameterCategoryRules
{
    /// <summary>Categories whose elements reference a reusable type in this schema.</summary>
    public static readonly IReadOnlySet<ElementCategory> TypedCategories =
        new HashSet<ElementCategory> { ElementCategory.Walls, ElementCategory.Doors, ElementCategory.Windows };

    /// <summary>Returns whether a binding is allowed for every category in the set.</summary>
    public static bool SupportsBinding(ParameterBinding binding, IEnumerable<ElementCategory> categories) =>
        binding == ParameterBinding.Instance || categories.All(TypedCategories.Contains);
}
