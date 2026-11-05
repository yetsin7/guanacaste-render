namespace Guanacaste3D.Core;

public sealed partial class DocumentSession
{
    /// <summary>Sets (or, with null, clears) the region a plan, elevation, section or 3D view prints on sheets, as one undoable step.</summary>
    public void SetViewModelCrop(Guid viewId, PlanBounds? crop)
    {
        var index = Document.Views.FindIndex(candidate => candidate.Id == viewId);
        if (index < 0) throw new InvalidDataException("The view does not exist.");
        Commit("Transaction.SetViewCrop", document => document.Views[index] = document.Views[index] with { ModelCrop = crop });
    }

    /// <summary>Commits persistent model-view graphics as one undoable operation, preserving all element assignments.</summary>
    public void SetViewGraphics(Guid viewId, ViewGraphicsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        var view = Document.Views.FirstOrDefault(candidate => candidate.Id == viewId);
        if (view is null || Equals(view.Graphics ?? new ViewGraphicsSettings(), settings)) return;
        Commit("Transaction.SetViewGraphics", document =>
        {
            var index = document.Views.FindIndex(candidate => candidate.Id == viewId);
            document.Views[index] = document.Views[index] with { Graphics = settings };
        });
    }
}
