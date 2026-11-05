namespace Guanacaste3D.Core;

/// <summary>
/// A kind of model element a view can hide as a whole. Unlike hiding chosen elements, hiding a category also
/// hides the elements of that category added later.
/// </summary>
public enum ViewCategory
{
    Walls,
    Doors,
    Windows,
    Floors,
    Ceilings,
    Roofs,
    Terrain,
    Columns,
    StructuralFraming,
    Foundations,
    Stairs,
    Railings,
    Ramps,
    CurtainWalls,
    Planting,
    Rooms,
    Masses,
    Mep,
    Reinforcement
}

/// <summary>Maps model elements to the view category that controls their visibility.</summary>
public static class ViewCategories
{
    /// <summary>The category of a slab-like element.</summary>
    public static ViewCategory Of(FloorKind kind) => kind switch
    {
        FloorKind.Ceiling => ViewCategory.Ceilings,
        FloorKind.Roof => ViewCategory.Roofs,
        FloorKind.Toposolid => ViewCategory.Terrain,
        _ => ViewCategory.Floors
    };

    /// <summary>The category of a structural or architectural element, or null when it has no category of its own.</summary>
    public static ViewCategory? Of(StructuralElementKind kind) => kind switch
    {
        StructuralElementKind.Beam or StructuralElementKind.Brace or StructuralElementKind.Truss or
            StructuralElementKind.Connection => ViewCategory.StructuralFraming,
        StructuralElementKind.WallFoundation or StructuralElementKind.IsolatedFoundation => ViewCategory.Foundations,
        StructuralElementKind.Stair => ViewCategory.Stairs,
        StructuralElementKind.Railing => ViewCategory.Railings,
        StructuralElementKind.Ramp => ViewCategory.Ramps,
        StructuralElementKind.CurtainWall => ViewCategory.CurtainWalls,
        StructuralElementKind.Planting => ViewCategory.Planting,
        StructuralElementKind.Mass => ViewCategory.Masses,
        var service when MepElements.IsMep(service) => ViewCategory.Mep,
        StructuralElementKind.Rebar or StructuralElementKind.FabricSheet => ViewCategory.Reinforcement,
        _ => null
    };
}

public sealed partial class DocumentSession
{
    /// <summary>Replaces the categories a view hides, as one undoable step.</summary>
    public void SetViewHiddenCategories(Guid viewId, IReadOnlyCollection<ViewCategory> hidden)
    {
        ArgumentNullException.ThrowIfNull(hidden);
        if (hidden.Any(category => !Enum.IsDefined(category))) throw new InvalidDataException("The view category is invalid.");
        if (Document.Views.All(view => view.Id != viewId)) return;
        var categories = hidden.Distinct().Order().ToList();
        Commit("Transaction.SetViewCategories", document =>
        {
            var index = document.Views.FindIndex(view => view.Id == viewId);
            document.Views[index] = document.Views[index] with { HiddenCategories = categories };
        });
    }
}
