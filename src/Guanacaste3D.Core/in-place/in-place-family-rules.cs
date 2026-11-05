namespace Guanacaste3D.Core;

/// <summary>
/// Project-level rules for in-place families. An in-place family is a <see cref="FamilyDocument"/> hosted in
/// place in the Generic Models category. The family validates itself; this class adds the rules that depend
/// on the project: unique names and identifiers, and the accepted range of length parameters.
/// </summary>
public static class InPlaceFamilyRules
{
    /// <summary>Largest number of in-place families one project may contain.</summary>
    public const int MaximumFamilies = 1000;

    /// <summary>Smallest accepted length parameter, in meters (1 mm).</summary>
    public const double MinimumLength = FamilyGeometry.MinimumDepth;

    /// <summary>Largest accepted length parameter, in meters (1 km).</summary>
    public const double MaximumLength = FamilyGeometry.MaximumDepth;

    /// <summary>Rejects an invalid in-place family, naming the first problem found.</summary>
    public static void Validate(BuildingDocument document)
    {
        var families = document.InPlaceFamilies;
        if (families is null || families.Count > MaximumFamilies || families.Any(family => family is null))
            throw new InvalidDataException("The in-place family collection is invalid.");
        foreach (var family in families)
        {
            if (family.Hosting != FamilyHostingBehavior.InPlace || family.Category != FamilyCategory.GenericModels)
                throw new InvalidDataException(
                    "An in-place family must be hosted in place in the Generic Models category.");
            family.Validate();
            ValidateLengths(family);
        }
        var identifiers = families.Select(family => family.Id)
            .Concat(families.SelectMany(family => family.Operations.Select(operation => operation.Id)))
            .ToArray();
        if (identifiers.Distinct().Count() != identifiers.Length ||
            families.Select(family => family.Name.Trim().ToUpperInvariant()).Distinct().Count() != families.Count)
            throw new InvalidDataException("In-place family and extrusion identifiers and names must be unique.");
    }

    /// <summary>A horizontal work plane at the given elevation, facing up.</summary>
    public static FamilyWorkPlane HorizontalPlane(double elevation) =>
        new(Guid.NewGuid(), "Plano horizontal", new(0, 0, elevation), new(0, 0, 1), new(1, 0, 0));

    /// <summary>Length parameters, in their default and in every type value, must stay between 1 mm and 1 km.</summary>
    private static void ValidateLengths(FamilyDocument family)
    {
        foreach (var parameter in family.Parameters.Where(parameter => parameter.Kind == FamilyParameterKind.Length))
        {
            var values = family.Types
                .SelectMany(type => type.Values)
                .Where(value => value.ParameterId == parameter.Id && value.Number is not null)
                .Select(value => value.Number!.Value)
                .ToList();
            if (parameter.DefaultNumber is double defaultValue) values.Add(defaultValue);
            if (values.Any(value => !double.IsFinite(value) || value < MinimumLength || value > MaximumLength))
                throw new InvalidDataException(
                    $"Length parameter '{parameter.Name}' must be between 1 mm and 1 km.");
        }
    }
}
