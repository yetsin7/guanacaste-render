using System.Windows;
using System.Windows.Controls;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>
/// Spends free ribbon width on captions. Buttons start icon-only; large buttons, then whole small-button columns,
/// get their caption one unit at a time while the ribbon still fits, so a wide window shows text on many buttons and
/// a narrow one on few or none. It lays the ribbon out for real after each step, so callers run it from a dispatcher
/// callback rather than from a SizeChanged handler (WPF ignores UpdateLayout while a layout pass is running).
/// </summary>
internal static class RibbonLabelFitter
{
    /// <summary>Enables captions on as many buttons of <paramref name="host"/> as fit in <paramref name="available"/>.</summary>
    public static void Fit(Panel host, double available)
    {
        var units = Buttons(host)
            .GroupBy(button => button.Height > 40 ? (object)button : LogicalTreeHelper.GetParent(button) ?? button)
            .Select(group => group.ToList())
            .OrderByDescending(unit => unit[0].Height > 40)
            .ToList();
        foreach (var unit in units) Set(unit, false);
        if (available <= 0) return;
        foreach (var unit in units)
        {
            Set(unit, true);
            if (Measure(host) > available) Set(unit, false);
        }
        Measure(host);
    }

    /// <summary>Runs a real layout pass and returns the natural width; must not be called from inside a layout pass.</summary>
    private static double Measure(Panel host)
    {
        host.InvalidateMeasure();
        host.UpdateLayout();
        return host.DesiredSize.Width;
    }

    private static void Set(IEnumerable<Button> unit, bool value)
    {
        foreach (var button in unit) RibbonButton.SetShowLabel(button, value);
    }

    private static IEnumerable<Button> Buttons(DependencyObject node)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
        {
            if (child is Button { Content: string } button) yield return button;
            else foreach (var inner in Buttons(child)) yield return inner;
        }
    }
}
