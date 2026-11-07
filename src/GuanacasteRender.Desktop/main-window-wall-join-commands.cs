using System.IO;
using System.Windows;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>
    /// Applies a wall-join command from the Wall ribbon to both ends of the selected wall. Allow, wall joins and
    /// miter restore the default mitered corner; butt, square off and disallow keep their own meaning.
    /// </summary>
    private bool TryExecuteWallJoinCommand(string commandId)
    {
        WallJoinSetting chosen;
        switch (commandId)
        {
            case "AllowJoin":
            case "MiterJoin":
            case "WallJoins":
            case "JoinGeometry": chosen = WallJoinSetting.Miter; break;
            case "ButtJoin": chosen = WallJoinSetting.Butt; break;
            case "SquareOff": chosen = WallJoinSetting.SquareOff; break;
            case "DisallowJoin":
            case "UnjoinGeometry": chosen = WallJoinSetting.Disallowed; break;
            default: return false;
        }
        if (_selectedWallId is not Guid wallId)
        {
            StatusText.Text = GetText("Status.SelectWallToJoin");
            return true;
        }
        try
        {
            _session.SetWallJoins([wallId], chosen);
            StatusText.Text = GetText("Status.WallJoinChanged");
            RefreshAll();
        }
        catch (InvalidDataException exception)
        {
            MessageBox.Show(this, LocalizedErrors.Translate(exception.Message), GetText("Dialog.InvalidGeometry"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        return true;
    }
}
