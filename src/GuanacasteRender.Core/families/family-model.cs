namespace Guanacaste3D.Core;

/// <summary>Persistent parametric family definition, including reusable drawing-sheet families.</summary>
public sealed class FamilyDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled Family";
    public FamilyCategory Category { get; set; } = FamilyCategory.GenericModels;
    public FamilyHostingBehavior Hosting { get; set; } = FamilyHostingBehavior.Unhosted;
    public List<FamilyParameterDefinition> Parameters { get; set; } = [];
    public List<FamilyTypeDefinition> Types { get; set; } = [];
    public List<FamilyWorkPlane> WorkPlanes { get; set; } = [];
    public List<FamilyOperation> Operations { get; set; } = [];
    public List<NestedFamilyReference> NestedFamilies { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = [];
    /// <summary>Optional editable paper-space definition for a title-block family.</summary>
    public SheetFamilyLayout? SheetLayout { get; set; }
    /// <summary>Mesh component families only: the detailed triangles placed by every instance of the family.</summary>
    public FamilyMesh? Mesh { get; set; }

    /// <summary>Returns a copy whose lists and dictionaries can be edited without changing this instance.</summary>
    public FamilyDocument Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        Id = Id,
        Name = Name,
        Category = Category,
        Hosting = Hosting,
        Parameters = [.. Parameters],
        Types = Types.Select(type => type with { Values = [.. type.Values] }).ToList(),
        WorkPlanes = [.. WorkPlanes],
        Operations = Operations.Select(operation => operation with
        {
            DependsOn = [.. operation.DependsOn],
            Expressions = new Dictionary<string, string>(operation.Expressions, StringComparer.Ordinal),
            Profile = [.. operation.Profile]
        }).ToList(),
        NestedFamilies = NestedFamilies.Select(nested => nested with
        {
            ParameterLinks = new Dictionary<Guid, Guid>(nested.ParameterLinks)
        }).ToList(),
        Metadata = new Dictionary<string, string>(Metadata),
        SheetLayout = SheetLayout?.Copy(),
        // The mesh is treated as immutable once created, so copies share its (large) arrays.
        Mesh = Mesh
    };

    /// <summary>Validates family geometry, parameters, dependencies, and optional editable paper graphics.</summary>
    public void Validate()
    {
        SheetLayout?.Validate();
        Mesh?.Validate();
        if (Mesh is not null && Category == FamilyCategory.TitleBlocks)
            throw new InvalidDataException("A drawing sheet family cannot carry a mesh.");
        if ((Category == FamilyCategory.TitleBlocks) != (SheetLayout is not null))
            throw new InvalidDataException("The family is not a drawing sheet family.");
        if (SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException("Unsupported family schema version.");
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 160)
            throw new InvalidDataException("The family identity or name is invalid.");
        if (!Enum.IsDefined(Category) || !Enum.IsDefined(Hosting))
            throw new InvalidDataException("The family category or hosting behavior is invalid.");
        if (Parameters is null || Types is null || WorkPlanes is null || Operations is null ||
            NestedFamilies is null || Metadata is null ||
            Parameters.Count > 10000 || Types.Count > 10000 || WorkPlanes.Count > 2000 ||
            Operations.Count > 100000 || NestedFamilies.Count > 10000 || Metadata.Count > 1000)
            throw new InvalidDataException("The family exceeds supported collection limits.");

        ValidateParameters();
        ValidateTypes();
        ValidateWorkPlanes();
        ValidateOperations();
        ValidateNestedFamilies();
        FamilyGeometry.ValidateSolids(this);

        if (Metadata.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 120 ||
                                 pair.Value is null || pair.Value.Length > 4000))
            throw new InvalidDataException("Family metadata contains an invalid entry.");

        var ids = Parameters.Select(item => item.Id)
            .Concat((SheetLayout?.Graphics ?? []).Select(graphic => graphic.Id))
            .Concat(Types.Select(item => item.Id))
            .Concat(WorkPlanes.Select(item => item.Id))
            .Concat(Operations.Select(item => item.Id))
            .Concat(NestedFamilies.Select(item => item.Id))
            .ToArray();
        if (ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            throw new InvalidDataException("Family object identifiers must be non-empty and unique.");
    }

    /// <summary>Rejects ambiguous names, invalid bounds, and inconsistent calculated parameter definitions.</summary>
    private void ValidateParameters()
    {
        if (Parameters.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Parameters.Count)
            throw new InvalidDataException("Family parameter names must be unique.");

        foreach (var parameter in Parameters)
        {
            if (parameter.Id == Guid.Empty || string.IsNullOrWhiteSpace(parameter.Name) || parameter.Name.Length > 120 ||
                !Enum.IsDefined(parameter.Kind) || !Enum.IsDefined(parameter.Scope) ||
                parameter.Group is null || parameter.Group.Length > 120 ||
                parameter.Description is null || parameter.Description.Length > 1000 ||
                parameter.Formula is null || parameter.Formula.Length > 2000 ||
                (parameter.DefaultNumber is double number && !double.IsFinite(number)) ||
                (parameter.Minimum is double minimum && !double.IsFinite(minimum)) ||
                (parameter.Maximum is double maximum && !double.IsFinite(maximum)) ||
                (parameter.Minimum is double min && parameter.Maximum is double max && min > max) ||
                (parameter.DefaultText is not null && parameter.DefaultText.Length > 4000) ||
                (parameter.Scope == FamilyParameterScope.Calculated && string.IsNullOrWhiteSpace(parameter.Formula)))
                throw new InvalidDataException("A family parameter definition is invalid.");
        }

        FamilyFormulaRules.Validate(Parameters);
    }

    /// <summary>Ensures type values refer to declared parameters and have a single numeric or textual representation.</summary>
    private void ValidateTypes()
    {
        if (Types.Count < 1 ||
            Types.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Types.Count)
            throw new InvalidDataException("A family must contain at least one uniquely named type.");

        var parameterIds = Parameters.Select(item => item.Id).ToHashSet();
        foreach (var type in Types)
        {
            if (type.Id == Guid.Empty || string.IsNullOrWhiteSpace(type.Name) || type.Name.Length > 120 ||
                type.Values is null || type.Values.Count > Parameters.Count ||
                type.Values.Select(item => item.ParameterId).Distinct().Count() != type.Values.Count)
                throw new InvalidDataException("A family type is invalid.");
            foreach (var value in type.Values)
            {
                if (!parameterIds.Contains(value.ParameterId) ||
                    (value.Number is null) == (value.Text is null) ||
                    (value.Number is double number && !double.IsFinite(number)) ||
                    (value.Text is not null && value.Text.Length > 4000))
                    throw new InvalidDataException("A family type contains an invalid parameter value.");
            }
        }
    }

    /// <summary>Checks finite and independent basis vectors before geometric regeneration.</summary>
    private void ValidateWorkPlanes()
    {
        if (WorkPlanes.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != WorkPlanes.Count)
            throw new InvalidDataException("Family work-plane names must be unique.");

        foreach (var plane in WorkPlanes)
        {
            if (plane.Id == Guid.Empty || string.IsNullOrWhiteSpace(plane.Name) || plane.Name.Length > 120 ||
                !plane.Origin.IsFinite || !plane.Normal.IsFinite || !plane.XAxis.IsFinite ||
                !double.IsFinite(plane.Offset) || plane.Normal.Length < 1e-9 || plane.XAxis.Length < 1e-9 ||
                FamilyPoint3.Cross(plane.Normal, plane.XAxis).Length < 1e-9)
                throw new InvalidDataException("A family work plane is invalid.");
        }
    }

    /// <summary>Rejects invalid work-plane, material, and dependency references before evaluating solids.</summary>
    private void ValidateOperations()
    {
        var planeIds = WorkPlanes.Select(item => item.Id).ToHashSet();
        var operationIds = Operations.Select(item => item.Id).ToHashSet();
        var parameterIds = Parameters.Select(item => item.Id).ToHashSet();

        foreach (var operation in Operations)
        {
            if (operation.Id == Guid.Empty || string.IsNullOrWhiteSpace(operation.Name) || operation.Name.Length > 160 ||
                !Enum.IsDefined(operation.Kind) ||
                (operation.WorkPlaneId is Guid planeId && !planeIds.Contains(planeId)) ||
                (operation.MaterialParameterId is Guid materialId && !parameterIds.Contains(materialId)) ||
                operation.DependsOn is null || operation.DependsOn.Distinct().Count() != operation.DependsOn.Count ||
                operation.DependsOn.Any(id => id == operation.Id || !operationIds.Contains(id)) ||
                operation.Expressions is null || operation.Expressions.Count > 100 ||
                operation.Expressions.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 80 ||
                                                  string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Length > 2000) ||
                operation.ComponentRole is null || operation.ComponentRole.Length > 120 ||
                operation.Profile is null || operation.Profile.Count > InPlaceProfileRules.MaximumVertices)
                throw new InvalidDataException("A family geometry operation is invalid.");
        }

        FamilyDependencyGraph.TopologicalSort(Operations);
    }

    /// <summary>Prevents self references and malformed nested parameter links.</summary>
    private void ValidateNestedFamilies()
    {
        foreach (var nested in NestedFamilies)
        {
            if (nested.Id == Guid.Empty || nested.FamilyId == Guid.Empty || nested.FamilyId == Id ||
                string.IsNullOrWhiteSpace(nested.Name) || nested.Name.Length > 160 ||
                string.IsNullOrWhiteSpace(nested.Role) || nested.Role.Length > 120 ||
                nested.ParameterLinks is null)
                throw new InvalidDataException("A nested-family reference is invalid.");
        }
    }
}
