namespace Guanacaste3D.Core;

public sealed partial class DocumentSession
{
    /// <summary>Replaces the elements a view hides and the ones it isolates, as one undoable step.</summary>
    public void SetViewVisibility(Guid viewId, IReadOnlyList<Guid> hidden, IReadOnlyList<Guid> isolated)
    {
        ArgumentNullException.ThrowIfNull(hidden);
        ArgumentNullException.ThrowIfNull(isolated);
        if (Document.Views.All(view => view.Id != viewId)) return;
        Commit("Transaction.SetViewVisibility", document =>
        {
            var index = document.Views.FindIndex(view => view.Id == viewId);
            document.Views[index] = document.Views[index] with
            {
                HiddenElementIds = hidden.Distinct().ToList(),
                IsolatedElementIds = isolated.Distinct().ToList()
            };
        });
    }

    /// <summary>Saves what a view hides and isolates as a named template. Returns the new template's id, or null for an empty name.</summary>
    public Guid? CreateViewTemplate(ProjectView source, string name)
    {
        ArgumentNullException.ThrowIfNull(source);
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return null;
        var id = Guid.NewGuid();
        Commit("Transaction.CreateViewTemplate", document => document.ViewTemplates.Add(new ViewTemplate(
            id, trimmed, source.HiddenElementIds.ToList(), source.IsolatedElementIds.ToList())
        {
            Graphics = source.Graphics,
            HiddenCategories = source.HiddenCategories.ToList()
        }));
        return id;
    }

    /// <summary>
    /// Takes a template's display settings but keeps the target view's own camera and crop, which belong to that view
    /// and make no sense on another.
    /// </summary>
    private static ViewGraphicsSettings? MergeGraphics(ViewGraphicsSettings? template, ViewGraphicsSettings? own) =>
        template is null ? own : template with
        {
            CameraPose = own?.CameraPose,
            CropActive = own?.CropActive ?? false,
            CropBoundaryVisible = own?.CropBoundaryVisible ?? true,
            Crop = own?.Crop
        };

    /// <summary>Gives each of the views the visibility of a template, as one undoable step.</summary>
    public void ApplyViewTemplate(Guid templateId, IReadOnlyCollection<Guid> viewIds)
    {
        ArgumentNullException.ThrowIfNull(viewIds);
        var template = Document.ViewTemplates.FirstOrDefault(item => item.Id == templateId);
        if (template is null) return;
        var targets = Document.Views.Where(view => viewIds.Contains(view.Id)).Select(view => view.Id).ToHashSet();
        if (targets.Count == 0) return;
        Commit("Transaction.ApplyViewTemplate", document =>
        {
            for (var index = 0; index < document.Views.Count; index++)
            {
                if (!targets.Contains(document.Views[index].Id)) continue;
                document.Views[index] = document.Views[index] with
                {
                    HiddenElementIds = template.HiddenElementIds.ToList(),
                    IsolatedElementIds = template.IsolatedElementIds.ToList(),
                    HiddenCategories = template.HiddenCategories.ToList(),
                    Graphics = MergeGraphics(template.Graphics, document.Views[index].Graphics)
                };
            }
        });
    }
}
