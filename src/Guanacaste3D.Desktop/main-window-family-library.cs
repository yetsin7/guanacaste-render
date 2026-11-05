using System.IO;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private Window? _libraryWindow;

    /// <summary>Routes Load Cloud Family: the shared family library.</summary>
    private bool TryExecuteFamilyLibraryCommand(string commandId)
    {
        switch (commandId)
        {
            case "LoadCloudFamily": ShowFamilyLibraryWindow(); return true;
            default: return false;
        }
    }

    /// <summary>The folder the library lives in: the one the person chose, the environment's when set, or a default under Documents.</summary>
    private static string LibraryFolder()
    {
        if (Environment.GetEnvironmentVariable("G3D_CLOUD_LIBRARY") is { Length: > 0 } custom) return custom;
        var saved = DesktopOptions.Load().CloudLibraryFolder;
        return string.IsNullOrWhiteSpace(saved) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Guanacaste 3D", "Biblioteca de familias") : saved;
    }

    /// <summary>Brings a family of the library into the project, the way importing a family file does; the text says what happened.</summary>
    internal string LoadLibraryFamily(string path)
    {
        try
        {
            if (TryImportSheetFamily(path) || TryImportMeshFamily(path)) return StatusText.Text;
            var family = FamilyFile.Load(path);
            _session.ImportOpeningFamily(family);
            RefreshAll();
            StatusText.Text = FormatText("Status.FamilyImported", family.Name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
        }
        return StatusText.Text;
    }

    private void ShowFamilyLibraryWindow()
    {
        _libraryWindow?.Close();
        var window = NewManageWindow("Library.Title", 600);
        window.Closed += (_, _) => _libraryWindow = null;
        _libraryWindow = window;
        var folder = new TextBox { Name = "LibraryFolder", Text = LibraryFolder(), Margin = new Thickness(0, 2, 0, 6), Padding = new Thickness(4, 2, 4, 2) };
        var search = new TextBox { Name = "LibrarySearch", Margin = new Thickness(0, 2, 0, 6), Padding = new Thickness(4, 2, 4, 2) };
        var list = new ListBox { Name = "LibraryList", Height = 200, Margin = new Thickness(0, 0, 0, 6) };
        var message = new TextBlock { Name = "LibraryMessage", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        var shown = new List<LibraryFamily>();
        void Refill()
        {
            list.Items.Clear();
            shown.Clear();
            try
            {
                var terms = search.Text.Split((char)32, StringSplitOptions.RemoveEmptyEntries);
                foreach (var item in FamilyLibrary.Scan(folder.Text.Trim()).Where(item => terms.All(term => item.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase))))
                {
                    shown.Add(item);
                    list.Items.Add($"{item.Name} · {GetText("Library.Kind." + item.Kind)} · {item.Bytes / 1024.0:N0} KB");
                }
                message.Text = shown.Count == 0 ? GetText("Library.None") : FormatText("Library.Count", shown.Count);
            }
            catch (InvalidDataException exception) { message.Text = LocalizedErrors.Translate(exception.Message); }
        }
        folder.TextChanged += (_, _) => Refill();
        search.TextChanged += (_, _) => Refill();
        var buttons = new WrapPanel();
        buttons.Children.Add(ManageButton("LibraryBrowse", GetText("Library.Browse"), (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = folder.Text };
            if (dialog.ShowDialog(this) == true) folder.Text = dialog.FolderName;
        }));
        buttons.Children.Add(ManageButton("LibraryLoad", GetText("Library.Load"), (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= shown.Count) { message.Text = GetText("Library.Pick"); return; }
            message.Text = LoadLibraryFamily(shown[list.SelectedIndex].Path);
        }));
        buttons.Children.Add(ManageButton("LibraryPublish", GetText("Library.Publish"), (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = GetText("Dialog.FamilyFilter"), CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) message.Text = PublishLibraryFamily(dialog.FileName, folder.Text.Trim());
            Refill();
        }));
        buttons.Children.Add(ManageButton("LibraryClose", GetText("Phases.Close"), (_, _) =>
        {
            var options = DesktopOptions.Load();
            options.CloudLibraryFolder = folder.Text.Trim();
            options.Save();
            window.Close();
        }));
        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText("Library.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(new TextBlock { Text = GetText("Library.Folder") });
        layout.Children.Add(folder);
        layout.Children.Add(new TextBlock { Text = GetText("Library.Search") });
        layout.Children.Add(search);
        layout.Children.Add(list);
        layout.Children.Add(buttons);
        layout.Children.Add(message);
        window.Content = layout;
        Refill();
        window.Show();
    }

    /// <summary>Adds a family file to the library folder; the text says where it went or why not.</summary>
    internal string PublishLibraryFamily(string source, string folder)
    {
        try
        {
            var target = FamilyLibrary.Publish(source, folder);
            StatusText.Text = FormatText("Library.Published", Path.GetFileName(target));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            StatusText.Text = exception is InvalidDataException ? LocalizedErrors.Translate(exception.Message) : exception.Message;
        }
        return StatusText.Text;
    }
}
