using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>Asks for the annotation text and places it at the clicked position; cancelling places nothing.</summary>
    private void PlaceModelText(Point2 position)
    {
        var text = TextPrompt.Ask(this, GetText("Prompt.ModelText.Title"), GetText("Prompt.ModelText.Label"),
            GetText("Prompt.Accept"), GetText("Prompt.Cancel"));
        if (text is null)
        {
            StatusText.Text = GetText("Status.ModelTextCancelled");
            return;
        }
        _selectedStructuralId = _session.AddModelText(_activeLevelId, position, text);
        StatusText.Text = FormatText("Status.StructuralCreated", text);
        RefreshAll();
    }

}
