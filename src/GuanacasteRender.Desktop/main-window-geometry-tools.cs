using System.IO;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>Routes Cut Geometry, Cut Profile and the 3D sketch of a mass.</summary>
    private bool TryExecuteGeometryToolCommand(string commandId)
    {
        switch (commandId)
        {
            case "CutGeometry": CutSelectedGeometry(); return true;
            case "CutProfile": EditSelectedCutProfile(); return true;
            case "Sketch3D": SketchMassInSpace(); return true;
            default: return false;
        }
    }

    private Wall? SelectedWallOrNull() =>
        _selectedWallId is { } id ? _session.Document.Walls.FirstOrDefault(wall => wall.Id == id) : null;

    /// <summary>Cuts the selected wall or member with another element, asked by name.</summary>
    private void CutSelectedGeometry()
    {
        if (SelectedWallOrNull() is { } wall)
        {
            var text = TextPrompt.Ask(this, GetText("Steel.CutGeometry"), GetText("Prompt.CutGeometry"), GetText("Prompt.Accept"), GetText("Prompt.Cancel"), null);
            if (string.IsNullOrWhiteSpace(text)) return;
            RunWallEdit(() => _session.CutWallBy(wall.Id, OtherMemberNamed(Words(text)).Id), "Status.GeometryCut");
            return;
        }
        if (SelectedMember() is null)
        {
            StatusText.Text = GetText("Status.NothingToCut");
            return;
        }
        RunMemberEdit("CutGeometry", "Prompt.CutGeometry", "", ApplyCutBy);
    }

    /// <summary>
    /// The profile of what is cut: for a wall, the shape of its top (the wall profile editor); for a member, a notch taken out of it,
    /// given as the lengths along and across it where the notch begins and ends, and how deep it goes.
    /// </summary>
    private void EditSelectedCutProfile()
    {
        if (SelectedWallOrNull() is not null)
        {
            ShowWallProfileEditor();
            return;
        }
        if (SelectedMember() is null)
        {
            StatusText.Text = GetText("Status.NothingToCut");
            return;
        }
        RunMemberEdit("CutProfile", "Prompt.CutProfile", "0.5 1 -0.1 0.1 0.1",
            (member, words) => _session.AddMemberCut(member.Id, new MemberCut(Length(words, 0), Length(words, 1), Length(words, 2), Length(words, 3), Length(words, 4))));
    }

    /// <summary>Places a mass from numbers: two corners, the elevation of its base above the level and its height.</summary>
    private void SketchMassInSpace()
    {
        var text = TextPrompt.Ask(this, GetText("Sketch.Title"), GetText("Prompt.Sketch3D"), GetText("Prompt.Accept"), GetText("Prompt.Cancel"), "0 0 6 4 3 3");
        if (string.IsNullOrWhiteSpace(text)) return;
        var words = Words(text);
        try
        {
            var id = _session.AddMassAtElevation(_activeLevelId, new Point2(Length(words, 0), Length(words, 1)), new Point2(Length(words, 2), Length(words, 3)),
                Length(words, 4), Length(words, 5), GetText("Entity.Mass"));
            _selectedStructuralId = id;
            _selectedWallId = null;
            StatusText.Text = GetText("Status.SketchMass");
            RefreshAll();
        }
        catch (FormatException) { StatusText.Text = GetText("Status.MemberValuesInvalid"); }
        catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
    }
}
