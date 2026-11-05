using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Checks steel members: plates, bolts, welds, cuts and fittings, with exact volumes and random cuts on the breeze-block house.</summary>
static class SteelChecks
{
    private static bool Near(double first, double second, double tolerance = 1e-9) => Math.Abs(first - second) < tolerance;

    private static bool Throws(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return true; }
        return false;
    }

    private static double Area(IReadOnlyList<Point2> polygon)
    {
        var sum = 0.0;
        for (var index = 0; index < polygon.Count; index++)
        {
            var next = polygon[(index + 1) % polygon.Count];
            sum += polygon[index].X * next.Y - next.X * polygon[index].Y;
        }
        return Math.Abs(sum) / 2;
    }

    private static double Volume(DocumentSession session, Guid id) =>
        ElementGeometry.Parts(session.Document.StructuralElements.Single(item => item.Id == id)).Sum(part => Area(part.Footprint) * (part.Top - part.Bottom));

    public static void Run(Action<bool, string> check)
    {
        var session = new DocumentSession(BreezeBlockHouseShowcase.Create());
        var level = session.Document.Levels[0].Id;
        Beams(session, level, check);
        Plates(session, level, check);
        Fittings(session, level, check);
        Random(check);
    }

    private static Guid NewBeam(DocumentSession session, Guid level, double x, double y, ElementStyle style = ElementStyle.Default)
    {
        var id = session.AddStructuralElement(level, StructuralElementKind.Beam, "Viga", new Point2(x, y), new Point2(x + 6, y));
        session.UpdateStructuralElement(id, "Viga", 0.3, 0.4, 2.5, style);
        return id;
    }

    private static void Beams(DocumentSession session, Guid level, Action<bool, string> check)
    {
        var beam = NewBeam(session, level, 800, 800);
        var full = Volume(session, beam);
        check(Near(full, 6 * 0.3 * 0.4, 1e-9), "A beam 6 m long, 30 cm wide and 40 cm deep holds 0.72 m³ of steel");

        session.AddCope(beam, false, 0.5, 0.1);
        check(Near(Volume(session, beam), full - 0.5 * 0.3 * 0.1, 1e-9), "Coping its end 50 cm long and 10 cm deep takes exactly 0.015 m³ off");
        session.AddCope(beam, true, 0.4, 0.05, 0.5);
        check(Near(Volume(session, beam), full - 0.5 * 0.3 * 0.1 - 0.4 * 0.15 * 0.05, 1e-9), "A parametric cope on the other end, half the width, takes off 0.003 m³ more");
        check(session.Document.StructuralElements.Single(item => item.Id == beam).Cuts!.Count == 2 && ElementGeometry.Parts(session.Document.StructuralElements.Single(item => item.Id == beam)).All(part => part.Top - part.Bottom >= 0.002),
            "The cuts are kept with the member, and no piece is left thinner than 2 mm");

        session.ClearMemberCuts(beam);
        check(Near(Volume(session, beam), full) && session.Document.StructuralElements.Single(item => item.Id == beam).Cuts is null, "Clearing the cuts gives the whole member back");

        // A wide-flange beam: the top flange goes first, then the web.
        var wide = NewBeam(session, level, 800, 810, ElementStyle.IShape);
        var flange = 0.4 * 0.12;
        var web = 0.3 * 0.12;
        var wideFull = Volume(session, wide);
        session.AddCope(wide, false, 0.5, 0.03);
        check(Near(Volume(session, wide), wideFull - 0.5 * 0.3 * 0.03, 1e-9), "On a wide-flange beam a shallow cope only takes off the top flange");
        session.ClearMemberCuts(wide);
        session.AddCope(wide, false, 0.5, 0.1);
        check(Near(Volume(session, wide), wideFull - 0.5 * 0.3 * flange - 0.5 * web * (0.1 - flange), 1e-9), "A deep one takes the whole flange and then part of the web");

        var slotted = NewBeam(session, level, 800, 820);
        session.AddCutThrough(slotted, 2.5, 0.2);
        var pieces = ElementGeometry.Parts(session.Document.StructuralElements.Single(item => item.Id == slotted)).ToArray();
        check(pieces.Length == 2 && Near(Volume(session, slotted), 0.72 - 0.2 * 0.3 * 0.4, 1e-9), "A cut right through splits the beam in two and takes out exactly the slot");

        var holed = NewBeam(session, level, 800, 830);
        session.AddContourCut(holed, 2.0, -0.05, 0.3, 0.1);
        check(Near(Volume(session, holed), 0.72 - 0.3 * 0.1 * 0.4, 1e-9) && ElementGeometry.Parts(session.Document.StructuralElements.Single(item => item.Id == holed)).Count() >= 3, "A hole through the middle of a beam takes out its volume and leaves the pieces round it");

        check(Throws(() => session.AddCope(beam, false, 5, 0.1)) && Throws(() => session.AddCope(beam, false, 0.5, 0.9)) && Throws(() => session.AddCope(beam, false, 0.5, 0.1, 0)) &&
              Throws(() => session.AddCornerCut(beam, 0, 0.1, 0.1)) && Throws(() => session.AddMemberCut(beam, new MemberCut(2, 1, 0, 1, 0.1))) && Throws(() => session.AddMemberCut(beam, new MemberCut(0, 1, 0, 1, double.NaN))) &&
              Throws(() => session.AddMemberCut(Guid.NewGuid(), new MemberCut(0, 1, 0, 1, 0.1))),
            "A cope longer than half the member or deeper than it, a corner cut on a beam, and cuts that make no sense are refused");
        for (var index = 0; index < MemberCuts.MaximumCuts; index++) session.AddMemberCut(beam, new MemberCut(0.01 * index, 0.01 * index + 0.005, 0, 0.01, 0.01));
        check(Throws(() => session.AddMemberCut(beam, new MemberCut(5, 5.1, 0, 0.1, 0.1))), "A member takes 64 cuts and no more");
    }

    private static void Plates(DocumentSession session, Guid level, Action<bool, string> check)
    {
        var plate = session.AddStructuralElement(level, StructuralElementKind.Plate, "Placa", new Point2(850, 850), new Point2(851, 850.6));
        var full = Volume(session, plate);
        check(session.Document.StructuralElements.Single(item => item.Id == plate).HasSolid && Near(full, 1 * 0.6 * 0.012, 1e-9), "A plate drawn between two corners is a 12 mm slab: 1 m by 0.6 m is 0.0072 m³");
        session.AddCornerCut(plate, 2, 0.1, 0.1);
        check(Near(Volume(session, plate), full - 0.1 * 0.1 * 0.012, 1e-9), "Cutting a 10 cm corner off takes 0.00012 m³");
        for (var corner = 0; corner < 4; corner++) if (corner != 2) session.AddCornerCut(plate, corner, 0.05, 0.05);
        check(Near(Volume(session, plate), full - 0.1 * 0.1 * 0.012 - 3 * 0.05 * 0.05 * 0.012, 1e-9), "All four corners can be cut, each its own way");
        session.AddContourCut(plate, 0.4, 0.2, 0.2, 0.2);
        check(Near(Volume(session, plate), full - 0.01 * 0.012 - 3 * 0.0025 * 0.012 - 0.04 * 0.012, 1e-9), "A square hole through the middle of the plate takes 0.04 m² of it");
        check(Throws(() => session.AddCornerCut(plate, 4, 0.1, 0.1)) && Throws(() => session.AddCornerCut(plate, 0, 2, 0.1)) && Throws(() => session.AddCornerCut(plate, 0, 0.001, 0.1)),
            "A corner that does not exist, or a cut bigger than the plate, is refused");
        check(BuildingMesher.Build(session.Document).Positions.All(p => float.IsFinite(p.X + p.Y + p.Z)), "The house with the cut members still meshes");
    }

    private static void Fittings(DocumentSession session, Guid level, Action<bool, string> check)
    {
        var bolts = session.AddBoltPattern(level, new Point2(870, 870), 2, 3, 0.075, 0.02);
        var placed = session.Document.StructuralElements.Where(item => bolts.Contains(item.Id)).ToArray();
        check(placed.Length == 6 && placed.All(bolt => bolt.GroupId == placed[0].GroupId && bolt.Style == ElementStyle.Round && bolt.HasSolid) &&
              Near(placed.Max(bolt => bolt.Start.X) - placed.Min(bolt => bolt.Start.X), 0.15, 1e-9) && Near(placed.Max(bolt => bolt.Start.Y) - placed.Min(bolt => bolt.Start.Y), 0.075, 1e-9),
            "A pattern of 2 rows by 3 columns, 75 mm apart, makes 6 grouped round bolts centred on the click");
        session.Undo();
        var oneStep = !session.Document.StructuralElements.Any(item => bolts.Contains(item.Id));
        session.Redo();
        check(oneStep && bolts.All(id => session.Document.StructuralElements.Any(item => item.Id == id)), "One undo takes the whole pattern away and redo brings it back");
        session.RemoveStructuralElement(bolts[2]);
        check(!session.Document.StructuralElements.Any(item => bolts.Contains(item.Id)), "Deleting one bolt deletes the pattern");
        session.Undo();
        check(Throws(() => session.AddBoltPattern(level, new Point2(0, 0), 0, 3, 0.075, 0.02)) && Throws(() => session.AddBoltPattern(level, new Point2(0, 0), 20, 20, 0.075, 0.02)) &&
              Throws(() => session.AddBoltPattern(level, new Point2(0, 0), 2, 2, 0.001, 0.02)) && Throws(() => session.AddBoltPattern(Guid.NewGuid(), new Point2(0, 0), 2, 2, 0.075, 0.02)),
            "A pattern with no rows, over 100 bolts, bolts nearly touching or an unknown level is refused");
        var weld = session.AddStructuralElement(level, StructuralElementKind.Weld, "Soldadura", new Point2(880, 880), new Point2(881, 880));
        check(session.Document.StructuralElements.Single(item => item.Id == weld).HasSolid, "A weld is a bead along a line, with a solid");

        // Shorten.
        var member = NewBeam(session, level, 900, 900);
        session.ShortenMember(member, false, 1.5);
        var shortened = session.Document.StructuralElements.Single(item => item.Id == member);
        check(Near(shortened.Length, 4.5) && shortened.Start == new Point2(900, 900), "Shortening a beam by 1.5 m at its end leaves 4.5 m");
        session.ShortenMember(member, true, 1.0);
        check(Near(session.Document.StructuralElements.Single(item => item.Id == member).Start.X, 901) && Throws(() => session.ShortenMember(member, true, 10)) && Throws(() => session.ShortenMember(member, true, 0)),
            "Shortening the other end moves its start; a distance longer than the beam or zero is refused");

        // Miter: two beams whose axes cross just past their ends meet there.
        var first = session.AddStructuralElement(level, StructuralElementKind.Beam, "A", new Point2(950, 950), new Point2(954, 950));
        var second = session.AddStructuralElement(level, StructuralElementKind.Beam, "B", new Point2(956, 952), new Point2(956, 958));
        session.MiterMembers(first, second);
        var a = session.Document.StructuralElements.Single(item => item.Id == first);
        var b = session.Document.StructuralElements.Single(item => item.Id == second);
        check(a.End == new Point2(956, 950) && b.Start == new Point2(956, 950), "Two beams that would cross at (956, 950) are both taken to that point");
        var parallel = session.AddStructuralElement(level, StructuralElementKind.Beam, "C", new Point2(960, 950), new Point2(964, 950));
        check(Throws(() => session.MiterMembers(first, parallel)) && Throws(() => session.MiterMembers(first, first)) &&
              Throws(() => session.MiterMembers(first, session.AddStructuralElement(level, StructuralElementKind.Beam, "Lejos", new Point2(1100, 1000), new Point2(1100, 1006)))),
            "Parallel beams, a beam with itself and beams that meet far from their ends cannot be mitred");

        // Cut by: a beam that runs through another is cut where it passes.
        var host = NewBeam(session, level, 1000, 1000);
        var cutter = session.AddStructuralElement(level, StructuralElementKind.Beam, "Atraviesa", new Point2(1003, 995), new Point2(1003, 1005));
        session.UpdateStructuralElement(cutter, "Atraviesa", 0.2, 1.0, 2.0, ElementStyle.Default);
        var before = Volume(session, host);
        session.CutMemberBy(host, cutter);
        check(Near(Volume(session, host), before - 0.2 * 0.3 * 0.4, 1e-6), "A beam passing through another cuts a hole exactly its size in it");
        var high = session.AddStructuralElement(level, StructuralElementKind.Beam, "Arriba", new Point2(1004, 995), new Point2(1004, 1005));
        session.UpdateStructuralElement(high, "Arriba", 0.2, 0.3, 5.0, ElementStyle.Default);
        check(Throws(() => session.CutMemberBy(host, high)) && Throws(() => session.CutMemberBy(host, host)), "A member that does not reach the host, or the host itself, cannot cut it");

        var file = Path.Combine(Path.GetTempPath(), $"steel-{Guid.NewGuid():N}.g3d");
        ProjectFile.Save(file, session.Document);
        var reopened = ProjectFile.Load(file);
        File.Delete(file);
        check(reopened.StructuralElements.Single(item => item.Id == host).Cuts is { Count: 1 } && reopened.StructuralElements.Any(item => item.Kind == StructuralElementKind.Plate && item.Cuts is { Count: > 0 }),
            "The cuts survive saving and opening the project");
    }

    /// <summary>Random cuts on a member, with the volume left worked out cell by cell by a separate count.</summary>
    private static void Random(Action<bool, string> check)
    {
        var random = new System.Random(20261110);
        var worst = 0.0;
        for (var round = 0; round < 30; round++)
        {
            var session = new DocumentSession(BreezeBlockHouseShowcase.Create());
            var level = session.Document.Levels[0].Id;
            var beam = NewBeam(session, level, 800, 800);
            var cuts = new List<MemberCut>();
            for (var count = random.Next(1, 6); count > 0; count--)
            {
                var u0 = random.NextDouble() * 5;
                var v0 = -0.15 + random.NextDouble() * 0.25;
                var cut = new MemberCut(u0, u0 + 0.1 + random.NextDouble() * 1.5, v0, v0 + 0.02 + random.NextDouble() * 0.2, 0.02 + random.NextDouble() * 0.4);
                cuts.Add(cut);
                session.AddMemberCut(beam, cut);
            }
            // Separate count: sample the member on a fine grid and take off the deepest cut at each point.
            const int nu = 600, nv = 60;
            var expected = 0.0;
            for (var i = 0; i < nu; i++)
                for (var j = 0; j < nv; j++)
                {
                    var u = (i + 0.5) / nu * 6;
                    var v = -0.15 + (j + 0.5) / nv * 0.3;
                    var depth = cuts.Where(cut => u >= cut.U0 && u <= cut.U1 && v >= cut.V0 && v <= cut.V1).Select(cut => cut.Depth).DefaultIfEmpty(0).Max();
                    expected += 6.0 / nu * 0.3 / nv * (0.4 - Math.Min(0.4, depth));
                }
            var actual = Volume(session, beam);
            worst = Math.Max(worst, Math.Abs(actual - expected) / 0.72);
            if (actual > 0.72 + 1e-9 || actual < 0) worst = 1;
        }
        check(worst < 0.01, $"30 random sets of cuts leave the volume a separate grid count says they should, within {worst * 100:0.00} % of the beam");
    }
}
