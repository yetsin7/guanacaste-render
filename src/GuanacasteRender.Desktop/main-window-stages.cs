using System.IO;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private Window? _phasesWindow;
    private Window? _optionsWindow;
    private Guid? _optionsSelectedSet;

    /// <summary>Routes the Phases and Design Options buttons of the Manage tab.</summary>
    private bool TryExecuteStagesCommand(string commandId)
    {
        switch (commandId)
        {
            case "Phases": ShowPhasesWindow(); return true;
            case "DesignOptions": ShowOptionsWindow(); return true;
            default: return false;
        }
    }

    /// <summary>Runs one change from a stages window, reporting a refused one in the status line, and redraws the views.</summary>
    private bool RunStagesEdit(Action edit, string statusKey, params object[] arguments)
    {
        try
        {
            edit();
            StatusText.Text = arguments.Length == 0 ? GetText(statusKey) : FormatText(statusKey, arguments);
            ApplyViewVisibility();
            return true;
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
            return false;
        }
    }

    private static Button StageButton(string name, string text, RoutedEventHandler click)
    {
        var button = new Button { Name = name, Content = text, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(8, 2, 8, 2) };
        button.Click += click;
        return button;
    }

    private static ComboBox StageCombo(string name, IEnumerable<string> items, int selected)
    {
        var box = new ComboBox { Name = name, Margin = new Thickness(0, 0, 0, 6), MinWidth = 220 };
        foreach (var item in items) box.Items.Add(item);
        box.SelectedIndex = Math.Clamp(selected, 0, Math.Max(0, box.Items.Count - 1));
        return box;
    }

    private Window NewStagesWindow(string titleKey)
    {
        var window = new Window
        {
            Title = GetText(titleKey), Owner = this, Width = 440, SizeToContent = SizeToContent.Height, MaxHeight = 760,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false
        };
        return window;
    }

    // ------------------------------------------------------------------ phases

    private void ShowPhasesWindow()
    {
        if (_phasesWindow is { IsVisible: true } open)
        {
            open.Activate();
            return;
        }
        // A project that has never used phases starts with the two every renovation needs.
        RunStagesEdit(() => _session.EnsureDefaultPhases([GetText("Phase.Existing"), GetText("Phase.New")]), "Status.PhasesChanged");
        _phasesWindow = NewStagesWindow("Phases.Title");
        _phasesWindow.Closed += (_, _) => _phasesWindow = null;
        BuildPhasesContent();
        _phasesWindow.Show();
    }

    /// <summary>Draws the phases window from the current project, keeping the choices that are still valid.</summary>
    private void BuildPhasesContent(int selectedPhase = 0)
    {
        if (_phasesWindow is null) return;
        var phases = _session.Document.Phases;
        var view = ActiveProjectView();
        var selection = SelectedIds();
        var first = selection.Count == 0 ? null : _session.Document.PhaseAssignments.LastOrDefault(item => item.ElementId == selection[0]);
        int PhaseIndex(Guid? id) => id is null ? -1 : phases.FindIndex(phase => phase.Id == id);

        var list = new ListBox { Name = "PhaseList", Height = 110, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var phase in phases) list.Items.Add(phase.Name);
        list.SelectedIndex = Math.Clamp(selectedPhase, 0, Math.Max(0, phases.Count - 1));
        Guid? Selected() => list.SelectedIndex >= 0 && list.SelectedIndex < phases.Count ? phases[list.SelectedIndex].Id : null;

        var rowOne = new WrapPanel();
        rowOne.Children.Add(StageButton("PhaseAdd", GetText("Phases.New"), (_, _) =>
        {
            var name = TextPrompt.Ask(this, GetText("Phases.Title"), GetText("Prompt.Phase.NewName"), GetText("Prompt.Accept"), GetText("Prompt.Cancel"));
            if (string.IsNullOrWhiteSpace(name)) return;
            if (RunStagesEdit(() => _session.AddPhase(name), "Status.PhasesChanged")) BuildPhasesContent(_session.Document.Phases.Count - 1);
        }));
        rowOne.Children.Add(StageButton("PhaseRename", GetText("Phases.Rename"), (_, _) =>
        {
            if (Selected() is not { } id) return;
            var name = TextPrompt.Ask(this, GetText("Phases.Title"), GetText("Prompt.Phase.Rename"), GetText("Prompt.Accept"), GetText("Prompt.Cancel"),
                _session.Document.Phases.First(phase => phase.Id == id).Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            if (RunStagesEdit(() => _session.RenamePhase(id, name), "Status.PhasesChanged")) BuildPhasesContent(list.SelectedIndex);
        }));
        rowOne.Children.Add(StageButton("PhaseRemove", GetText("Phases.Remove"), (_, _) =>
        {
            if (Selected() is not { } id) return;
            if (RunStagesEdit(() => _session.RemovePhase(id), "Status.PhasesChanged")) BuildPhasesContent();
        }));
        rowOne.Children.Add(StageButton("PhaseUp", GetText("Phases.Up"), (_, _) =>
        {
            if (Selected() is not { } id) return;
            var index = list.SelectedIndex;
            if (RunStagesEdit(() => _session.MovePhase(id, -1), "Status.PhasesChanged")) BuildPhasesContent(Math.Max(0, index - 1));
        }));
        rowOne.Children.Add(StageButton("PhaseDown", GetText("Phases.Down"), (_, _) =>
        {
            if (Selected() is not { } id) return;
            var index = list.SelectedIndex;
            if (RunStagesEdit(() => _session.MovePhase(id, 1), "Status.PhasesChanged")) BuildPhasesContent(index + 1);
        }));

        var created = StageCombo("PhaseCreatedBox", new[] { GetText("Phases.FirstPhase") }.Concat(phases.Select(phase => phase.Name)), PhaseIndex(first?.CreatedPhaseId) + 1);
        var demolished = StageCombo("PhaseDemolishedBox", new[] { GetText("Phases.None") }.Concat(phases.Select(phase => phase.Name)), PhaseIndex(first?.DemolishedPhaseId) + 1);
        var assign = StageButton("PhaseAssign", GetText("Phases.Assign"), (_, _) =>
        {
            var ids = SelectedIds();
            if (ids.Count == 0)
            {
                StatusText.Text = GetText("Status.PhaseNoSelection");
                return;
            }
            Guid? CreatedId() => created.SelectedIndex <= 0 ? null : _session.Document.Phases[created.SelectedIndex - 1].Id;
            Guid? DemolishedId() => demolished.SelectedIndex <= 0 ? null : _session.Document.Phases[demolished.SelectedIndex - 1].Id;
            RunStagesEdit(() => _session.AssignPhases(ids, CreatedId(), DemolishedId()), "Status.PhaseAssigned", ids.Count);
        });

        var viewPhase = StageCombo("PhaseViewBox", new[] { GetText("Phases.ViewLatest") }.Concat(phases.Select(phase => phase.Name)), PhaseIndex(view?.PhaseId) + 1);
        var viewFilter = StageCombo("PhaseFilterBox", Enum.GetValues<PhaseFilter>().Select(filter => GetText("PhaseFilter." + filter)), (int)(view?.PhaseFilter ?? PhaseFilter.ShowAll));
        var applyView = StageButton("PhaseViewApply", GetText("Phases.ApplyView"), (_, _) =>
        {
            if (ActiveProjectView() is not { } active)
            {
                StatusText.Text = GetText("Status.NoViewForCategories");
                return;
            }
            Guid? ViewPhaseId() => viewPhase.SelectedIndex <= 0 ? null : _session.Document.Phases[viewPhase.SelectedIndex - 1].Id;
            RunStagesEdit(() => _session.SetViewPhase(active.Id, ViewPhaseId(), (PhaseFilter)Math.Max(0, viewFilter.SelectedIndex)), "Status.PhaseViewSet");
        });

        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText("Phases.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(list);
        layout.Children.Add(rowOne);
        layout.Children.Add(new TextBlock { Text = FormatText("Phases.ElementsGroup", selection.Count), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
        layout.Children.Add(new TextBlock { Text = GetText("Phases.Created") });
        layout.Children.Add(created);
        layout.Children.Add(new TextBlock { Text = GetText("Phases.Demolished") });
        layout.Children.Add(demolished);
        layout.Children.Add(assign);
        layout.Children.Add(new TextBlock { Text = FormatText("Phases.ViewGroup", view?.Name ?? "-"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
        layout.Children.Add(new TextBlock { Text = GetText("Phases.ViewPhase") });
        layout.Children.Add(viewPhase);
        layout.Children.Add(new TextBlock { Text = GetText("Phases.ViewFilter") });
        layout.Children.Add(viewFilter);
        layout.Children.Add(applyView);
        layout.Children.Add(StageButton("PhaseClose", GetText("Phases.Close"), (_, _) => _phasesWindow?.Close()));
        _phasesWindow.Content = new ScrollViewer { Content = layout, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    // ------------------------------------------------------------------ design options

    private void ShowOptionsWindow()
    {
        if (_optionsWindow is { IsVisible: true } open)
        {
            open.Activate();
            return;
        }
        _optionsWindow = NewStagesWindow("Options.Title");
        _optionsWindow.Closed += (_, _) => _optionsWindow = null;
        BuildOptionsContent();
        _optionsWindow.Show();
    }

    private void BuildOptionsContent(int selectedOption = 0)
    {
        if (_optionsWindow is null) return;
        var sets = _session.Document.OptionSets;
        var view = ActiveProjectView();
        var setIndex = _optionsSelectedSet is { } remembered ? sets.FindIndex(set => set.Id == remembered) : -1;
        if (setIndex < 0 && sets.Count > 0) setIndex = 0;
        var current = setIndex >= 0 ? sets[setIndex] : null;
        _optionsSelectedSet = current?.Id;

        var setList = new ListBox { Name = "OptionSetList", Height = 80, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var set in sets) setList.Items.Add(set.Name);
        setList.SelectedIndex = setIndex;
        setList.SelectionChanged += (_, _) =>
        {
            if (setList.SelectedIndex < 0 || setList.SelectedIndex >= sets.Count || sets[setList.SelectedIndex].Id == _optionsSelectedSet) return;
            _optionsSelectedSet = sets[setList.SelectedIndex].Id;
            BuildOptionsContent();
        };

        var optionList = new ListBox { Name = "OptionList", Height = 90, Margin = new Thickness(0, 0, 0, 6) };
        if (current is not null)
            foreach (var option in current.Options) optionList.Items.Add((option.Id == current.PrimaryOptionId ? "★ " : string.Empty) + option.Name);
        optionList.SelectedIndex = current is null ? -1 : Math.Clamp(selectedOption, 0, current.Options.Count - 1);
        DesignOption? SelectedOption() => current is not null && optionList.SelectedIndex >= 0 ? current.Options[optionList.SelectedIndex] : null;

        string? Ask(string labelKey, string? initial = null) =>
            TextPrompt.Ask(this, GetText("Options.Title"), GetText(labelKey), GetText("Prompt.Accept"), GetText("Prompt.Cancel"), initial);

        var setButtons = new WrapPanel();
        setButtons.Children.Add(StageButton("OptionSetAdd", GetText("Options.NewSet"), (_, _) =>
        {
            var name = Ask("Prompt.Option.SetName");
            if (string.IsNullOrWhiteSpace(name)) return;
            var primary = Ask("Prompt.Option.PrimaryName");
            if (string.IsNullOrWhiteSpace(primary)) return;
            if (RunStagesEdit(() => _optionsSelectedSet = _session.AddOptionSet(name, primary), "Status.OptionsChanged")) BuildOptionsContent();
        }));
        setButtons.Children.Add(StageButton("OptionSetRemove", GetText("Options.RemoveSet"), (_, _) =>
        {
            if (current is null) { StatusText.Text = GetText("Status.OptionNoSet"); return; }
            if (RunStagesEdit(() => _session.RemoveOptionSet(current.Id), "Status.OptionsChanged")) { _optionsSelectedSet = null; BuildOptionsContent(); }
        }));

        var optionButtons = new WrapPanel();
        optionButtons.Children.Add(StageButton("OptionAdd", GetText("Options.NewOption"), (_, _) =>
        {
            if (current is null) { StatusText.Text = GetText("Status.OptionNoSet"); return; }
            var name = Ask("Prompt.Option.NewName");
            if (string.IsNullOrWhiteSpace(name)) return;
            if (RunStagesEdit(() => _session.AddDesignOption(current.Id, name), "Status.OptionsChanged")) BuildOptionsContent(int.MaxValue);
        }));
        optionButtons.Children.Add(StageButton("OptionRemove", GetText("Options.RemoveOption"), (_, _) =>
        {
            if (current is null || SelectedOption() is not { } option) return;
            if (RunStagesEdit(() => _session.RemoveDesignOption(current.Id, option.Id), "Status.OptionsChanged")) BuildOptionsContent();
        }));
        optionButtons.Children.Add(StageButton("OptionPrimary", GetText("Options.MakePrimary"), (_, _) =>
        {
            if (current is null || SelectedOption() is not { } option) return;
            var index = optionList.SelectedIndex;
            if (RunStagesEdit(() => _session.SetPrimaryOption(current.Id, option.Id), "Status.OptionsChanged")) BuildOptionsContent(index);
        }));

        var elements = new WrapPanel();
        elements.Children.Add(StageButton("OptionAssign", GetText("Options.Assign"), (_, _) =>
        {
            var ids = SelectedIds();
            if (ids.Count == 0) { StatusText.Text = GetText("Status.PhaseNoSelection"); return; }
            if (SelectedOption() is not { } option) { StatusText.Text = GetText("Status.OptionNoSet"); return; }
            RunStagesEdit(() => _session.AssignToOption(ids, option.Id), "Status.OptionAssigned", ids.Count);
        }));
        elements.Children.Add(StageButton("OptionUnassign", GetText("Options.Unassign"), (_, _) =>
        {
            var ids = SelectedIds();
            if (ids.Count == 0) { StatusText.Text = GetText("Status.PhaseNoSelection"); return; }
            RunStagesEdit(() => _session.AssignToOption(ids, null), "Status.OptionAssigned", ids.Count);
        }));

        var shownIndex = 0;
        if (current is not null && view is not null)
        {
            var shownId = view.OptionChoices.FirstOrDefault(choice => choice.SetId == current.Id)?.OptionId ?? current.PrimaryOptionId;
            shownIndex = Math.Max(0, current.Options.FindIndex(option => option.Id == shownId));
        }
        var viewBox = StageCombo("OptionViewBox", current?.Options.Select(option => option.Name) ?? [], shownIndex);
        var applyView = StageButton("OptionViewApply", GetText("Options.ApplyView"), (_, _) =>
        {
            if (ActiveProjectView() is not { } active) { StatusText.Text = GetText("Status.NoViewForCategories"); return; }
            if (current is null || viewBox.SelectedIndex < 0) { StatusText.Text = GetText("Status.OptionNoSet"); return; }
            RunStagesEdit(() => _session.SetViewOption(active.Id, current.Id, current.Options[viewBox.SelectedIndex].Id), "Status.OptionsChanged");
        });

        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText("Options.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(new TextBlock { Text = GetText("Options.Sets") });
        layout.Children.Add(setList);
        layout.Children.Add(setButtons);
        layout.Children.Add(new TextBlock { Text = GetText("Options.Options") });
        layout.Children.Add(optionList);
        layout.Children.Add(optionButtons);
        layout.Children.Add(elements);
        layout.Children.Add(new TextBlock { Text = FormatText("Options.ViewGroup", view?.Name ?? "-"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
        layout.Children.Add(viewBox);
        layout.Children.Add(applyView);
        layout.Children.Add(StageButton("OptionClose", GetText("Phases.Close"), (_, _) => _optionsWindow?.Close()));
        _optionsWindow.Content = new ScrollViewer { Content = layout, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
