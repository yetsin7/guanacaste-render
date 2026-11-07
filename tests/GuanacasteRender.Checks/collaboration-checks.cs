using System.Text.Json;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Checks the central model on the breeze-block house: two people working on their own copies, worksets, conflicts and random rounds.</summary>
static class CollaborationChecks
{
    private static bool Throws(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return true; }
        return false;
    }

    private static string WallsText(DocumentSession session) =>
        JsonSerializer.Serialize(session.Document.Walls.OrderBy(wall => wall.Id).Select(wall => (wall.Id, wall.Start, wall.End, wall.Height)).ToArray());

    public static void Run(Action<bool, string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), $"central-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var previous = Environment.GetEnvironmentVariable("G3D_CENTRAL_DIR");
        Environment.SetEnvironmentVariable("G3D_CENTRAL_DIR", Path.Combine(folder, "bases"));
        try
        {
            var central = Path.Combine(folder, "casa-central.g3d");
            var ana = new DocumentSession(BreezeBlockHouseShowcase.Create());
            ana.EnableCollaboration("Ana", central);
            var first = ana.SynchronizeWithCentral();
            check(first.CreatedCentral && File.Exists(central), "The first synchronization creates the central model from the project");

            var beto = new DocumentSession(ProjectFile.Load(central));
            beto.EnableCollaboration("Beto", central);
            var join = beto.SynchronizeWithCentral();
            check(!join.CreatedCentral && join.Merge!.TakenFromCentral == 0 && join.Merge.Conflicts == 0 && WallsText(ana) == WallsText(beto),
                "A second person opens the central model, joins it, and finds nothing to merge");

            var walls = ana.Document.Walls.Select(wall => wall.Id).ToArray();

            // Different walls: both keep their work.
            ana.MoveWall(walls[0], 1, 0);
            beto.MoveWall(walls[5], 0, -2);
            ana.SynchronizeWithCentral();
            var betoSync = beto.SynchronizeWithCentral();
            ana.SynchronizeWithCentral(writeCentral: false);
            var a0 = ana.Document.Walls.Single(wall => wall.Id == walls[0]);
            var b5 = beto.Document.Walls.Single(wall => wall.Id == walls[5]);
            check(betoSync.Merge!.Conflicts == 0 && WallsText(ana) == WallsText(beto) &&
                  beto.Document.Walls.Single(wall => wall.Id == walls[0]).Start == a0.Start && ana.Document.Walls.Single(wall => wall.Id == walls[5]).Start == b5.Start,
                $"Two people who move different walls both keep their work after synchronizing ({betoSync.Merge.TakenFromCentral} came in, {betoSync.Merge.KeptFromMine} stayed)");

            // Additions on both sides.
            var level = ana.Document.Levels[0].Id;
            var anaWall = ana.AddWall(level, new Point2(950, 950), new Point2(954, 950));
            var betoWall = beto.AddWall(level, new Point2(960, 960), new Point2(964, 960));
            ana.SynchronizeWithCentral();
            beto.SynchronizeWithCentral();
            ana.SynchronizeWithCentral(writeCentral: false);
            check(ana.Document.Walls.Any(wall => wall.Id == betoWall) && ana.Document.Walls.Any(wall => wall.Id == anaWall) &&
                  beto.Document.Walls.Any(wall => wall.Id == anaWall) && WallsText(ana) == WallsText(beto),
                "A wall each person adds ends up in both copies");

            // The same wall changed differently: the person who synchronizes second keeps theirs, and it is reported.
            ana.MoveWall(walls[2], 5, 0);
            beto.MoveWall(walls[2], 0, 7);
            ana.SynchronizeWithCentral();
            var clash = beto.SynchronizeWithCentral();
            check(clash.Merge!.Conflicts == 1 && clash.Merge.Details[0].Description == "EditedBoth" && clash.Merge.Details[0].Resolution == "KeptMine" &&
                  Math.Abs(beto.Document.Walls.Single(wall => wall.Id == walls[2]).Start.Y - ana.Document.Walls.Single(wall => wall.Id == walls[2]).Start.Y) > 1,
                "Both moving the same wall is a conflict, reported, and the second person's version is kept");
            ana.SynchronizeWithCentral(writeCentral: false);

            // Deleted on one side and changed on the other.
            ana.RemoveWall(walls[8]);
            beto.MoveWall(walls[8], 1, 1);
            ana.SynchronizeWithCentral();
            var deleteClash = beto.SynchronizeWithCentral();
            check(deleteClash.Merge!.Conflicts == 1 && beto.Document.Walls.Any(wall => wall.Id == walls[8]), "A wall one person deleted and the other moved is kept, with the conflict reported");

            check(ana.SynchronizeWithCentral().Merge!.Conflicts == 0 && ana.SynchronizeWithCentral().Merge!.TakenFromCentral == 0, "Synchronizing again with nothing new changes nothing");

            // Worksets.
            var facade = ana.AddWorkset("Fachada");
            var mine = walls.Skip(10).Take(3).ToArray();
            ana.AssignToWorkset(mine, facade);
            ana.BorrowWorkset(facade);
            ana.SynchronizeWithCentral();
            beto.SynchronizeWithCentral();
            check(beto.Document.Worksets.Single().Owner == "Ana" && beto.Document.WorksetAssignments.Count == 3, "A workset one person took arrives at the other copy, with its owner");
            check(Throws(() => beto.MoveWall(mine[0], 1, 0)) && Throws(() => beto.RemoveWall(mine[1])) && Throws(() => beto.BorrowWorkset(facade)) &&
                  Throws(() => beto.RemoveWorkset(facade)) && Throws(() => beto.AssignToWorkset([walls[0]], facade)),
                "The other person cannot move or delete its walls, take it, remove it or add to it");
            beto.MoveWall(walls[0], 0, 1);
            check(beto.Document.Walls.Single(wall => wall.Id == walls[0]).Start.Y != ana.Document.Walls.Single(wall => wall.Id == walls[0]).Start.Y, "Walls outside the workset remain free to edit");
            beto.SetWorksetVisible(facade, false);
            var hidden = ViewVisibility.Apply(beto.Document, beto.Document.Views.First(view => view.Kind == ProjectViewKind.Model3D));
            check(hidden.Walls.Count == beto.Document.Walls.Count - 3 && beto.Document.Walls.Count == ana.Document.Walls.Count, "A workset switched off takes its walls out of the views and not out of the model");
            beto.SetWorksetVisible(facade, true);

            var status = beto.MonitorCollaboration();
            check(status.Enabled && status.User == "Beto" && status.CentralExists && status.LocalChanged >= 1 && status.Worksets.Count == 1, $"The monitor reports this copy's changes since it last synchronized ({status.LocalChanged} changed)");

            Assert(ana.RelinquishWorksets() == 1 && ana.RelinquishWorksets() == 0, check, "Letting go of a workset frees it once");
            ana.SynchronizeWithCentral();
            beto.SynchronizeWithCentral();
            beto.MoveWall(mine[0], 1, 0);
            check(beto.Document.Worksets.Single().Owner is null, "Once it is released and synchronized, the other person can edit its walls");

            // Random rounds: whatever they do, in the end both copies and the central model hold the same walls.
            var random = new Random(20261108);
            for (var round = 0; round < 25; round++)
            {
                var person = random.Next(2) == 0 ? ana : beto;
                var target = person.Document.Walls[random.Next(person.Document.Walls.Count)].Id;
                try
                {
                    switch (random.Next(3))
                    {
                        case 0: person.MoveWall(target, random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1); break;
                        case 1: person.AddWall(level, new Point2(1000 + round * 5, 1000), new Point2(1003 + round * 5, 1000)); break;
                        default: person.RemoveWall(target); break;
                    }
                }
                catch (InvalidDataException) { }
                if (random.Next(3) == 0) person.SynchronizeWithCentral();
            }
            ana.SynchronizeWithCentral();
            beto.SynchronizeWithCentral();
            ana.SynchronizeWithCentral();
            beto.SynchronizeWithCentral(writeCentral: false);
            var centralCopy = new DocumentSession(ProjectFile.Load(central));
            check(WallsText(ana) == WallsText(beto) && WallsText(beto) == WallsText(centralCopy) && ana.Document.Openings.All(opening => ana.Document.Walls.Any(wall => wall.Id == opening.WallId)),
                $"After 25 random rounds of moving, adding and deleting by both people, the two copies and the central model hold the same {ana.Document.Walls.Count} walls");
            check(BuildingMesher.Build(ana.Document).Positions.All(point => float.IsFinite(point.X + point.Y + point.Z)), "The merged house still meshes");

            Assert(Throws(() => new DocumentSession(BreezeBlockHouseShowcase.Create()).SynchronizeWithCentral()) &&
                   Throws(() => new DocumentSession(BreezeBlockHouseShowcase.Create()).EnableCollaboration("", central)) &&
                   Throws(() => new DocumentSession(BreezeBlockHouseShowcase.Create()).EnableCollaboration("Ana", "")),
                check, "Synchronizing without collaboration, or with no name or no central file, is refused");
        }
        finally
        {
            Environment.SetEnvironmentVariable("G3D_CENTRAL_DIR", previous);
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }

    private static void Assert(bool condition, Action<bool, string> check, string name) => check(condition, name);
}
