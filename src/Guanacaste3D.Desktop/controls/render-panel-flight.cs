using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Guanacaste3D.Desktop.Controls;

public partial class RenderPanel
{
    private DispatcherTimer? _navigationSettleTimer;
    private bool _interactivePreview;

    /// <summary>Adds metric flight settings to the existing camera card without changing model materials.</summary>
    private void InitializeFlightControls()
    {
        if (_navigator is null || CollisionCheck.Parent is not StackPanel card) return;
        _navigator.TravelSpeed = 2;
        _navigator.FreeTravel = true;
        var controls = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
        var title = new TextBlock { TextWrapping = TextWrapping.Wrap };
        title.SetResourceReference(StyleProperty, "PanelLabel");
        title.SetResourceReference(TextBlock.TextProperty, "RenderPanel.TravelSpeed");
        controls.Children.Add(title);
        var speed = new Slider { Minimum = 0.2, Maximum = 10, Value = 2, TickFrequency = 0.2,
            IsSnapToTickEnabled = true, AutoToolTipPlacement = System.Windows.Controls.Primitives.AutoToolTipPlacement.TopLeft };
        speed.SetResourceReference(ToolTipProperty, "RenderPanel.TravelSpeedHint");
        speed.ValueChanged += TravelSpeed_Changed;
        var speedRow = new DockPanel();
        var speedValue = new TextBlock();
        speedValue.SetResourceReference(StyleProperty, "SliderValue");
        speedValue.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Slider.Value))
            { Source = speed, StringFormat = "F1" });
        speedRow.Children.Add(speedValue);
        speedRow.Children.Add(speed);
        controls.Children.Add(speedRow);
        var horizontal = new CheckBox { Margin = new Thickness(0, 8, 0, 8) };
        horizontal.SetResourceReference(StyleProperty, "PanelCheck");
        horizontal.SetResourceReference(ContentControl.ContentProperty, "RenderPanel.HorizontalTravel");
        horizontal.Checked += HorizontalTravel_Changed;
        horizontal.Unchecked += HorizontalTravel_Changed;
        controls.Children.Add(horizontal);
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        hint.SetResourceReference(StyleProperty, "PanelHint");
        hint.SetResourceReference(TextBlock.TextProperty, "RenderPanel.FlightHint");
        controls.Children.Add(hint);
        card.Children.Insert(card.Children.IndexOf(CollisionCheck) + 1, controls);
    }

    /// <summary>Applies user-selected metric speed immediately without moving the camera.</summary>
    private void TravelSpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_navigator is not null) _navigator.TravelSpeed = (float)e.NewValue;
    }

    /// <summary>Switches between flying along the sightline and level interior exploration.</summary>
    private void HorizontalTravel_Changed(object sender, RoutedEventArgs e)
    {
        if (_navigator is not null) _navigator.HorizontalTravel = ((CheckBox)sender).IsChecked == true;
    }

    /// <summary>Temporarily lowers preview cost during motion; geometry acceleration remains reusable.</summary>
    private void BeginInteractivePreview()
    {
        if (_preview is null || _rendering || PreviewToggle.IsChecked != true || !IsVisible) return;
        _interactivePreview = true;
        var (width, height) = PreviewSize();
        _preview.SetSize(width, height);
        if (_navigationSettleTimer is null)
        {
            _navigationSettleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _navigationSettleTimer.Tick += RestoreSettledPreview;
        }
        _navigationSettleTimer.Stop();
        _navigationSettleTimer.Start();
    }

    /// <summary>Restores detailed progressive rendering once navigation has stopped for a quarter second.</summary>
    private void RestoreSettledPreview(object? sender, EventArgs e)
    {
        _navigationSettleTimer?.Stop();
        _interactivePreview = false;
        if (_preview is null || !IsVisible || _rendering || PreviewToggle.IsChecked != true) return;
        var (width, height) = PreviewSize();
        _preview.SetSize(width, height);
    }

    /// <summary>Releases the settling callback whenever the workspace is hidden or unloaded.</summary>
    private void StopInteractivePreview()
    {
        _navigationSettleTimer?.Stop();
        _interactivePreview = false;
    }
}
