using System.Globalization;
using System.Windows;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>Edits a plan's vertical visibility with three ordered, level-relative heights.</summary>
public partial class PlanViewRangeWindow : Window
{
    private readonly double _fullLevelHeight;

    /// <summary>Gets the validated range after the dialog is accepted.</summary>
    public PlanViewRange Range { get; private set; }

    /// <summary>Initializes fields from the persisted range and the distance to the next level.</summary>
    public PlanViewRangeWindow(string levelName, PlanViewRange range, double fullLevelHeight)
    {
        InitializeComponent();
        LevelText.Text = levelName;
        _fullLevelHeight = Math.Max(0.1, fullLevelHeight);
        Range = range;
        SetFields(range);
    }

    /// <summary>Loads the common architectural range that cuts typical doors and windows.</summary>
    private void Architectural_Click(object sender, RoutedEventArgs e) => SetFields(PlanViewRange.Architectural);

    /// <summary>Loads a high slice suited to reflected ceiling coordination.</summary>
    private void Ceiling_Click(object sender, RoutedEventArgs e) => SetFields(new(2, 2.4, Math.Max(3, _fullLevelHeight)));

    /// <summary>Loads the complete height between this level and the one above.</summary>
    private void FullLevel_Click(object sender, RoutedEventArgs e) => SetFields(new(0, 1.2, _fullLevelHeight));

    /// <summary>Formats a range into the three culture-aware numeric inputs.</summary>
    private void SetFields(PlanViewRange range)
    {
        BottomBox.Text = Format(range.BottomOffset);
        CutBox.Text = Format(range.CutOffset);
        TopBox.Text = Format(range.TopOffset);
    }

    /// <summary>Formats a metric offset without unnecessary trailing zeroes.</summary>
    private static string Format(double value) => value.ToString("0.###", CultureInfo.CurrentCulture);

    /// <summary>Parses and validates the ordered offsets before closing the dialog.</summary>
    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (!TryNumber(BottomBox.Text, out var bottom) || !TryNumber(CutBox.Text, out var cut) ||
            !TryNumber(TopBox.Text, out var top) || new PlanViewRange(bottom, cut, top) is not { IsValid: true } range)
        {
            ValidationText.Text = ProjectParametersWindow.Text("ViewRange.Invalid");
            return;
        }
        Range = range;
        DialogResult = true;
    }

    /// <summary>Parses a localized decimal number, accepting a trailing metre symbol.</summary>
    private static bool TryNumber(string text, out double value) => double.TryParse(
        text.Replace("m", "", StringComparison.OrdinalIgnoreCase).Trim(),
        NumberStyles.Float, CultureInfo.CurrentCulture, out value);
}
