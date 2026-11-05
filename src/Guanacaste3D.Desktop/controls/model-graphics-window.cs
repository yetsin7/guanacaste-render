using System.Windows;
using System.Windows.Controls;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>Transactional editing of model-view graphics without overwriting BIM material assignments.</summary>
internal sealed class ModelGraphicsWindow : Window
{
    private readonly Slider _ambient;
    private readonly Slider _fieldOfView;
    private readonly ComboBox _background;
    private readonly CheckBox _edges;
    public double Ambient => _ambient.Value;
    public float FieldOfView => (float)_fieldOfView.Value;
    public string BackgroundMode => (_background.SelectedItem as ComboBoxItem)?.Tag as string ?? "White";
    public bool ShowEdges => _edges.IsChecked == true;

    /// <summary>Creates localized controls initialized from the active view; changes apply only after acceptance.</summary>
    public ModelGraphicsWindow(double ambient, string background, bool edges, float fieldOfView, bool locked)
    {
        Title = UiText.Get("Display.Graphics");
        Width = 400;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = UiText.Get("Display.Ambient"), Margin = new Thickness(0, 0, 0, 8) });
        _ambient = new Slider { Minimum = 0.15, Maximum = 1, Value = ambient, TickFrequency = 0.05,
            IsSnapToTickEnabled = true, Margin = new Thickness(0, 0, 0, 20) };
        panel.Children.Add(_ambient);
        panel.Children.Add(new TextBlock { Text = UiText.Get("Display.Background"), Margin = new Thickness(0, 0, 0, 8) });
        _background = new ComboBox { Margin = new Thickness(0, 0, 0, 20) };
        foreach (var mode in new[] { "White", "Gray", "Sky" })
        {
            var item = new ComboBoxItem { Content = UiText.Get("Display.Background." + mode), Tag = mode };
            _background.Items.Add(item);
            if (mode == background) _background.SelectedItem = item;
        }
        panel.Children.Add(_background);
        panel.Children.Add(new TextBlock { Text = UiText.Get("Display.FieldOfView"), Margin = new Thickness(0, 0, 0, 8) });
        _fieldOfView = new Slider { Minimum = 15, Maximum = 100, Value = fieldOfView, TickFrequency = 5,
            IsSnapToTickEnabled = true, IsEnabled = !locked, Margin = new Thickness(0, 0, 0, 20), AutoToolTipPlacement = System.Windows.Controls.Primitives.AutoToolTipPlacement.TopLeft };
        panel.Children.Add(_fieldOfView);
        _edges = new CheckBox { Content = UiText.Get("View3D.Edges"), IsChecked = edges, Margin = new Thickness(0, 0, 0, 24) };
        panel.Children.Add(_edges);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = UiText.Get("Dialog.Cancel"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var accept = new Button { Content = UiText.Get("Dialog.Accept"), IsDefault = true };
        accept.Click += AcceptClicked;
        buttons.Children.Add(cancel);
        buttons.Children.Add(accept);
        panel.Children.Add(buttons);
        Content = panel;
    }

    /// <summary>Commits the draft graphics configuration to the owning viewport.</summary>
    private void AcceptClicked(object sender, RoutedEventArgs e) => DialogResult = true;
}
