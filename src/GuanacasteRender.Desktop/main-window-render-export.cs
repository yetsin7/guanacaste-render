using System.IO;
using System.Windows;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Import;
using Microsoft.Win32;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>Preserves the original location of external material files when a snapshot moves.</summary>
    private static string? RebaseRenderAsset(string? value, string sourceFolder)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var prefix = value.StartsWith("pbr:", StringComparison.OrdinalIgnoreCase) ? "pbr:" : "";
        var path = prefix.Length == 0 ? value : value[prefix.Length..];
        if (prefix.Length == 0 && (path.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("material:", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("stock:", StringComparison.OrdinalIgnoreCase))) return value;
        return prefix + (Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(sourceFolder, path)));
    }

    /// <summary>Writes an independent project snapshot without changing the active document or its save path.</summary>
    private void ExportRenderProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = GetText("RenderExport.Filter"),
            FileName = _session.Document.Name + "-render.g3d",
            DefaultExt = ".g3d",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        if (_filePath is not null && string.Equals(Path.GetFullPath(_filePath), Path.GetFullPath(dialog.FileName), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, GetText("RenderExport.SameFile"), GetText("Dialog.ExportFailed"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            var snapshot = _session.Document.Clone();
            snapshot.RenderSetup = RenderPanelControl.CaptureSetupForProject(_navigation, dialog.FileName);
            if (_filePath is not null)
            {
                var sourceFolder = Path.GetDirectoryName(Path.GetFullPath(_filePath))!;
                snapshot.RenderMaterials = snapshot.RenderMaterials.Select(material => material with
                {
                    TexturePath = RebaseRenderAsset(material.TexturePath, sourceFolder)!,
                    NormalPath = RebaseRenderAsset(material.NormalPath, sourceFolder),
                    RoughnessPath = RebaseRenderAsset(material.RoughnessPath, sourceFolder),
                    MetallicPath = RebaseRenderAsset(material.MetallicPath, sourceFolder)
                }).ToList();
            }
            ProjectFile.Save(dialog.FileName, snapshot);
            var links = _links.Links.Select(link => new ModelLink
            {
                Id = link.Id, Path = link.Path, RelativePath = link.RelativePath, Name = link.Name,
                Visible = link.Visible, Placement = link.Placement, UpAxis = link.UpAxis,
                MetersPerUnit = link.MetersPerUnit, CenterHorizontally = link.CenterHorizontally,
                PlaceOnGround = link.PlaceOnGround
            }).ToArray();
            ModelLinkFile.Save(dialog.FileName, links);
            StatusText.Text = GetText("RenderExport.Completed");
            MessageBox.Show(this, GetText("RenderExport.ExternalAssets"), GetText("Action.ExportRenderProject"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, LocalizedErrors.Translate(exception.Message), GetText("Dialog.ExportFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
