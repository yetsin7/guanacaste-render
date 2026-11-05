using System.Globalization;

namespace Guanacaste3D.Core;

/// <summary>
/// In-place family editing on the unified <see cref="FamilyDocument"/> model. Each public method is one named
/// transaction, so every change can be undone, and a change that fails validation leaves the document untouched.
/// </summary>
public sealed partial class DocumentSession
{
    /// <summary>Name of the single type every in-place family starts with; its values drive the parameters.</summary>
    private const string DefaultTypeName = "Tipo 1";

    /// <summary>Prefix of work planes that in-place extrusions create, so unused ones can be pruned.</summary>
    private const string PlanePrefix = "Plano de extrusión";

    /// <summary>Prefix of material parameters that in-place extrusions create, so unused ones can be pruned.</summary>
    private const string MaterialPrefix = "Material ";

    /// <summary>Creates an empty in-place family in a category that accepts in-place families.</summary>
    public Guid CreateInPlaceFamily(string categoryId, string name)
    {
        var category = BimCategoryCatalog.Find(categoryId)
            ?? throw new InvalidDataException("The family category does not exist.");
        if (!category.SupportsInPlace)
            throw new InvalidDataException("This category does not accept in-place families yet.");
        var id = Guid.NewGuid();
        Commit("Transaction.CreateInPlaceFamily", document => document.InPlaceFamilies.Add(new FamilyDocument
        {
            Id = id,
            Name = name.Trim(),
            Category = FamilyCategory.GenericModels,
            Hosting = FamilyHostingBehavior.InPlace,
            Types = [new(Guid.NewGuid(), DefaultTypeName, [])]
        }));
        return id;
    }

    /// <summary>Renames an in-place family. Names stay unique within the document.</summary>
    public void RenameInPlaceFamily(Guid familyId, string name)
    {
        RequireFamily(familyId);
        Commit("Transaction.RenameInPlaceFamily", document =>
            document.InPlaceFamilies[FamilyIndex(document, familyId)].Name = name.Trim());
    }

    /// <summary>
    /// Adds a length parameter to a family, or updates an existing one (meters), matched case-insensitively.
    /// The value is stored as the parameter's default and as the value of the family's type.
    /// </summary>
    public void SetInPlaceParameter(Guid familyId, string name, double value)
    {
        RequireFamily(familyId);
        var trimmed = name.Trim();
        Commit("Transaction.SetInPlaceParameter", document =>
        {
            var family = document.InPlaceFamilies[FamilyIndex(document, familyId)];
            var index = family.Parameters.FindIndex(candidate =>
                string.Equals(candidate.Name, trimmed, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                family.Parameters.Add(new FamilyParameterDefinition(Guid.NewGuid(), trimmed,
                    FamilyParameterKind.Length, FamilyParameterScope.Type));
                index = family.Parameters.Count - 1;
            }
            var parameter = family.Parameters[index];
            if (parameter.Kind != FamilyParameterKind.Length)
                throw new InvalidDataException("Only length parameters can take a value.");
            family.Parameters[index] = parameter with { DefaultNumber = value };
            var values = family.Types[0].Values;
            var valueIndex = values.FindIndex(entry => entry.ParameterId == parameter.Id);
            var typed = new FamilyParameterValue(parameter.Id, value, null);
            if (valueIndex >= 0) values[valueIndex] = typed;
            else values.Add(typed);
        });
    }

    /// <summary>
    /// Adds an extrusion of a closed profile along the plane normal, and returns its operation identifier. The
    /// depth is <paramref name="depthParameter"/> when it is set; it may be a parameter name or a formula over
    /// parameters. Otherwise the depth is <paramref name="depth"/>. The start is <paramref name="startOffset"/>.
    /// </summary>
    public Guid AddInPlaceExtrusion(Guid familyId, FamilyWorkPlane plane, IReadOnlyList<Point2> profile,
        double startOffset, double depth, string? depthParameter = null, string materialName = "",
        string? name = null)
    {
        RequireFamily(familyId);
        var id = Guid.NewGuid();
        Commit("Transaction.AddInPlaceExtrusion", document =>
        {
            var family = document.InPlaceFamilies[FamilyIndex(document, familyId)];
            var label = name?.Trim() is { Length: > 0 } trimmed
                ? trimmed
                : $"Extrusion {CountExtrusions(family) + 1}";
            family.Operations.Add(new FamilyOperation(id, label, FamilyOperationKind.SolidExtrusion)
            {
                WorkPlaneId = PlaneId(family, plane),
                Profile = [.. profile],
                Expressions = Expressions(depthParameter, depth, startOffset),
                ComponentRole = "In-place",
                MaterialParameterId = MaterialParameter(family, label, materialName, null)
            });
        });
        return id;
    }

    /// <summary>
    /// Replaces the geometry of one extrusion while keeping its identifier and name, so the solid is regenerated
    /// in place rather than deleted and recreated.
    /// </summary>
    public void UpdateInPlaceExtrusion(Guid familyId, Guid operationId, FamilyWorkPlane plane,
        IReadOnlyList<Point2> profile, double startOffset, double depth, string? depthParameter,
        string materialName)
    {
        RequireExtrusion(familyId, operationId);
        Commit("Transaction.UpdateInPlaceExtrusion", document =>
        {
            var family = document.InPlaceFamilies[FamilyIndex(document, familyId)];
            var index = family.Operations.FindIndex(operation => operation.Id == operationId);
            var operation = family.Operations[index];
            family.Operations[index] = operation with
            {
                WorkPlaneId = PlaneId(family, plane),
                Profile = [.. profile],
                Expressions = Expressions(depthParameter, depth, startOffset),
                MaterialParameterId = MaterialParameter(family, operation.Name, materialName,
                    operation.MaterialParameterId)
            };
            PruneUnused(family);
        });
    }

    /// <summary>Removes one extrusion from a family; the family itself stays.</summary>
    public void RemoveInPlaceExtrusion(Guid familyId, Guid operationId)
    {
        if (!Document.InPlaceFamilies.Any(family => family.Id == familyId &&
                family.Operations.Any(operation => operation.Id == operationId)))
            return;
        Commit("Transaction.RemoveInPlaceExtrusion", document =>
        {
            var family = document.InPlaceFamilies[FamilyIndex(document, familyId)];
            family.Operations.RemoveAll(operation => operation.Id == operationId);
            PruneUnused(family);
        });
    }

    /// <summary>Removes an in-place family and all of its operations.</summary>
    public void RemoveInPlaceFamily(Guid familyId)
    {
        if (!Document.InPlaceFamilies.Any(family => family.Id == familyId)) return;
        Commit("Transaction.RemoveInPlaceFamily", document =>
            document.InPlaceFamilies.RemoveAll(family => family.Id == familyId));
    }

    /// <summary>Throws when the family does not exist in the current document.</summary>
    private void RequireFamily(Guid familyId)
    {
        if (!Document.InPlaceFamilies.Any(family => family.Id == familyId))
            throw new InvalidDataException("The in-place family does not exist.");
    }

    /// <summary>Throws when the family or the extrusion inside it does not exist.</summary>
    private void RequireExtrusion(Guid familyId, Guid operationId)
    {
        RequireFamily(familyId);
        if (!Document.InPlaceFamilies.Single(family => family.Id == familyId).Operations
                .Any(operation => operation.Id == operationId))
            throw new InvalidDataException("The extrusion does not exist.");
    }

    /// <summary>Returns the index of a family inside a document copy, or throws when it is missing.</summary>
    private static int FamilyIndex(BuildingDocument document, Guid familyId)
    {
        var index = document.InPlaceFamilies.FindIndex(family => family.Id == familyId);
        return index >= 0 ? index : throw new InvalidDataException("The in-place family does not exist.");
    }

    /// <summary>Counts the solid extrusions in a family, for default names.</summary>
    private static int CountExtrusions(FamilyDocument family) =>
        family.Operations.Count(operation => operation.Kind == FamilyOperationKind.SolidExtrusion);

    /// <summary>Builds the depth and offset expressions. A depth parameter, when given, replaces the number.</summary>
    private static Dictionary<string, string> Expressions(string? depthParameter, double depth, double startOffset) =>
        new(StringComparer.Ordinal)
        {
            [FamilyGeometry.DepthKey] = string.IsNullOrWhiteSpace(depthParameter)
                ? FormatNumber(depth)
                : depthParameter.Trim(),
            [FamilyGeometry.OffsetKey] = FormatNumber(startOffset)
        };

    /// <summary>Formats a length as a plain decimal, which the formula parser reads exactly.</summary>
    private static string FormatNumber(double value) =>
        value.ToString("0.###############", CultureInfo.InvariantCulture);

    /// <summary>Returns the id of a work plane with the same geometry, adding one when the family has none.</summary>
    private static Guid PlaneId(FamilyDocument family, FamilyWorkPlane plane)
    {
        var existing = family.WorkPlanes.FirstOrDefault(candidate =>
            candidate.Origin == plane.Origin && candidate.Normal == plane.Normal && candidate.XAxis == plane.XAxis);
        if (existing is not null) return existing.Id;
        var added = new FamilyWorkPlane(Guid.NewGuid(),
            UniqueName(PlanePrefix, family.WorkPlanes.Select(candidate => candidate.Name)),
            plane.Origin, plane.Normal, plane.XAxis);
        family.WorkPlanes.Add(added);
        return added.Id;
    }

    /// <summary>
    /// Returns the material parameter of an operation: it is created, renamed to match the operation, or dropped
    /// when the material name is blank. The caller prunes parameters that end up unused.
    /// </summary>
    private static Guid? MaterialParameter(FamilyDocument family, string label, string materialName, Guid? existing)
    {
        var material = materialName.Trim();
        if (material.Length == 0) return null;
        var index = existing is Guid currentId
            ? family.Parameters.FindIndex(parameter => parameter.Id == currentId)
            : -1;
        if (index >= 0)
        {
            family.Parameters[index] = family.Parameters[index] with { DefaultText = material };
            return family.Parameters[index].Id;
        }
        var caption = label.Length > 100 ? label[..100] : label;
        var parameter = new FamilyParameterDefinition(Guid.NewGuid(),
            UniqueName(MaterialPrefix + caption, family.Parameters.Select(candidate => candidate.Name)),
            FamilyParameterKind.Material, FamilyParameterScope.Type)
        {
            DefaultText = material
        };
        family.Parameters.Add(parameter);
        return parameter.Id;
    }

    /// <summary>Removes work planes and material parameters that in-place extrusions created and nothing uses now.</summary>
    private static void PruneUnused(FamilyDocument family)
    {
        var usedPlanes = family.Operations
            .Where(operation => operation.WorkPlaneId is not null)
            .Select(operation => operation.WorkPlaneId!.Value)
            .ToHashSet();
        family.WorkPlanes.RemoveAll(plane =>
            plane.Name.StartsWith(PlanePrefix, StringComparison.Ordinal) && !usedPlanes.Contains(plane.Id));

        var usedMaterials = family.Operations
            .Where(operation => operation.MaterialParameterId is not null)
            .Select(operation => operation.MaterialParameterId!.Value)
            .ToHashSet();
        family.Parameters.RemoveAll(parameter =>
            parameter.Kind == FamilyParameterKind.Material &&
            parameter.Name.StartsWith(MaterialPrefix, StringComparison.Ordinal) &&
            !usedMaterials.Contains(parameter.Id));
    }

    /// <summary>Returns a name that no existing name matches, ignoring case, by adding a numeric suffix when needed.</summary>
    private static string UniqueName(string baseName, IEnumerable<string> taken)
    {
        var names = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = baseName;
        for (var suffix = 2; names.Contains(candidate); suffix++)
            candidate = $"{baseName} ({suffix})";
        return candidate;
    }
}
