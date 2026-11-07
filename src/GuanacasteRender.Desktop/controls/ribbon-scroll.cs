using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>
/// Lets the mouse wheel pan a ribbon sideways, so overflowing panels stay reachable without a
/// scroll bar eating into the fixed ribbon height.
/// </summary>
public static class RibbonScroll
{
    public static readonly DependencyProperty WheelScrollsHorizontallyProperty = DependencyProperty.RegisterAttached(
        "WheelScrollsHorizontally",
        typeof(bool),
        typeof(RibbonScroll),
        new PropertyMetadata(false, OnWheelScrollsHorizontallyChanged));

    public static bool GetWheelScrollsHorizontally(DependencyObject target) =>
        (bool)target.GetValue(WheelScrollsHorizontallyProperty);

    public static void SetWheelScrollsHorizontally(DependencyObject target, bool value) =>
        target.SetValue(WheelScrollsHorizontallyProperty, value);

    private static void OnWheelScrollsHorizontallyChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ScrollViewer viewer) return;
        viewer.PreviewMouseWheel -= HandleWheel;
        if ((bool)args.NewValue) viewer.PreviewMouseWheel += HandleWheel;
    }

    private static void HandleWheel(object sender, MouseWheelEventArgs e)
    {
        var viewer = (ScrollViewer)sender;
        if (viewer.ScrollableWidth <= 0) return;
        viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset - e.Delta);
        e.Handled = true;
    }
}
