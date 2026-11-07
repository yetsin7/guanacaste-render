namespace Guanacaste3D.Core;

public enum FamilyCategory
{
    Doors, Windows, Furniture, Casework, PlumbingFixtures, LightingFixtures,
    ElectricalEquipment, MechanicalEquipment, StructuralElements, FacadeComponents,
    RoofComponents, Profiles, GenericModels, DecorativeComponents, FabricationComponents,
    Hardware, Custom, TitleBlocks,
    /// <summary>Trees, shrubs and other plants placed on the site.</summary>
    Planting,
    /// <summary>People, vehicles and other objects that give a scene its scale.</summary>
    Entourage
}

public enum FamilyHostingBehavior { Unhosted, Wall, Face, WorkPlane, InPlace }
public enum FamilyParameterKind
{
    Length, Area, Volume, Angle, Radius, Diameter, Integer, Number, Boolean, Text,
    Material, ElementReference, Enumeration, Visibility
}
public enum FamilyParameterScope { Instance, Type, Shared, Calculated }
public enum FamilyOperationKind
{
    SolidExtrusion, VoidExtrusion, SolidRevolution, VoidRevolution, SolidSweep, VoidSweep,
    VariableSweep, SolidLoft, VoidLoft, PrimitiveBox, PrimitiveCylinder, Union, Difference,
    Intersection, Array, Mirror
}

public readonly record struct FamilyPoint3(double X, double Y, double Z)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public static double Dot(FamilyPoint3 a, FamilyPoint3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static FamilyPoint3 Cross(FamilyPoint3 a, FamilyPoint3 b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
}

public sealed record FamilyParameterDefinition(Guid Id, string Name, FamilyParameterKind Kind, FamilyParameterScope Scope)
{
    public double? DefaultNumber { get; init; }
    public string? DefaultText { get; init; }
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public string Formula { get; init; } = string.Empty;
    public string Group { get; init; } = "Dimensions";
    public string Description { get; init; } = string.Empty;
}

public sealed record FamilyParameterValue(Guid ParameterId, double? Number, string? Text);

public sealed record FamilyTypeDefinition(Guid Id, string Name, List<FamilyParameterValue> Values);

public sealed record FamilyWorkPlane(
    Guid Id,
    string Name,
    FamilyPoint3 Origin,
    FamilyPoint3 Normal,
    FamilyPoint3 XAxis)
{
    public double Offset { get; init; }
}

public sealed record FamilyOperation(Guid Id, string Name, FamilyOperationKind Kind)
{
    public Guid? WorkPlaneId { get; init; }
    public List<Guid> DependsOn { get; init; } = [];
    public Dictionary<string, string> Expressions { get; init; } = new(StringComparer.Ordinal);
    public Guid? MaterialParameterId { get; init; }
    public string ComponentRole { get; init; } = string.Empty;

    /// <summary>
    /// Closed profile of an extrusion, in its work plane's coordinates and meters. Empty when the operation has
    /// no profile; such operations have no geometry to regenerate yet.
    /// </summary>
    public List<Point2> Profile { get; init; } = [];
}

public sealed record NestedFamilyReference(Guid Id, Guid FamilyId, string Name, string Role)
{
    public Dictionary<Guid, Guid> ParameterLinks { get; init; } = [];
    public bool Shared { get; init; }
}

public static class FamilyFormulaRules
{
    public static void Validate(IReadOnlyCollection<FamilyParameterDefinition> parameters)
    {
        var byName = parameters.ToDictionary(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase);
        var calculated = parameters.Where(parameter => !string.IsNullOrWhiteSpace(parameter.Formula)).ToArray();
        if (calculated.Length == 0) return;

        var parsed = calculated.ToDictionary(parameter => parameter.Id,
            parameter => FormulaParser.Parse(parameter.Formula));

        foreach (var parameter in calculated)
        {
            var actual = FormulaTypeChecker.Check(parsed[parameter.Id], name =>
                byName.TryGetValue(name, out var referenced) ? TypeFor(referenced.Kind) : null);
            var expected = TypeFor(parameter.Kind);
            if (actual != expected)
                throw new InvalidDataException("Formula error: '" + parameter.Name +
                    "' produces " + actual.Describe() + " but the parameter is " + expected.Describe() + ".");
        }

        var calculatedByName = calculated.ToDictionary(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase);
        var state = new Dictionary<Guid, int>();
        void Visit(FamilyParameterDefinition parameter)
        {
            if (state.GetValueOrDefault(parameter.Id) == 2) return;
            if (state.GetValueOrDefault(parameter.Id) == 1)
                throw new InvalidDataException("Formula error: '" + parameter.Name +
                    "' depends on itself through other formulas.");
            state[parameter.Id] = 1;
            foreach (var reference in FormulaParser.References(parsed[parameter.Id]))
                if (calculatedByName.TryGetValue(reference, out var dependency))
                    Visit(dependency);
            state[parameter.Id] = 2;
        }

        foreach (var parameter in calculated)
            Visit(parameter);
    }

    private static FormulaType TypeFor(FamilyParameterKind kind) => kind switch
    {
        FamilyParameterKind.Length or FamilyParameterKind.Radius or FamilyParameterKind.Diameter =>
            new FormulaType(FormulaKind.Number, 1),
        FamilyParameterKind.Area => new FormulaType(FormulaKind.Number, 2),
        FamilyParameterKind.Volume => new FormulaType(FormulaKind.Number, 3),
        FamilyParameterKind.Boolean or FamilyParameterKind.Visibility => FormulaType.Boolean,
        FamilyParameterKind.Text or FamilyParameterKind.Material or FamilyParameterKind.ElementReference or
            FamilyParameterKind.Enumeration => FormulaType.Text,
        _ => FormulaType.Scalar
    };
}

public static class FamilyDependencyGraph
{
    public static IReadOnlyList<Guid> TopologicalSort(IEnumerable<FamilyOperation> operations)
    {
        var list = operations.ToList();
        var byId = list.ToDictionary(item => item.Id);
        var indegree = list.ToDictionary(item => item.Id, item => item.DependsOn.Count);
        var dependents = list.ToDictionary(item => item.Id, _ => new List<Guid>());

        foreach (var operation in list)
            foreach (var dependency in operation.DependsOn)
            {
                if (!byId.ContainsKey(dependency))
                    throw new InvalidDataException("A family operation references a missing dependency.");
                dependents[dependency].Add(operation.Id);
            }

        var queue = new Queue<Guid>(list.Where(item => indegree[item.Id] == 0).Select(item => item.Id));
        var ordered = new List<Guid>(list.Count);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            ordered.Add(id);
            foreach (var dependent in dependents[id])
                if (--indegree[dependent] == 0)
                    queue.Enqueue(dependent);
        }

        if (ordered.Count != list.Count)
            throw new InvalidDataException("The family operation graph contains a circular dependency.");
        return ordered;
    }
}

public static class BuiltInFamilyLibrary
{
    public static readonly Guid ArchitecturalDoorId = Guid.Parse("10b54e34-f88c-42af-bc6e-44bb8965c10a");
    public static readonly Guid MortiseLockId = Guid.Parse("3238fb16-28df-428d-b5dd-71f1cf479b70");
    public static readonly Guid HingeId = Guid.Parse("407a5f80-746c-4da8-a7f7-3211710ce4d4");
    public static readonly Guid ArchitecturalWindowId = Guid.Parse("68729f17-339d-45ec-96fc-e4330d6b23da");

    public static FamilyDocument CreateArchitecturalDoor()
    {
        var width = NumberParameter("Ancho nominal", FamilyParameterKind.Length, FamilyParameterScope.Type, 0.9, 0.4, 4);
        var height = NumberParameter("Alto nominal", FamilyParameterKind.Length, FamilyParameterScope.Type, 2.1, 0.8, 6);
        var leafThickness = NumberParameter("Espesor de hoja", FamilyParameterKind.Length, FamilyParameterScope.Type, 0.045, 0.02, 0.15);
        var frameDepth = NumberParameter("Profundidad de marco", FamilyParameterKind.Length, FamilyParameterScope.Instance, 0.20, 0.05, 1);
        var handleHeight = NumberParameter("Altura del eje de manija", FamilyParameterKind.Length, FamilyParameterScope.Instance, 1.0, 0.5, 1.5);
        var lockBackset = NumberParameter("Distancia de entrada de cerradura", FamilyParameterKind.Length, FamilyParameterScope.Type, 0.06, 0.03, 0.15);
        var hingeCount = NumberParameter("Bisagras", FamilyParameterKind.Integer, FamilyParameterScope.Type, 3, 2, 8);
        var openingAngle = NumberParameter("Apertura de demostración", FamilyParameterKind.Angle, FamilyParameterScope.Instance, 90, 0, 180);
        var grilleVisible = NumberParameter("Rejilla visible", FamilyParameterKind.Visibility, FamilyParameterScope.Instance, 1, 0, 1);
        var grilleWidth = NumberParameter("Ancho rejilla", FamilyParameterKind.Length, FamilyParameterScope.Type, 0.45, 0.10, 1.5);
        var grilleHeight = NumberParameter("Alto rejilla", FamilyParameterKind.Length, FamilyParameterScope.Type, 0.35, 0.10, 1.5);
        var grilleBottom = NumberParameter("Altura inferior rejilla", FamilyParameterKind.Length, FamilyParameterScope.Instance, 0.25, 0.05, 1.5);
        var louverCount = NumberParameter("Número de lamas", FamilyParameterKind.Integer, FamilyParameterScope.Type, 8, 1, 50);
        var louverAngle = NumberParameter("Ángulo de lamas", FamilyParameterKind.Angle, FamilyParameterScope.Type, 35, -80, 80);
        var material = TextParameter("Material hoja", FamilyParameterKind.Material, FamilyParameterScope.Type, "Madera");

        var plane = new FamilyWorkPlane(Guid.NewGuid(), "Plano central", new(0, 0, 0), new(0, 0, 1), new(1, 0, 0));
        var leaf = Operation("Hoja", FamilyOperationKind.SolidExtrusion, plane.Id, "Hoja",
            ("Ancho", "Ancho nominal"), ("Alto", "Alto nominal"), ("Profundidad", "Espesor de hoja"));
        var frameLeft = Operation("Jamba izquierda", FamilyOperationKind.SolidExtrusion, plane.Id, "Marco",
            ("Alto", "Alto nominal"), ("Profundidad", "Profundidad de marco"));
        var frameRight = Operation("Jamba derecha", FamilyOperationKind.SolidExtrusion, plane.Id, "Marco",
            ("Alto", "Alto nominal"), ("Profundidad", "Profundidad de marco"));
        var head = Operation("Dintel", FamilyOperationKind.SolidExtrusion, plane.Id, "Marco",
            ("Ancho", "Ancho nominal"), ("Profundidad", "Profundidad de marco"));
        var grilleVoid = Operation("Hueco de rejilla", FamilyOperationKind.VoidExtrusion, plane.Id, "Rejilla",
            ("Ancho", "Ancho rejilla"), ("Alto", "Alto rejilla"), ("PosiciónZ", "Altura inferior rejilla"),
            ("Visible", "Rejilla visible"));
        grilleVoid = grilleVoid with { DependsOn = [leaf.Id] };
        var grilleFrame = Operation("Bastidor de rejilla", FamilyOperationKind.SolidExtrusion, plane.Id, "Rejilla",
            ("Ancho", "Ancho rejilla"), ("Alto", "Alto rejilla"), ("Visible", "Rejilla visible"));
        grilleFrame = grilleFrame with { DependsOn = [grilleVoid.Id] };
        var louvers = Operation("Matriz de lamas", FamilyOperationKind.Array, plane.Id, "Rejilla",
            ("Cantidad", "Número de lamas"), ("Ángulo", "Ángulo de lamas"), ("Visible", "Rejilla visible"));
        louvers = louvers with { DependsOn = [grilleFrame.Id] };
        var lockMortise = Operation("Alojamiento de cerradura", FamilyOperationKind.VoidExtrusion, plane.Id, "Cerradura",
            ("Altura", "Altura del eje de manija"), ("Entrada", "Distancia de entrada de cerradura"));
        lockMortise = lockMortise with { DependsOn = [leaf.Id] };
        var spindleBore = Operation("Perforación de eje de manija", FamilyOperationKind.PrimitiveCylinder, plane.Id, "Cerradura",
            ("Altura", "Altura del eje de manija"), ("Entrada", "Distancia de entrada de cerradura"),
            ("Operación", "Diferencia"));
        spindleBore = spindleBore with { DependsOn = [leaf.Id] };
        var hingeMortises = Operation("Alojamientos de bisagras", FamilyOperationKind.Array, plane.Id, "Bisagras",
            ("Cantidad", "Bisagras"), ("Alto", "Alto nominal"));
        hingeMortises = hingeMortises with { DependsOn = [leaf.Id] };

        var family = new FamilyDocument
        {
            Id = ArchitecturalDoorId,
            Name = "Puerta Arquitectónica Paramétrica — Guanacaste 3D",
            Category = FamilyCategory.Doors,
            Hosting = FamilyHostingBehavior.Wall,
            Parameters = [width, height, leafThickness, frameDepth, handleHeight, lockBackset, hingeCount,
                openingAngle, grilleVisible, grilleWidth, grilleHeight, grilleBottom, louverCount, louverAngle, material],
            Types =
            [
                new(Guid.NewGuid(), "900 × 2100 mm",
                [
                    new(width.Id, 0.9, null),
                    new(height.Id, 2.1, null),
                    new(leafThickness.Id, 0.045, null)
                ])
            ],
            WorkPlanes = [plane],
            Operations = [leaf, frameLeft, frameRight, head, grilleVoid, grilleFrame, louvers, lockMortise, spindleBore, hingeMortises],
            NestedFamilies =
            [
                new(Guid.NewGuid(), MortiseLockId, "Cerradura embutida paramétrica", "Cerradura"),
                new(Guid.NewGuid(), HingeId, "Bisagra arquitectónica paramétrica", "Bisagra")
            ],
            Metadata = new()
            {
                ["Manufacturer"] = "Guanacaste 3D",
                ["FamilyStandard"] = "G3D Architectural Door v1",
                ["GeometryStatus"] = "Operation tree ready for exact-kernel evaluation"
            }
        };
        family.Validate();
        return family;
    }

    public static FamilyDocument CreateMortiseLock()
    {
        var backset = NumberParameter("Entrada", FamilyParameterKind.Length, FamilyParameterScope.Type, 0.06, 0.03, 0.15);
        var bodyHeight = NumberParameter("Alto cuerpo", FamilyParameterKind.Length, FamilyParameterScope.Type, 0.17, 0.08, 0.35);
        var bodyDepth = NumberParameter("Profundidad cuerpo", FamilyParameterKind.Length, FamilyParameterScope.Type, 0.08, 0.04, 0.20);
        var spindleDiameter = NumberParameter("Diámetro eje", FamilyParameterKind.Diameter, FamilyParameterScope.Type, 0.008, 0.005, 0.02);
        var cylinderDiameter = NumberParameter("Diámetro cilindro", FamilyParameterKind.Diameter, FamilyParameterScope.Type, 0.032, 0.015, 0.07);
        var plane = new FamilyWorkPlane(Guid.NewGuid(), "Plano de montaje", new(0, 0, 0), new(0, 0, 1), new(1, 0, 0));
        var body = Operation("Cuerpo", FamilyOperationKind.SolidExtrusion, plane.Id, "Cerradura",
            ("Alto", "Alto cuerpo"), ("Profundidad", "Profundidad cuerpo"));
        var spindle = Operation("Eje", FamilyOperationKind.PrimitiveCylinder, plane.Id, "Cerradura",
            ("Diámetro", "Diámetro eje"), ("Entrada", "Entrada"));
        var cylinder = Operation("Cilindro", FamilyOperationKind.PrimitiveCylinder, plane.Id, "Cerradura",
            ("Diámetro", "Diámetro cilindro"), ("Entrada", "Entrada"));

        var family = new FamilyDocument
        {
            Id = MortiseLockId,
            Name = "Cerradura embutida paramétrica",
            Category = FamilyCategory.Hardware,
            Hosting = FamilyHostingBehavior.Face,
            Parameters = [backset, bodyHeight, bodyDepth, spindleDiameter, cylinderDiameter],
            Types = [new(Guid.NewGuid(), "60 mm entrada", [new(backset.Id, 0.06, null)])],
            WorkPlanes = [plane],
            Operations = [body, spindle, cylinder]
        };
        family.Validate();
        return family;
    }

    public static FamilyDocument CreateArchitecturalWindow()
    {
        var width = NumberParameter("Ancho", FamilyParameterKind.Length, FamilyParameterScope.Type, 1.2, 0.3, 8);
        var height = NumberParameter("Alto", FamilyParameterKind.Length, FamilyParameterScope.Type, 1.2, 0.3, 6);
        var sill = NumberParameter("Antepecho", FamilyParameterKind.Length, FamilyParameterScope.Instance, 0.9, 0, 20);
        var leafCount = NumberParameter("Número de hojas", FamilyParameterKind.Integer, FamilyParameterScope.Type, 2, 1, 8);
        var glassThickness = NumberParameter("Espesor vidrio", FamilyParameterKind.Length, FamilyParameterScope.Type, 0.006, 0.003, 0.05);
        var plane = new FamilyWorkPlane(Guid.NewGuid(), "Plano central", new(0, 0, 0), new(0, 0, 1), new(1, 0, 0));
        var frame = Operation("Marco", FamilyOperationKind.SolidExtrusion, plane.Id, "Marco",
            ("Ancho", "Ancho"), ("Alto", "Alto"));
        var glazing = Operation("Vidrio", FamilyOperationKind.SolidExtrusion, plane.Id, "Vidrio",
            ("Ancho", "Ancho"), ("Alto", "Alto"), ("Espesor", "Espesor vidrio"));
        glazing = glazing with { DependsOn = [frame.Id] };
        var slidingLeaves = Operation("Hojas corredizas", FamilyOperationKind.Array, plane.Id, "Hojas",
            ("Cantidad", "Número de hojas"), ("Ancho total", "Ancho"));

        var family = new FamilyDocument
        {
            Id = ArchitecturalWindowId,
            Name = "Ventana Arquitectónica Paramétrica — Guanacaste 3D",
            Category = FamilyCategory.Windows,
            Hosting = FamilyHostingBehavior.Wall,
            Parameters = [width, height, sill, leafCount, glassThickness],
            Types = [new(Guid.NewGuid(), "1200 × 1200 mm — Corrediza", [new(width.Id, 1.2, null), new(height.Id, 1.2, null)])],
            WorkPlanes = [plane],
            Operations = [frame, glazing, slidingLeaves]
        };
        family.Validate();
        return family;
    }

    private static FamilyParameterDefinition NumberParameter(string name, FamilyParameterKind kind,
        FamilyParameterScope scope, double value, double minimum, double maximum) =>
        new(Guid.NewGuid(), name, kind, scope)
        {
            DefaultNumber = value,
            Minimum = minimum,
            Maximum = maximum
        };

    private static FamilyParameterDefinition TextParameter(string name, FamilyParameterKind kind,
        FamilyParameterScope scope, string value) =>
        new(Guid.NewGuid(), name, kind, scope) { DefaultText = value };

    private static FamilyOperation Operation(string name, FamilyOperationKind kind, Guid planeId, string role,
        params (string Key, string Value)[] expressions) =>
        new(Guid.NewGuid(), name, kind)
        {
            WorkPlaneId = planeId,
            ComponentRole = role,
            Expressions = expressions.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)
        };
}
