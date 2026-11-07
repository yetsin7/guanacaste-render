using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>Recognizes native title-block .g3df containers before the legacy door/window reader handles family files.</summary>
    private bool TryImportSheetFamily(string path)
    {
        if (!NativeDocumentStore.TryInspect(path, out var info) || info?.Kind != NativeDocumentKind.Family) return false;
        var family = NativeDocumentStore.LoadFamily(path);
        if (family.Category != FamilyCategory.TitleBlocks) return false;
        _selectedSheetId = _session.AddSheetFromFamily(family);
        _selectedViewId = null;
        ViewTabs.SelectedIndex = 3;
        RefreshAll();
        StatusText.Text = FormatText("Status.FamilyImported", family.Name);
        return true;
    }

    /// <summary>
    /// Loads a mesh component family (a detailed tree or car) and places one instance at the origin of the active level,
    /// where it can be moved and copied like any component.
    /// </summary>
    private bool TryImportMeshFamily(string path)
    {
        if (!NativeDocumentStore.TryInspect(path, out var info) || info?.Kind != NativeDocumentKind.Family) return false;
        var family = NativeDocumentStore.LoadFamily(path);
        if (family.Mesh is null) return false;
        var familyId = _session.LoadComponentFamily(family);
        _session.PlaceComponentFamily(_activeLevelId, familyId, new Point2(0, 0));
        RefreshAll();
        StatusText.Text = FormatText("Status.MeshFamilyPlaced", family.Name);
        return true;
    }
}
