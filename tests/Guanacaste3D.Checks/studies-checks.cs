using Guanacaste3D.Core;

/// <summary>Checks the design studies (shapes of a mass tried from a seed and ranked) and the automation scripts.</summary>
static class StudiesChecks
{
    private static bool Near(double first, double second, double tolerance = 1e-6) => Math.Abs(first - second) < tolerance;

    private static bool Throws(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return true; }
        return false;
    }

    public static void Run(Action<bool, string> check)
    {
        Studies(check);
        Scripts(check);
        Nesting(check);
    }

    private static (DocumentSession Session, Guid Mass) House()
    {
        var session = new DocumentSession(BuildingDocument.Create("Estudio"));
        var level = session.Document.Levels[0].Id;
        var mass = session.AddStructuralElement(level, StructuralElementKind.Mass, "Masa", new Point2(0, 0), new Point2(10, 6));
        session.UpdateStructuralElement(mass, "Masa", session.Document.StructuralElements.First(item => item.Id == mass).Width, 3, 0, ElementStyle.Default);
        return (session, mass);
    }

    private static Guid Add(DocumentSession session, Guid mass, StudyGoal goal, string name = "Prueba", double target = 0, double areaMin = 0, double areaMax = 0, int count = 300, int seed = 7) =>
        session.AddStudy(name, mass, 4, 20, 3, 14, 3, 12, goal, target, areaMin, areaMax, count, seed);

    private static void Studies(Action<bool, string> check)
    {
        var (session, mass) = House();
        var id = Add(session, mass, StudyGoal.Compactness);
        var study = session.Document.Studies.Single();
        var first = DesignStudies.Evaluate(study);
        var again = DesignStudies.Evaluate(study);
        var other = DesignStudies.Evaluate(study with { Seed = 8 });
        check(first.Count == 300 && first.SequenceEqual(again) && !first.Select(item => (item.Width, item.Depth)).SequenceEqual(other.Select(item => (item.Width, item.Depth))),
            "A study tries the same 300 shapes every time, and another seed tries others");
        check(first.All(item => item.Width >= 4 && item.Width <= 20 && item.Depth >= 3 && item.Depth <= 14 && item.Height >= 3 && item.Height <= 12 &&
                                Near(item.Width / 0.05, Math.Round(item.Width / 0.05), 1e-6) && Near(item.Area, item.Width * item.Depth) && Near(item.Volume, item.Area * item.Height)) &&
              first.Select(item => item.Rank).SequenceEqual(Enumerable.Range(1, 300)), "Every shape is inside the ranges, in steps of 5 cm, with its measures worked out, and ranked 1 to 300");
        var bestCompact = first.Max(item => item.Compactness);
        check(Near(first[0].Compactness, bestCompact) && first.Zip(first.Skip(1)).All(pair => pair.First.Cost <= pair.Second.Cost + 1e-12),
            "The most compact goal ranks the most compact shape first and never ranks a worse one above a better one");

        var facade = Add(session, mass, StudyGoal.FacadeToVolume, "Fachada");
        var ranked = DesignStudies.Evaluate(session.Document.Studies.Single(item => item.Id == facade));
        var bestRatio = ranked.Min(item => item.FacadeArea / item.Volume);
        check(Near(ranked[0].FacadeArea / ranked[0].Volume, bestRatio, 1e-9) && ranked.Max(item => item.Width * item.Depth) >= ranked[0].Area,
            "The facade-to-volume goal ranks first the shape with the least wall for the volume it encloses");

        var target = Add(session, mass, StudyGoal.TargetArea, "Meta", target: 80);
        var close = DesignStudies.Evaluate(session.Document.Studies.Single(item => item.Id == target));
        check(Near(Math.Abs(close[0].Area - 80), close.Min(item => Math.Abs(item.Area - 80)), 1e-9) && Math.Abs(close[0].Area - 80) < 3, $"The target-area goal ranks first the plan closest to 80 m²: {close[0].Area:0.0}");

        var limited = Add(session, mass, StudyGoal.Compactness, "Limite", areaMin: 40, areaMax: 60);
        var bounded = DesignStudies.Evaluate(session.Document.Studies.Single(item => item.Id == limited));
        var feasible = bounded.Count(item => item.Feasible);
        check(feasible > 0 && feasible < bounded.Count && bounded.Take(feasible).All(item => item.Feasible && item.Area >= 40 - 1e-9 && item.Area <= 60 + 1e-9) && bounded.Skip(feasible).All(item => !item.Feasible),
            $"Shapes outside the allowed 40 to 60 m² rank after every shape inside it ({feasible} of {bounded.Count} fit)");

        check(Throws(() => session.AddStudy("", mass, 4, 20, 3, 14, 3, 12, StudyGoal.Compactness, 0, 0, 0, 10, 1)) && Throws(() => session.AddStudy("Mal", mass, 20, 4, 3, 14, 3, 12, StudyGoal.Compactness, 0, 0, 0, 10, 1)) &&
              Throws(() => session.AddStudy("Mal", mass, 4, 20, 3, 14, 3, 12, StudyGoal.Compactness, 0, 0, 0, 0, 1)) && Throws(() => session.AddStudy("Mal", mass, 4, 20, 3, 14, 3, 12, StudyGoal.Compactness, 0, 0, 0, 5000, 1)) &&
              Throws(() => session.AddStudy("Mal", mass, 4, double.NaN, 3, 14, 3, 12, StudyGoal.Compactness, 0, 0, 0, 10, 1)) && Throws(() => session.AddStudy("Mal", mass, 4, 20, 3, 14, 3, 12, StudyGoal.TargetArea, 0, 0, 0, 10, 1)) &&
              Throws(() => session.AddStudy("Mal", Guid.NewGuid(), 4, 20, 3, 14, 3, 12, StudyGoal.Compactness, 0, 0, 0, 10, 1)) && Throws(() => session.AddStudy("prueba", mass, 4, 20, 3, 14, 3, 12, StudyGoal.Compactness, 0, 0, 0, 10, 1)) &&
              session.Document.Studies.Count == 4, "Studies with no name, backward ranges, no shapes, too many, a number that is not one, a goal without a target, a missing mass or a repeated name are refused");

        // Applying an outcome.
        var before = session.Document.StructuralElements.First(item => item.Id == mass);
        var centre = (X: (before.Footprint.Max(p => p.X) + before.Footprint.Min(p => p.X)) / 2, Y: (before.Footprint.Max(p => p.Y) + before.Footprint.Min(p => p.Y)) / 2);
        session.ApplyStudyOutcome(id, 1);
        var after = session.Document.StructuralElements.First(item => item.Id == mass);
        var best = first[0];
        var sizeX = after.Footprint.Max(p => p.X) - after.Footprint.Min(p => p.X);
        var sizeY = after.Footprint.Max(p => p.Y) - after.Footprint.Min(p => p.Y);
        check(Near(sizeX, best.Width) && Near(sizeY, best.Depth) && Near(after.Height, best.Height) && Near((after.Footprint.Max(p => p.X) + after.Footprint.Min(p => p.X)) / 2, centre.X) &&
              Near((after.Footprint.Max(p => p.Y) + after.Footprint.Min(p => p.Y)) / 2, centre.Y), "Applying the first outcome gives the mass its size and keeps its centre");
        session.Undo();
        check(Near(session.Document.StructuralElements.First(item => item.Id == mass).Height, 3) && Throws(() => session.ApplyStudyOutcome(id, 99999)) && Throws(() => session.ApplyStudyOutcome(Guid.NewGuid(), 1)), "Undo gives the mass its old shape back, and an outcome that does not exist is refused");

        // Options.
        var sets = session.Document.OptionSets.Count;
        var masses = session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.Mass);
        var made = session.AddStudyOutcomesAsOptions(id, 3);
        var optionMasses = session.Document.StructuralElements.Where(item => item.Kind == StructuralElementKind.Mass).ToList();
        check(made == 3 && session.Document.OptionSets.Count == sets + 1 && optionMasses.Count == masses + 3 && session.Document.OptionSets.Last().Options.Count >= 4, "The three best outcomes become three more options next to the original");
        session.Undo();
        check(session.Document.OptionSets.Count == sets && session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.Mass) == masses, "Making the options is one undoable step");
        var impossible = Add(session, mass, StudyGoal.Compactness, "Imposible", areaMin: 100000, areaMax: 200000);
        check(Throws(() => session.AddStudyOutcomesAsOptions(impossible, 3)) && Throws(() => session.AddStudyOutcomesAsOptions(id, 0)) && Throws(() => session.AddStudyOutcomesAsOptions(id, 13)), "No options are made when no shape fits or the count is out of range");

        // Studies belong to their mass.
        var json = ProjectFile.SerializeDocument(session.Document);
        var back = ProjectFile.DeserializeDocument(json);
        check(back.Studies.Count == session.Document.Studies.Count && back.Studies.Select(item => item.Seed).SequenceEqual(session.Document.Studies.Select(item => item.Seed)), "Studies are saved with the project and read back the same");
        session.RemoveStructuralElement(mass);
        check(session.Document.Studies.Count == 0, "Deleting a mass takes its studies with it");
        session.Undo();
        check(session.Document.Studies.Count == 5 && session.Document.StructuralElements.Any(item => item.Id == mass), "Undoing the deletion brings back the mass and its studies");
        session.RemoveStudy(impossible);
        check(session.Document.Studies.Count == 4 && Throws(() => session.RemoveStudy(impossible)), "A study can be removed, once");
    }

    private static void Scripts(Action<bool, string> check)
    {
        var steps = AutomationScripts.Parse("# prueba\n\nRebar | 3 0.016 abajo | 0.05\n  Cover|0.05  \nZone\n");
        check(steps.Count == 3 && steps[0].CommandId == "Rebar" && steps[0].Answers.SequenceEqual(["3 0.016 abajo", "0.05"]) && steps[1].CommandId == "Cover" && steps[1].Answers.SequenceEqual(["0.05"]) && steps[2].Answers.Count == 0,
            "A script is read one step per line, with its answers after the bars, ignoring blank lines and comments");
        check(AutomationScripts.Parse(AutomationScripts.ToText(steps)).Zip(steps).All(pair => pair.First.CommandId == pair.Second.CommandId && pair.First.Answers.SequenceEqual(pair.Second.Answers)), "Writing a script and reading it back gives the same steps");
        check(Throws(() => AutomationScripts.Parse("")) && Throws(() => AutomationScripts.Parse("# nada\n\n")) && Throws(() => AutomationScripts.Parse("1mal")) && Throws(() => AutomationScripts.Parse("dos palabras | x")) &&
              Throws(() => AutomationScripts.Parse("Cmd | " + string.Join(" | ", Enumerable.Range(0, 21).Select(number => "a" + number)))) && Throws(() => AutomationScripts.Parse("Cmd | " + new string('x', 501))) &&
              Throws(() => AutomationScripts.Parse(string.Join("\n", Enumerable.Repeat("Zone", 301)))) && Throws(() => AutomationScripts.Parse(new string('A', 65))),
            "Empty scripts, commands that are not names, too many answers, a long answer, too many steps and a long command are refused");

        var session = new DocumentSession(BuildingDocument.Create("Guiones"));
        var id = session.SaveAutomation("Armadura", "Rebar | 3 0.016 abajo\nCover | 0.05");
        check(session.Document.Automations.Single().Steps.Count == 2 && session.Document.Automations.Single().Id == id, "A script is saved with its steps");
        var replaced = session.SaveAutomation("ARMADURA", "Zone");
        check(replaced == id && session.Document.Automations.Count == 1 && session.Document.Automations.Single().Steps.Count == 1, "Saving under a name that exists replaces the script and keeps its identity");
        session.Undo();
        check(session.Document.Automations.Single().Steps.Count == 2, "Undo gives back the script as it was");
        check(Throws(() => session.SaveAutomation("", "Zone")) && Throws(() => session.SaveAutomation(new string('n', 121), "Zone")) && Throws(() => session.SaveAutomation("Mal", "1mal")) && session.Document.Automations.Count == 1, "A script with no name, a long name or a bad step is refused");
        var json = ProjectFile.SerializeDocument(session.Document);
        var back = ProjectFile.DeserializeDocument(json);
        check(back.Automations.Single().Steps[0].Answers.SequenceEqual(["3 0.016 abajo"]), "Scripts are saved with the project and read back the same");
        var clone = session.Document.Clone();
        clone.Automations[0].Steps[0].Answers.Add("cambio");
        check(session.Document.Automations[0].Steps[0].Answers.Count == 1, "A copy of the project does not share the answers of its scripts");
        session.RemoveAutomation(id);
        check(session.Document.Automations.Count == 0 && Throws(() => session.RemoveAutomation(id)), "A script can be removed, once");
        for (var number = 0; number < AutomationScripts.MaximumScripts; number++) session.SaveAutomation("G" + number, "Zone");
        check(Throws(() => session.SaveAutomation("Uno más", "Zone")) && session.Document.Automations.Count == AutomationScripts.MaximumScripts, "A project holds at most 100 scripts");
    }

    private static void Nesting(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Historia"));
        var level = session.Document.Levels[0].Id;
        var levels = session.Document.Levels.Count;
        var mark = session.MarkHistory();
        session.CollapseHistorySince(mark, "Transaction.RunAutomation");
        check(!session.CanUndo, "Collapsing when nothing was done leaves the history as it was");
        // Each of these commands is a group of its own; together they become one step.
        session.AddMassAtElevation(level, new Point2(0, 0), new Point2(4, 3), 2, 3, "Uno");
        session.AddMassAtElevation(level, new Point2(10, 0), new Point2(14, 3), 0, 3, "Dos");
        session.AddLevel();
        session.CollapseHistorySince(mark, "Transaction.RunAutomation");
        var masses = session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.Mass);
        check(masses == 2 && session.Document.Levels.Count == levels + 1, $"Steps that are groups of their own can be collapsed into one ({masses} masses, {session.Document.Levels.Count} levels)");
        session.Undo();
        check(session.Document.StructuralElements.All(item => item.Kind != StructuralElementKind.Mass) && session.Document.Levels.Count == levels && !session.CanUndo, "One undo reverts everything done since the mark");
        session.Redo();
        check(session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.Mass) == 2 && session.Document.Levels.Count == levels + 1, "One redo does it all again");
        using var open = session.BeginGroup("Transaction.RunAutomation");
        var threw = false;
        try { session.CollapseHistorySince(mark, "Transaction.RunAutomation"); } catch (InvalidOperationException) { threw = true; }
        check(threw, "History cannot be collapsed while a group is open");
    }
}
