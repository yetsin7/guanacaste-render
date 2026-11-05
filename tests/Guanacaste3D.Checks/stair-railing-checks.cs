using Guanacaste3D.Core;

/// <summary>Checks the lifetime and persistence of railings generated for a stair.</summary>
static class StairRailingChecks
{
    /// <summary>Exercises editing, movement, level copying, persistence, and undo of a hosted railing.</summary>
    public static void Run(Action<bool, string> check)
    {
        StairContinuityChecks.Run(check);
        var session = new DocumentSession(BuildingDocument.Create("Stair association"));
        var ground = session.Document.Levels[0].Id;
        var upper = session.AddLevel();
        var preset = ArchitecturalLibrary.PresetsFor(StructuralElementKind.Stair)
            .First(candidate => candidate.Style == ElementStyle.Default);
        var stairId = session.AddElementFromPreset(ground, preset, new(0, 0), new(4, 0));
        session.AddRailingsOnHost(stairId);
        var original = session.Document.StructuralElements.Where(element => element.RailingHostId == stairId).ToArray();
        check(original.Length == 2, "Generated stair railings retain their host");

        session.MoveStructuralElement(stairId, 2, 3);
        var moved = session.Document.StructuralElements.Where(element => element.RailingHostId == stairId).ToArray();
        check(moved.Zip(original).All(pair => pair.First.Start == new Point2(pair.Second.Start.X + 2, pair.Second.Start.Y + 3)),
            "Moving a stair translates its generated railings");
        session.Undo();
        check(session.Document.StructuralElements.Single(element => element.Id == original[0].Id).Start == original[0].Start,
            "Undo restores the stair and its railings together");

        session.UpdateStructuralElement(stairId, "Stair", 1.4, 3.5, 0.2, ElementStyle.LShaped);
        var edited = session.Document.StructuralElements.Single(element => element.Id == stairId);
        var editedRails = session.Document.StructuralElements.Where(element => element.RailingHostId == stairId).ToArray();
        var expected = StairGeometry.GuardLines(edited);
        check(editedRails.Length == expected.Count && editedRails.Zip(expected).All(pair =>
                pair.First.Start == pair.Second.Start && pair.First.End == pair.Second.End &&
                pair.First.Offset == pair.Second.Offset && pair.First.Rise == pair.Second.Rise),
            "Editing a stair rebuilds the associated railings from its new geometry");
        session.Undo();
        check(session.Document.StructuralElements.Count(element => element.RailingHostId == stairId) == 2,
            "Undo restores the previous stair railing layout");

        session.CopyLevelContent(ground, [upper]);
        var copiedStair = session.Document.StructuralElements.Single(element => element.LevelId == upper && element.Kind == StructuralElementKind.Stair);
        check(session.Document.StructuralElements.Count(element => element.LevelId == upper && element.RailingHostId == copiedStair.Id) == 2,
            "Copying a level reconnects the copied railings to the copied stair");

        var path = Path.Combine(Path.GetTempPath(), $"stair-railing-{Guid.NewGuid():N}.g3");
        try
        {
            ProjectFile.Save(path, session.Document);
            var restored = ProjectFile.Load(path);
            check(restored.StructuralElements.Count(element => element.RailingHostId == stairId) == 2,
                "Stair railing associations survive a project round trip");
        }
        finally { File.Delete(path); }

        session.RemoveStructuralElement(stairId);
        check(session.Document.StructuralElements.All(element => element.Id != stairId && element.RailingHostId != stairId) &&
              session.Document.StructuralElements.Count(element => element.RailingHostId == copiedStair.Id) == 2,
            "Deleting a stair removes only its own generated railings");
        session.Undo();
        check(session.Document.StructuralElements.Count(element => element.RailingHostId == stairId) == 2,
            "Undo restores a deleted stair with its generated railings");
    }
}
