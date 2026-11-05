using System.IO;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private Window? _interferenceWindow;
    private Window? _publishWindow;
    private Window? _sharedModelsWindow;
    private IReadOnlyList<Interference> _interferences = [];
    private int _interferenceIndex;

    /// <summary>Routes the Coordinate, Share, Publish Settings and Manage Models buttons of the Collaborate tab.</summary>
    private bool TryExecuteCoordinationCommand(string commandId)
    {
        switch (commandId)
        {
            case "Coordinate": ShowInterferenceWindow(); return true;
            case "Share": ShareProject(); return true;
            case "PublishSettings": ShowPublishWindow(); return true;
            case "ManageCloudModels": ShowSharedModelsWindow(); return true;
            default: return false;
        }
    }

    private string KindName(string kind) => GetText("Interference.Kind." + kind);

    private string InterferenceLine(Interference item) =>
        FormatText("Interference.Line", KindName(item.KindA), item.IdA.ToString("N")[..6], KindName(item.KindB), item.IdB.ToString("N")[..6], item.Area, item.Height);

    // ------------------------------------------------------------------ interference check

    private void ShowInterferenceWindow()
    {
        _interferenceWindow?.Close();
        _interferences = InterferenceCheck.Run(_session.Document);
        var window = NewManageWindow("Interference.Title", 560);
        window.Closed += (_, _) => _interferenceWindow = null;
        _interferenceWindow = window;
        var list = new ListBox { Name = "InterferenceList", Height = 220, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var item in _interferences) list.Items.Add(InterferenceLine(item));
        list.SelectedIndex = _interferences.Count == 0 ? -1 : Math.Clamp(_interferenceIndex, 0, _interferences.Count - 1);
        list.SelectionChanged += (_, _) => _interferenceIndex = Math.Max(0, list.SelectedIndex);
        var summary = new TextBlock
        {
            Name = "InterferenceSummary", Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap,
            Text = _interferences.Count == 0 ? GetText("Interference.None") : FormatText("Interference.Found", _interferences.Count)
        };
        var buttons = new WrapPanel();
        buttons.Children.Add(ManageButton("InterferenceSelect", GetText("Interference.Select"), (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= _interferences.Count) return;
            var item = _interferences[list.SelectedIndex];
            SelectElement(item.IdA);
            _viewSelection = new HashSet<Guid> { item.IdA, item.IdB };
            RenderPlan();
            StatusText.Text = GetText("Status.InterferenceSelected");
        }));
        buttons.Children.Add(ManageButton("InterferenceRefresh", GetText("Interference.Refresh"), (_, _) => ShowInterferenceWindow()));
        buttons.Children.Add(ManageButton("InterferenceClose", GetText("Phases.Close"), (_, _) => window.Close()));
        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText("Interference.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(summary);
        layout.Children.Add(list);
        layout.Children.Add(buttons);
        window.Content = layout;
        window.Show();
    }

    // ------------------------------------------------------------------ share and publish

    private void ShareProject()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = GetText("Share.Title") };
        if (dialog.ShowDialog(this) == true) SharePackageTo(dialog.FolderName);
    }

    /// <summary>Writes the project, IFC, DXF, PDF and a summary into a folder, to hand to someone else.</summary>
    private bool SharePackageTo(string folder)
    {
        try
        {
            var files = Publishing.CreateSharePackage(_session.Document, folder, DateTimeOffset.Now);
            StatusText.Text = FormatText("Status.Shared", files.Count, folder);
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            StatusText.Text = exception is InvalidDataException ? LocalizedErrors.Translate(exception.Message) : exception.Message;
            return false;
        }
    }

    private void ShowPublishWindow()
    {
        _publishWindow?.Close();
        var settings = _session.Document.Publish;
        var window = NewManageWindow("Publish.Title", 500);
        window.Closed += (_, _) => _publishWindow = null;
        _publishWindow = window;
        var sheets = _session.Document.Sheets.Select(sheet => new CheckBox
        {
            Content = $"{sheet.Number} · {sheet.Name}", Tag = sheet.Id, IsChecked = settings.SheetIds.Contains(sheet.Id) || settings.SheetIds.Count == 0
        }).ToList();
        var sheetPanel = new StackPanel { Name = "PublishSheets" };
        foreach (var box in sheets) sheetPanel.Children.Add(box);
        if (sheets.Count == 0) sheetPanel.Children.Add(new TextBlock { Text = GetText("Publish.NoSheets") });
        var pdf = new CheckBox { Name = "PublishPdf", Content = GetText("Publish.Pdf"), IsChecked = settings.Pdf };
        var dxf = new CheckBox { Name = "PublishDxf", Content = GetText("Publish.Dxf"), IsChecked = settings.Dxf };
        var ifc = new CheckBox { Name = "PublishIfc", Content = GetText("Publish.Ifc"), IsChecked = settings.Ifc };
        var folder = new TextBox { Name = "PublishFolder", Text = settings.Folder, Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(4, 2, 4, 2) };

        PublishSettings Read() => new(sheets.Where(box => box.IsChecked == true).Select(box => (Guid)box.Tag).ToList(), pdf.IsChecked == true, dxf.IsChecked == true, ifc.IsChecked == true, folder.Text);
        var buttons = new WrapPanel();
        buttons.Children.Add(ManageButton("PublishBrowse", GetText("Publish.Browse"), (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = GetText("Publish.Title") };
            if (dialog.ShowDialog(this) == true) folder.Text = dialog.FolderName;
        }));
        buttons.Children.Add(ManageButton("PublishSave", GetText("Publish.Save"), (_, _) =>
        {
            try
            {
                _session.SetPublishSettings(Read());
                StatusText.Text = GetText("Status.PublishSaved");
            }
            catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
        }));
        buttons.Children.Add(ManageButton("PublishNow", GetText("Publish.Now"), (_, _) =>
        {
            try
            {
                var chosen = Read();
                _session.SetPublishSettings(chosen);
                var files = Publishing.Publish(_session.Document, chosen, DateTimeOffset.Now);
                StatusText.Text = FormatText("Status.Published", files.Count, chosen.Folder);
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                StatusText.Text = exception is InvalidDataException ? LocalizedErrors.Translate(exception.Message) : exception.Message;
            }
        }));
        buttons.Children.Add(ManageButton("PublishClose", GetText("Phases.Close"), (_, _) => window.Close()));
        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText("Publish.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(new TextBlock { Text = GetText("Publish.Sheets"), FontWeight = FontWeights.SemiBold });
        layout.Children.Add(sheetPanel);
        layout.Children.Add(new TextBlock { Text = GetText("Publish.Formats"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        layout.Children.Add(pdf);
        layout.Children.Add(dxf);
        layout.Children.Add(ifc);
        layout.Children.Add(new TextBlock { Text = GetText("Publish.Folder"), Margin = new Thickness(0, 8, 0, 0) });
        layout.Children.Add(folder);
        layout.Children.Add(buttons);
        window.Content = new ScrollViewer { Content = layout, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        window.Show();
    }

    // ------------------------------------------------------------------ shared models

    private int _sharedModelIndex;

    private void ShowSharedModelsWindow()
    {
        _sharedModelsWindow?.Close();
        var window = NewManageWindow("SharedModels.Title", 540);
        window.Closed += (_, _) => _sharedModelsWindow = null;
        _sharedModelsWindow = window;
        var recent = RecentCentralModels.List();
        var list = new ListBox { Name = "SharedModelList", Height = 170, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var path in recent) list.Items.Add(path + (File.Exists(path) ? string.Empty : "  " + GetText("SharedModels.Missing")));
        list.SelectedIndex = recent.Count == 0 ? -1 : Math.Clamp(_sharedModelIndex, 0, recent.Count - 1);
        list.SelectionChanged += (_, _) => _sharedModelIndex = Math.Max(0, list.SelectedIndex);
        var buttons = new WrapPanel();
        buttons.Children.Add(ManageButton("SharedModelOpen", GetText("SharedModels.Open"), (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= recent.Count) return;
            var path = recent[list.SelectedIndex];
            if (!File.Exists(path)) { StatusText.Text = GetText("Status.SharedModelMissing"); return; }
            window.Close();
            OpenProjectPath(path);
        }));
        buttons.Children.Add(ManageButton("SharedModelForget", GetText("SharedModels.Forget"), (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= recent.Count) return;
            RecentCentralModels.Remove(recent[list.SelectedIndex]);
            ShowSharedModelsWindow();
        }));
        buttons.Children.Add(ManageButton("SharedModelClose", GetText("Phases.Close"), (_, _) => window.Close()));
        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText("SharedModels.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(list);
        layout.Children.Add(buttons);
        window.Content = layout;
        window.Show();
    }
}
