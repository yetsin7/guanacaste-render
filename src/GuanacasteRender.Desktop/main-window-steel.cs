using System.Globalization;
using System.IO;
using System.Windows;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>The bolt pattern the next click will place: rows, columns, pitch and diameter in meters.</summary>
    private (int Rows, int Columns, double Pitch, double Diameter)? _pendingBolts;

    /// <summary>Routes the Steel tab: plates, bolts, welds and the cuts and fittings of steel members.</summary>
    private bool TryExecuteSteelCommand(string commandId)
    {
        switch (commandId)
        {
            case "Plate": BeginKindTool(StructuralElementKind.Plate); return true;
            case "Welds": BeginKindTool(StructuralElementKind.Weld); return true;
            case "Bolts": BeginBolts(); return true;
            case "Cope": RunMemberEdit("Cope", "Prompt.Cope", "end 0.15 0.03", ApplyCope); return true;
            case "ParametricCope": RunMemberEdit("ParametricCope", "Prompt.ParametricCope", "end 0.15 0.03 0.5", ApplyCope); return true;
            case "CornerCut": RunMemberEdit("CornerCut", "Prompt.CornerCut", "1 0.05 0.05", ApplyCornerCut); return true;
            case "ContourCut": RunMemberEdit("ContourCut", "Prompt.ContourCut", "0.1 0.1 0.1 0.1", ApplyContourCut); return true;
            case "CutThrough": RunMemberEdit("CutThrough", "Prompt.CutThrough", "0.5 0.05", ApplyCutThrough); return true;
            case "Shorten": RunMemberEdit("Shorten", "Prompt.Shorten", "end 0.1", ApplyShorten); return true;
            case "CutBy": RunMemberEdit("CutBy", "Prompt.CutBy", "", ApplyCutBy); return true;
            case "Miter": RunMemberEdit("Miter", "Prompt.Miter", "", ApplyMiter); return true;
            default: return false;
        }
    }

    private StructuralElement? SelectedMember() =>
        _selectedStructuralId is { } id ? _session.Document.StructuralElements.FirstOrDefault(item => item.Id == id && item.HasSolid) : null;

    /// <summary>Asks for the numbers of an edit of the selected member and applies it; a refused edit says why in the status line.</summary>
    private bool RunMemberEdit(string titleKey, string promptKey, string suggestion, Action<StructuralElement, string[]> apply)
    {
        if (SelectedMember() is not { } member)
        {
            StatusText.Text = GetText("Status.MemberNotSelected");
            return false;
        }
        var text = TextPrompt.Ask(this, GetText("Steel." + titleKey), GetText(promptKey), GetText("Prompt.Accept"), GetText("Prompt.Cancel"), suggestion.Length == 0 ? null : suggestion);
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            apply(member, text.Split([(char)32, ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            StatusText.Text = GetText("Status.MemberEdited");
            RefreshAll();
            return true;
        }
        catch (FormatException)
        {
            StatusText.Text = GetText("Status.MemberValuesInvalid");
            return false;
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
            return false;
        }
    }

    private double Length(string[] words, int index) =>
        index < words.Length && Units.TryParseLength(words[index], out var meters) ? meters : throw new FormatException();

    private static double Plain(string[] words, int index) =>
        index < words.Length && double.TryParse(words[index].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : throw new FormatException();

    private static bool IsStart(string word) => word.ToLowerInvariant() is "start" or "inicio" or "principio" or "1" or "i";

    private static bool IsEndWord(string word) => word.ToLowerInvariant() is "end" or "final" or "fin" or "2" or "f";

    private void ApplyCope(StructuralElement member, string[] words)
    {
        if (words.Length < 3 || !(IsStart(words[0]) || IsEndWord(words[0]))) throw new FormatException();
        _session.AddCope(member.Id, IsStart(words[0]), Length(words, 1), Length(words, 2), words.Length > 3 ? Plain(words, 3) : 1);
    }

    private void ApplyCornerCut(StructuralElement member, string[] words) =>
        _session.AddCornerCut(member.Id, (int)Plain(words, 0) - 1, Length(words, 1), Length(words, 2));

    private void ApplyContourCut(StructuralElement member, string[] words) =>
        _session.AddContourCut(member.Id, Length(words, 0), Length(words, 1), Length(words, 2), Length(words, 3));

    private void ApplyCutThrough(StructuralElement member, string[] words) =>
        _session.AddCutThrough(member.Id, Length(words, 0), Length(words, 1));

    private void ApplyShorten(StructuralElement member, string[] words)
    {
        if (words.Length < 2 || !(IsStart(words[0]) || IsEndWord(words[0]))) throw new FormatException();
        _session.ShortenMember(member.Id, IsStart(words[0]), Length(words, 1));
    }

    private StructuralElement OtherMemberNamed(string[] words)
    {
        var name = string.Join(" ", words);
        return _session.Document.StructuralElements.FirstOrDefault(item => item.HasSolid && item.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase))
               ?? throw new InvalidDataException("The member does not exist.");
    }

    private void ApplyCutBy(StructuralElement member, string[] words) => _session.CutMemberBy(member.Id, OtherMemberNamed(words).Id);

    private void ApplyMiter(StructuralElement member, string[] words) => _session.MiterMembers(member.Id, OtherMemberNamed(words).Id);

    // ------------------------------------------------------------------ bolts

    private void BeginBolts()
    {
        var text = TextPrompt.Ask(this, GetText("Steel.Bolts"), GetText("Prompt.Bolts"), GetText("Prompt.Accept"), GetText("Prompt.Cancel"), "2 2 0.075 0.02");
        if (string.IsNullOrWhiteSpace(text)) return;
        var words = text.Split([(char)32, ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        try
        {
            _pendingBolts = ((int)Plain(words, 0), (int)Plain(words, 1), Length(words, 2), Length(words, 3));
        }
        catch (FormatException)
        {
            StatusText.Text = GetText("Status.MemberValuesInvalid");
            return;
        }
        BeginStructuralTool(StructuralElementKind.Bolt, StructuralPlacementMode.Point);
        StatusText.Text = GetText("Status.BoltsArmed");
    }

    private void PlaceBolts(Point2 point)
    {
        if (_pendingBolts is not { } pattern) return;
        try
        {
            var ids = _session.AddBoltPattern(_activeLevelId, point, pattern.Rows, pattern.Columns, pattern.Pitch, pattern.Diameter);
            _selectedStructuralId = ids[0];
            StatusText.Text = FormatText("Status.BoltsPlaced", ids.Count);
            RefreshAll();
        }
        catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
    }
}
