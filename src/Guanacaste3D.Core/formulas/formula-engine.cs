namespace Guanacaste3D.Core;

/// <summary>
/// Connects formulas to the building model. A calculated parameter's formula
/// may reference other parameters bound to the same category (by name) and the
/// category's model quantities (Length, Height, Width, Sill, Thickness,
/// NetVolume, Area, Elevation, or their Spanish names). Formulas are validated
/// for every bound category, cycles between formulas are rejected, and values
/// are computed in dependency order without being stored.
/// </summary>
public static class FormulaEngine
{
    /// <summary>Alternative names accepted for model quantities, mapped to their schedule keys.</summary>
    private static readonly Dictionary<string, string> QuantityAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Longitud"] = ScheduleQuantities.Length, ["Altura"] = ScheduleQuantities.Height,
        ["Alto"] = ScheduleQuantities.Height, ["Ancho"] = ScheduleQuantities.Width,
        ["Antepecho"] = ScheduleQuantities.Sill, ["Espesor"] = ScheduleQuantities.Thickness,
        ["Volumen"] = ScheduleQuantities.NetVolume, ["Volume"] = ScheduleQuantities.NetVolume,
        ["Área"] = ScheduleQuantities.Area, ["Elevación"] = ScheduleQuantities.Elevation
    };

    /// <summary>Returns whether a definition is calculated by a formula.</summary>
    public static bool IsCalculated(ParameterDefinition definition) => definition.Formula.Length > 0;

    /// <summary>
    /// Validates every formula definition: syntax, names, units, result type
    /// against the definition's spec in each bound category, and absence of
    /// dependency cycles. Throws <see cref="InvalidDataException"/> on the first problem.
    /// </summary>
    public static void ValidateDefinitions(BuildingDocument document)
    {
        var calculated = document.ParameterDefinitions.Where(IsCalculated).ToArray();
        if (calculated.Length == 0) return;
        var parsed = calculated.ToDictionary(definition => definition.Id,
            definition => FormulaParser.Parse(definition.Formula));
        foreach (var definition in calculated)
        {
            foreach (var category in definition.Categories)
            {
                var symbols = SymbolTypes(document, category);
                var type = FormulaTypeChecker.Check(parsed[definition.Id],
                    name => symbols.TryGetValue(name, out var symbolType) ? symbolType : null);
                var expected = FormulaType.ForSpec(definition.Spec);
                if (type != expected)
                    throw new InvalidDataException(
                        $"Formula error: '{definition.Name}' produces {type.Describe()} but the parameter is {expected.Describe()}.");
            }
        }
        _ = Order(document, parsed);
    }

    /// <summary>
    /// Computes calculated parameter values for every element of a category.
    /// Results that are undefined or violate the spec's range are omitted.
    /// </summary>
    public static Dictionary<Guid, Dictionary<Guid, ParameterValue>> EvaluateCategory(BuildingDocument document,
        ElementCategory category, IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, object>> quantities)
    {
        var result = new Dictionary<Guid, Dictionary<Guid, ParameterValue>>();
        var bound = ParameterRules.ForCategory(document, category);
        var calculated = bound.Where(IsCalculated).ToArray();
        if (calculated.Length == 0) return result;
        var parsed = calculated.ToDictionary(definition => definition.Id,
            definition => FormulaParser.Parse(definition.Formula));
        var order = Order(document, parsed);
        var byName = bound.ToDictionary(definition => definition.Name, StringComparer.OrdinalIgnoreCase);
        var stored = document.ParameterValues.ToDictionary(value => (value.DefinitionId, value.OwnerId));
        foreach (var element in ElementCatalog.List(document, category))
        {
            var computed = new Dictionary<Guid, ParameterValue>();
            var elementQuantities = quantities.GetValueOrDefault(element.Id);
            FormulaValue? Resolve(string name)
            {
                if (byName.TryGetValue(name, out var definition))
                {
                    if (IsCalculated(definition))
                        return computed.TryGetValue(definition.Id, out var value) ? ToFormula(definition, value) : null;
                    return ParameterRules.ResolveOwner(definition, element) is Guid owner &&
                           stored.TryGetValue((definition.Id, owner), out var storedValue)
                        ? ToFormula(definition, storedValue) : null;
                }
                var key = QuantityAliases.GetValueOrDefault(name, name);
                return elementQuantities is not null && elementQuantities.TryGetValue(key, out var quantity) &&
                       quantity is double number ? FormulaValue.Of(number) : null;
            }
            foreach (var definitionId in order)
            {
                if (!parsed.TryGetValue(definitionId, out var node)) continue;
                var definition = calculated.First(candidate => candidate.Id == definitionId);
                if (FormulaEvaluator.Evaluate(node, Resolve) is { } value &&
                    FromFormula(definition, element, value) is { } parameterValue)
                    computed[definitionId] = parameterValue;
            }
            result[element.Id] = computed;
        }
        return result;
    }

    /// <summary>Returns the static type of every name a formula can use in a category.</summary>
    private static Dictionary<string, FormulaType> SymbolTypes(BuildingDocument document, ElementCategory category)
    {
        var symbols = new Dictionary<string, FormulaType>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in ScheduleQuantities.ColumnsFor(category))
        {
            var power = column.Unit switch { "m" => 1, "m²" => 2, "m³" => 3, _ => -1 };
            if (power >= 0) symbols[column.Key] = new FormulaType(FormulaKind.Number, power);
        }
        foreach (var (alias, key) in QuantityAliases)
            if (symbols.TryGetValue(key, out var type)) symbols[alias] = type;
        foreach (var definition in ParameterRules.ForCategory(document, category))
            symbols[definition.Name] = FormulaType.ForSpec(definition.Spec);
        return symbols;
    }

    /// <summary>Orders calculated definitions so each comes after the formulas it uses; rejects cycles.</summary>
    private static List<Guid> Order(BuildingDocument document, IReadOnlyDictionary<Guid, FormulaNode> parsed)
    {
        var byName = document.ParameterDefinitions.Where(definition => parsed.ContainsKey(definition.Id))
            .ToDictionary(definition => definition.Name, StringComparer.OrdinalIgnoreCase);
        var order = new List<Guid>();
        var state = new Dictionary<Guid, int>();
        void Visit(Guid id, string name)
        {
            if (state.GetValueOrDefault(id) == 2) return;
            if (state.GetValueOrDefault(id) == 1)
                throw new InvalidDataException($"Formula error: '{name}' depends on itself through other formulas.");
            state[id] = 1;
            foreach (var reference in FormulaParser.References(parsed[id]))
                if (byName.TryGetValue(reference, out var dependency)) Visit(dependency.Id, dependency.Name);
            state[id] = 2;
            order.Add(id);
        }
        foreach (var definition in byName.Values) Visit(definition.Id, definition.Name);
        return order;
    }

    /// <summary>Converts a stored or computed parameter value to a formula value.</summary>
    private static FormulaValue? ToFormula(ParameterDefinition definition, ParameterValue value) => definition.Spec switch
    {
        ParameterSpec.Text => value.Text is { } text ? FormulaValue.Of(text) : null,
        ParameterSpec.YesNo => value.Number is double flag ? FormulaValue.Of(flag == 1) : null,
        _ => value.Number is double number ? FormulaValue.Of(number) : null
    };

    /// <summary>Converts a formula result to a parameter value, rounding integers and enforcing spec ranges.</summary>
    private static ParameterValue? FromFormula(ParameterDefinition definition, ElementEntry element, FormulaValue value)
    {
        var owner = ParameterRules.ResolveOwner(definition, element) ?? element.Id;
        ParameterValue candidate = definition.Spec switch
        {
            ParameterSpec.Text => new(definition.Id, owner, null, value.Text),
            ParameterSpec.YesNo => new(definition.Id, owner, value.IsTrue ? 1 : 0, null),
            ParameterSpec.Integer => new(definition.Id, owner, Math.Round(value.Number, MidpointRounding.AwayFromZero), null),
            _ => new(definition.Id, owner, value.Number, null)
        };
        return ParameterRules.IsValueValid(definition.Spec, candidate.Number, candidate.Text) ? candidate : null;
    }
}
