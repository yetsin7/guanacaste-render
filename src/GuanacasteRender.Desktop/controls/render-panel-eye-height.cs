using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Guanacaste3D.Desktop.Controls;

public partial class RenderPanel
{
    private bool _updatingEyeHeight;
    private bool _eyeHeightGesture;
    private double? _eyeHeightBase;

    // Keep the same reference surface while the user edits, even as the camera moves through the scene.
    private double EyeHeightBase()
    {
        if (_source is null) return 0;
        var position = _source.Camera.Position;
        var origin = position + Vector3.UnitZ * .02f;
        if (_source.Query?.Pick(origin, -Vector3.UnitZ) is { } distance && float.IsFinite(distance))
            return origin.Z - distance;
        return 0; // No query/surface: use the documented project origin.
    }

    private void SyncEyeHeight()
    {
        if (_source is null || EyeHeightInput is null || EyeHeightInput.IsKeyboardFocusWithin) return;
        var height = _source.Camera.Position.Z - EyeHeightBase();
        _updatingEyeHeight = true;
        try { EyeHeightInput.Text = height.ToString("0.##", CultureInfo.CurrentCulture); }
        finally { _updatingEyeHeight = false; }
        EyeHeightInput.ClearValue(Border.BorderBrushProperty);
    }

    private void EyeHeightInput_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _eyeHeightBase = EyeHeightBase();
        if (_navigator is not null && !StudioBusy)
        {
            _navigator.BeginExternalGesture();
            _eyeHeightGesture = true;
        }
    }

    private void EyeHeightInput_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_eyeHeightGesture) _navigator?.EndExternalGesture();
        _eyeHeightGesture = false;
        _eyeHeightBase = null;
        SyncEyeHeight();
    }

    private void EyeHeightInput_Changed(object sender, TextChangedEventArgs e)
    {
        if (_updatingEyeHeight || !_ready || _source is null || _navigator is null || StudioBusy || !EyeHeightInput.IsKeyboardFocusWithin) return;
        var text = EyeHeightInput.Text.Trim().Replace(',', '.');
        if (!double.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var height) || !double.IsFinite(height) || height is < 0 or > 1000)
        {
            EyeHeightInput.BorderBrush = Brushes.IndianRed;
            return;
        }
        EyeHeightInput.ClearValue(Border.BorderBrushProperty);
        ApplyEyeHeight(height, _eyeHeightBase ??= EyeHeightBase());
    }

    private void EyeHeightPreset_Click(object sender, RoutedEventArgs e)
    {
        if (_source is null || _navigator is null || StudioBusy) return;
        ApplyEyeHeight(1.70, EyeHeightBase());
        SyncEyeHeight();
    }

    private void ApplyEyeHeight(double height, double ground)
    {
        if (_source is null || _navigator is null) return;
        if (_navigator.IsLocked) { StatusText.Text = "Desbloquea la cámara para cambiar la altura de ojo."; return; }
        _navigator.SetEyeElevation((float)(ground + height));
        StatusText.Text = $"Altura de ojo: {height:0.##} m sobre " + (ground == 0 ? "el origen o la superficie de referencia." : "la superficie bajo la cámara.");
    }
}
