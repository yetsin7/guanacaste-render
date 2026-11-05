using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Checks ducts, trays, conduits and devices, stiffeners, routes and the services schematic on the breeze-block house.</summary>
static class MepChecks
{
    private static bool Throws(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return true; }
        return false;
    }

    public static void Run(Action<bool, string> check)
    {
        var session = new DocumentSession(BreezeBlockHouseShowcase.Create());
        var level = session.Document.Levels[0].Id;
        var baseline = BuildingMesher.Build(session.Document).TriangleCount;
        var steps = session.UndoNames.Count;

        var made = new Dictionary<StructuralElementKind, Guid>();
        var x = 800.0;
        foreach (var kind in MepElements.Runs)
        {
            made[kind] = session.AddStructuralElement(level, kind, kind.ToString(), new Point2(x, 800), new Point2(x + 4, 800));
            x += 6;
        }
        foreach (var kind in MepElements.Devices)
        {
            made[kind] = session.AddStructuralElement(level, kind, kind.ToString(), new Point2(x, 800), new Point2(x, 800));
            x += 3;
        }
        var all = made.Select(entry => session.Document.StructuralElements.Single(item => item.Id == entry.Value)).ToArray();
        check(all.Length == 9 && all.All(item => item.HasSolid && item.Width >= 0.02 && item.Height > 0) && session.UndoNames.Count == steps + 9 &&
              BuildingMesher.Build(session.Document).TriangleCount > baseline,
            "Each duct, tray, conduit, pipe, fabrication part and device is added with the size of its standard and a solid in the model");
        check(MepElements.Runs.All(kind => ElementGeometry.Parts(all.Single(item => item.Kind == kind)).Count() == (MepElements.IsRound(kind) ? 2 : 1)),
            "Round runs (conduit, flexible duct and pipe) are drawn as two crossed boxes, rectangular ones as one");
        var terminal = all.Single(item => item.Kind == StructuralElementKind.AirTerminal);
        check(Math.Abs(terminal.Footprint.Max(p => p.X) - terminal.Footprint.Min(p => p.X) - 0.6) < 1e-9 && terminal.Offset > 2, "An air terminal is a 60 cm square on the ceiling");
        check(BuildingMesher.Build(session.Document).Positions.All(p => float.IsFinite(p.X + p.Y + p.Z)), "The house with all of them still meshes");

        // Stiffeners.
        var duct = made[StructuralElementKind.Duct];
        var longDuct = session.AddStructuralElement(level, StructuralElementKind.Duct, "Ducto largo", new Point2(900, 900), new Point2(906, 900));
        var count = session.AddDuctStiffeners(longDuct, 1.5);
        var bands = session.Document.StructuralElements.Where(item => item.Kind == StructuralElementKind.DuctStiffener).ToArray();
        check(count == 4 && bands.Length == 4 && bands.All(band => band.GroupId == bands[0].GroupId && band.Footprint.All(p => p.X >= 899.9 && p.X <= 906.1)) &&
              bands.All(band => Math.Abs(band.Width - 0.48) < 1e-9 && Math.Abs(band.Offset - 2.56) < 1e-9),
            "A 6 m duct with stiffeners every 1.5 m gets 4 bands, a little bigger than the duct and grouped");
        session.RemoveStructuralElement(bands[1].Id);
        check(!session.Document.StructuralElements.Any(item => item.Kind == StructuralElementKind.DuctStiffener), "Deleting one stiffener deletes them all");
        session.Undo();
        check(Throws(() => session.AddDuctStiffeners(made[StructuralElementKind.CableTray], 1)) && Throws(() => session.AddDuctStiffeners(duct, 0.1)) &&
              Throws(() => session.AddDuctStiffeners(duct, 20)) && Throws(() => session.AddDuctStiffeners(Guid.NewGuid(), 1)),
            "Stiffeners only go on ducts, at a spacing the duct is long enough for");

        // Routes.
        var before = session.UndoNames.Count;
        var route = session.AddRoute(level, StructuralElementKind.FabricationPart, [new Point2(700, 700), new Point2(710, 700), new Point2(710, 708), new Point2(715, 708)], "Ruta A");
        var pieces = session.Document.StructuralElements.Where(item => route.Contains(item.Id)).ToArray();
        check(route.Count == 3 && session.UndoNames.Count == before + 1 && pieces[0].End == pieces[1].Start && pieces[1].End == pieces[2].Start &&
              session.Document.ElementGroups.Any(item => item.Kind == ElementGroupKind.Assembly && item.Name == "Ruta A" && item.MemberIds.Count == 3),
            "A route through 4 points makes 3 joined pieces, an assembly of them, in one undoable step");
        check(Throws(() => session.AddRoute(level, StructuralElementKind.FabricationPart, [new Point2(0, 0), new Point2(5, 0)])) &&
              Throws(() => session.AddRoute(level, StructuralElementKind.Beam, [new Point2(0, 0), new Point2(5, 0), new Point2(5, 5)])) &&
              Throws(() => session.AddRoute(level, StructuralElementKind.Duct, [new Point2(0, 0), new Point2(0.01, 0), new Point2(5, 5)])) &&
              Throws(() => session.AddRoute(Guid.NewGuid(), StructuralElementKind.Duct, [new Point2(0, 0), new Point2(5, 0), new Point2(5, 5)])),
            "Fewer than three points, a kind that is not a run, points too close or an unknown level are refused");

        // Views and interference.
        var view = session.Document.Views.First(v => v.Kind == ProjectViewKind.Model3D);
        session.SetViewHiddenCategories(view.Id, [ViewCategory.Mep]);
        var hidden = ViewVisibility.Apply(session.Document, session.Document.Views.First(v => v.Id == view.Id));
        check(!hidden.StructuralElements.Any(item => MepElements.IsMep(item.Kind)) && session.Document.StructuralElements.Any(item => MepElements.IsMep(item.Kind)),
            "Hiding Services in a view takes every duct, tray and device out of it and keeps them in the model");
        session.SetViewHiddenCategories(view.Id, []);

        var clash = new DocumentSession(BuildingDocument.Create("Choque"));
        var lv = clash.Document.Levels[0].Id;
        clash.AddWall(lv, new Point2(0, 0), new Point2(0, 8));
        clash.AddStructuralElement(lv, StructuralElementKind.Duct, "Ducto", new Point2(-3, 4), new Point2(3, 4));
        var found = InterferenceCheck.Run(clash.Document);
        check(found.Count == 1 && new[] { found[0].KindA, found[0].KindB }.Order().SequenceEqual(["Duct", "Wall"]), "A duct passing through a wall is found by the interference check");

        // The schematic.
        var schematic = MepSchematic.Build(session.Document);
        var ids = schematic.Nodes.Select(node => node.Id).ToArray();
        check(ids.Distinct().Count() == ids.Length && schematic.Edges.All(edge => ids.Contains(edge.From) && ids.Contains(edge.To)) &&
              schematic.Nodes.Count(node => node.Group == "Mechanical") == session.Document.StructuralElements.Count(item => MepElements.Runs.Contains(item.Kind) || MepElements.Devices.Contains(item.Kind)),
            $"The schematic of the house has {schematic.Nodes.Count} boxes, none repeated, every line joining two of them, and every duct and device listed");
        var empty = MepSchematic.Build(BuildingDocument.Create("Vacío"));
        check(empty.Nodes.Count == 0 && empty.Edges.Count == 0, "A project without services has an empty schematic");

        var file = Path.Combine(Path.GetTempPath(), $"mep-{Guid.NewGuid():N}.g3d");
        ProjectFile.Save(file, session.Document);
        var reopened = ProjectFile.Load(file);
        File.Delete(file);
        check(MepElements.Runs.All(kind => reopened.StructuralElements.Any(item => item.Kind == kind)) && reopened.StructuralElements.Any(item => item.Kind == StructuralElementKind.DuctStiffener),
            "All the services survive saving and opening the project");
    }
}
