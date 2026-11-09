using System.Windows;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>Collects the name and cardinal look direction for a new elevation view.</summary>
public partial class ElevationViewWindow : Window
{
    /// <summary>Gets the trimmed view name after the dialog is accepted.</summary>
    public string ViewName => NameBox.Text.Trim();

    /// <summary>Gets the selected cardinal look direction.</summary>
    public ViewOrientation Orientation => OrientationPicker.SelectedValue is ViewOrientation value
        ? value : ViewOrientation.North;

    /// <summary>Creates a localized orientation picker and suggests the next available view name.</summary>
    public ElevationViewWindow(int suggestedNumber)
    {
        InitializeComponent();
        NameBox.Text = $"{ProjectParametersWindow.Text("View.Elevation")} {suggestedNumber}";
        OrientationPicker.DisplayMemberPath = nameof(LabeledValue<ViewOrientation>.Label);
        OrientationPicker.SelectedValuePath = nameof(LabeledValue<ViewOrientation>.Value);
        OrientationPicker.ItemsSource = new[]
        {
            Choice("Elevation.North", ViewOrientation.North), Choice("Elevation.South", ViewOrientation.South),
            Choice("Elevation.East", ViewOrientation.East), Choice("Elevation.West", ViewOrientation.West)
        };
        OrientationPicker.SelectedIndex = 0;
        Loaded += (_, _) => { NameBox.SelectAll(); NameBox.Focus(); };
    }

    /// <summary>Builds one localized orientation choice.</summary>
    private static LabeledValue<ViewOrientation> Choice(string key, ViewOrientation value) =>
        new(ProjectParametersWindow.Text(key), value);

    /// <summary>Accepts only a non-empty name and a selected direction.</summary>
    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (ViewName.Length is 0 or > 120 || OrientationPicker.SelectedValue is not ViewOrientation)
        {
            ValidationText.Text = ProjectParametersWindow.Text("Error.The view name is not valid.");
            return;
        }
        DialogResult = true;
    }
}
