using System.Windows.Media;

namespace Guanacaste3D.Desktop.Controls;

public partial class RenderPanel
{
    private static readonly Brush ModelUpdatePendingBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x7A, 0x6B));
    private Brush? _modelUpdateIdleBrush;

    /// <summary>True while the studio bar announces a newer model waiting to be brought in.</summary>
    internal bool ModelUpdatePending { get; private set; }

    /// <summary>
    /// Shows on the studio bar whether the linked model file changed since the last exchange. The button always
    /// works; the highlight only tells the person that pressing it now will bring something new.
    /// </summary>
    public void SetModelUpdateState(bool available, string? sourceName)
    {
        _modelUpdateIdleBrush ??= StudioUpdateModelButton.Background;
        ModelUpdatePending = available;
        StudioUpdateModelButton.Content = UiText.Get(available ? "ModelUpdate.ButtonPending" : "ModelUpdate.Button");
        StudioUpdateModelButton.Background = available ? ModelUpdatePendingBrush : _modelUpdateIdleBrush;
        StudioUpdateModelButton.ToolTip = sourceName is null
            ? UiText.Get("ModelUpdate.TooltipUnlinked")
            : string.Format(System.Globalization.CultureInfo.CurrentCulture,
                UiText.Get(available ? "ModelUpdate.TooltipPending" : "ModelUpdate.TooltipLinked"), sourceName);
        if (available) StatusText.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiText.Get("ModelUpdate.Pending"), sourceName);
    }
}
