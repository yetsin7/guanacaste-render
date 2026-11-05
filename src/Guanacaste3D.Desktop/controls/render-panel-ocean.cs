using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>
/// The ocean of the render: a switch, the water's height and the size of its waves, and its colour. It lives in the
/// project (not in the photo), is changed in one undoable step a moment after the last edit, and the scene is rebuilt
/// because the water is geometry: it takes the place of the endless ground.
/// </summary>
public partial class RenderPanel
{
    private static readonly string[] OceanTints = ["Caribe (turquesa)", "Pacífico (azul profundo)", "Lago (verde)"];
    private DispatcherTimer? _oceanSaveTimer;
    private bool _restoringOcean;

    /// <summary>Gives the colour list its entries; they are plain text so the effect stack can show the same list.</summary>
    private void FillOceanPicker()
    {
        foreach (var tint in OceanTints) OceanTintPicker.Items.Add(tint);
        OceanTintPicker.SelectedIndex = 0;
    }

    private OceanSettings CurrentOcean() => new(OceanCheck.IsChecked == true, OceanLevelSlider.Value, OceanWavesSlider.Value,
        (OceanTint)Math.Clamp(OceanTintPicker.SelectedIndex, 0, OceanTints.Length - 1));

    private void Ocean_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _restoringOcean || _source is null) return;
        _oceanSaveTimer ??= new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(0.8) };
        _oceanSaveTimer.Tick -= OceanSaveTick;
        _oceanSaveTimer.Tick += OceanSaveTick;
        _oceanSaveTimer.Stop();
        _oceanSaveTimer.Start();
    }

    private void Ocean_SliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => Ocean_Changed(sender, e);

    private void Ocean_SelectionChanged(object sender, SelectionChangedEventArgs e) => Ocean_Changed(sender, e);

    private void OceanSaveTick(object? sender, EventArgs e) => SaveOceanNow();

    /// <summary>Writes the ocean to the project at once, which rebuilds the render scene with or without the sea.</summary>
    internal void SaveOceanNow()
    {
        _oceanSaveTimer?.Stop();
        if (_source is null || _restoringOcean) return;
        var settings = CurrentOcean();
        if (settings == _source.GetOcean()) return;
        _source.SetOcean(settings);
        StatusText.Text = settings.Enabled ? "El océano sustituye al suelo infinito en el render." : "El render vuelve a usar el suelo.";
    }

    /// <summary>Shows the ocean of the project that was just opened, without writing anything back.</summary>
    internal void RestoreOcean()
    {
        if (_source is null) return;
        _oceanSaveTimer?.Stop();
        var ocean = _source.GetOcean();
        _restoringOcean = true;
        try
        {
            OceanCheck.IsChecked = ocean.Enabled;
            OceanLevelSlider.Value = Math.Clamp(ocean.Level, OceanLevelSlider.Minimum, OceanLevelSlider.Maximum);
            OceanWavesSlider.Value = Math.Clamp(ocean.WaveSize, OceanWavesSlider.Minimum, OceanWavesSlider.Maximum);
            OceanTintPicker.SelectedIndex = (int)ocean.Tint;
            if (ocean.Enabled) _fxActive.Add("ocean"); else _fxActive.Remove("ocean");
            RefreshFxStack();
        }
        finally { _restoringOcean = false; }
    }
}
