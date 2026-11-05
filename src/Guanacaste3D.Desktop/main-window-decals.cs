using System.IO;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop;

/// <summary>Insert > Decal: images stuck on walls, kept in the project and drawn in the render.</summary>
public partial class MainWindow
{
    /// <summary>Opens the window of decals, on the selected wall if there is one, and saves what it returns.</summary>
    private void ShowDecalWindow()
    {
        var dialog = new DecalDialog(_session.Document, SelectedWallOrNull(), _filePath) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } decals) return;
        SaveDecals(decals);
    }

    /// <summary>Saves the decals as one undoable step; the 3D views and the render scene are refreshed because they draw them.</summary>
    private void SaveDecals(IReadOnlyCollection<Decal> decals)
    {
        try
        {
            _session.SetExtension(Decals.Key, Decals.Serialize(decals));
            StatusText.Text = decals.Count == 0 ? "No hay estampados en el proyecto." : $"{decals.Count} estampado(s) en el proyecto; se ven en el render.";
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = "No se pudieron guardar los estampados: " + exception.Message;
        }
    }

    /// <summary>The file of a decal's image: an absolute path as it is, a relative one from the project's folder.</summary>
    private static string DecalImagePath(string path, string? projectFile)
    {
        if (Path.IsPathRooted(path) || projectFile is null) return path;
        var folder = Path.GetDirectoryName(Path.GetFullPath(projectFile));
        return folder is null ? path : Path.GetFullPath(Path.Combine(folder, path));
    }
}
