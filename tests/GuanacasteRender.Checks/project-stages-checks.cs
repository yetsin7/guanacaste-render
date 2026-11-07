using Guanacaste3D.Core;

/// <summary>Checks phases and design options against an independent count, with random assignments on the breeze-block house.</summary>
static class ProjectStagesChecks
{
    private static bool Throws(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return true; }
        return false;
    }

    public static void Run(Action<bool, string> check)
    {
        Phases(check);
        Options(check);
    }

    private static ProjectView View(DocumentSession session, ProjectViewKind kind) => session.Document.Views.First(view => view.Kind == kind);

    private static HashSet<Guid> ShownWalls(DocumentSession session, Guid viewId) =>
        ViewVisibility.Apply(session.Document, session.Document.Views.First(view => view.Id == viewId)).Walls.Select(wall => wall.Id).ToHashSet();

    private static void Phases(Action<bool, string> check)
    {
        var session = new DocumentSession(BreezeBlockHouseShowcase.Create());
        var model = View(session, ProjectViewKind.Model3D);
        var walls = session.Document.Walls.Select(wall => wall.Id).ToArray();

        check(ShownWalls(session, model.Id).Count == walls.Length, "A project without phases shows every wall");
        session.EnsureDefaultPhases(["Existente", "Nueva construcción", "Demolición"]);
        var phases = session.Document.Phases.Select(phase => phase.Id).ToArray();
        check(phases.Length == 3 && ShownWalls(session, model.Id).Count == walls.Length,
            "Three phases with nothing assigned still show every wall at the latest phase");

        // Random assignments, checked against a count made from the wall list rather than from the code under test.
        var random = new Random(20261103);
        var created = new Dictionary<Guid, int>();
        var demolished = new Dictionary<Guid, int>();
        foreach (var wall in walls)
        {
            var make = random.Next(3);
            var demolish = random.Next(4);
            if (demolish < make) demolish = make;
            session.AssignPhases([wall], make == 0 ? null : phases[make], demolish == 3 ? null : phases[demolish]);
            created[wall] = make;
            demolished[wall] = demolish == 3 ? int.MaxValue : demolish;
        }
        var mismatches = new List<string>();
        for (var phase = 0; phase < 3; phase++)
            foreach (var filter in Enum.GetValues<PhaseFilter>())
            {
                session.SetViewPhase(model.Id, phases[phase], filter);
                var expected = walls.Where(wall => filter switch
                {
                    PhaseFilter.ShowNew => created[wall] == phase,
                    PhaseFilter.ShowExisting => created[wall] < phase && demolished[wall] > phase,
                    PhaseFilter.ShowDemolished => demolished[wall] == phase,
                    _ => created[wall] <= phase && demolished[wall] >= phase
                }).ToHashSet();
                if (!ShownWalls(session, model.Id).SetEquals(expected)) mismatches.Add($"{phase}/{filter}");
            }
        check(mismatches.Count == 0, "Every phase and filter shows exactly the walls an independent count says it should (12 combinations)" +
            (mismatches.Count == 0 ? string.Empty : ": " + string.Join(", ", mismatches)));

        session.SetViewPhase(model.Id, phases[1], PhaseFilter.ShowAll);
        var plan = View(session, ProjectViewKind.Plan);
        var atLatest = walls.Count(wall => demolished[wall] >= 2);
        check(ShownWalls(session, plan.Id).Count == atLatest && atLatest < walls.Length && session.Document.Walls.Count == walls.Length,
            $"A view with no phase of its own draws the latest phase ({atLatest} walls) while its filter and the model itself are untouched");

        var opening = session.Document.Openings[0];
        var host = opening.WallId;
        session.AssignPhases([host], phases[2], null);
        check(!ShownWalls(session, model.Id).Contains(host) &&
              ViewVisibility.Apply(session.Document, session.Document.Views.First(view => view.Id == model.Id)).Openings.All(item => item.WallId != host),
            "A wall created after the view's phase is not shown, and its doors and windows go with it");

        check(Throws(() => session.AssignPhases([host], phases[2], phases[1])) &&
              Throws(() => session.AssignPhases([Guid.NewGuid()], phases[1], null)) &&
              Throws(() => session.AssignPhases([host], Guid.NewGuid(), null)) &&
              Throws(() => session.AddPhase("existente")) && Throws(() => session.AddPhase("   ")),
            "Demolishing before creating, unknown elements or phases, repeated and empty names are refused");

        session.MovePhase(phases[2], -2);
        check(session.Document.Phases[0].Id == phases[2], "A phase moves earlier in the order");
        session.Undo();
        session.RemovePhase(phases[2]);
        check(session.Document.Phases.Count == 2 && session.Document.PhaseAssignments.All(item => item.CreatedPhaseId != phases[2] && item.DemolishedPhaseId != phases[2]),
            "Removing a phase takes it out of every assignment");

        var file = Path.Combine(Path.GetTempPath(), $"phases-{Guid.NewGuid():N}.g3d");
        ProjectFile.Save(file, session.Document);
        var reopened = ProjectFile.Load(file);
        File.Delete(file);
        check(reopened.Phases.Count == 2 && reopened.PhaseAssignments.Count == session.Document.PhaseAssignments.Count,
            "Phases and their assignments survive saving and opening the project");
    }

    private static void Options(Action<bool, string> check)
    {
        var session = new DocumentSession(BreezeBlockHouseShowcase.Create());
        var model = View(session, ProjectViewKind.Model3D);
        var walls = session.Document.Walls.Select(wall => wall.Id).ToArray();
        var set = session.AddOptionSet("Fachada", "Cobogó");
        var primary = session.Document.OptionSets[0].PrimaryOptionId;
        var alternative = session.AddDesignOption(set, "Muro liso");
        var inPrimary = walls.Take(6).ToArray();
        var inAlternative = walls.Skip(6).Take(5).ToArray();
        session.AssignToOption(inPrimary, primary);
        session.AssignToOption(inAlternative, alternative);

        var main = walls.Length - inPrimary.Length - inAlternative.Length;
        check(ShownWalls(session, model.Id).Count == walls.Length - inAlternative.Length,
            $"By default a view shows the main model ({main} walls) and the primary option ({inPrimary.Length}), not the alternative ({inAlternative.Length})");
        session.SetViewOption(model.Id, set, alternative);
        var shown = ShownWalls(session, model.Id);
        check(shown.Count == walls.Length - inPrimary.Length && inAlternative.All(shown.Contains) && !inPrimary.Any(shown.Contains),
            "Choosing the alternative for the view swaps the two groups of walls");
        check(ShownWalls(session, View(session, ProjectViewKind.Plan).Id).Count == walls.Length - inAlternative.Length &&
              session.Document.Walls.Count == walls.Length,
            "Another view keeps showing the primary option, and the model keeps every wall");

        session.SetPrimaryOption(set, alternative);
        check(ShownWalls(session, View(session, ProjectViewKind.Plan).Id).Count == walls.Length - inPrimary.Length,
            "Making the alternative primary changes what the other views show");
        check(Throws(() => session.RemoveDesignOption(set, alternative)) && Throws(() => session.AddDesignOption(set, "muro LISO")) &&
              Throws(() => session.AddOptionSet("fachada", "x")) && Throws(() => session.AssignToOption([Guid.NewGuid()], primary)) &&
              Throws(() => session.SetViewOption(model.Id, set, Guid.NewGuid())),
            "The primary option cannot be removed; repeated names, unknown elements and unknown options are refused");

        var file = Path.Combine(Path.GetTempPath(), $"options-{Guid.NewGuid():N}.g3d");
        ProjectFile.Save(file, session.Document);
        var reopened = ProjectFile.Load(file);
        File.Delete(file);
        check(reopened.OptionSets.Count == 1 && reopened.OptionSets[0].Options.Count == 2 && reopened.OptionAssignments.Count == 11,
            "Option sets and assignments survive saving and opening the project");

        session.RemoveDesignOption(set, primary);
        check(session.Document.OptionAssignments.All(item => item.OptionId == alternative) && ShownWalls(session, model.Id).Count == walls.Length,
            "Removing an option sends its walls back to the main model, so the view shows them again");
        session.RemoveOptionSet(set);
        check(session.Document.OptionSets.Count == 0 && session.Document.OptionAssignments.Count == 0 && ShownWalls(session, model.Id).Count == walls.Length,
            "Removing the set leaves every wall in the main model");
        session.Undo();
        check(session.Document.OptionSets.Count == 1, "Undo brings the set back");
    }
}
