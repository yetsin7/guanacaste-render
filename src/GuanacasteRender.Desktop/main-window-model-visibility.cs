using Guanacaste3D.Core;
using System.Windows.Media;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private DocumentSession? _temporarySession;
    private readonly HashSet<Guid> _temporaryHidden = [];
    private readonly HashSet<Guid> _temporaryIsolated = [];
    private bool _temporaryActive;
    private bool _revealHidden;

    /// <summary>Filters modeling, picking, bounds, and rendering consistently without changing saved view settings.</summary>
    private BuildingDocument ModelDisplayDocument(bool reveal = false)
    {
        EnsureTemporarySession();
        var view = Model3DView();
        if (reveal) return _session.Document.Clone();
        var persistent = ViewVisibility.Apply(_session.Document, view);
        if (!_temporaryActive || view is null) return persistent;
        return ViewVisibility.Apply(persistent, view with
        {
            HiddenElementIds = _temporaryHidden.ToList(), IsolatedElementIds = _temporaryIsolated.ToList()
        });
    }

    /// <summary>Prevents a new or opened document from inheriting another document's transient filtering.</summary>
    private void EnsureTemporarySession()
    {
        if (ReferenceEquals(_temporarySession, _session)) return;
        _temporarySession = _session;
        _temporaryHidden.Clear();
        _temporaryIsolated.Clear();
        _temporaryActive = _revealHidden = false;
    }

    /// <summary>Applies temporary element/category filters or explicitly promotes them to persistent view visibility.</summary>
    private void ExecuteTemporaryVisibility(string command)
    {
        EnsureTemporarySession();
        if (command == "Temporary.Reset")
        {
            _temporaryHidden.Clear();
            _temporaryIsolated.Clear();
            _temporaryActive = false;
        }
        else if (command == "Temporary.Apply" && Model3DView() is { } view)
        {
            var visible = ModelElementIds(ModelDisplayDocument()).ToHashSet();
            var hidden = ModelElementIds(_session.Document).Where(id => !visible.Contains(id)).ToList();
            _session.SetViewVisibility(view.Id, hidden, []);
            _temporaryHidden.Clear();
            _temporaryIsolated.Clear();
            _temporaryActive = false;
        }
        else if (command == "Unhide")
        {
            var selected = SelectedIds();
            if (Model3DView() is { } permanent)
                _session.SetViewVisibility(permanent.Id, permanent.HiddenElementIds.Except(selected).ToList(),
                    permanent.IsolatedElementIds.Count > 0 ? permanent.IsolatedElementIds.Union(selected).ToList() : []);
            _temporaryHidden.ExceptWith(selected);
            if (_temporaryIsolated.Count > 0) _temporaryIsolated.UnionWith(selected);
        }
        else
        {
            var selected = SelectedIds();
            if (selected.Count == 0) return;
            if (command.EndsWith("Category", StringComparison.Ordinal)) selected = SelectedCategoryIds();
            if (command.Contains("Isolate", StringComparison.Ordinal))
            {
                ExpandHostedSelection(selected);
                _temporaryIsolated.Clear();
                _temporaryIsolated.UnionWith(selected);
                _temporaryHidden.ExceptWith(selected);
            }
            else _temporaryHidden.UnionWith(selected);
            _temporaryActive = true;
        }
        _picker = null;
        MarkSceneDirty();
        RebuildModelVisual();
        UpdateViewControlState();
    }

    /// <summary>Includes required hosts and hosted openings so isolation retains valid architectural geometry.</summary>
    private void ExpandHostedSelection(List<Guid> selected)
    {
        var selectedWalls = _session.Document.Walls.Where(wall => selected.Contains(wall.Id)).Select(wall => wall.Id).ToHashSet();
        selected.AddRange(_session.Document.Openings.Where(opening => selectedWalls.Contains(opening.WallId)).Select(opening => opening.Id));
        selected.AddRange(_session.Document.Openings.Where(opening => selected.Contains(opening.Id)).Select(opening => opening.WallId));
    }

    /// <summary>Resolves selected categories against all native pickable model elements.</summary>
    private List<Guid> SelectedCategoryIds()
    {
        var selected = SelectedIds().ToHashSet();
        var document = _session.Document;
        var result = new List<Guid>();
        if (document.Walls.Any(wall => selected.Contains(wall.Id))) result.AddRange(document.Walls.Select(wall => wall.Id));
        foreach (var kind in document.Openings.Where(opening => selected.Contains(opening.Id)).Select(opening => opening.Kind).Distinct())
            result.AddRange(document.Openings.Where(opening => opening.Kind == kind).Select(opening => opening.Id));
        if (document.Floors.Any(floor => selected.Contains(floor.Id))) result.AddRange(document.Floors.Select(floor => floor.Id));
        if (document.Columns.Any(column => selected.Contains(column.Id))) result.AddRange(document.Columns.Select(column => column.Id));
        foreach (var kind in document.StructuralElements.Where(element => selected.Contains(element.Id)).Select(element => element.Kind).Distinct())
            result.AddRange(document.StructuralElements.Where(element => element.Kind == kind).Select(element => element.Id));
        return result;
    }

    /// <summary>Enumerates element identities for visibility diffs and reveal-mode geometry.</summary>
    private static IEnumerable<Guid> ModelElementIds(BuildingDocument document) => document.Walls.Select(wall => wall.Id)
        .Concat(document.Openings.Select(opening => opening.Id)).Concat(document.Floors.Select(floor => floor.Id))
        .Concat(document.Columns.Select(column => column.Id)).Concat(document.StructuralElements.Select(element => element.Id));

}
