using Guanacaste3D.Core.Navigation;

namespace Guanacaste3D.Core;

/// <summary>
/// Finds the elements that share a type with the ones already selected, the way Revit's "Select All Instances" does:
/// not everything of the same category, but exactly the same type (two wall types stay apart even though both are walls).
/// </summary>
public static class ElementTypeSelection
{
    /// <summary>What an id refers to in the document, or null when it is not a pickable model element.</summary>
    public static PickedKind? KindOf(BuildingDocument document, Guid id)
    {
        if (document.Walls.Any(item => item.Id == id)) return PickedKind.Wall;
        if (document.Openings.Any(item => item.Id == id)) return PickedKind.Opening;
        if (document.Floors.Any(item => item.Id == id)) return PickedKind.Floor;
        if (document.Columns.Any(item => item.Id == id)) return PickedKind.Column;
        if (document.StructuralElements.Any(item => item.Id == id)) return PickedKind.Structural;
        return null;
    }

    /// <summary>
    /// Every element of the same type as any of <paramref name="seeds"/>, the seeds included. A type is: a wall's wall type;
    /// an opening's kind and opening type; a slab's role and thickness; a column's shape and section; a structural or
    /// component element's kind and name.
    /// </summary>
    public static HashSet<Guid> SameType(BuildingDocument document, IEnumerable<Guid> seeds)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(seeds);
        var chosen = seeds.ToHashSet();
        var result = new HashSet<Guid>();

        var wallTypes = document.Walls.Where(item => chosen.Contains(item.Id)).Select(item => item.WallTypeId).ToHashSet();
        result.UnionWith(document.Walls.Where(item => wallTypes.Contains(item.WallTypeId)).Select(item => item.Id));

        var openingTypes = document.Openings.Where(item => chosen.Contains(item.Id))
            .Select(item => (item.Kind, item.OpeningTypeId)).ToHashSet();
        result.UnionWith(document.Openings.Where(item => openingTypes.Contains((item.Kind, item.OpeningTypeId))).Select(item => item.Id));

        var slabTypes = document.Floors.Where(item => chosen.Contains(item.Id))
            .Select(item => (item.Kind, item.Thickness)).ToHashSet();
        result.UnionWith(document.Floors.Where(item => slabTypes.Contains((item.Kind, item.Thickness))).Select(item => item.Id));

        var columnTypes = document.Columns.Where(item => chosen.Contains(item.Id))
            .Select(item => (item.Shape, item.Width, item.Depth)).ToHashSet();
        result.UnionWith(document.Columns.Where(item => columnTypes.Contains((item.Shape, item.Width, item.Depth))).Select(item => item.Id));

        var structuralTypes = document.StructuralElements.Where(item => chosen.Contains(item.Id))
            .Select(item => (item.Kind, item.Name)).ToHashSet();
        result.UnionWith(document.StructuralElements.Where(item => structuralTypes.Contains((item.Kind, item.Name))).Select(item => item.Id));

        return result;
    }
}
