using System.IO;
using System.Windows;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Import;
using Guanacaste3D.Core.Interop;
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

    /// <summary>
    /// Tells the person where the export stands and, when the render application is installed and closed, offers
    /// to open the export there. A running render application picks model changes up by itself.
    /// </summary>
    private void OfferRenderApplication(string exportPath)
    {
        var note = StatusText.Text + Environment.NewLine + Environment.NewLine + GetText("RenderExport.ExternalAssets");
        var title = GetText("Action.ExportRenderProject");
        var executable = RenderApplication.FindExecutable();
        if (executable is null || RenderApplication.IsRunning())
        {
            var hint = executable is null ? "RenderExport.NotInstalled" : "RenderExport.AlreadyOpen";
            MessageBox.Show(this, note + Environment.NewLine + Environment.NewLine + GetText(hint), title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, note + Environment.NewLine + Environment.NewLine + GetText("RenderExport.OpenNow"), title,
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = false };
            start.ArgumentList.Add(exportPath);
            System.Diagnostics.Process.Start(start)?.Dispose();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            MessageBox.Show(this, GetText("RenderExport.LaunchFailed"), title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Writes an independent project snapshot without changing the active document or its save path.</summary>
    private void ExportRenderProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = GetText("RenderExport.Filter"),
            FileName = _session.Document.Name + "-render" + ProjectFile.RenderExtension,
            DefaultExt = ProjectFile.RenderExtension,
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
            var outcome = RenderModelExport.Write(dialog.FileName, snapshot, _filePath);
            var links = _links.Links.Select(link => new ModelLink
            {
                Id = link.Id, Path = link.Path, RelativePath = link.RelativePath, Name = link.Name,
                Visible = link.Visible, Placement = link.Placement, UpAxis = link.UpAxis,
                MetersPerUnit = link.MetersPerUnit, CenterHorizontally = link.CenterHorizontally,
                PlaceOnGround = link.PlaceOnGround
            }).ToArray();
            ModelLinkFile.Save(dialog.FileName, links);
            StatusText.Text = GetText(outcome == RenderExportOutcome.MergedWithRenderWork ? "RenderExport.Merged" : "RenderExport.Completed");
            OfferRenderApplication(dialog.FileName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, LocalizedErrors.Translate(exception.Message), GetText("Dialog.ExportFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
