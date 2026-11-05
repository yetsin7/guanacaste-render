namespace Guanacaste3D.Core;

/// <summary>
/// Project rules for loaded mesh component families: each carries a mesh, identifiers and names are unique, and
/// every planting or component that names a family names one the project has loaded.
/// </summary>
public static class ComponentFamilyRules
{
    /// <summary>Largest number of mesh families one project may load.</summary>
    public const int MaximumFamilies = 500;

    public static void Validate(BuildingDocument document)
    {
        var families = document.ComponentFamilies ??= [];
        if (families.Count > MaximumFamilies || families.Any(family => family is null || family.Mesh is null))
            throw new InvalidDataException("The component family collection is invalid.");
        foreach (var family in families) family.Validate();
        if (families.Select(family => family.Name.Trim().ToUpperInvariant()).Distinct().Count() != families.Count)
            throw new InvalidDataException("Component family names must be unique.");
        var ids = families.Select(family => family.Id).ToHashSet();
        if (document.StructuralElements.Any(element => element.FamilyId is Guid id &&
                (!ids.Contains(id) || element.Kind is not (StructuralElementKind.Planting or StructuralElementKind.Component))))
            throw new InvalidDataException("An element refers to a component family that is not loaded.");
    }

    /// <summary>The mesh an element draws in 3D, or null when it uses its procedural geometry.</summary>
    public static FamilyMesh? MeshOf(BuildingDocument document, StructuralElement element) =>
        element.FamilyId is Guid id ? document.ComponentFamilies.FirstOrDefault(family => family.Id == id)?.Mesh : null;
}
