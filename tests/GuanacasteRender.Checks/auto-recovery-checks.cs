using Guanacaste3D.Core;

/// <summary>Checks the automatic recovery copies: complete, atomic, listed newest first and removable.</summary>
static class AutoRecoveryChecks
{
    /// <summary>Runs the recovery scenarios in a throwaway folder, using the breeze-block house as the project.</summary>
    public static void Run(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "g3d-recovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            var house = BreezeBlockHouseShowcase.Create();
            AutoRecovery.Save(house, @"C:\proyectos\cobogo.g3d", directory);
            var copies = AutoRecovery.Find(directory);
            check(copies.Count == 1 && copies[0].DocumentId == house.Id && copies[0].OriginalPath == @"C:\proyectos\cobogo.g3d" &&
                  copies[0].Name == house.Name && (DateTime.UtcNow - copies[0].SavedAtUtc).TotalMinutes < 2,
                "A recovery copy remembers the project, its original file and when it was made");
            check(ProjectFile.SerializeDocument(AutoRecovery.Load(copies[0])) == ProjectFile.SerializeDocument(house),
                "Recovering the breeze-block house gives back exactly the project that was being edited");

            var edited = new DocumentSession(house.Clone());
            edited.AddWall(edited.Document.Levels[0].Id, new Point2(40, 40), new Point2(46, 40));
            AutoRecovery.Save(edited.Document, null, directory);
            copies = AutoRecovery.Find(directory);
            check(copies.Count == 1 && AutoRecovery.Load(copies[0]).Walls.Count == house.Walls.Count + 1,
                "Saving again replaces the project's copy with the newer one instead of adding another");

            var other = BuildingDocument.Create("Otro proyecto");
            AutoRecovery.Save(other, null, directory);
            copies = AutoRecovery.Find(directory);
            check(copies.Count == 2 && copies[0].DocumentId == other.Id,
                "Copies of different projects are listed together, the newest first");

            // A failed save never costs the previous good copy: the new file is complete before it replaces the old.
            var broken = edited.Document.Clone();
            broken.Walls.Add(new Wall(Guid.NewGuid(), Guid.NewGuid(), new Point2(0, 0), new Point2(1, 0), 0.2, 3));
            var refused = false;
            try { AutoRecovery.Save(broken, null, directory); }
            catch (InvalidDataException) { refused = true; }
            check(refused && AutoRecovery.Find(directory).Any(copy => copy.DocumentId == house.Id) &&
                  AutoRecovery.Load(AutoRecovery.Find(directory).Single(copy => copy.DocumentId == house.Id)).Walls.Count == house.Walls.Count + 1,
                "An invalid project is refused and leaves the previous complete copy untouched");

            // Debris a crash can leave behind must not hide or damage any copy.
            File.WriteAllText(Path.Combine(directory, ".half-written.tmp"), "{ \"truncated\":");
            File.WriteAllText(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".recovery.json"), "not json at all");
            var orphan = Guid.NewGuid();
            File.WriteAllText(Path.Combine(directory, orphan.ToString("N") + ".recovery.json"),
                $"{{\"DocumentId\":\"{orphan}\",\"Name\":\"sin archivo\",\"OriginalPath\":null,\"SavedAtUtc\":\"2026-01-01T00:00:00Z\"}}");
            check(AutoRecovery.Find(directory).Count == 2,
                "A leftover temporary file, a damaged description and a description without its project are ignored");

            // Several projects saved at once, as when two windows autosave together.
            var many = Enumerable.Range(0, 6).Select(index => BuildingDocument.Create("Proyecto " + index)).ToArray();
            Parallel.ForEach(many, document => AutoRecovery.Save(document, null, directory));
            check(AutoRecovery.Find(directory).Count == 8 &&
                  many.All(document => AutoRecovery.Load(AutoRecovery.Find(directory).Single(copy => copy.DocumentId == document.Id)).Id == document.Id),
                "Copies saved at the same time are all complete");

            AutoRecovery.Discard(house.Id, directory);
            AutoRecovery.Discard(Guid.NewGuid(), directory);
            check(AutoRecovery.Find(directory).All(copy => copy.DocumentId != house.Id) &&
                  !Directory.EnumerateFiles(directory, house.Id.ToString("N") + "*").Any(),
                "Discarding a project's copy removes it with its backup, and discarding an unknown one is harmless");

            Environment.SetEnvironmentVariable(AutoRecovery.DirectoryVariable, directory);
            check(AutoRecovery.DefaultDirectory == directory && AutoRecovery.Find().Count == AutoRecovery.Find(directory).Count,
                "The recovery folder can be redirected with an environment variable");
            Environment.SetEnvironmentVariable(AutoRecovery.DirectoryVariable, null);
            check(AutoRecovery.DefaultDirectory.EndsWith(Path.Combine("Guanacaste 3D", "Recovery"), StringComparison.Ordinal),
                "By default the copies live in the user's local application data");
        }
        finally
        {
            Environment.SetEnvironmentVariable(AutoRecovery.DirectoryVariable, null);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
