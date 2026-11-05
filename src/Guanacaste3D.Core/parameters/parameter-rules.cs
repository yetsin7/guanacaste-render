namespace Guanacaste3D.Core;

/// <summary>Validates, regenerates, and resolves project parameters and their values.</summary>
public static class ParameterRules
{
    public const int MaximumDefinitions = 2000;
    public const int MaximumValues = 2000000;
    public const int MaximumNameLength = 80;
    public const int MaximumTextLength = 1000;

    /// <summary>Returns whether a stored value satisfies the storage and range rules of a spec.</summary>
    public static bool IsValueValid(ParameterSpec spec, double? number, string? text)
    {
        if (spec == ParameterSpec.Text)
            return number is null && text is not null && text.Length <= MaximumTextLength;
        if (text is not null || number is not double value || !double.IsFinite(value)) return false;
        return spec switch
        {
            ParameterSpec.Integer => value == Math.Round(value) && Math.Abs(value) <= int.MaxValue,
            ParameterSpec.YesNo => value is 0 or 1,
            ParameterSpec.Area or ParameterSpec.Volume => value >= 0,
            _ => true
        };
    }

    /// <summary>Rejects inconsistent definitions or values; never mutates the document.</summary>
    public static void Validate(BuildingDocument document)
    {
        var definitions = document.ParameterDefinitions;
        var values = document.ParameterValues;
        if (definitions is null || values is null || definitions.Count > MaximumDefinitions ||
            values.Count > MaximumValues || definitions.Any(definition => definition is null) ||
            values.Any(value => value is null))
            throw new InvalidDataException("The project parameter collections are invalid.");
        foreach (var definition in definitions)
        {
            if (definition.Id == Guid.Empty || string.IsNullOrWhiteSpace(definition.Name) ||
                definition.Name.Length > MaximumNameLength || definition.Name != definition.Name.Trim() ||
                !Enum.IsDefined(definition.Spec) || !Enum.IsDefined(definition.Binding) ||
                definition.Categories is null || definition.Categories.Count == 0 ||
                definition.Categories.Any(category => !Enum.IsDefined(category)) ||
                definition.Categories.Distinct().Count() != definition.Categories.Count ||
                !ParameterCategoryRules.SupportsBinding(definition.Binding, definition.Categories) ||
                definition.Group is null || definition.Group.Length > MaximumNameLength ||
                definition.Description is null || definition.Description.Length > MaximumTextLength ||
                definition.Formula is null || definition.Formula.Length > 2000 ||
                definition.Formula != definition.Formula.Trim())
                throw new InvalidDataException("A project parameter definition is invalid.");
            if (FormulaEngine.IsCalculated(definition) && definition.Binding != ParameterBinding.Instance)
                throw new InvalidDataException("Calculated parameters must use instance binding.");
        }
        if (definitions.Select(definition => definition.Id).Distinct().Count() != definitions.Count ||
            definitions.Select(definition => definition.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != definitions.Count)
            throw new InvalidDataException("Project parameter identifiers and names must be unique.");

        FormulaEngine.ValidateDefinitions(document);
        var byId = definitions.ToDictionary(definition => definition.Id);
        var elements = ElementCatalog.Index(document);
        var types = TypeCategories(document);
        var keys = new HashSet<(Guid, Guid)>();
        foreach (var value in values)
        {
            if (!byId.TryGetValue(value.DefinitionId, out var definition) || FormulaEngine.IsCalculated(definition) ||
                !IsOwnerValid(definition, value.OwnerId, elements, types) ||
                !IsValueValid(definition.Spec, value.Number, value.Text) ||
                !keys.Add((value.DefinitionId, value.OwnerId)))
                throw new InvalidDataException("A project parameter value is invalid or orphaned.");
        }
    }

    /// <summary>
    /// Regeneration step run inside each transaction: drops values whose
    /// definition, owner, or category binding no longer exists, so deleting an
    /// element or narrowing a binding never leaves orphaned data behind.
    /// </summary>
    public static int RemoveOrphanedValues(BuildingDocument document)
    {
        var byId = document.ParameterDefinitions.ToDictionary(definition => definition.Id);
        var elements = ElementCatalog.Index(document);
        var types = TypeCategories(document);
        return document.ParameterValues.RemoveAll(value =>
            !byId.TryGetValue(value.DefinitionId, out var definition) || FormulaEngine.IsCalculated(definition) ||
            !IsOwnerValid(definition, value.OwnerId, elements, types));
    }

    /// <summary>Returns the identifier that stores a definition's value for an element.</summary>
    public static Guid? ResolveOwner(ParameterDefinition definition, ElementEntry element)
    {
        if (!definition.Categories.Contains(element.Category)) return null;
        return definition.Binding == ParameterBinding.Instance ? element.Id : element.TypeId;
    }

    /// <summary>Returns the effective value an element reports for a definition, if any.</summary>
    public static ParameterValue? GetValue(BuildingDocument document, ElementEntry element, Guid definitionId)
    {
        var definition = document.ParameterDefinitions.FirstOrDefault(candidate => candidate.Id == definitionId);
        if (definition is null || ResolveOwner(definition, element) is not Guid ownerId) return null;
        return document.ParameterValues.FirstOrDefault(value =>
            value.DefinitionId == definitionId && value.OwnerId == ownerId);
    }

    /// <summary>Returns the definitions that apply to a category, ordered by group then name.</summary>
    public static IReadOnlyList<ParameterDefinition> ForCategory(BuildingDocument document, ElementCategory category) =>
        document.ParameterDefinitions
            .Where(definition => definition.Categories.Contains(category))
            .OrderBy(definition => definition.Group, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(definition => definition.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    /// <summary>Maps every element type identifier to the instance category it types.</summary>
    private static Dictionary<Guid, ElementCategory> TypeCategories(BuildingDocument document)
    {
        var map = document.WallTypes.ToDictionary(type => type.Id, _ => ElementCategory.Walls);
        foreach (var type in document.OpeningTypes)
            map[type.Id] = type.Kind == OpeningKind.Door ? ElementCategory.Doors : ElementCategory.Windows;
        return map;
    }

    /// <summary>Checks that a value owner exists and matches the definition's binding and categories.</summary>
    private static bool IsOwnerValid(ParameterDefinition definition, Guid ownerId,
        IReadOnlyDictionary<Guid, ElementEntry> elements, IReadOnlyDictionary<Guid, ElementCategory> types)
    {
        if (definition.Binding == ParameterBinding.Type)
            return types.TryGetValue(ownerId, out var typeCategory) && definition.Categories.Contains(typeCategory);
        return elements.TryGetValue(ownerId, out var element) && definition.Categories.Contains(element.Category);
    }
}
