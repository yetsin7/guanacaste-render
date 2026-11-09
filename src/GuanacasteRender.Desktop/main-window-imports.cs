using System.IO;
using System.Windows;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Import;
using Microsoft.Win32;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Importing models and drawings: dropping Revit, SketchUp, AutoCAD, IFC or mesh files on the window, the File menu and
/// ribbon commands, and the list of linked models. Imported geometry is linked, not embedded: the project remembers the
/// file and where it was placed, and reads it again when the project is opened.
/// </summary>
public partial class MainWindow
{
    private bool _restoringLinks;
    private CancellationTokenSource? _linkLoad;

    /// <summary>Accepts files dragged over the window.</summary>
    private void MainWindow_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Imports every file dropped on the window; a dropped project opens as a project.</summary>
    private async void MainWindow_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        e.Handled = true;
        await ImportFilesAsync(files);
    }

    /// <summary>Chooses model or drawing files and imports them.</summary>
    private async void ImportModel_Click(object sender, RoutedEventArgs e) => await ImportFromDialogAsync();

    private async Task ImportFromDialogAsync()
    {
        var dialog = new OpenFileDialog { Filter = ModelImporter.DialogFilter, Multiselect = true, CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        await ImportFilesAsync(dialog.FileNames);
    }

    /// <summary>Shows the linked models and their placement.</summary>
    private void LinkedModels_Click(object sender, RoutedEventArgs e) =>
        new LinkedModelsWindow(_links, ImportFromDialogAsync) { Owner = this }.ShowDialog();

    private async Task ImportFilesAsync(IEnumerable<string> paths)
    {
        var files = paths.Where(File.Exists).ToList();
        var imported = 0;
        foreach (var path in files)
        {
            if (FamilyFile.IsFamilyPath(path))
            {
                if (OpenAssociatedDocumentPath(path)) imported++;
                continue;
            }
            if (TemplateFile.IsTemplatePath(path))
            {
                if (ConfirmDiscard()) OpenAssociatedDocumentPath(path);
                continue;
            }
            if (ProjectFile.IsNativePath(path) || ProjectFile.IsPreviousNativePath(path) || ProjectFile.IsLegacyPath(path))
            {
                if (ConfirmDiscard()) OpenProjectPath(path);
                continue;
            }
            if (IsRevitProject(path))
            {
                if (ConfirmDiscard() && OpenRevitProject(path)) imported++;
                continue;
            }
            if (await ImportFileAsync(path)) imported++;
        }
        if (files.Count > 1) StatusText.Text = FormatText("Import.MultipleDone", imported, files.Count);
    }

    /// <summary>Revit projects and exchange files open as editable projects; Revit families and templates are only inspected.</summary>
    private static bool IsRevitProject(string path) =>
        Core.Import.Revit.ExchangeFile.IsExchangePath(path) ||
        Path.GetExtension(path).Equals(".rvt", StringComparison.OrdinalIgnoreCase);

    /// <summary>Converts a Revit project into native elements and families, and opens it as a new, unsaved project.</summary>
    private bool OpenRevitProject(string path)
    {
        // Opening a .rvt by double-click reaches this from the constructor, before the main window has ever been
        // shown, and WPF refuses a window as owner until it has (this crashed the program on startup).
        var window = new RevitProjectWindow(path);
        if (IsVisible) window.Owner = this;
        if (window.ShowDialog() != true || window.Result is not { } document) return false;
        AttachDocument(document, null);
        // Revit projects list footing and beam levels below and around the ground floor; open on the ground floor plan.
        var ground = document.Levels.First(level => level.Role == LevelRole.BaseFloor);
        if (document.Views.FirstOrDefault(view => view.Kind == ProjectViewKind.Plan && view.LevelId == ground.Id) is { } plan)
            SelectProjectView(plan.Id);
        _dirty = true;
        Title = "Guanacaste 3D — " + document.Name + " *";
        StatusText.Text = FormatText("RevitImport.Status", document.Walls.Count, document.Openings.Count, document.ComponentFamilies.Count);
        return true;
    }

    /// <summary>Inspects a file, asks for import options, imports it and places it in the project.</summary>
    private async Task<bool> ImportFileAsync(string path)
    {
        ImportInspection inspection;
        try
        {
            inspection = ModelImporter.Inspect(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, LocalizedErrors.Translate(ex.Message), GetText("Import.Failed"), MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        var hadContent = HasSceneContent();
        var window = new ImportWindow(path, inspection, _links, () => HasSceneContent() ? SceneBounds() : null) { Owner = this };
        if (window.ShowDialog() != true || window.Result is not { } link) return false;
        if (!hadContent) FrameCameraToScene();
        StatusText.Text = FormatText("Import.Imported", link.Name, _links.Get(link.Id)?.Model.TriangleCount ?? 0, 0.0);
        await Task.CompletedTask;
        return true;
    }

    /// <summary>Redraws views and marks the project as changed when links are added, moved, hidden or removed.</summary>
    private void OnLinksChanged()
    {
        _linkEdges.Clear();
        MarkSceneDirty();
        if (Model3DVisible) RebuildModelVisual();
        if (_restoringLinks) return;
        _dirty = true;
        Title = "Guanacaste 3D — " + _session.Document.Name + " *";
    }

    /// <summary>Forgets the previous project's links and reads those saved beside the one just opened.</summary>
    private void RestoreLinks(string? projectPath)
    {
        _linkLoad?.Cancel();
        _linkLoad = new CancellationTokenSource();
        _restoringLinks = true;
        try
        {
            _links.Clear();
            if (projectPath is null) return;
            _links.Restore(projectPath);
        }
        catch (InvalidDataException ex)
        {
            StatusText.Text = LocalizedErrors.Translate(ex.Message);
            return;
        }
        finally
        {
            if (_links.Links.Count == 0) _restoringLinks = false;
        }
        if (_links.Links.Count > 0) _ = LoadLinksAsync(_linkLoad.Token);
    }

    private async Task LoadLinksAsync(CancellationToken cancellation)
    {
        try
        {
            StatusText.Text = GetText("Import.Reading");
            await _links.LoadAllAsync(null, cancellation);
            var missing = _links.Links.Where(link => !_links.IsLoaded(link.Id)).Select(link => link.Path).ToList();
            StatusText.Text = missing.Count > 0 ? FormatText("Import.LinkMissing", missing[0]) : GetText("Status.Ready");
            if (!HasModelElements()) FrameCameraToScene();
        }
        catch (OperationCanceledException)
        {
            // A newer project was opened; its own load takes over.
        }
        finally
        {
            _restoringLinks = false;
        }
    }

    /// <summary>Whether the project itself, apart from linked models, has anything drawn.</summary>
    private bool HasModelElements()
    {
        var document = _session.Document;
        return document.Walls.Count + document.Floors.Count + document.Columns.Count + document.StructuralElements.Count +
               document.InPlaceFamilies.Count > 0;
    }
}
