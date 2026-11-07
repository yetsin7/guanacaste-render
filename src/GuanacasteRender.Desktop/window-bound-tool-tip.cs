using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Constrains native WPF tooltip popups to their owning application window and dismisses them when that window loses
/// activation. WPF renders a tooltip in a separate native window, so explicit ownership behavior is required to keep
/// a long-running hint from appearing over another application.
/// </summary>
public static class WindowBoundToolTip
{
    private const double WindowMargin = 8;
    private const double TargetGap = 4;
    private static readonly ConditionalWeakTable<ToolTip, OwnerRegistration> Registrations = new();

    /// <summary>
    /// Applies active-window validation and window-relative placement when a tooltip is about to become visible.
    /// Tooltips without a visual placement target are closed because they cannot be safely associated with a window.
    /// </summary>
    public static void Open(ToolTip toolTip)
    {
        if (toolTip.PlacementTarget is not FrameworkElement target ||
            Window.GetWindow(target) is not { } owner ||
            !owner.IsActive)
        {
            toolTip.IsOpen = false;
            return;
        }

        var registration = Registrations.GetValue(toolTip, static current => new OwnerRegistration(current));
        registration.Attach(owner);
        // WPF's automatic tooltip service rejects reused tooltips when StaysOpen is false.
        // Owner deactivation is handled explicitly rather than relying on popup mouse capture.
        toolTip.StaysOpen = true;
        toolTip.MaxWidth = Math.Max(120, owner.ActualWidth - WindowMargin * 2);
        toolTip.MaxHeight = Math.Max(80, owner.ActualHeight - WindowMargin * 2);
        toolTip.Placement = PlacementMode.Custom;
        toolTip.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
            PlaceWithinOwner(popupSize, targetSize, target, owner);
    }

    /// <summary>Detaches activation tracking after a tooltip is no longer visible.</summary>
    public static void Close(ToolTip toolTip)
    {
        if (!Registrations.TryGetValue(toolTip, out var registration)) return;
        registration.Detach();
        Registrations.Remove(toolTip);
    }

    /// <summary>
    /// Calculates a popup origin relative to its target while keeping the complete popup within the owner window.
    /// The preferred position is below the target; the position moves above it when there is insufficient space.
    /// </summary>
    public static CustomPopupPlacement[] PlaceWithinOwner(
        Size popupSize,
        Size targetSize,
        FrameworkElement target,
        Window owner)
    {
        Point targetOrigin;
        try
        {
            targetOrigin = target.TranslatePoint(new Point(), owner);
        }
        catch (InvalidOperationException)
        {
            return [new CustomPopupPlacement(new Point(0, targetSize.Height + TargetGap), PopupPrimaryAxis.Horizontal)];
        }

        var minimumX = WindowMargin;
        var maximumX = owner.ActualWidth - popupSize.Width - WindowMargin;
        var desiredX = Clamp(targetOrigin.X, minimumX, maximumX);
        var below = targetOrigin.Y + targetSize.Height + TargetGap;
        var above = targetOrigin.Y - popupSize.Height - TargetGap;
        var maximumY = owner.ActualHeight - popupSize.Height - WindowMargin;
        var desiredY = below <= maximumY ? below : above;
        desiredY = Clamp(desiredY, WindowMargin, maximumY);

        var relativeOrigin = new Point(desiredX - targetOrigin.X, desiredY - targetOrigin.Y);
        return [new CustomPopupPlacement(relativeOrigin, PopupPrimaryAxis.Horizontal)];
    }

    /// <summary>Clamps a coordinate while tolerating a popup that is temporarily larger than its owner.</summary>
    private static double Clamp(double value, double minimum, double maximum)
    {
        if (maximum < minimum) return minimum;
        return Math.Min(Math.Max(value, minimum), maximum);
    }

    /// <summary>Tracks the owner of one visible tooltip without extending the lifetime of the tooltip itself.</summary>
    private sealed class OwnerRegistration
    {
        private readonly WeakReference<ToolTip> _toolTip;
        private Window? _owner;

        /// <summary>Creates a registration for a tooltip that may be shown by different windows over its lifetime.</summary>
        public OwnerRegistration(ToolTip toolTip)
        {
            _toolTip = new WeakReference<ToolTip>(toolTip);
        }

        /// <summary>Starts observing the supplied owner, replacing any previous owner registration.</summary>
        public void Attach(Window owner)
        {
            if (ReferenceEquals(_owner, owner)) return;
            Detach();
            _owner = owner;
            _owner.Deactivated += Owner_Deactivated;
        }

        /// <summary>Stops observing the currently registered owner.</summary>
        public void Detach()
        {
            if (_owner is null) return;
            _owner.Deactivated -= Owner_Deactivated;
            _owner = null;
        }

        /// <summary>Closes the native tooltip popup as soon as its Guanacaste 3D window loses activation.</summary>
        private void Owner_Deactivated(object? sender, EventArgs eventArgs)
        {
            if (_toolTip.TryGetTarget(out var toolTip)) toolTip.IsOpen = false;
            Detach();
        }
    }
}
