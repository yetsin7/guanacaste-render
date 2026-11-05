using Guanacaste3D.Core;

internal static partial class InteractionChecks
{
    /// <summary>
    /// Runs the wall join, pin, flip, copy, cut and paste ribbon commands through the window's real command router
    /// on walls placed away from the other checks, and removes everything it created.
    /// </summary>
    private static void WallEditCommandChecks(DocumentSession session)
    {
        var level = Field<Guid>("_activeLevelId");
        var first = session.AddWall(level, new Point2(200, 200), new Point2(206, 200));
        var second = session.AddWall(level, new Point2(206, 200), new Point2(206, 203));
        var created = new List<Guid> { first, second };
        Wall Find(Guid id) => session.Document.Walls.Single(wall => wall.Id == id);
        void Select(Guid id) => SetField("_selectedWallId", (Guid?)id);

        Select(first);
        Invoke("ExecuteApplicationCommand", "DisallowJoin");
        Check(Find(first).StartJoin == WallJoinSetting.Disallowed && Find(first).EndJoin == WallJoinSetting.Disallowed,
            "The Disallow Join ribbon command releases both ends of the selected wall");
        Invoke("ExecuteApplicationCommand", "ButtJoin");
        Check(Find(first).EndJoin == WallJoinSetting.Butt, "The Butt Join ribbon command makes the selected wall butt");
        Invoke("ExecuteApplicationCommand", "AllowJoin");
        Check(Find(first).StartJoin == WallJoinSetting.Miter && Find(first).EndJoin == WallJoinSetting.Miter,
            "The Allow Join ribbon command restores mitered corners");

        Invoke("ExecuteApplicationCommand", "Pin");
        Check(Find(first).Pinned, "The Pin ribbon command pins the selected wall");
        Invoke("ExecuteApplicationCommand", "Pin");
        Check(!Find(first).Pinned, "The Pin ribbon command toggles the pin off again");

        var start = Find(first).Start;
        Invoke("ExecuteApplicationCommand", "FlipWall");
        Check(Find(first).End == start, "The Flip Wall ribbon command swaps the ends of the selected wall");
        Invoke("ExecuteApplicationCommand", "FlipWall");
        Check(Find(first).Start == start, "Flipping a second time restores the wall");

        var before = session.Document.Walls.Count;
        Invoke("ExecuteApplicationCommand", "Paste");
        Check(session.Document.Walls.Count == before, "Pasting with an empty clipboard changes nothing");
        Invoke("ExecuteApplicationCommand", "Copy");
        Invoke("ExecuteApplicationCommand", "Paste");
        Check(session.Document.Walls.Count == before + 1, "Copy and Paste ribbon commands add one wall");
        var pasted = Field<Guid?>("_selectedWallId")!.Value;
        created.Add(pasted);
        Check(pasted != first && Find(pasted).Start.DistanceTo(Find(first).Start) > 0.5,
            "The pasted wall is selected and sits apart from its source");

        Invoke("ExecuteApplicationCommand", "Cut");
        Check(session.Document.Walls.Count == before && session.Document.Walls.All(wall => wall.Id != pasted),
            "The Cut ribbon command removes the selected wall");
        Invoke("ExecuteApplicationCommand", "Paste");
        var restored = Field<Guid?>("_selectedWallId")!.Value;
        created.Add(restored);
        Check(session.Document.Walls.Count == before + 1, "A cut wall can be pasted back");

        Select(first);
        Invoke("ExecuteApplicationCommand", "ReviewWarningsUnavailable");
        Check(Field<Guid?>("_selectedWallId") == first, "An unknown ribbon command leaves the selection alone");

        foreach (var id in created.Where(id => session.Document.Walls.Any(wall => wall.Id == id)))
            session.RemoveWall(id);
        SetField("_selectedWallId", null);
        Invoke("RenderPlan");
        Pump();
    }

    /// <summary>
    /// Drives the real Rotate ribbon command without opening a modal window in CI.
    /// The warning sink is scoped to this check; normal users still see the WPF dialog.
    /// </summary>
    private static void RotateRibbonChecks(DocumentSession session)
    {
        var level = Field<Guid>("_activeLevelId");
        var wallId = session.AddWall(level, new Point2(300, 300), new Point2(306, 300));
        var warnings = new List<string>();
        SetField("_rotationWarningSink", (Action<string, string>)((message, title) => warnings.Add(message)));
        Wall Find() => session.Document.Walls.Single(wall => wall.Id == wallId);
        try
        {
            SetField("_selectedWallId", (Guid?)wallId);
            Invoke("ExecuteApplicationCommand", "Rotate");
            Check(Find().Start.DistanceTo(new Point2(300, 300)) < 1e-8 &&
                  Find().End.DistanceTo(new Point2(300, 306)) < 1e-8,
                "The real Rotate ribbon command turns the selected wall 90 degrees");

            session.Undo();
            Check(Find().End.DistanceTo(new Point2(306, 300)) < 1e-8,
                "Undo restores the wall before ribbon rotation");
            session.Redo();
            Check(Find().End.DistanceTo(new Point2(300, 306)) < 1e-8,
                "Redo reapplies the same ribbon rotation");

            session.SetWallsPinned([wallId], true);
            var beforeRejected = Find();
            var undoName = session.UndoName;
            SetField("_selectedWallId", (Guid?)wallId);
            Invoke("ExecuteApplicationCommand", "Rotate");
            Check(warnings.Count == 1 && !string.IsNullOrWhiteSpace(warnings[0]),
                "Rotating a pinned wall reports a warning without opening a modal dialog");
            Check(Find().Start == beforeRejected.Start && Find().End == beforeRejected.End &&
                  Find().Pinned && session.UndoName == undoName,
                "Rejected rotation preserves the pinned wall and undo history");

            SetField("_selectedWallId", null);
            Invoke("ExecuteApplicationCommand", "Rotate");
            Check(warnings.Count == 1 && Find().End == beforeRejected.End,
                "Rotate with no wall selected does not change the wall or show a warning");
            session.Document.Validate();
        }
        finally
        {
            SetField("_rotationWarningSink", null);
            SetField("_selectedWallId", null);
            if (session.Document.Walls.Any(wall => wall.Id == wallId))
            {
                if (Find().Pinned) session.SetWallsPinned([wallId], false);
                session.RemoveWall(wallId);
            }
            Invoke("RenderPlan");
            Pump();
        }
    }
}

internal static partial class InteractionChecks
{
    /// <summary>
    /// Runs random sequences of non-modal ribbon commands on random selections, including an empty one, over several
    /// layouts, and checks that no command throws and the project stays valid after every step. Layouts and the order
    /// of commands differ per seed, so a failure names its seed and step. Undo and redo are left out on purpose: they
    /// would reach back into what earlier checks did.
    /// </summary>
    private static void RandomizedRibbonChecks(DocumentSession session)
    {
        // Capture rejected rotations without blocking the headless runner; real users keep the modal dialog.
        string[] commands =
        [
            "AllowJoin", "MiterJoin", "ButtJoin", "SquareOff", "DisallowJoin", "Pin", "FlipWall", "DetachAll",
            "Copy", "Cut", "Paste", "CopyElement", "Rotate"
        ];
        var level = Field<Guid>("_activeLevelId");
        var initial = session.Document.Walls.Select(wall => wall.Id).ToHashSet();
        var failures = new List<string>();
        var executed = 0;

        var capturedRotationWarnings = 0;
        SetField("_rotationWarningSink", (Action<string, string>)((message, title) => capturedRotationWarnings++));
        try
        {
            for (var seed = 1; seed <= 12; seed++)
            {
                var random = new Random(seed * 31);
                var seedBudget = System.Diagnostics.Stopwatch.StartNew();
                var baseX = 400.0 + seed * 40;
                var angle = random.Next(0, 4) * Math.PI / 6 + random.NextDouble() * 0.2;
                var length = 3 + random.NextDouble() * 6;
                var corner = new Point2(baseX + length, 400);
                session.AddWall(level, new Point2(baseX, 400), corner);
                session.AddWall(level, corner, new Point2(corner.X + Math.Cos(angle) * length, 400 + Math.Sin(angle) * length));
                var host = session.AddWall(level, new Point2(baseX, 410), new Point2(baseX + length * 1.5, 410));
                session.AddWall(level, new Point2(baseX + length * 0.7, 410), new Point2(baseX + length * 0.7, 410 + length));
                session.AddOpening(host, random.Next(2) == 0 ? OpeningKind.Door : OpeningKind.Window, offset: length * 0.4);

                for (var step = 0; step < 30; step++)
                {
                    var mine = session.Document.Walls.Where(wall => !initial.Contains(wall.Id)).Select(wall => wall.Id).ToArray();
                    var selected = mine.Length == 0 || random.Next(8) == 0 ? (Guid?)null : mine[random.Next(mine.Length)];
                    SetField("_selectedWallId", selected);
                    var command = commands[random.Next(commands.Length)];
                    try
                    {
                        Console.WriteLine($"Ribbon fuzz: seed={seed} step={step} command={command}");
                        Invoke("ExecuteApplicationCommand", command);
                        if (seedBudget.Elapsed > TimeSpan.FromSeconds(45))
                            throw new TimeoutException($"Ribbon fuzz seed {seed} exceeded 45 seconds at step {step} ({command}).");
                        session.Document.Validate();
                        executed++;
                        if (step % 6 == 0)
                        {
                            Invoke("RenderPlan");
                            Pump();
                        }
                    }
                    catch (Exception exception)
                    {
                        var cause = (exception as System.Reflection.TargetInvocationException)?.InnerException ?? exception;
                        failures.Add($"seed {seed} step {step} {command}: {cause.GetType().Name}: {cause.Message}");
                        break;
                    }
                }

                SetField("_selectedWallId", null);
                var leftovers = session.Document.Walls.Where(wall => !initial.Contains(wall.Id)).Select(wall => wall.Id).ToArray();
                session.SetWallsPinned(leftovers.Length == 0 ? [] : leftovers, false);
                foreach (var id in leftovers) session.RemoveWall(id);
            }
        }
        finally
        {
            SetField("_rotationWarningSink", null);
        }
        Console.WriteLine($"Randomized rotation warnings captured: {capturedRotationWarnings}");
        Check(failures.Count == 0,
            $"{executed} random ribbon commands on random selections and layouts never throw and keep the project valid" +
            (failures.Count == 0 ? "" : ": " + string.Join(" | ", failures.Take(5))));
        Check(session.Document.Walls.Select(wall => wall.Id).ToHashSet().SetEquals(initial),
            "The random ribbon run leaves the project's walls as it found them");
        Invoke("RenderPlan");
        Pump();
    }
}
