using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Navigation;

namespace Guanacaste3D.Desktop;

/// <summary>Applies one selection-centered orbit policy to the modeling and rendering workspaces.</summary>
public partial class MainWindow
{
    private (int Version, BuildingDocument Document, ElementPicker Picker)? _orbitSelectionCache;
    private (int Version, BuildingDocument Document, Vector3 Center)? _orbitProjectCache;

    /// <summary>
    /// Uses the selected element's full bounds, including every stair flight and its level elevation.
    /// With no surviving selection, uses the complete project and linked geometry, independent of view visibility.
    /// Results are cached between edits so pointer movement never rebuilds project geometry.
    /// </summary>
    private Vector3 ResolveModelOrbitPivot()
    {
        var document = _session.Document;
        if (SelectedIds() is { Count: > 0 } selectedIds)
        {
            if (_orbitSelectionCache is not { } cached || cached.Version != _sceneVersion ||
                !ReferenceEquals(cached.Document, document))
                _orbitSelectionCache = (_sceneVersion, document, new ElementPicker(document));
            if (_orbitSelectionCache.Value.Picker.Bounds(selectedIds) is { } selected)
                return (selected.Min + selected.Max) / 2;
        }
        if (_orbitProjectCache is { } project && project.Version == _sceneVersion &&
            ReferenceEquals(project.Document, document)) return project.Center;

        var hasModel = HasModelElements();
        var bounds = hasModel ? BuildingMesher.Build(document).Bounds() :
            (Min: new Vector3(-5, -5, 0), Max: new Vector3(5, 5, 3));
        if (_links.Bounds() is { } linked)
            bounds = hasModel ? (Vector3.Min(bounds.Min, linked.Min), Vector3.Max(bounds.Max, linked.Max)) : linked;
        var center = (bounds.Min + bounds.Max) / 2;
        _orbitProjectCache = (_sceneVersion, document, center);
        return center;
    }
}
