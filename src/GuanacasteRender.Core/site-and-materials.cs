using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core;

/// <summary>
/// The project's render appearance for one surface category: an image texture projected in world space (so BIM
/// geometry needs no texture coordinates), the size in meters that one copy of the image covers, and how strongly the
/// image's light and dark areas raise and lower the surface. The path may be relative to the project file.
/// </summary>
public sealed record RenderMaterialAssignment(SurfaceKind Category, string TexturePath)
{
    /// <summary>Null selects the project-wide category; an id selects only the matching faces of one BIM element.</summary>
    public Guid? ElementId { get; init; }
    /// <summary>Optional tangent-space normal map (OpenGL +Y), relative to the project file.</summary>
    public string? NormalPath { get; init; }
    /// <summary>Optional linear roughness map, relative to the project file.</summary>
    public string? RoughnessPath { get; init; }
    /// <summary>Optional linear metalness map, relative to the project file.</summary>
    public string? MetallicPath { get; init; }

    /// <summary>Meters covered by one tile of the image, along both directions.</summary>
    public double TextureSize { get; init; } = 1.0;

    /// <summary>0 for a flat image, 1 for a strong relief taken from the image's brightness.</summary>
    public double Relief { get; init; } = 0.3;
    /// <summary>Per-category or per-element roughness; null inherits the selected material/preset.</summary>
    public double? RoughnessOverride { get; init; }
    /// <summary>Per-category or per-element metalness; null inherits the selected material/preset.</summary>
    public double? MetallicOverride { get; init; }
    /// <summary>Colorización: an sRGB <c>#RRGGBB</c> that multiplies the material's colour; null leaves the colour untouched.</summary>
    public string? TintHex { get; init; }
}

/// <summary>Site terrain points and render materials; each public method is one undoable transaction.</summary>
public sealed partial class DocumentSession
{
    /// <summary>Adds (or replaces, at the same place) an elevation point of a toposolid. The point must lie on it.</summary>
    public void AddTopoPoint(Guid toposolidId, TopoPoint point)
    {
        var toposolid = Document.Floors.FirstOrDefault(floor => floor.Id == toposolidId && floor.IsToposolid)
            ?? throw new InvalidDataException("The toposolid does not exist.");
        Commit("Transaction.AddTopoPoint", document =>
        {
            var index = document.Floors.FindIndex(floor => floor.Id == toposolidId);
            var points = (toposolid.TopoPoints ?? []).Where(existing => existing.Plan.DistanceTo(point.Plan) >= 0.01).ToList();
            points.Add(point);
            document.Floors[index] = document.Floors[index] with { TopoPoints = points };
        });
    }

    /// <summary>Removes the elevation point of a toposolid nearest to <paramref name="near"/>, within the tolerance.</summary>
    public bool RemoveTopoPoint(Guid toposolidId, Point2 near, double tolerance)
    {
        var toposolid = Document.Floors.FirstOrDefault(floor => floor.Id == toposolidId && floor.IsToposolid);
        if (toposolid?.TopoPoints is not { Count: > 0 } points) return false;
        var nearest = points.MinBy(point => point.Plan.DistanceTo(near));
        if (nearest.Plan.DistanceTo(near) > tolerance) return false;
        Commit("Transaction.RemoveTopoPoint", document =>
        {
            var index = document.Floors.FindIndex(floor => floor.Id == toposolidId);
            document.Floors[index] = document.Floors[index] with { TopoPoints = points.Where(point => point != nearest).ToList() };
        });
        return true;
    }

    /// <summary>Assigns the render material of a surface category, replacing any earlier assignment for it.</summary>
    public void SetRenderMaterial(RenderMaterialAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        // Re-selecting an unchanged material must not rebuild the BIM document,
        // invalidate the live render, or consume an undo step.
        if (Document.RenderMaterials.Any(existing =>
            existing.Category == assignment.Category && existing.ElementId == assignment.ElementId && existing == assignment)) return;
        Commit("Transaction.SetRenderMaterial", document =>
        {
            document.RenderMaterials.RemoveAll(existing => existing.Category == assignment.Category && existing.ElementId == assignment.ElementId);
            document.RenderMaterials.Add(assignment);
        });
    }

    /// <summary>Returns a surface category to its stock render material.</summary>
    public void ClearRenderMaterial(SurfaceKind category, Guid? elementId = null)
    {
        if (Document.RenderMaterials.All(existing => existing.Category != category || existing.ElementId != elementId)) return;
        Commit("Transaction.ClearRenderMaterial", document =>
            document.RenderMaterials.RemoveAll(existing => existing.Category == category && existing.ElementId == elementId));
    }
}

/// <summary>Rules for render material assignments.</summary>
public static class RenderMaterialRules
{
    private static bool ValidOptionalMapPath(string? path) =>
        path is null || (!string.IsNullOrWhiteSpace(path) && path.Length <= 1024);

    public static void Validate(BuildingDocument document)
    {
        if (document.RenderMaterials is null) throw new InvalidDataException("Invalid render material.");
        if (document.RenderMaterials.Count > 512 ||
            document.RenderMaterials.GroupBy(material => (material.Category, material.ElementId)).Any(group => group.Count() > 1) ||
            document.RenderMaterials.Any(material => !Enum.IsDefined(material.Category) || material.ElementId == Guid.Empty ||
                !ValidOptionalMapPath(material.NormalPath) ||
                !ValidOptionalMapPath(material.RoughnessPath) ||
                !ValidOptionalMapPath(material.MetallicPath) ||
                !Rendering.MaterialTint.IsValid(material.TintHex) ||
                string.IsNullOrWhiteSpace(material.TexturePath) || material.TexturePath.Length > 1024 ||
                !double.IsFinite(material.TextureSize) || material.TextureSize is < 0.01 or > 1000 ||
                !double.IsFinite(material.Relief) || material.Relief is < 0 or > 1 ||
                (material.RoughnessOverride is { } rough && (!double.IsFinite(rough) || rough is < 0.02 or > 1)) ||
                (material.MetallicOverride is { } metal && (!double.IsFinite(metal) || metal is < 0 or > 1))))
            throw new InvalidDataException("Invalid render material.");
    }
}
