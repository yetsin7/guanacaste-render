using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>Provides focus-free numeric length entry during wall placement.</summary>
public partial class MainWindow
{
    private string _directWallLengthText = string.Empty;
    private bool _synchronizingWallLengthInput;

    /// <summary>Captures numeric text while a wall direction is active and rejects nonnumeric text.</summary>
    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        if (CanCaptureDirectWallLength() && Keyboard.FocusedElement is not TextBox)
        {
            if (IsLengthText(e.Text)) AppendDirectWallLength(e.Text);
            e.Handled = true;
        }
        base.OnPreviewTextInput(e);
    }

    /// <summary>Handles Enter, Backspace, and F8 without requiring focus on the contextual length field.</summary>
    private bool TryHandleDirectWallLengthKey(KeyEventArgs e)
    {
        if (!CanCaptureDirectWallLength() ||
            Keyboard.FocusedElement is TextBox textBox && !ReferenceEquals(textBox, WallLengthInput))
            return false;
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(CurrentWallLengthText()))
        {
            PlaceWallByLength_Click(this, new RoutedEventArgs());
            return true;
        }
        if (e.Key == Key.Back && Keyboard.FocusedElement is not TextBox)
        {
            if (_directWallLengthText.Length > 0)
                SetDirectWallLength(_directWallLengthText[..^1]);
            return true;
        }
        if (e.Key == Key.F8)
        {
            WallOrthogonalCheck.IsChecked = WallOrthogonalCheck.IsChecked != true;
            StatusText.Text = GetText(WallOrthogonalCheck.IsChecked == true
                ? "Status.WallOrthogonalEnabled"
                : "Status.WallOrthogonalDisabled");
            return true;
        }
        if (Keyboard.FocusedElement is not TextBox && e.Key is >= Key.A and <= Key.Z)
            return true;
        return false;
    }

    /// <summary>Restricts manual editing of the contextual field to positive metric number syntax.</summary>
    private void WallLengthInput_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !IsLengthText(e.Text);

    /// <summary>Refreshes the exact-length preview when the contextual field is edited manually.</summary>
    private void WallLengthInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_synchronizingWallLengthInput || PlanCanvas is null || _session is null) return;
        _directWallLengthText = WallLengthInput.Text;
        if (_drawingWall) RenderPlan();
    }

    /// <summary>Returns whether wall placement currently has both an origin and a pointer direction.</summary>
    private bool CanCaptureDirectWallLength() =>
        _drawingWall && WallRectangleMode.IsChecked != true &&
        _pendingWallStart is not null && _wallPreviewPoint is not null;

    /// <summary>Returns the active direct-entry value used for exact placement and live preview.</summary>
    private string CurrentWallLengthText() =>
        string.IsNullOrWhiteSpace(_directWallLengthText) ? WallLengthInput.Text : _directWallLengthText;

    /// <summary>Projects the preview to the typed length while preserving the pointer-defined direction.</summary>
    private Point2 ResolveWallPreviewEndpoint(Point2 start, Point2 pointer)
    {
        if (!double.TryParse(CurrentWallLengthText(), NumberStyles.Float,
                CultureInfo.CurrentCulture, out var length) ||
            !double.IsFinite(length) || length is < 0.05 or > 100000)
            return ConstrainWallEndpoint(start, pointer);
        try
        {
            return WallPlacementGeometry.EndpointAtLength(
                start, pointer, length, OrthogonalDrawingActive());
        }
        catch (InvalidDataException)
        {
            return ConstrainWallEndpoint(start, pointer);
        }
    }

    /// <summary>Appends a culture-compatible numeric character to the focus-free wall length.</summary>
    private void AppendDirectWallLength(string text)
    {
        var decimalSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        var normalized = text is "." or "," ? decimalSeparator : text;
        if (normalized == decimalSeparator && _directWallLengthText.Contains(decimalSeparator, StringComparison.Ordinal))
            return;
        SetDirectWallLength(_directWallLengthText + normalized);
    }

    /// <summary>Synchronizes the captured numeric buffer with its visible contextual field.</summary>
    private void SetDirectWallLength(string value)
    {
        _directWallLengthText = value;
        _synchronizingWallLengthInput = true;
        WallLengthInput.Text = value;
        WallLengthInput.CaretIndex = value.Length;
        _synchronizingWallLengthInput = false;
        RenderPlan();
    }

    /// <summary>Clears exact-length state before a new independent or chained segment begins.</summary>
    private void ClearDirectWallLength()
    {
        if (WallLengthInput is null) return;
        SetDirectWallLength(string.Empty);
    }

    /// <summary>Accepts only digits and a decimal separator.</summary>
    private static bool IsLengthText(string text) =>
        text.Length == 1 && (char.IsDigit(text[0]) || text[0] is '.' or ',');
}
