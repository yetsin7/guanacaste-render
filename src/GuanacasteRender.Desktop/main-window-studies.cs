using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private Window? _studyWindow;
    private Window? _outcomeWindow;

    /// <summary>Routes Create Study and Explore Outcomes.</summary>
    private bool TryExecuteStudyCommand(string commandId)
    {
        switch (commandId)
        {
            case "CreateStudy": ShowStudyWindow(); return true;
            case "ExploreOutcomes": ShowOutcomeWindow(null); return true;
            default: return false;
        }
    }

    private static string Numbers(params double[] values) => string.Join(" ", values.Select(value => value.ToString("0.###", CultureInfo.InvariantCulture)));

    /// <summary>Reads "least greatest" in the project's units.</summary>
    private bool TryReadRange(string text, out double least, out double greatest)
    {
        least = greatest = 0;
        var words = text.Split([(char)32, ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length == 2 && Units.TryParseLength(words[0], out least) && Units.TryParseLength(words[1], out greatest);
    }

    /// <summary>Saves a study of the selected mass from the numbers typed; returns the message to show.</summary>
    internal string CreateStudyFromText(string name, string widths, string depths, string heights, StudyGoal goal, string target, string areas, string count, string seed)
    {
        try
        {
            if (SelectedMass() is not { } mass) throw new InvalidDataException("Select a mass first.");
            if (!TryReadRange(widths, out var widthMin, out var widthMax) || !TryReadRange(depths, out var depthMin, out var depthMax) || !TryReadRange(heights, out var heightMin, out var heightMax) ||
                !double.TryParse(target.Length == 0 ? "0" : target.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var targetArea) ||
                !int.TryParse(count, NumberStyles.Integer, CultureInfo.InvariantCulture, out var shapes) || !int.TryParse(seed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var random))
                throw new InvalidDataException("The study is invalid.");
            double areaMin = 0, areaMax = 0;
            if (areas.Trim().Length > 0)
            {
                var words = areas.Split([(char)32, ';'], StringSplitOptions.RemoveEmptyEntries);
                if (words.Length != 2 || !double.TryParse(words[0].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out areaMin) ||
                    !double.TryParse(words[1].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out areaMax)) throw new InvalidDataException("The study is invalid.");
            }
            var id = _session.AddStudy(name, mass.Id, widthMin, widthMax, depthMin, depthMax, heightMin, heightMax, goal, targetArea, areaMin, areaMax, shapes, random);
            RefreshAll();
            var best = DesignStudies.Evaluate(_session.Document.Studies.Single(study => study.Id == id))[0];
            var message = FormatText("Study.Created", name.Trim(), shapes, Units.FormatLength(best.Width), Units.FormatLength(best.Depth), Units.FormatLength(best.Height));
            StatusText.Text = message;
            return message;
        }
        catch (InvalidDataException exception)
        {
            var message = LocalizedErrors.Translate(exception.Message);
            StatusText.Text = message;
            return message;
        }
    }

    private void ShowStudyWindow()
    {
        _studyWindow?.Close();
        var window = NewManageWindow("Study.Title", 520);
        window.Closed += (_, _) => _studyWindow = null;
        _studyWindow = window;
        var mass = SelectedMass();
        var footprint = mass?.Footprint;
        var width = footprint is null ? 10 : footprint.Max(p => p.X) - footprint.Min(p => p.X);
        var depth = footprint is null ? 6 : footprint.Max(p => p.Y) - footprint.Min(p => p.Y);
        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText(mass is null ? "Study.NoMass" : "Study.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        TextBox Add(string name, string label, string text)
        {
            layout.Children.Add(new TextBlock { Text = GetText(label) });
            var box = new TextBox { Name = name, Text = text, Margin = new Thickness(0, 2, 0, 6), Padding = new Thickness(4, 2, 4, 2) };
            layout.Children.Add(box);
            return box;
        }
        var name = Add("StudyName", "Study.Name", $"{GetText("Study.Default")} {_session.Document.Studies.Count + 1}");
        var widths = Add("StudyWidths", "Study.Widths", Numbers(width * 0.6, width * 1.6));
        var depths = Add("StudyDepths", "Study.Depths", Numbers(depth * 0.6, depth * 1.6));
        var heights = Add("StudyHeights", "Study.Heights", Numbers(mass?.Height ?? 3, (mass?.Height ?? 3) * 1.5));
        layout.Children.Add(new TextBlock { Text = GetText("Study.Goal") });
        var goal = new ComboBox { Name = "StudyGoal", Margin = new Thickness(0, 2, 0, 6) };
        foreach (var item in Enum.GetValues<StudyGoal>()) goal.Items.Add(GetText("Study.Goal." + item));
        goal.SelectedIndex = 0;
        layout.Children.Add(goal);
        var target = Add("StudyTarget", "Study.Target", Numbers(width * depth));
        var areas = Add("StudyAreas", "Study.Areas", "");
        var count = Add("StudyCount", "Study.Count", "200");
        var seed = Add("StudySeed", "Study.Seed", "1");
        var message = new TextBlock { Name = "StudyMessage", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
        var buttons = new WrapPanel();
        buttons.Children.Add(ManageButton("StudyCreate", GetText("Study.Create"), (_, _) =>
        {
            message.Text = CreateStudyFromText(name.Text, widths.Text, depths.Text, heights.Text, (StudyGoal)Math.Max(0, goal.SelectedIndex), target.Text, areas.Text, count.Text, seed.Text);
            if (_session.Document.Studies.Any(study => study.Name.Equals(name.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)))
            {
                window.Close();
                ShowOutcomeWindow(_session.Document.Studies.First(study => study.Name.Equals(name.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).Id);
            }
        }));
        buttons.Children.Add(ManageButton("StudyClose", GetText("Phases.Close"), (_, _) => window.Close()));
        layout.Children.Add(buttons);
        layout.Children.Add(message);
        window.Content = new ScrollViewer { Content = layout, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        window.Show();
    }

    private string OutcomeLine(StudyOutcome item) =>
        FormatText(item.Feasible ? "Outcome.Line" : "Outcome.LineOut", item.Rank, Units.FormatLength(item.Width), Units.FormatLength(item.Depth), Units.FormatLength(item.Height),
            Units.FormatArea(item.Area), Units.FormatVolume(item.Volume), Units.FormatArea(item.FacadeArea), item.Compactness);

    private void ShowOutcomeWindow(Guid? preferred)
    {
        _outcomeWindow?.Close();
        var window = NewManageWindow("Outcome.Title", 640);
        window.Closed += (_, _) => _outcomeWindow = null;
        _outcomeWindow = window;
        var studies = _session.Document.Studies.ToList();
        var layout = new StackPanel { Margin = new Thickness(14) };
        if (studies.Count == 0)
        {
            StatusText.Text = GetText("Outcome.NoStudies");
            layout.Children.Add(new TextBlock { Text = GetText("Outcome.NoStudies"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            layout.Children.Add(ManageButton("OutcomeClose", GetText("Phases.Close"), (_, _) => window.Close()));
            window.Content = layout;
            window.Show();
            return;
        }
        var picker = new ComboBox { Name = "OutcomeStudy", Margin = new Thickness(0, 0, 0, 6) };
        foreach (var study in studies) picker.Items.Add(study.Name);
        picker.SelectedIndex = Math.Max(0, studies.FindIndex(study => study.Id == preferred));
        var list = new ListBox { Name = "OutcomeList", Height = 250, Margin = new Thickness(0, 0, 0, 6) };
        var summary = new TextBlock { Name = "OutcomeSummary", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        IReadOnlyList<StudyOutcome> outcomes = [];
        void Refill()
        {
            var study = studies[Math.Max(0, picker.SelectedIndex)];
            outcomes = DesignStudies.Evaluate(study);
            list.Items.Clear();
            foreach (var item in outcomes.Take(60)) list.Items.Add(OutcomeLine(item));
            summary.Text = FormatText("Outcome.Summary", GetText("Study.Goal." + study.Goal), outcomes.Count, outcomes.Count(item => item.Feasible));
        }
        picker.SelectionChanged += (_, _) => Refill();
        var buttons = new WrapPanel();
        buttons.Children.Add(ManageButton("OutcomeApply", GetText("Outcome.Apply"), (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= outcomes.Count) { StatusText.Text = GetText("Outcome.Pick"); return; }
            try
            {
                _session.ApplyStudyOutcome(studies[picker.SelectedIndex].Id, outcomes[list.SelectedIndex].Rank);
                StatusText.Text = GetText("Outcome.Applied");
                RefreshAll();
            }
            catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
        }));
        buttons.Children.Add(ManageButton("OutcomeOptions", GetText("Outcome.Options"), (_, _) =>
        {
            try
            {
                var made = _session.AddStudyOutcomesAsOptions(studies[picker.SelectedIndex].Id, 5);
                StatusText.Text = FormatText("Outcome.OptionsMade", made);
                RefreshAll();
            }
            catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
        }));
        buttons.Children.Add(ManageButton("OutcomeRemove", GetText("Outcome.Remove"), (_, _) =>
        {
            try
            {
                _session.RemoveStudy(studies[picker.SelectedIndex].Id);
                StatusText.Text = GetText("Outcome.Removed");
                RefreshAll();
                ShowOutcomeWindow(null);
            }
            catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
        }));
        buttons.Children.Add(ManageButton("OutcomeClose", GetText("Phases.Close"), (_, _) => window.Close()));
        layout.Children.Add(new TextBlock { Text = GetText("Outcome.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(picker);
        layout.Children.Add(summary);
        layout.Children.Add(list);
        layout.Children.Add(buttons);
        window.Content = layout;
        Refill();
        window.Show();
    }
}
