using System.Windows.Controls;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>
/// The material list of the Highlight effect. It offers the materials the scene really uses, by the name the render
/// carries, and keeps a name chosen in a saved project even before that project's scene has been built.
/// </summary>
public partial class RenderPanel
{
    private bool _refreshingHighlight;

    /// <summary>The material the Highlight effect picks out, or null when "none" is chosen.</summary>
    private string? SelectedHighlight() => HighlightPicker.SelectedIndex > 0 ? HighlightPicker.SelectedItem as string : null;

    /// <summary>Lists the materials of the scene, keeping the current choice whenever it still exists.</summary>
    private void RefreshHighlightPicker(RenderScene? scene)
    {
        var chosen = SelectedHighlight();
        var names = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        if (scene is not null)
        {
            var used = new bool[scene.Materials.Length];
            foreach (var material in scene.TriangleMaterial)
                if ((uint)material < (uint)used.Length) used[material] = true;
            for (var id = 0; id < used.Length; id++)
                if (used[id] && !string.IsNullOrWhiteSpace(scene.Materials[id].Name)) names.Add(scene.Materials[id].Name);
        }
        // A name from a saved project stays selectable until a scene that lacks it replaces the list.
        if (chosen is not null && scene is null) names.Add(chosen);
        FillHighlightPicker(names, chosen);
    }

    private void FillHighlightPicker(IEnumerable<string> names, string? chosen)
    {
        _refreshingHighlight = true;
        try
        {
            HighlightPicker.Items.Clear();
            HighlightPicker.Items.Add(UiText.Get("RenderPanel.HighlightNone"));
            foreach (var name in names) HighlightPicker.Items.Add(name);
            var index = chosen is null ? 0 : HighlightPicker.Items.IndexOf(chosen);
            HighlightPicker.SelectedIndex = Math.Max(0, index);
        }
        finally { _refreshingHighlight = false; }
    }

    /// <summary>Restores the material a project saved; it is added to the list when the scene has not been built yet.</summary>
    private void SelectHighlight(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            HighlightPicker.SelectedIndex = 0;
            return;
        }
        var existing = HighlightPicker.Items.OfType<string>().Skip(1).ToList();
        if (!existing.Contains(name)) existing.Add(name);
        FillHighlightPicker(existing.OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase), name);
    }

    private void Highlight_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingHighlight) return;
        Finish_Changed(sender, e);
    }
}
