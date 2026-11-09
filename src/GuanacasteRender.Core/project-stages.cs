namespace Guanacaste3D.Core;

/// <summary>One stage of the building's life, such as the existing building or the new construction. Phases are ordered.</summary>
public sealed record ProjectPhase(Guid Id, string Name);

/// <summary>
/// The phases in which an element was created and demolished. A null created phase means the first phase; a null
/// demolished phase means it is never demolished.
/// </summary>
public sealed record PhaseAssignment(Guid ElementId, Guid? CreatedPhaseId, Guid? DemolishedPhaseId);

/// <summary>Which of the elements that exist in a view's phase the view shows.</summary>
public enum PhaseFilter
{
    /// <summary>Everything built by the view's phase and not yet gone: existing, new and being demolished.</summary>
    ShowAll,

    /// <summary>Only what is created in the view's phase.</summary>
    ShowNew,

    /// <summary>Only what was built in an earlier phase and still stands after the view's phase.</summary>
    ShowExisting,

    /// <summary>Only what is demolished in the view's phase.</summary>
    ShowDemolished
}

/// <summary>An alternative of a design option set. The set's primary option is the one views show unless told otherwise.</summary>
public sealed record DesignOption(Guid Id, string Name);

/// <summary>A group of mutually exclusive alternatives for one part of the building.</summary>
public sealed record DesignOptionSet(Guid Id, string Name, List<DesignOption> Options, Guid PrimaryOptionId);

/// <summary>Places an element in one design option; elements with no assignment belong to the main model and always show.</summary>
public sealed record OptionAssignment(Guid ElementId, Guid OptionId);

/// <summary>The option of a set that one view shows in place of the set's primary one.</summary>
public sealed record OptionChoice(Guid SetId, Guid OptionId);

/// <summary>Decides which elements a view shows from their phases and design options.</summary>
public static class ProjectStages
{
    public const int MaximumPhases = 50;
    public const int MaximumOptionSets = 20;
    public const int MaximumOptionsPerSet = 20;

    /// <summary>The position of the phase a view is drawn at: its own, or the latest one when it has none.</summary>
    public static int ViewPhaseIndex(BuildingDocument document, ProjectView view)
    {
        var index = view.PhaseId is { } id ? document.Phases.FindIndex(phase => phase.Id == id) : -1;
        return index >= 0 ? index : document.Phases.Count - 1;
    }

    /// <summary>Whether phases can hide anything in this view.</summary>
    public static bool PhasingActive(BuildingDocument document, ProjectView view) =>
        document.Phases.Count > 0 && (view.PhaseFilter != PhaseFilter.ShowAll || document.PhaseAssignments.Count > 0);

    /// <summary>The ids of the elements the view's phase and filter hide.</summary>
    public static HashSet<Guid> HiddenByPhase(BuildingDocument document, ProjectView view, IEnumerable<Guid> elementIds)
    {
        var hidden = new HashSet<Guid>();
        if (!PhasingActive(document, view)) return hidden;
        var viewIndex = ViewPhaseIndex(document, view);
        var positions = document.Phases.Select((phase, index) => (phase.Id, index)).ToDictionary(entry => entry.Id, entry => entry.index);
        var assignments = document.PhaseAssignments.GroupBy(item => item.ElementId).ToDictionary(group => group.Key, group => group.Last());
        foreach (var id in elementIds)
        {
            var created = 0;
            var demolished = int.MaxValue;
            if (assignments.TryGetValue(id, out var assignment))
            {
                if (assignment.CreatedPhaseId is { } createdId && positions.TryGetValue(createdId, out var createdIndex)) created = createdIndex;
                if (assignment.DemolishedPhaseId is { } demolishedId && positions.TryGetValue(demolishedId, out var demolishedIndex)) demolished = demolishedIndex;
            }
            var shown = view.PhaseFilter switch
            {
                PhaseFilter.ShowNew => created == viewIndex,
                PhaseFilter.ShowExisting => created < viewIndex && demolished > viewIndex,
                PhaseFilter.ShowDemolished => demolished == viewIndex,
                _ => created <= viewIndex && demolished >= viewIndex
            };
            if (!shown) hidden.Add(id);
        }
        return hidden;
    }

    /// <summary>The ids of the elements placed in a design option that this view is not showing.</summary>
    public static HashSet<Guid> HiddenByOption(BuildingDocument document, ProjectView view)
    {
        var hidden = new HashSet<Guid>();
        if (document.OptionAssignments.Count == 0) return hidden;
        var owner = new Dictionary<Guid, DesignOptionSet>();
        foreach (var set in document.OptionSets)
            foreach (var option in set.Options) owner[option.Id] = set;
        foreach (var assignment in document.OptionAssignments)
        {
            if (!owner.TryGetValue(assignment.OptionId, out var set)) continue;
            var shown = view.OptionChoices.FirstOrDefault(choice => choice.SetId == set.Id)?.OptionId ?? set.PrimaryOptionId;
            if (shown != assignment.OptionId) hidden.Add(assignment.ElementId);
        }
        return hidden;
    }

    /// <summary>Rejects phases and options that are unnamed, repeated, out of bounds or that point at nothing.</summary>
    public static void Validate(BuildingDocument document)
    {
        if (document.Phases is null || document.PhaseAssignments is null || document.OptionSets is null || document.OptionAssignments is null ||
            document.Phases.Count > MaximumPhases || document.OptionSets.Count > MaximumOptionSets ||
            document.PhaseAssignments.Count > 200000 || document.OptionAssignments.Count > 200000 ||
            document.Phases.Any(phase => phase is null || phase.Id == Guid.Empty || string.IsNullOrWhiteSpace(phase.Name) || phase.Name.Length > 120) ||
            document.Phases.Select(phase => phase.Id).Distinct().Count() != document.Phases.Count ||
            document.Phases.Select(phase => phase.Name.Trim().ToUpperInvariant()).Distinct().Count() != document.Phases.Count ||
            document.OptionSets.Any(set => set is null || set.Id == Guid.Empty || string.IsNullOrWhiteSpace(set.Name) || set.Name.Length > 120 ||
                set.Options is null || set.Options.Count is < 1 or > MaximumOptionsPerSet ||
                set.Options.Any(option => option is null || option.Id == Guid.Empty || string.IsNullOrWhiteSpace(option.Name) || option.Name.Length > 120) ||
                set.Options.Select(option => option.Id).Distinct().Count() != set.Options.Count ||
                set.Options.All(option => option.Id != set.PrimaryOptionId)) ||
            document.OptionSets.Select(set => set.Id).Distinct().Count() != document.OptionSets.Count ||
            document.Views.Any(view => view.OptionChoices is null))
            throw new InvalidDataException("Invalid project phases or design options.");
    }
}

public sealed partial class DocumentSession
{
    private static string CleanName(string name)
    {
        var clean = name?.Trim() ?? string.Empty;
        if (clean.Length == 0 || clean.Length > 120) throw new InvalidDataException("The name is empty or too long.");
        return clean;
    }

    private bool IsStagedElement(Guid id) =>
        Document.Walls.Any(item => item.Id == id) || Document.Openings.Any(item => item.Id == id) ||
        Document.Floors.Any(item => item.Id == id) || Document.Columns.Any(item => item.Id == id) ||
        Document.StructuralElements.Any(item => item.Id == id);

    // ------------------------------------------------------------------ phases

    /// <summary>Adds a phase after the last one, as one undoable step.</summary>
    public Guid AddPhase(string name)
    {
        var clean = CleanName(name);
        if (Document.Phases.Count >= ProjectStages.MaximumPhases) throw new InvalidDataException("A project cannot have so many phases.");
        if (Document.Phases.Any(phase => phase.Name.Equals(clean, StringComparison.CurrentCultureIgnoreCase)))
            throw new InvalidDataException("A phase with that name already exists.");
        var id = Guid.NewGuid();
        Commit("Transaction.AddPhase", document => document.Phases.Add(new ProjectPhase(id, clean)));
        return id;
    }

    /// <summary>Adds the given phases, in order, to a project that has none, so a project starts with an existing and a new phase.</summary>
    public void EnsureDefaultPhases(IReadOnlyList<string> names)
    {
        if (Document.Phases.Count > 0) return;
        var clean = names.Select(CleanName).ToList();
        if (clean.Select(name => name.ToUpperInvariant()).Distinct().Count() != clean.Count)
            throw new InvalidDataException("A phase with that name already exists.");
        Commit("Transaction.AddPhase", document =>
        {
            foreach (var name in clean) document.Phases.Add(new ProjectPhase(Guid.NewGuid(), name));
        });
    }

    public void RenamePhase(Guid id, string name)
    {
        var clean = CleanName(name);
        if (Document.Phases.All(phase => phase.Id != id)) throw new InvalidDataException("The phase does not exist.");
        if (Document.Phases.Any(phase => phase.Id != id && phase.Name.Equals(clean, StringComparison.CurrentCultureIgnoreCase)))
            throw new InvalidDataException("A phase with that name already exists.");
        Commit("Transaction.RenamePhase", document =>
        {
            var index = document.Phases.FindIndex(phase => phase.Id == id);
            document.Phases[index] = document.Phases[index] with { Name = clean };
        });
    }

    /// <summary>Moves a phase earlier (negative) or later (positive) in the order of the building's life.</summary>
    public void MovePhase(Guid id, int steps)
    {
        var index = Document.Phases.FindIndex(phase => phase.Id == id);
        if (index < 0) throw new InvalidDataException("The phase does not exist.");
        var target = Math.Clamp(index + steps, 0, Document.Phases.Count - 1);
        if (target == index) return;
        Commit("Transaction.MovePhase", document =>
        {
            var phase = document.Phases[index];
            document.Phases.RemoveAt(index);
            document.Phases.Insert(target, phase);
        });
    }

    /// <summary>Removes a phase. Elements created in it go back to the first phase; those demolished in it are no longer demolished.</summary>
    public void RemovePhase(Guid id)
    {
        if (Document.Phases.All(phase => phase.Id != id)) throw new InvalidDataException("The phase does not exist.");
        Commit("Transaction.RemovePhase", document =>
        {
            document.Phases.RemoveAll(phase => phase.Id == id);
            for (var index = document.PhaseAssignments.Count - 1; index >= 0; index--)
            {
                var item = document.PhaseAssignments[index];
                var updated = item with
                {
                    CreatedPhaseId = item.CreatedPhaseId == id ? null : item.CreatedPhaseId,
                    DemolishedPhaseId = item.DemolishedPhaseId == id ? null : item.DemolishedPhaseId
                };
                if (updated.CreatedPhaseId is null && updated.DemolishedPhaseId is null) document.PhaseAssignments.RemoveAt(index);
                else document.PhaseAssignments[index] = updated;
            }
            for (var index = 0; index < document.Views.Count; index++)
                if (document.Views[index].PhaseId == id) document.Views[index] = document.Views[index] with { PhaseId = null };
        });
    }

    /// <summary>Sets the phases in which elements are created and demolished; a created phase cannot come after the demolished one.</summary>
    public void AssignPhases(IReadOnlyCollection<Guid> elementIds, Guid? createdPhaseId, Guid? demolishedPhaseId)
    {
        ArgumentNullException.ThrowIfNull(elementIds);
        if (elementIds.Count == 0 || elementIds.Any(id => !IsStagedElement(id))) throw new InvalidDataException("The element does not exist.");
        int IndexOf(Guid? phaseId) => phaseId is null ? -1 : Document.Phases.FindIndex(phase => phase.Id == phaseId);
        if ((createdPhaseId is not null && IndexOf(createdPhaseId) < 0) || (demolishedPhaseId is not null && IndexOf(demolishedPhaseId) < 0))
            throw new InvalidDataException("The phase does not exist.");
        if (demolishedPhaseId is not null && IndexOf(demolishedPhaseId) < Math.Max(0, IndexOf(createdPhaseId)))
            throw new InvalidDataException("An element cannot be demolished before it is created.");
        var ids = elementIds.Distinct().ToArray();
        Commit("Transaction.AssignPhases", document =>
        {
            document.PhaseAssignments.RemoveAll(item => ids.Contains(item.ElementId));
            if (createdPhaseId is null && demolishedPhaseId is null) return;
            foreach (var id in ids) document.PhaseAssignments.Add(new PhaseAssignment(id, createdPhaseId, demolishedPhaseId));
        });
    }

    /// <summary>Sets the phase a view is drawn at (null for the latest) and which elements of it the view shows.</summary>
    public void SetViewPhase(Guid viewId, Guid? phaseId, PhaseFilter filter)
    {
        if (!Enum.IsDefined(filter)) throw new InvalidDataException("The phase filter is invalid.");
        if (phaseId is not null && Document.Phases.All(phase => phase.Id != phaseId)) throw new InvalidDataException("The phase does not exist.");
        if (Document.Views.All(view => view.Id != viewId)) return;
        Commit("Transaction.SetViewPhase", document =>
        {
            var index = document.Views.FindIndex(view => view.Id == viewId);
            document.Views[index] = document.Views[index] with { PhaseId = phaseId, PhaseFilter = filter };
        });
    }

    // ------------------------------------------------------------------ design options

    /// <summary>Adds an option set with its first, primary option.</summary>
    public Guid AddOptionSet(string name, string primaryOptionName)
    {
        var clean = CleanName(name);
        var option = CleanName(primaryOptionName);
        if (Document.OptionSets.Count >= ProjectStages.MaximumOptionSets) throw new InvalidDataException("A project cannot have so many option sets.");
        if (Document.OptionSets.Any(set => set.Name.Equals(clean, StringComparison.CurrentCultureIgnoreCase)))
            throw new InvalidDataException("An option set with that name already exists.");
        var id = Guid.NewGuid();
        var optionId = Guid.NewGuid();
        Commit("Transaction.AddOptionSet", document =>
            document.OptionSets.Add(new DesignOptionSet(id, clean, [new DesignOption(optionId, option)], optionId)));
        return id;
    }

    /// <summary>Adds an alternative to a set.</summary>
    public Guid AddDesignOption(Guid setId, string name)
    {
        var clean = CleanName(name);
        var set = Document.OptionSets.FirstOrDefault(item => item.Id == setId) ?? throw new InvalidDataException("The option set does not exist.");
        if (set.Options.Count >= ProjectStages.MaximumOptionsPerSet) throw new InvalidDataException("A set cannot have so many options.");
        if (set.Options.Any(option => option.Name.Equals(clean, StringComparison.CurrentCultureIgnoreCase)))
            throw new InvalidDataException("An option with that name already exists.");
        var id = Guid.NewGuid();
        Commit("Transaction.AddDesignOption", document =>
        {
            var index = document.OptionSets.FindIndex(item => item.Id == setId);
            document.OptionSets[index] = document.OptionSets[index] with
            {
                Options = [.. document.OptionSets[index].Options, new DesignOption(id, clean)]
            };
        });
        return id;
    }

    /// <summary>Makes an option the one views show when they have not chosen another.</summary>
    public void SetPrimaryOption(Guid setId, Guid optionId)
    {
        var set = Document.OptionSets.FirstOrDefault(item => item.Id == setId) ?? throw new InvalidDataException("The option set does not exist.");
        if (set.Options.All(option => option.Id != optionId)) throw new InvalidDataException("The option does not exist.");
        Commit("Transaction.SetPrimaryOption", document =>
        {
            var index = document.OptionSets.FindIndex(item => item.Id == setId);
            document.OptionSets[index] = document.OptionSets[index] with { PrimaryOptionId = optionId };
        });
    }

    /// <summary>Removes an option that is not the primary one; the elements in it return to the main model.</summary>
    public void RemoveDesignOption(Guid setId, Guid optionId)
    {
        var set = Document.OptionSets.FirstOrDefault(item => item.Id == setId) ?? throw new InvalidDataException("The option set does not exist.");
        if (set.Options.All(option => option.Id != optionId)) throw new InvalidDataException("The option does not exist.");
        if (set.PrimaryOptionId == optionId) throw new InvalidDataException("The primary option cannot be removed; make another one primary first.");
        Commit("Transaction.RemoveDesignOption", document =>
        {
            var index = document.OptionSets.FindIndex(item => item.Id == setId);
            document.OptionSets[index] = document.OptionSets[index] with
            {
                Options = document.OptionSets[index].Options.Where(option => option.Id != optionId).ToList()
            };
            document.OptionAssignments.RemoveAll(item => item.OptionId == optionId);
            for (var view = 0; view < document.Views.Count; view++)
                document.Views[view] = document.Views[view] with { OptionChoices = document.Views[view].OptionChoices.Where(choice => choice.OptionId != optionId).ToList() };
        });
    }

    /// <summary>Removes a whole set; its elements return to the main model.</summary>
    public void RemoveOptionSet(Guid setId)
    {
        var set = Document.OptionSets.FirstOrDefault(item => item.Id == setId) ?? throw new InvalidDataException("The option set does not exist.");
        var optionIds = set.Options.Select(option => option.Id).ToHashSet();
        Commit("Transaction.RemoveOptionSet", document =>
        {
            document.OptionSets.RemoveAll(item => item.Id == setId);
            document.OptionAssignments.RemoveAll(item => optionIds.Contains(item.OptionId));
            for (var view = 0; view < document.Views.Count; view++)
                document.Views[view] = document.Views[view] with { OptionChoices = document.Views[view].OptionChoices.Where(choice => choice.SetId != setId).ToList() };
        });
    }

    /// <summary>Places elements in an option, or (null) back in the main model. An element is in one option at a time.</summary>
    public void AssignToOption(IReadOnlyCollection<Guid> elementIds, Guid? optionId)
    {
        ArgumentNullException.ThrowIfNull(elementIds);
        if (elementIds.Count == 0 || elementIds.Any(id => !IsStagedElement(id))) throw new InvalidDataException("The element does not exist.");
        if (optionId is not null && Document.OptionSets.All(set => set.Options.All(option => option.Id != optionId)))
            throw new InvalidDataException("The option does not exist.");
        var ids = elementIds.Distinct().ToArray();
        Commit("Transaction.AssignToOption", document =>
        {
            document.OptionAssignments.RemoveAll(item => ids.Contains(item.ElementId));
            if (optionId is null) return;
            foreach (var id in ids) document.OptionAssignments.Add(new OptionAssignment(id, optionId.Value));
        });
    }

    /// <summary>Chooses the option of a set that a view shows.</summary>
    public void SetViewOption(Guid viewId, Guid setId, Guid optionId)
    {
        var set = Document.OptionSets.FirstOrDefault(item => item.Id == setId) ?? throw new InvalidDataException("The option set does not exist.");
        if (set.Options.All(option => option.Id != optionId)) throw new InvalidDataException("The option does not exist.");
        if (Document.Views.All(view => view.Id != viewId)) return;
        Commit("Transaction.SetViewOption", document =>
        {
            var index = document.Views.FindIndex(view => view.Id == viewId);
            var choices = document.Views[index].OptionChoices.Where(choice => choice.SetId != setId).ToList();
            if (optionId != set.PrimaryOptionId) choices.Add(new OptionChoice(setId, optionId));
            document.Views[index] = document.Views[index] with { OptionChoices = choices };
        });
    }
}
