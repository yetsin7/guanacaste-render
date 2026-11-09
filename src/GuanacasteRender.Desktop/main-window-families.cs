using System.IO;
using System.Windows;
using Guanacaste3D.Core;
using Microsoft.Win32;

namespace Guanacaste3D.Desktop;

/// <summary>File commands for project templates (.g3dt) and door or window families (.g3df).</summary>
public partial class MainWindow
{
    /// <summary>Routes an associated file to its native workflow and reports whether it opened successfully.</summary>
    private bool OpenAssociatedDocumentPath(string path)
    {
        try
        {
            if (FamilyFile.IsFamilyPath(path))
            {
                if (TryImportSheetFamily(path) || TryImportMeshFamily(path)) return true;
                var family = FamilyFile.Load(path);
                _session.ImportOpeningFamily(family);
                StatusText.Text = FormatText("Status.FamilyImported", family.Name);
            }
            else if (IsRevitProject(path))
            {
                // "Open with Guanacaste 3D" on a .rvt or .g3dx converts it, as dropping it on the window does.
                return OpenRevitProject(path);
            }
            else if (TemplateFile.IsTemplatePath(path))
            {
                var template = TemplateFile.Load(path);
                AttachDocument(TemplateRules.CreateProject(template, GetText("Project.Untitled")), null);
                StatusText.Text = GetText("Status.ProjectFromTemplate");
            }
            else
            {
                OpenProjectPath(path);
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.Text.Json.JsonException or InvalidDataException)
        {
            MessageBox.Show(this, LocalizedErrors.Translate(exception.Message), GetText("Dialog.OpenFailed"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>Starts a project from the bundled metric architecture template after protecting unsaved edits.</summary>
    private void NewArchitecturalMetric_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        AttachDocument(TemplateRules.CreateProject(OfficialTemplates.CreateArchitecturalMetric(),
            GetText("Project.Untitled")), null);
        StatusText.Text = GetText("Status.ProjectFromTemplate");
    }

    /// <summary>Opens the modern villa sample, the reference scene for photorealistic rendering.</summary>
    private void OpenModernHouse_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        AttachDocument(ModernHouseShowcase.Create(), null);
        StatusText.Text = GetText("Status.ModernHouseOpened");
    }

    /// <summary>
    /// Opens the embedded breeze-block house as an untitled copy, preserving the original example.
    /// </summary>
    private void OpenBreezeBlockHouse_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        OpenBreezeBlockHouse();
    }

    /// <summary>Loads the breeze-block house sample and reports whether it opened.</summary>
    private bool OpenBreezeBlockHouse()
    {
        try
        {
            AttachDocument(BreezeBlockSample.Load(), null);
            StatusText.Text = GetText("Status.BreezeBlockHouseOpened");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, LocalizedErrors.Translate(ex.Message), GetText("Dialog.OpenFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>Starts a project from a .g3dt file. The template is only read, so it never changes.</summary>
    private void NewFromTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dialog = new OpenFileDialog
        {
            Filter = GetText("Dialog.TemplateFilter"),
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var template = TemplateFile.Load(dialog.FileName);
            AttachDocument(TemplateRules.CreateProject(template, GetText("Project.Untitled")), null);
            StatusText.Text = GetText("Status.ProjectFromTemplate");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            MessageBox.Show(this, LocalizedErrors.Translate(ex.Message), GetText("Dialog.OpenFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Writes the project's setup as a reusable template. The open project keeps its file and edits.</summary>
    private void SaveAsTemplate_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = GetText("Dialog.TemplateFilter"),
            FileName = SafeFileName(_session.Document.Name) + TemplateFile.Extension,
            AddExtension = true,
            DefaultExt = TemplateFile.Extension
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            TemplateFile.Save(dialog.FileName, _session.Document);
            StatusText.Text = FormatText("Status.TemplateSaved", Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, LocalizedErrors.Translate(ex.Message), GetText("Dialog.SaveFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Merges a .g3df family into the project's door and window catalog as one undoable step.</summary>
    private void ImportFamily_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = GetText("Dialog.FamilyFilter"),
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (TryImportSheetFamily(dialog.FileName) || TryImportMeshFamily(dialog.FileName)) return;
            var family = FamilyFile.Load(dialog.FileName);
            _session.ImportOpeningFamily(family);
            StatusText.Text = FormatText("Status.FamilyImported", family.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            MessageBox.Show(this, LocalizedErrors.Translate(ex.Message), GetText("Dialog.OpenFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Saves the family of the selected door or window, with every type that family uses in this project.</summary>
    private void SaveFamily_Click(object sender, RoutedEventArgs e)
    {
        var opening = _selectedOpeningId is Guid openingId
            ? _session.Document.Openings.FirstOrDefault(candidate => candidate.Id == openingId)
            : null;
        var type = opening is null
            ? null
            : _session.Document.OpeningTypes.FirstOrDefault(candidate => candidate.Id == opening.OpeningTypeId);
        if (type is null)
        {
            StatusText.Text = GetText("Status.SelectOpeningForFamily");
            return;
        }
        try
        {
            var family = OpeningFamilyDocument.FromProject(_session.Document, type.FamilyName);
            var dialog = new SaveFileDialog
            {
                Filter = GetText("Dialog.FamilyFilter"),
                FileName = SafeFileName(family.Name) + FamilyFile.Extension,
                AddExtension = true,
                DefaultExt = FamilyFile.Extension
            };
            if (dialog.ShowDialog(this) != true) return;
            FamilyFile.Save(dialog.FileName, family);
            StatusText.Text = FormatText("Status.FamilySaved", family.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, LocalizedErrors.Translate(ex.Message), GetText("Dialog.SaveFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Replaces characters Windows cannot use in file names, so suggested names always save.</summary>
    private static string SafeFileName(string name) =>
        string.Concat(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
}
