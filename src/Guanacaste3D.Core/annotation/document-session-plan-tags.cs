namespace Guanacaste3D.Core;

/// <summary>Persistent and undoable visibility of model-derived room and area tags per plan view.</summary>
public sealed partial class DocumentSession
{
    /// <summary>
    /// Shows or hides a plan's associative category labels without creating duplicate model objects.
    /// Repeating the same choice does not add an undo entry.
    /// </summary>
    public void SetPlanTagVisibility(Guid viewId, TagCategory category, bool visible)
    {
        if (category is not (TagCategory.Room or TagCategory.Area))
            throw new InvalidDataException("Only room and area labels can be configured with this command.");
        var view = Document.Views.FirstOrDefault(item => item.Id == viewId);
        if (view is null || view.Kind != ProjectViewKind.Plan || view.LevelId is null)
            throw new InvalidDataException("A plan view must be selected before enabling room or area labels.");
        if (category == TagCategory.Room && view.ShowRoomTags == visible ||
            category == TagCategory.Area && view.ShowAreaTags == visible) return;

        Commit("Transaction.SetPlanTagVisibility", document =>
        {
            var index = document.Views.FindIndex(item => item.Id == viewId);
            var old = document.Views[index];
            document.Views[index] = category == TagCategory.Room
                ? old with { ShowRoomTags = visible }
                : old with { ShowAreaTags = visible };
        });
    }
}
