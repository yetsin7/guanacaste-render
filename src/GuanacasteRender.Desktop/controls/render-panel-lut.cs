using System.IO;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core.Rendering;
using Microsoft.Win32;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>Offline photographic LUT workflow. It never changes underlying path tracing samples.</summary>
public partial class RenderPanel
{
    private ColorLookupTable? _customLut;
    private string? _lutFilePath;
    private int _lutLoadVersion;

    private ColorLookupTable? SelectedLut() =>
        Mode(LutPicker) == "custom" ? _customLut : ColorLutPresets.Find(Mode(LutPicker));

    private void Lut_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        // A previous asynchronous .cube import must not reactivate after switching to an integrated filter.
        if (Mode(LutPicker) != "custom")
        {
            _lutLoadVersion++;
            _customLut = null;
            _lutFilePath = null;
        }
        LutNameText.Text = Mode(LutPicker) switch
        {
            "none" => "LUT desactivado",
            "custom" => _lutFilePath is null ? "Importa un archivo .cube para usar este filtro"
                : Path.GetFileName(_lutFilePath),
            _ => "Filtro integrado; puedes ajustar su intensidad"
        };
        Finish_Changed(sender, e);
    }

    private void LutStrength_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_ready) Finish_Changed(sender, e);
    }

    private async void LutLoad_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Importar filtro de color LUT (.cube)",
            Filter = "LUT 3D (.cube)|*.cube"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        await LoadLutAsync(dialog.FileName, ++_lutLoadVersion, true);
    }

    private async Task LoadLutAsync(string file, int version, bool showError)
    {
        try
        {
            if (IsNetworkHdriPath(file) || new FileInfo(file).Length > ColorLookupTable.MaxCubeBytes)
                throw new InvalidDataException("Invalid color LUT.");
            var lookup = await Task.Run(() => ColorLookupTable.FromCube(File.ReadAllText(file)));
            if (version != _lutLoadVersion || Dispatcher.HasShutdownStarted) return;
            _customLut = lookup;
            _lutFilePath = file;
            Select(LutPicker, "custom");
            LutNameText.Text = Path.GetFileName(file) + $" · LUT 3D {lookup.Size}³";
            Finish_Changed(this, new RoutedEventArgs());
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (version != _lutLoadVersion || Dispatcher.HasShutdownStarted) return;
            _customLut = null;
            _lutFilePath = file;
            LutNameText.Text = "⚠ Archivo LUT no disponible; el filtro se omite";
            Finish_Changed(this, new RoutedEventArgs());
            if (showError)
                MessageBox.Show(Window.GetWindow(this), "No se pudo importar el filtro: " + error.Message,
                    "LUT .cube", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LutClear_Click(object sender, RoutedEventArgs e)
    {
        ClearLutForDocument();
        Select(LutPicker, "none");
        LutNameText.Text = "LUT desactivado";
        Finish_Changed(sender, e);
    }

    internal void ClearLutForDocument()
    {
        _lutLoadVersion++;
        _customLut = null;
        _lutFilePath = null;
        if (LutPicker is not null) Select(LutPicker, "none");
        if (LutNameText is not null) LutNameText.Text = "LUT desactivado";
    }

    internal void RestoreLutForProject(string? storedPath, string? projectFile)
    {
        if (storedPath is null) return;
        var full = ResolveSavedHdri(storedPath, projectFile);
        if (full is null)
        {
            LutNameText.Text = "⚠ Ruta LUT no válida; filtro original activo";
            return;
        }
        _lutFilePath = full;
        _ = LoadLutAsync(full, ++_lutLoadVersion, false);
    }
}
