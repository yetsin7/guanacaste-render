using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>Synchronizes the render preview guide with the current BIM selection and shared camera.</summary>
    private void UpdateRenderSelection()
    {
        if (RenderPanelControl is null || _session is null) return;
        var selection = CurrentSelection();
        IReadOnlyList<EdgeSegment> edges = [];
        if (ViewTabs.SelectedIndex == RenderTabIndex)
        {
            if (_viewSelection is { Count: > 0 } many)
                edges = many.SelectMany(id => ElementTypeSelection.KindOf(_session.Document, id) is { } kind
                    ? Picker.Outline(kind, id) : []).ToList();
            else if (selection is { } selected) edges = Picker.Outline(selected.Kind, selected.Id);
        }
        RenderPanelControl.ShowSelection(edges);
    }
}
