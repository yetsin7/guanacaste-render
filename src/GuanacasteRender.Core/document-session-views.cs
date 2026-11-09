namespace Guanacaste3D.Core;

/// <summary>Naming of the project's views (plans, elevations, sections, schedules and the 3D view).</summary>
public sealed partial class DocumentSession
{
    /// <summary>Adds a uniquely named elevation that initially follows the building extents automatically.</summary>
    public Guid AddElevationView(string name, ViewOrientation orientation)
    {
        name = (name ?? string.Empty).Trim();
        if (orientation == ViewOrientation.None) throw new InvalidDataException("An elevation requires an orientation.");
        ValidateViewName(name);
        var id = Guid.NewGuid();
        Commit("Transaction.AddElevationView", document => document.Views.Add(
            new ProjectView(id, name, ProjectViewKind.Elevation, null, orientation)));
        return id;
    }

    /// <summary>Deletes an elevation view when at least one other elevation remains and no sheet uses it.</summary>
    public void RemoveElevationView(Guid id)
    {
        var view = Document.Views.FirstOrDefault(candidate => candidate.Id == id)
            ?? throw new InvalidDataException("The view does not exist.");
        if (view.Kind != ProjectViewKind.Elevation) throw new InvalidDataException("Only elevation views can be removed here.");
        if (Document.Views.Count(candidate => candidate.Kind == ProjectViewKind.Elevation) <= 1)
            throw new InvalidDataException("A project requires at least one elevation view.");
        if (Document.Sheets.Any(sheet => sheet.Viewports.Any(viewport => viewport.ViewId == id)))
            throw new InvalidDataException("The view is placed on a sheet.");
        Commit("Transaction.RemoveElevationView", document => document.Views.RemoveAll(candidate => candidate.Id == id));
    }

    /// <summary>Persists a plan marker after direct manipulation or automatic coverage fitting.</summary>
    public void UpdateElevationMarker(Guid id, ElevationMarker marker)
    {
        if (!marker.IsValid) throw new InvalidDataException("The elevation coverage is invalid.");
        var view = Document.Views.FirstOrDefault(candidate => candidate.Id == id);
        if (view is not { Kind: ProjectViewKind.Elevation }) throw new InvalidDataException("The view does not exist.");
        Commit("Transaction.UpdateElevationMarker", document =>
        {
            var index = document.Views.FindIndex(candidate => candidate.Id == id);
            document.Views[index] = document.Views[index] with { ElevationMarker = marker };
        });
    }

    /// <summary>Updates the simple vertical slice used to render an architectural plan.</summary>
    public void UpdatePlanViewRange(Guid id, PlanViewRange range)
    {
        if (!range.IsValid) throw new InvalidDataException("The plan view range is invalid.");
        var view = Document.Views.FirstOrDefault(candidate => candidate.Id == id);
        if (view is not { Kind: ProjectViewKind.Plan }) throw new InvalidDataException("The view does not exist.");
        Commit("Transaction.UpdatePlanViewRange", document =>
        {
            var index = document.Views.FindIndex(candidate => candidate.Id == id);
            document.Views[index] = document.Views[index] with { Range = range };
        });
    }

    /// <summary>
    /// Renames a view, as the elevation symbol or the project browser does. Names are trimmed and must be unique, as in
    /// Revit, so a view can always be told apart in the browser and on sheets.
    /// </summary>
    public void RenameView(Guid id, string name)
    {
        var view = Document.Views.FirstOrDefault(candidate => candidate.Id == id)
            ?? throw new InvalidDataException("The view does not exist.");
        name = (name ?? string.Empty).Trim();
        ValidateViewName(name, id);
        if (name == view.Name) return;
        Commit("Transaction.RenameView", document =>
        {
            var index = document.Views.FindIndex(candidate => candidate.Id == id);
            document.Views[index] = document.Views[index] with { Name = name };
        });
    }

    /// <summary>Rejects blank, oversized, or duplicate view names before a transaction starts.</summary>
    private void ValidateViewName(string name, Guid? currentId = null)
    {
        if (name.Length is 0 or > 120) throw new InvalidDataException("The view name is not valid.");
        if (Document.Views.Any(other => other.Id != currentId &&
            string.Equals(other.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            throw new InvalidDataException("The view name is already in use.");
    }
}
