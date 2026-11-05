using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core;
using Guanacaste3D.Desktop.Controls;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private Window? _helpWindow;
    private Window? _transmitWindow;
    private Window? _converterWindow;

    private sealed record HelpEntry(string Workspace, string Group, string Label, string CommandId, string Description);

    /// <summary>Routes the Add-Ins buttons: the command help, the model transmittal and the family converter.</summary>
    private bool TryExecuteAddInsCommand(string commandId)
    {
        switch (commandId)
        {
            case "Help": ShowHelpWindow(); return true;
            case "TransmitModel": ShowTransmitWindow(); return true;
            case "FamilyConverter": ShowConverterWindow(); return true;
            default: return false;
        }
    }

    // ------------------------------------------------------------------ help

    /// <summary>Every command of the ribbon that matches the text (in its name, its tab, its group or its description), once per tab.</summary>
    internal IReadOnlyList<(string Workspace, string Group, string Label, string CommandId, string Description)> HelpEntries(string? filter)
    {
        var entries = new List<HelpEntry>();
        foreach (var workspace in Enum.GetValues<RibbonWorkspace>())
        {
            var tab = GetWorkspaceLabel(workspace);
            foreach (var group in RibbonCatalog.GetGroups(workspace))
                foreach (var command in group.Commands)
                {
                    if (command.CommandId == "Modify") continue;
                    var description = TryFindResource("Demo." + command.CommandId) as string ?? string.Empty;
                    entries.Add(new HelpEntry(tab, GetText(group.LabelResourceKey), GetText(command.LabelResourceKey), command.CommandId, description));
                }
        }
        var terms = (filter ?? string.Empty).Split((char)32, StringSplitOptions.RemoveEmptyEntries);
        return entries
            .Where(entry => terms.All(term => entry.Label.Contains(term, StringComparison.CurrentCultureIgnoreCase) || entry.Workspace.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                                                entry.Group.Contains(term, StringComparison.CurrentCultureIgnoreCase) || entry.Description.Contains(term, StringComparison.CurrentCultureIgnoreCase)))
            .DistinctBy(entry => (entry.Workspace, entry.CommandId))
            .Select(entry => (entry.Workspace, entry.Group, entry.Label, entry.CommandId, entry.Description))
            .ToList();
    }

    private void ShowHelpWindow()
    {
        if (_helpWindow is { IsVisible: true } open)
        {
            open.Activate();
            return;
        }
        var window = NewManageWindow("Help.Title", 620);
        window.Closed += (_, _) => _helpWindow = null;
        _helpWindow = window;
        var search = new TextBox { Name = "HelpSearch", Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(4, 2, 4, 2) };
        var list = new ListBox { Name = "HelpList", Height = 260, Margin = new Thickness(0, 0, 0, 6) };
        var detail = new TextBlock { Name = "HelpDetail", TextWrapping = TextWrapping.Wrap, MinHeight = 70, Margin = new Thickness(0, 0, 0, 6) };
        var count = new TextBlock { Name = "HelpCount", Margin = new Thickness(0, 0, 0, 4), Opacity = 0.7 };
        var shown = new List<(string Workspace, string Group, string Label, string CommandId, string Description)>();
        void Refill()
        {
            shown = HelpEntries(search.Text).ToList();
            list.Items.Clear();
            foreach (var entry in shown) list.Items.Add($"{entry.Workspace} › {entry.Group} › {entry.Label}");
            count.Text = FormatText("Help.Count", shown.Count);
            detail.Text = shown.Count == 0 ? GetText("Help.NoMatch") : GetText("Help.Pick");
        }
        search.TextChanged += (_, _) => Refill();
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= shown.Count) return;
            var entry = shown[list.SelectedIndex];
            detail.Text = $"{entry.Label}\n{entry.Workspace} › {entry.Group}\n\n{(entry.Description.Length > 0 ? entry.Description : GetText("Help.NoDescription"))}";
        };
        var buttons = new WrapPanel();
        buttons.Children.Add(ManageButton("HelpRun", GetText("Help.Run"), (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= shown.Count) return;
            var id = shown[list.SelectedIndex].CommandId;
            window.Close();
            ExecuteApplicationCommand(id);
        }));
        buttons.Children.Add(ManageButton("HelpClose", GetText("Phases.Close"), (_, _) => window.Close()));
        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText("Help.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(search);
        layout.Children.Add(count);
        layout.Children.Add(list);
        layout.Children.Add(detail);
        layout.Children.Add(buttons);
        window.Content = layout;
        Refill();
        window.Show();
    }

    // ------------------------------------------------------------------ transmit model

    /// <summary>Packs the project into a zip with a manifest; the text says what went in or why it did not.</summary>
    internal string CreateTransmittal(string zipPath, bool includeExports)
    {
        try
        {
            var report = ModelTransmittal.Create(_session.Document, zipPath, DateTimeOffset.Now, includeExports);
            var text = FormatText("Transmit.Created", report.Entries.Count, Path.GetFileName(zipPath));
            if (report.Notes.Contains("CentralMissing")) text += "\n" + GetText("Transmit.CentralMissing");
            StatusText.Text = text.Split('\n')[0];
            return text;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            var text = exception is InvalidDataException ? LocalizedErrors.Translate(exception.Message) : exception.Message;
            StatusText.Text = text;
            return text;
        }
    }

    /// <summary>Checks a received package against its manifest and says what is wrong with it, if anything.</summary>
    internal string VerifyTransmittal(string zipPath)
    {
        var problems = ModelTransmittal.Verify(zipPath);
        if (problems.Count == 0) return GetText("Transmit.Verified");
        return string.Join("\n", problems.Select(problem =>
        {
            var colon = problem.IndexOf(':');
            var code = colon < 0 ? problem : problem[..colon];
            return FormatText("Transmit.Problem." + code, colon < 0 ? string.Empty : problem[(colon + 1)..]);
        }));
    }

    private void ShowTransmitWindow()
    {
        _transmitWindow?.Close();
        var window = NewManageWindow("Transmit.Title", 520);
        window.Closed += (_, _) => _transmitWindow = null;
        _transmitWindow = window;
        var exports = new CheckBox { Name = "TransmitExports", Content = GetText("Transmit.Exports"), IsChecked = true, Margin = new Thickness(0, 0, 0, 8) };
        var message = new TextBox { Name = "TransmitMessage", IsReadOnly = true, Height = 120, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 6, 0, 6) };
        var buttons = new WrapPanel();
        buttons.Children.Add(ManageButton("TransmitCreate", GetText("Transmit.Create"), (_, _) =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "ZIP|*.zip", FileName = $"{_session.Document.Name}-transmision.zip" };
            if (dialog.ShowDialog(this) == true) message.Text = CreateTransmittal(dialog.FileName, exports.IsChecked == true);
        }));
        buttons.Children.Add(ManageButton("TransmitVerify", GetText("Transmit.Verify"), (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "ZIP|*.zip" };
            if (dialog.ShowDialog(this) == true) message.Text = VerifyTransmittal(dialog.FileName);
        }));
        buttons.Children.Add(ManageButton("TransmitClose", GetText("Phases.Close"), (_, _) => window.Close()));
        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText("Transmit.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(exports);
        layout.Children.Add(buttons);
        layout.Children.Add(message);
        window.Content = layout;
        window.Show();
    }

    // ------------------------------------------------------------------ family converter

    /// <summary>Converts the models into families in a folder; the text lists what was made and what could not be.</summary>
    internal string ConvertFamilies(IReadOnlyList<string> models, string folder, FamilyCategory category, int triangles, double metersPerUnit)
    {
        try
        {
            var (converted, failed) = FamilyConverter.ConvertMany(models, folder, category, triangles, metersPerUnit);
            var lines = converted.Select(item => FormatText("Converter.Line", item.Name, item.SourceTriangles, item.Triangles, item.Width, item.Depth, item.Height)).ToList();
            lines.AddRange(failed.Select(item => FormatText("Converter.Failed", Path.GetFileName(item.Source), LocalizedErrors.Translate(item.Reason))));
            StatusText.Text = FormatText("Converter.Done", converted.Count, failed.Count);
            return string.Join("\n", lines);
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
            return StatusText.Text;
        }
    }

    private void ShowConverterWindow()
    {
        _converterWindow?.Close();
        var window = NewManageWindow("Converter.Title", 560);
        window.Closed += (_, _) => _converterWindow = null;
        _converterWindow = window;
        var category = new ComboBox { Name = "ConverterCategory", Margin = new Thickness(0, 2, 0, 6) };
        foreach (var item in Enum.GetValues<FamilyCategory>().Where(item => item != FamilyCategory.TitleBlocks)) category.Items.Add(new ComboBoxItem { Content = item.ToString(), Tag = item });
        category.SelectedIndex = Math.Max(0, category.Items.Cast<ComboBoxItem>().ToList().FindIndex(item => (FamilyCategory)item.Tag == FamilyCategory.Entourage));
        var budget = new TextBox { Name = "ConverterTriangles", Text = "60000", Margin = new Thickness(0, 2, 0, 6), Padding = new Thickness(4, 2, 4, 2) };
        var scale = new TextBox { Name = "ConverterScale", Text = "1", Margin = new Thickness(0, 2, 0, 6), Padding = new Thickness(4, 2, 4, 2) };
        var result = new TextBox { Name = "ConverterResult", IsReadOnly = true, Height = 130, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 6, 0, 6) };
        var buttons = new WrapPanel();
        buttons.Children.Add(ManageButton("ConverterChoose", GetText("Converter.Choose"), (_, _) =>
        {
            var open = new Microsoft.Win32.OpenFileDialog { Filter = GetText("Converter.Filter") + "|*.obj;*.gltf;*.glb;*.stl", Multiselect = true };
            if (open.ShowDialog(this) != true) return;
            var folder = new Microsoft.Win32.OpenFolderDialog();
            if (folder.ShowDialog(this) != true) return;
            if (!int.TryParse(budget.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var triangles) ||
                !double.TryParse(scale.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var factor)) { result.Text = GetText("Converter.NumbersInvalid"); return; }
            result.Text = ConvertFamilies(open.FileNames, folder.FolderName, (FamilyCategory)((ComboBoxItem)category.SelectedItem).Tag, triangles, factor);
        }));
        buttons.Children.Add(ManageButton("ConverterClose", GetText("Phases.Close"), (_, _) => window.Close()));
        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText("Converter.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(new TextBlock { Text = GetText("Converter.Category") });
        layout.Children.Add(category);
        layout.Children.Add(new TextBlock { Text = GetText("Converter.Triangles") });
        layout.Children.Add(budget);
        layout.Children.Add(new TextBlock { Text = GetText("Converter.Scale") });
        layout.Children.Add(scale);
        layout.Children.Add(buttons);
        layout.Children.Add(result);
        window.Content = layout;
        window.Show();
    }
}
