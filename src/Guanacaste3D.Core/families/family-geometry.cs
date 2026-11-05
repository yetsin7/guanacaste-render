using System.Numerics;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core;

/// <summary>
/// Regeneration of family solids. An extrusion is a <see cref="FamilyOperation"/> of kind
/// <see cref="FamilyOperationKind.SolidExtrusion"/> with a <see cref="FamilyOperation.Profile"/> in its work
/// plane's coordinates. Its depth and start offset are expressions: a parameter name, a number, or a formula
/// over parameters. Operations without a profile have no geometry yet and regenerate nothing.
/// </summary>
public static class FamilyGeometry
{
    /// <summary>Expression key of the extrusion depth, in meters.</summary>
    public const string DepthKey = "Profundidad";

    /// <summary>Expression key of the start offset along the work-plane normal, in meters.</summary>
    public const string OffsetKey = "Desfase";

    /// <summary>Smallest accepted extrusion depth, in meters (1 mm).</summary>
    public const double MinimumDepth = 0.001;

    /// <summary>Largest accepted extrusion depth, in meters (1 km).</summary>
    public const double MaximumDepth = 1000;

    /// <summary>Largest accepted distance of an extrusion start from its work plane, in meters.</summary>
    public const double MaximumOffset = 1000;

    /// <summary>Largest deviation from unit length or orthogonality accepted for a work plane's axes.</summary>
    private const double PlaneTolerance = 1e-6;

    /// <summary>
    /// Validates every extrusion that has a profile: its work plane, its profile, and that its depth and
    /// offset evaluate to lengths in range for every family type. <see cref="FamilyDocument.Validate"/> calls this.
    /// </summary>
    public static void ValidateSolids(FamilyDocument family)
    {
        foreach (var operation in family.Operations.Where(operation => operation.Profile.Count > 0))
        {
            if (operation.Kind is not (FamilyOperationKind.SolidExtrusion or FamilyOperationKind.VoidExtrusion))
                throw new InvalidDataException("Only extrusions can take a profile.");
            ValidatePlane(PlaneOf(family, operation));
            InPlaceProfileRules.Validate(operation.Profile);
            foreach (var type in family.Types)
            {
                var depth = Depth(family, operation, type.Id);
                if (!double.IsFinite(depth) || depth < MinimumDepth || depth > MaximumDepth)
                    throw new InvalidDataException(
                        $"The depth of extrusion '{operation.Name}' must be between 1 mm and 1 km.");
                var offset = Offset(family, operation, type.Id);
                if (!double.IsFinite(offset) || Math.Abs(offset) > MaximumOffset)
                    throw new InvalidDataException($"The start offset of extrusion '{operation.Name}' is out of range.");
            }
        }
    }

    /// <summary>Returns the work plane an operation is drawn on.</summary>
    public static FamilyWorkPlane PlaneOf(FamilyDocument family, FamilyOperation operation) =>
        family.WorkPlanes.FirstOrDefault(plane => plane.Id == operation.WorkPlaneId)
        ?? throw new InvalidDataException("An extrusion refers to a work plane that does not exist.");

    /// <summary>Returns the extrusion depth in meters for the selected type, or the first type by default.</summary>
    public static double Depth(FamilyDocument family, FamilyOperation operation, Guid? typeId = null) =>
        Length(family, operation, DepthKey, FindType(family, typeId))
        ?? throw new InvalidDataException("An extrusion has no depth expression.");

    /// <summary>Returns the start offset for the selected type in meters; zero when the operation has none.</summary>
    public static double Offset(FamilyDocument family, FamilyOperation operation, Guid? typeId = null) =>
        Length(family, operation, OffsetKey, FindType(family, typeId)) ?? 0;

    /// <summary>Builds a triangle mesh for the selected type, or the first type when none is specified.</summary>
    public static SceneMesh Tessellate(FamilyDocument family, Guid? typeId = null)
    {
        var mesh = new SceneMesh();
        AppendTo(mesh, family, typeId);
        return mesh;
    }

    /// <summary>Appends the selected type's regenerated solids to a scene mesh used by views and exports.</summary>
    public static void AppendTo(SceneMesh mesh, FamilyDocument family, Guid? typeId = null)
    {
        FindType(family, typeId);
        foreach (var operation in family.Operations.Where(operation =>
                     operation.Kind == FamilyOperationKind.SolidExtrusion && operation.Profile.Count > 0))
            AppendExtrusion(mesh, family, operation, typeId);
    }

    /// <summary>
    /// Returns the signed volume enclosed by a closed mesh in cubic meters. Outward-facing triangles give a
    /// positive result, so this also checks face orientation.
    /// </summary>
    public static double SignedVolume(SceneMesh mesh)
    {
        var sum = 0d;
        for (var index = 0; index < mesh.Indices.Count; index += 3)
        {
            sum += Determinant(
                mesh.Positions[mesh.Indices[index]],
                mesh.Positions[mesh.Indices[index + 1]],
                mesh.Positions[mesh.Indices[index + 2]]);
        }
        return sum / 6;
    }

    /// <summary>
    /// Evaluates a length expression: a parameter name is read directly, anything else is parsed as a formula
    /// over the family's parameters. Returns null when the expression is absent.
    /// </summary>
    private static double? Length(FamilyDocument family, FamilyOperation operation, string key, FamilyTypeDefinition type)
    {
        if (!operation.Expressions.TryGetValue(key, out var expression)) return null;
        var text = expression.Trim();
        var parameter = family.Parameters.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, text, StringComparison.OrdinalIgnoreCase));
        if (parameter is not null)
        {
            var resolved = Resolve(family, type, parameter.Name);
            if (resolved is not { Kind: FormulaKind.Number })
                throw new InvalidDataException("An extrusion expression does not evaluate to a length.");
            return resolved.Value.Number;
        }
        var value = FormulaEvaluator.Evaluate(FormulaParser.Parse(text), name => Resolve(family, type, name));
        if (value is not { Kind: FormulaKind.Number })
            throw new InvalidDataException("An extrusion expression does not evaluate to a length.");
        return value.Value.Number;
    }

    /// <summary>Resolves a parameter name for the formula evaluator; null when the family has no such parameter.</summary>
    private static FormulaValue? Resolve(FamilyDocument family, FamilyTypeDefinition type, string name) =>
        ResolveParameter(family, type, name, new HashSet<Guid>());

    /// <summary>Evaluates calculated parameters recursively, detecting cycles even before document validation.</summary>
    private static FormulaValue? ResolveParameter(FamilyDocument family, FamilyTypeDefinition type, string name, HashSet<Guid> active)
    {
        var parameter = family.Parameters.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        if (parameter is null) return null;
        if (!active.Add(parameter.Id))
            throw new InvalidDataException($"Formula error: '{parameter.Name}' depends on itself through other formulas.");
        try
        {
            if (!string.IsNullOrWhiteSpace(parameter.Formula))
                return FormulaEvaluator.Evaluate(FormulaParser.Parse(parameter.Formula),
                    reference => ResolveParameter(family, type, reference, active));
            var typed = type.Values.FirstOrDefault(value => value.ParameterId == parameter.Id);
            if (parameter.Kind is FamilyParameterKind.Text or FamilyParameterKind.Material or
                FamilyParameterKind.ElementReference or FamilyParameterKind.Enumeration)
            {
                var value = typed?.Text ?? parameter.DefaultText;
                return value is null ? null : FormulaValue.Of(value);
            }
            var number = typed?.Number ?? parameter.DefaultNumber;
            if (number is null) return null;
            return parameter.Kind is FamilyParameterKind.Boolean or FamilyParameterKind.Visibility
                ? FormulaValue.Of(number.Value != 0) : FormulaValue.Of(number.Value);
        }
        finally { active.Remove(parameter.Id); }
    }

    /// <summary>Resolves an explicit type or retains the historical first-type default.</summary>
    private static FamilyTypeDefinition FindType(FamilyDocument family, Guid? typeId) =>
        (typeId is null ? family.Types.FirstOrDefault() : family.Types.FirstOrDefault(type => type.Id == typeId))
        ?? throw new InvalidDataException("A family type is invalid.");

    /// <summary>
    /// Emits the prism swept by a counter-clockwise profile: a bottom cap, a top cap and one quad per profile
    /// edge. Caps come from the ear-clipping triangulation of the same loop.
    /// </summary>
    private static void AppendExtrusion(SceneMesh mesh, FamilyDocument family, FamilyOperation operation, Guid? typeId)
    {
        var plane = PlaneOf(family, operation);
        var depth = Depth(family, operation, typeId);
        var offset = Offset(family, operation, typeId);
        var profile = InPlaceProfileRules.CounterClockwise(operation.Profile);
        IReadOnlyList<(int A, int B, int C)> triangles;
        try
        {
            triangles = PolygonTriangulator.Triangulate(profile);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"The profile cannot be triangulated: {exception.Message}");
        }
        var bottom = profile.Select(point => ToVector(plane, point, offset)).ToArray();
        var top = profile.Select(point => ToVector(plane, point, offset + depth)).ToArray();
        foreach (var (a, b, c) in triangles)
        {
            mesh.AddTriangle(top[a], top[b], top[c], SurfaceKind.Wall);
            mesh.AddTriangle(bottom[a], bottom[c], bottom[b], SurfaceKind.Wall);
        }
        for (var index = 0; index < profile.Count; index++)
        {
            var next = (index + 1) % profile.Count;
            mesh.AddQuad(bottom[index], bottom[next], top[next], top[index], SurfaceKind.Wall);
        }
    }

    /// <summary>Maps a plane-local point, lifted along the normal, to a single-precision mesh vertex.</summary>
    private static Vector3 ToVector(FamilyWorkPlane plane, Point2 local, double along)
    {
        var yAxis = FamilyPoint3.Cross(plane.Normal, plane.XAxis);
        var x = plane.Origin.X + plane.XAxis.X * local.X + yAxis.X * local.Y + plane.Normal.X * along;
        var y = plane.Origin.Y + plane.XAxis.Y * local.X + yAxis.Y * local.Y + plane.Normal.Y * along;
        var z = plane.Origin.Z + plane.XAxis.Z * local.X + yAxis.Z * local.Y + plane.Normal.Z * along;
        return new((float)x, (float)y, (float)z);
    }

    /// <summary>Validates the plane's origin, unit axes and orthogonality.</summary>
    private static void ValidatePlane(FamilyWorkPlane plane)
    {
        if (!plane.Origin.IsFinite || !plane.Normal.IsFinite || !plane.XAxis.IsFinite ||
            Math.Abs(plane.Normal.Length - 1) > PlaneTolerance ||
            Math.Abs(plane.XAxis.Length - 1) > PlaneTolerance ||
            Math.Abs(FamilyPoint3.Dot(plane.Normal, plane.XAxis)) > PlaneTolerance)
            throw new InvalidDataException("The work plane must have a finite origin and orthogonal unit axes.");
    }

    /// <summary>Returns the determinant a · (b × c), computed in double precision.</summary>
    private static double Determinant(Vector3 a, Vector3 b, Vector3 c)
    {
        var (ax, ay, az) = ((double)a.X, (double)a.Y, (double)a.Z);
        var (bx, by, bz) = ((double)b.X, (double)b.Y, (double)b.Z);
        var (cx, cy, cz) = ((double)c.X, (double)c.Y, (double)c.Z);
        return ax * (by * cz - bz * cy) - ay * (bx * cz - bz * cx) + az * (bx * cy - by * cx);
    }
}
