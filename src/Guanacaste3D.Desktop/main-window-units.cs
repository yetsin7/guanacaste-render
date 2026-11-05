using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private Window? _unitsWindow;
    private Window? _sharedWindow;
    private string? _sharedParameterFile;

    /// <summary>The units measurements are shown and typed in.</summary>
    private ProjectUnits Units => _session.Document.Units;

    /// <summary>Routes the Project Units and Shared Parameters buttons of the Manage tab.</summary>
    private bool TryExecuteUnitsCommand(string commandId)
    {
        switch (commandId)
        {
            case "ProjectUnits": ShowUnitsWindow(); return true;
            case "SharedParameters": ShowSharedParametersWindow(); return true;
            default: return false;
        }
    }

    private Window NewManageWindow(string titleKey, double width)
    {
        return new Window
        {
            Title = GetText(titleKey), Owner = this, Width = width, SizeToContent = SizeToContent.Height, MaxHeight = 720,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false
        };
    }

    private static Button ManageButton(string name, string text, RoutedEventHandler click)
    {
        var button = new Button { Name = name, Content = text, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(8, 2, 8, 2) };
        button.Click += click;
        return button;
    }

    // ------------------------------------------------------------------ project units

    private void ShowUnitsWindow()
    {
        if (_unitsWindow is { IsVisible: true } open)
        {
            open.Activate();
            return;
        }
        var window = NewManageWindow("Units.Title", 420);
        window.Closed += (_, _) => _unitsWindow = null;
        _unitsWindow = window;

        var length = new ComboBox { Name = "UnitLengthBox", Margin = new Thickness(0, 0, 0, 6) };
        foreach (var unit in Enum.GetValues<LengthUnit>()) length.Items.Add(GetText("LengthUnit." + unit));
        length.SelectedIndex = (int)Units.Length;
        var decimals = new ComboBox { Name = "UnitDecimalsBox", Margin = new Thickness(0, 0, 0, 6) };
        for (var count = 0; count <= ProjectUnits.MaximumDecimals; count++) decimals.Items.Add(count.ToString());
        decimals.SelectedIndex = Units.Decimals;
        var sample = new TextBlock { Name = "UnitSample", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };

        ProjectUnits Chosen() => new((LengthUnit)Math.Max(0, length.SelectedIndex), Math.Max(0, decimals.SelectedIndex));
        void ShowSample()
        {
            var units = Chosen();
            sample.Text = FormatText("Units.Sample", units.FormatLength(1.2345), units.FormatArea(12.3456), units.FormatVolume(123.4567));
        }
        length.SelectionChanged += (_, _) => ShowSample();
        decimals.SelectionChanged += (_, _) => ShowSample();

        var buttons = new WrapPanel();
        buttons.Children.Add(ManageButton("UnitApply", GetText("Units.Apply"), (_, _) =>
        {
            try
            {
                _session.SetProjectUnits(Chosen());
                StatusText.Text = FormatText("Status.UnitsChanged", Units.LengthSymbol, Units.Decimals);
                RefreshAll();
            }
            catch (InvalidDataException exception)
            {
                StatusText.Text = LocalizedErrors.Translate(exception.Message);
            }
        }));
        buttons.Children.Add(ManageButton("UnitClose", GetText("Phases.Close"), (_, _) => window.Close()));

        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText("Units.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(new TextBlock { Text = GetText("Units.Length") });
        layout.Children.Add(length);
        layout.Children.Add(new TextBlock { Text = GetText("Units.Decimals") });
        layout.Children.Add(decimals);
        layout.Children.Add(sample);
        layout.Children.Add(buttons);
        window.Content = layout;
        ShowSample();
        window.Show();
    }

    // ------------------------------------------------------------------ shared parameters

    private void ShowSharedParametersWindow()
    {
        if (_sharedWindow is { IsVisible: true } open)
        {
            open.Activate();
            return;
        }
        var window = NewManageWindow("SharedParameters.Title", 480);
        window.Closed += (_, _) => _sharedWindow = null;
        _sharedWindow = window;

        var file = new TextBlock { Name = "SharedFileText", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        var list = new ListBox { Name = "SharedList", Height = 150, Margin = new Thickness(0, 0, 0, 6) };
        var category = new ComboBox { Name = "SharedCategoryBox", Margin = new Thickness(0, 0, 0, 6) };
        foreach (var item in Enum.GetValues<ElementCategory>()) category.Items.Add(GetText("ElementCategory." + item));
        category.SelectedIndex = 0;
        var binding = new ComboBox { Name = "SharedBindingBox", Margin = new Thickness(0, 0, 0, 6) };
        foreach (var item in Enum.GetValues<ParameterBinding>()) binding.Items.Add(GetText("ParameterBinding." + item));
        binding.SelectedIndex = 0;

        void ShowFile()
        {
            file.Text = _sharedParameterFile is null ? GetText("SharedParameters.NoFile") : _sharedParameterFile;
            list.Items.Clear();
            if (_sharedParameterFile is null || !File.Exists(_sharedParameterFile)) return;
            try
            {
                foreach (var parameter in SharedParameterFile.Read(File.ReadAllText(_sharedParameterFile)).Parameters)
                    list.Items.Add(new CheckBox
                    {
                        Content = $"{parameter.Name} · {GetText("ParameterSpec." + parameter.Spec)}" + (parameter.Group.Length > 0 ? $" · {parameter.Group}" : string.Empty),
                        IsChecked = true, Tag = parameter
                    });
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                StatusText.Text = exception is InvalidDataException ? LocalizedErrors.Translate(exception.Message) : exception.Message;
            }
        }
        _sharedRefresh = ShowFile;

        var files = new WrapPanel();
        files.Children.Add(ManageButton("SharedExport", GetText("SharedParameters.Export"), (_, _) =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = GetText("SharedParameters.Filter"), FileName = "SharedParameters.txt" };
            if (dialog.ShowDialog(this) == true) ExportSharedParameters(dialog.FileName);
        }));
        files.Children.Add(ManageButton("SharedOpen", GetText("SharedParameters.Open"), (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = GetText("SharedParameters.Filter") };
            if (dialog.ShowDialog(this) == true) LoadSharedParameterFile(dialog.FileName);
        }));

        var actions = new WrapPanel();
        actions.Children.Add(ManageButton("SharedAdd", GetText("SharedParameters.Add"), (_, _) =>
        {
            var chosen = list.Items.OfType<CheckBox>().Where(box => box.IsChecked == true).Select(box => (SharedParameter)box.Tag).ToList();
            if (chosen.Count == 0)
            {
                StatusText.Text = GetText("Status.SharedNothingChosen");
                return;
            }
            try
            {
                var added = _session.ImportSharedParameters(chosen, [Enum.GetValues<ElementCategory>()[Math.Max(0, category.SelectedIndex)]],
                    Enum.GetValues<ParameterBinding>()[Math.Max(0, binding.SelectedIndex)]);
                StatusText.Text = FormatText("Status.SharedAdded", added, chosen.Count - added);
                RefreshAll();
            }
            catch (InvalidDataException exception)
            {
                StatusText.Text = LocalizedErrors.Translate(exception.Message);
            }
        }));
        actions.Children.Add(ManageButton("SharedManager", GetText("SharedParameters.Manager"), (_, _) =>
            new ProjectParametersWindow(_session, ElementCategory.Walls) { Owner = this }.Show()));
        actions.Children.Add(ManageButton("SharedClose", GetText("Phases.Close"), (_, _) => window.Close()));

        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText("SharedParameters.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(files);
        layout.Children.Add(file);
        layout.Children.Add(list);
        layout.Children.Add(new TextBlock { Text = GetText("SharedParameters.Category") });
        layout.Children.Add(category);
        layout.Children.Add(new TextBlock { Text = GetText("SharedParameters.Binding") });
        layout.Children.Add(binding);
        layout.Children.Add(actions);
        window.Content = layout;
        ShowFile();
        window.Show();
    }

    private Action? _sharedRefresh;

    /// <summary>Writes the project's parameters, except the calculated ones, as a shared parameter file other projects can read.</summary>
    private bool ExportSharedParameters(string path)
    {
        try
        {
            var all = _session.Document.ParameterDefinitions;
            var exported = all.Where(definition => definition.Formula.Length == 0).ToList();
            File.WriteAllText(path, SharedParameterFile.Write(exported), new UTF8Encoding(true));
            _sharedParameterFile = path;
            StatusText.Text = FormatText("Status.SharedExported", exported.Count, all.Count - exported.Count);
            _sharedRefresh?.Invoke();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = exception.Message;
            return false;
        }
    }

    /// <summary>Makes a shared parameter file the current one and lists its parameters in the window.</summary>
    private bool LoadSharedParameterFile(string path)
    {
        try
        {
            var read = SharedParameterFile.Read(File.ReadAllText(path));
            _sharedParameterFile = path;
            StatusText.Text = FormatText("Status.SharedLoaded", read.Parameters.Count, read.Skipped);
            _sharedRefresh?.Invoke();
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            StatusText.Text = exception is InvalidDataException ? LocalizedErrors.Translate(exception.Message) : exception.Message;
            return false;
        }
    }
}
