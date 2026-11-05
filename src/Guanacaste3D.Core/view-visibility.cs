namespace Guanacaste3D.Core;

/// <summary>
/// Per-view hiding and isolation. A view draws a copy of the building without the elements it hides, so the same
/// building can show different things in different views.
/// </summary>
/// <summary>A saved set of hidden and isolated elements that any view can take on, one at a time or many at once.</summary>
public sealed record ViewTemplate(Guid Id, string Name, List<Guid> HiddenElementIds, List<Guid> IsolatedElementIds)
{
    /// <summary>The display settings (style, detail, sun, edges) the template gives a view; null leaves a view's own settings alone.</summary>
    public ViewGraphicsSettings? Graphics { get; init; }

    /// <summary>The categories the template hides.</summary>
    public List<ViewCategory> HiddenCategories { get; init; } = [];
}

public static class ViewVisibility
{
    /// <summary>
    /// Returns a copy of the document that keeps only what the view shows. With no view, or a view that hides and
    /// isolates nothing, the copy is complete.
    /// </summary>
    public static BuildingDocument Apply(BuildingDocument document, ProjectView? view)
    {
        var copy = document.Clone();
        if (view is null || (view.HiddenElementIds.Count == 0 && view.IsolatedElementIds.Count == 0 &&
                             view.HiddenCategories.Count == 0 && view.Filters.Count == 0 &&
                             document.OptionAssignments.Count == 0 && !ProjectStages.PhasingActive(document, view) &&
                             document.Worksets.All(workset => workset.Visible))) return copy;

        // Phases and design options hide elements of any kind, so they are worked out once, for every element at once.
        var staged = document.Walls.Select(item => item.Id).Concat(document.Openings.Select(item => item.Id))
            .Concat(document.Floors.Select(item => item.Id)).Concat(document.Columns.Select(item => item.Id))
            .Concat(document.StructuralElements.Select(item => item.Id)).ToArray();
        var stageHidden = ProjectStages.HiddenByPhase(document, view, staged);
        stageHidden.UnionWith(ProjectStages.HiddenByOption(document, view));
        // A workset that is switched off takes its elements out of every view.
        var hiddenWorksets = document.Worksets.Where(workset => !workset.Visible).Select(workset => workset.Id).ToHashSet();
        if (hiddenWorksets.Count > 0)
            stageHidden.UnionWith(document.WorksetAssignments.Where(item => hiddenWorksets.Contains(item.WorksetId)).Select(item => item.Id));

        bool CategoryShown(ViewCategory category) => !view.HiddenCategories.Contains(category);

        bool Shown(Guid id) => !view.HiddenElementIds.Contains(id) && !stageHidden.Contains(id) &&
                               (view.IsolatedElementIds.Count == 0 || view.IsolatedElementIds.Contains(id));

        copy.Walls.RemoveAll(wall => !Shown(wall.Id) || !CategoryShown(ViewCategory.Walls) ||
                                     view.Filters.Any(filter => filter.Matches(document, wall)));
        // An opening belongs to its wall: it shows with the wall unless it is hidden itself.
        copy.Openings.RemoveAll(opening => !Shown(opening.Id) ||
            !CategoryShown(opening.Kind == OpeningKind.Door ? ViewCategory.Doors : ViewCategory.Windows) ||
            !copy.Walls.Any(wall => wall.Id == opening.WallId));
        copy.Floors.RemoveAll(floor => !Shown(floor.Id) || !CategoryShown(ViewCategories.Of(floor.Kind)));
        copy.Columns.RemoveAll(column => !Shown(column.Id) || !CategoryShown(ViewCategory.Columns));
        copy.StructuralElements.RemoveAll(element => !Shown(element.Id) ||
            (ViewCategories.Of(element.Kind) is { } category && !CategoryShown(category)));
        if (!CategoryShown(ViewCategory.Rooms)) copy.Rooms.Clear();
        return copy;
    }
}
