using System.Windows;
using System.Windows.Media;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private void ConfigureApplicationFileMenu()
    {
        FileMenu.RecentItemsProvider = () => _recents.Load();
        FileMenu.RecentPathSelected += path =>
        {
            if (!ConfirmDiscard()) return;
            OpenProjectPath(path);
        };
    }

    private void Options_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new DesktopOptionsWindow(this, ApplyDesktopOptions);
        dialog.ShowDialog();
    }

    private void ApplyDesktopOptions(DesktopOptions options)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(options.CanvasColor);
            StandardWorkspace.Background = new SolidColorBrush(color);
        }
        catch (FormatException) { }
        ViewNavigation.SetCubeVisible(options.ShowViewCube);
        ViewNavigation.HorizontalAlignment = options.ViewCubePosition.Contains("izquierda", StringComparison.OrdinalIgnoreCase)
            ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        ViewNavigation.VerticalAlignment = options.ViewCubePosition.Contains("Inferior", StringComparison.OrdinalIgnoreCase)
            ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        if (StartScreenControl.Visibility == Visibility.Visible) StartScreenControl.Refresh();
    }

    /// <summary>Routes reusable file-menu entries to existing document and workspace commands.</summary>
    private void FileMenu_CommandInvoked(string command)
    {
        RoutedEventHandler? handler = command switch
        {
            "Home" => Home_Click,
            "Options" => Options_Click,
            "New" => New_Click,
            "Open" => Open_Click,
            "OpenSample" => OpenSample_Click,
            "NewArchitecturalMetric" => NewArchitecturalMetric_Click,
            "OpenModernHouse" => OpenModernHouse_Click,
            "OpenBreezeBlockHouse" => OpenBreezeBlockHouse_Click,
            "NewFromTemplate" => NewFromTemplate_Click,
            "Save" => Save_Click,
            "SaveAs" => SaveAs_Click,
            "SaveAsTemplate" => SaveAsTemplate_Click,
            "ImportFamily" => ImportFamily_Click,
            "SaveFamily" => SaveFamily_Click,
            "ImportModel" => ImportModel_Click,
            "LinkedModels" => LinkedModels_Click,
            "ExportQuantities" => ExportQuantities_Click,
            "ExportDxf" => ExportDxf_Click,
            "ExportPdf" => ExportPdf_Click,
            "ExportIfc" => ExportIfc_Click,
            "ExportModel3D" => ExportModel3D_Click,
            "ExportRenderProject" => ExportRenderProject_Click,
            "Undo" => Undo_Click,
            "Redo" => Redo_Click,
            "Delete" => Delete_Click,
            "Duplicate" => Duplicate_Click,
            "Move" => Move_Click,
            "Rotate" => Rotate_Click,
            "ShowPlan" => ShowPlan_Click,
            "ShowElevation" => ShowElevation_Click,
            "ShowSection" => ShowSection_Click,
            "ShowSchedule" => ShowSchedule_Click,
            "ShowSheet" => ShowSheet_Click,
            "ShowRender" => ShowRender_Click,
            "FitPlan" => FitPlan_Click,
            "About" => About_Click,
            "Exit" => Exit_Click,
            _ => null
        };
        handler?.Invoke(this, new RoutedEventArgs());
    }
}
