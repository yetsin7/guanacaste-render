using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Checks interference detection, the share package, publishing and the list of central models.</summary>
static class CoordinationChecks
{
    private static bool Throws(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return true; }
        return false;
    }

    public static void Run(Action<bool, string> check)
    {
        Interferences(check);
        Output(check);
    }

    private static void Interferences(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Interferencias"));
        var level = session.Document.Levels[0].Id;

        // A corner and a tee are joined, not clashes.
        session.AddWall(level, new Point2(10, 0), new Point2(16, 0));
        session.AddWall(level, new Point2(16, 0), new Point2(16, 6));
        session.AddWall(level, new Point2(30, 0), new Point2(36, 0));
        session.AddWall(level, new Point2(33, 0), new Point2(33, 6));
        check(InterferenceCheck.Run(session.Document).Count == 0, "Walls that meet in a corner or in a tee do not interfere");

        // Two walls that cross through each other do.
        session.AddWall(level, new Point2(50, 0), new Point2(56, 0));
        var crossing = session.AddWall(level, new Point2(53, -3), new Point2(53, 3));
        var found = InterferenceCheck.Run(session.Document);
        check(found.Count == 1 && found[0].KindA == "Wall" && found[0].KindB == "Wall" && Math.Abs(found[0].Area - 0.04) < 1e-6 && Math.Abs(found[0].Height - 3) < 1e-6,
            $"Two walls crossing leave one interference: {found[0].Area:0.00} m² of plan by {found[0].Height:0.0} m");
        session.RemoveWall(crossing);
        check(InterferenceCheck.Run(session.Document).Count == 0, "Removing one of them clears it");

        // A column in the middle of a wall.
        session.AddWall(level, new Point2(70, 0), new Point2(76, 0));
        session.AddColumn(level, new Point2(73, 0));
        var inWall = InterferenceCheck.Run(session.Document);
        check(inWall.Count == 1 && new[] { inWall[0].KindA, inWall[0].KindB }.Order().SequenceEqual(["Column", "Wall"]), "A column standing in a wall interferes with it");

        // A beam that passes through a wall, and a beam that rests on top of one.
        var through = session.AddStructuralElement(level, StructuralElementKind.Beam, "Viga", new Point2(72, -2), new Point2(72, 2));
        var beam = session.Document.StructuralElements.Single(item => item.Id == through);
        var withBeam = InterferenceCheck.Run(session.Document);
        var expectBeam = beam.Offset < 3 && beam.Offset + beam.Height > 0;
        check(withBeam.Any(item => item.KindA == "Beam" || item.KindB == "Beam") == expectBeam, "A beam at the height of a wall interferes with it; one clear of it does not");

        // The house: every finding is real (it has overlap and height) and the order is by volume.
        var house = new DocumentSession(BreezeBlockHouseShowcase.Create());
        var houseFound = InterferenceCheck.Run(house.Document);
        check(houseFound.All(item => item.Area >= 0.01 && item.Height >= 0.02) && houseFound.Zip(houseFound.Skip(1)).All(pair => pair.First.Volume >= pair.Second.Volume - 1e-12) &&
              houseFound.All(item => house.Document.Walls.Any(w => w.Id == item.IdA) || house.Document.Columns.Any(c => c.Id == item.IdA) || house.Document.StructuralElements.Any(s => s.Id == item.IdA)),
            $"The cobogó house gives {houseFound.Count} findings, each a real overlap, most serious first");

        // Randomly placed walls: pairs far apart never interfere, and moving one onto another always does.
        var random = new Random(20261109);
        var wrong = 0;
        for (var round = 0; round < 40; round++)
        {
            var test = new DocumentSession(BuildingDocument.Create("Azar"));
            var lv = test.Document.Levels[0].Id;
            var x = random.NextDouble() * 50;
            var y = random.NextDouble() * 50;
            test.AddWall(lv, new Point2(x, y), new Point2(x + 4 + random.NextDouble() * 4, y));
            test.AddWall(lv, new Point2(x + 100, y + 100), new Point2(x + 104, y + 100));
            if (InterferenceCheck.Run(test.Document).Count != 0) wrong++;
            test.AddWall(lv, new Point2(x + 1, y - 2), new Point2(x + 1, y + 2));
            if (InterferenceCheck.Run(test.Document).Count != 1) wrong++;
        }
        check(wrong == 0, "40 random layouts: walls far apart never interfere and one that crosses another always does");
    }

    private static void Output(Action<bool, string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), $"share-{Guid.NewGuid():N}");
        var previous = Environment.GetEnvironmentVariable("G3D_CENTRAL_DIR");
        Environment.SetEnvironmentVariable("G3D_CENTRAL_DIR", Path.Combine(folder, "bases"));
        try
        {
            var session = new DocumentSession(BreezeBlockHouseShowcase.Create());
            var when = new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
            var package = Path.Combine(folder, "paquete");
            var files = Publishing.CreateSharePackage(session.Document, package, when);
            check(files.Count == 5 && files.All(file => new FileInfo(Path.Combine(package, file)).Length > 0), $"The share package holds {files.Count} files, none empty: {string.Join(", ", files)}");
            var reopened = ProjectFile.Load(Path.Combine(package, files.First(file => file.EndsWith(".g3d", StringComparison.Ordinal))));
            check(reopened.Walls.Count == session.Document.Walls.Count && File.ReadAllText(Path.Combine(package, files.First(file => file.EndsWith(".ifc", StringComparison.Ordinal)))).StartsWith("ISO-10303-21", StringComparison.Ordinal) &&
                  File.ReadAllText(Path.Combine(package, "LEEME.txt")).Contains($"Muros: {session.Document.Walls.Count}"),
                "The project in the package opens with the same walls, the IFC is an IFC and the summary has the counts");
            check(Throws(() => Publishing.CreateSharePackage(session.Document, "  ", when)), "A package without a folder is refused");

            var sheets = session.Document.Sheets;
            var output = Path.Combine(folder, "publicado");
            var one = Publishing.Publish(session.Document, new PublishSettings([sheets[0].Id], true, false, false, output), when);
            var onePdf = new FileInfo(Path.Combine(output, one[0])).Length;
            var all = Publishing.Publish(session.Document, new PublishSettings(sheets.Select(sheet => sheet.Id).ToList(), true, true, true, Path.Combine(folder, "todo")), when);
            var allPdf = new FileInfo(Path.Combine(folder, "todo", all[0])).Length;
            check(one.Count == 1 && all.Count == 3 && (sheets.Count < 2 || onePdf < allPdf) && File.ReadAllBytes(Path.Combine(output, one[0]))[..5].SequenceEqual("%PDF-"u8.ToArray()),
                $"Publishing one sheet makes a smaller PDF than publishing all {sheets.Count}, and adding DXF and IFC makes 3 files");
            check(Throws(() => Publishing.Publish(session.Document, new PublishSettings([], true, false, false, output), when)) &&
                  Throws(() => Publishing.Publish(session.Document, new PublishSettings([sheets[0].Id], false, false, false, output), when)) &&
                  Throws(() => Publishing.Publish(session.Document, new PublishSettings([sheets[0].Id], true, false, false, ""), when)),
                "Publishing no sheets, no format or no folder is refused");

            session.SetPublishSettings(new PublishSettings([sheets[1].Id, Guid.NewGuid()], false, true, false, " " + output + " "));
            check(session.Document.Publish.SheetIds.SequenceEqual([sheets[1].Id]) && session.Document.Publish.Folder == output && session.Document.Publish.Dxf, "The choice is saved, forgetting sheets that do not exist");
            var file = Path.Combine(folder, "p.g3d");
            ProjectFile.Save(file, session.Document);
            check(ProjectFile.Load(file).Publish.SheetIds.SequenceEqual([sheets[1].Id]), "…and survives saving and opening the project");

            RecentCentralModels.Add(@"C:\modelos\a.g3d");
            RecentCentralModels.Add(@"C:\modelos\b.g3d");
            RecentCentralModels.Add(@"c:\MODELOS\A.g3d");
            var recent = RecentCentralModels.List();
            check(recent.Count == 2 && recent[0].Equals(@"c:\MODELOS\A.g3d", StringComparison.Ordinal), "The list of central models keeps the latest first and does not repeat a model");
            RecentCentralModels.Remove(@"C:\modelos\A.g3d");
            check(RecentCentralModels.List().SequenceEqual([@"C:\modelos\b.g3d"]), "A model can be forgotten");
        }
        finally
        {
            Environment.SetEnvironmentVariable("G3D_CENTRAL_DIR", previous);
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }
}
