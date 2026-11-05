using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Checks bars, fabric, covers, precast panels, settings and the reinforcement reports on the breeze-block house.</summary>
static class ConcreteChecks
{
    private static bool Near(double first, double second, double tolerance = 1e-9) => Math.Abs(first - second) < tolerance;

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

        // Bars in a beam stay inside the cover.
        var beam = session.AddStructuralElement(level, StructuralElementKind.Beam, "Viga", new Point2(800, 800), new Point2(806, 800));
        session.UpdateStructuralElement(beam, "Viga", 0.3, 0.5, 2.5, ElementStyle.Default);
        var bars = session.AddMemberRebar(beam, 3, 0.016);
        var placed = session.Document.StructuralElements.Where(item => bars.Contains(item.Id)).ToArray();
        check(placed.Length == 3 && placed.All(bar => bar.Kind == StructuralElementKind.Rebar && bar.GroupId == placed[0].GroupId) &&
              placed.All(bar => Near(bar.Length, 6 - 2 * 0.04, 1e-9) && Near(bar.Offset, 2.5 + 0.04 + 0.008, 1e-9)) &&
              placed.All(bar => Math.Abs(bar.Start.Y - 800) <= 0.15 - 0.04 - 0.008 + 1e-9) && Near(placed.Max(bar => bar.Start.Y) - placed.Min(bar => bar.Start.Y), 0.3 - 0.08 - 0.016, 1e-9),
            "Three 16 mm bars in a 6 m beam run 40 mm short of each end, sit 40 mm above the bottom and spread inside the 40 mm side cover");
        var top = session.AddMemberRebar(beam, 2, 0.012, top: true);
        check(session.Document.StructuralElements.Where(item => top.Contains(item.Id)).All(bar => Near(bar.Offset, 2.5 + 0.5 - 0.04 - 0.006, 1e-9)), "Top bars sit under the top face by the cover");
        session.SetCover(beam, 0.06);
        var more = session.AddMemberRebar(beam, 2, 0.012);
        check(session.Document.StructuralElements.Where(item => more.Contains(item.Id)).All(bar => Near(bar.Length, 6 - 0.12, 1e-9)) && Near(session.CoverOf(beam), 0.06),
            "Giving the beam its own cover of 60 mm makes the next bars 60 mm short of each end");
        session.SetCover(beam, null);
        check(Near(session.CoverOf(beam), 0.04), "Taking the own cover off returns the project's");
        check(Throws(() => session.AddMemberRebar(beam, 0, 0.012)) && Throws(() => session.AddMemberRebar(beam, 30, 0.012)) && Throws(() => session.AddMemberRebar(beam, 10, 0.05)) &&
              Throws(() => session.AddMemberRebar(Guid.NewGuid(), 2, 0.012)) && Throws(() => session.SetCover(beam, 0.5)) && Throws(() => session.SetCover(Guid.NewGuid(), 0.04)),
            "No bars, too many, bars that do not fit, a host that does not exist and a silly cover are refused");

        // Bars by path and by area.
        var path = session.AddRebarPath(level, [new Point2(700, 700), new Point2(700.3, 700), new Point2(700.3, 700.4), new Point2(700, 700.4)], 0.01, 1.0);
        check(path.Count == 3 && Throws(() => session.AddRebarPath(level, [new Point2(0, 0), new Point2(1, 0)], 0.01, 1)) && Throws(() => session.AddRebarPath(level, [new Point2(0, 0), new Point2(0.001, 0), new Point2(1, 1)], 0.01, 1)),
            "A bar around a stirrup path is three joined pieces; fewer than three points or points too close are refused");
        var area = session.AddRebarArea(level, new Point2(720, 720), new Point2(725, 724), 0.2, 0.012, alongX: true, 0.05);
        var inArea = session.Document.StructuralElements.Where(item => area.Contains(item.Id)).ToArray();
        check(inArea.Length == (int)Math.Floor((4 - 2 * 0.025) / 0.2) + 1 && inArea.All(bar => bar.Start.X >= 720.025 - 1e-9 && bar.End.X <= 725 - 0.025 + 1e-9 && Near(bar.Start.Y, bar.End.Y)),
            $"A 5 by 4 m slab gets {inArea.Length} bars every 20 cm, all inside the 25 mm cover");

        // Fabric.
        var wires = new WireSpec(0.006, 0.15);
        var sheet = session.AddFabricSheet(level, new Point2(740, 740), new Point2(746, 740), 2.4, wires);
        var tiles = session.AddFabricArea(level, new Point2(760, 760), new Point2(771.8, 764.6), wires);
        check(tiles.Count == 4 && session.Document.StructuralElements.Single(item => item.Id == sheet).Width == 2.4 && session.Document.StructuralElements.Where(item => tiles.Contains(item.Id)).All(item => item.GroupId == session.Document.StructuralElements.Single(i => i.Id == tiles[0]).GroupId),
            "An area of 11.8 by 4.6 m is covered with 2 by 2 sheets of 6 by 2.4 m, lapped by 20 cm, in one group");
        var nineSheets = session.AddFabricArea(level, new Point2(780, 780), new Point2(792, 785), wires);
        check(nineSheets.Count == 9, "A bigger area, 12 by 5 m, needs 3 by 3 sheets because of the laps");
        check(Throws(() => session.AddFabricSheet(level, new Point2(0, 0), new Point2(0.1, 0), 2.4, wires)) && Throws(() => session.AddFabricSheet(level, new Point2(0, 0), new Point2(6, 0), 2.4, new WireSpec(0.0001, 0.15))) &&
              Throws(() => session.AddFabricArea(level, new Point2(0, 0), new Point2(0.1, 0.1), wires)) && Throws(() => session.AddFabricArea(level, new Point2(0, 0), new Point2(2000, 2000), wires)),
            "A sheet that is too short, wires that make no sense, an area too small or one needing over 400 sheets is refused");

        // The reports.
        var report = ReinforcementReport.Build(session.Document);
        var expectedBarWeight = session.Document.StructuralElements.Where(item => item.Kind == StructuralElementKind.Rebar).Sum(bar => bar.Length * Math.PI * bar.Width * bar.Width / 4 * 7850);
        check(Near(report.BarWeight, expectedBarWeight, 1e-6) && report.Bars.Sum(mark => mark.Count) == session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.Rebar),
            $"The bars are counted by diameter and length and weigh {report.BarWeight:0.0} kg, the sum of every bar's length times its section times the density");
        var sheetsArea = session.Document.StructuralElements.Where(item => item.Kind == StructuralElementKind.FabricSheet).Sum(item => item.Length * item.Width);
        var sheetsWeight = session.Document.StructuralElements.Where(item => item.Kind == StructuralElementKind.FabricSheet)
            .Sum(item => 2 * item.Length * item.Width / item.Wires!.Spacing * Math.PI * item.Wires.Diameter * item.Wires.Diameter / 4 * 7850);
        check(Near(report.FabricArea, sheetsArea, 1e-9) && Near(report.FabricWeight, sheetsWeight, 1e-6) && Near(report.TotalWeight, report.BarWeight + report.FabricWeight, 1e-9),
            "The fabric weighs 2 wires of length equal to the area over the spacing, and the total is bars plus fabric");
        var csv = report.ToCsv();
        check(csv.Split('\n').Count(line => line.StartsWith('B')) == report.Bars.Count && csv.Contains("Total;") && csv.StartsWith("Marca;", StringComparison.Ordinal), "The table has a line for each bar mark, then the fabric and the total");
        var cuts = report.ToCutList("Casa del cobogó", "Grado 60").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        check(cuts.Length == report.Bars.Count && cuts.All(line => line.StartsWith("BF2D@Hj", StringComparison.Ordinal) && line.TrimEnd().EndsWith('@')) &&
              cuts.All(line => { var body = line[..line.LastIndexOf("C", StringComparison.Ordinal)]; return line.Contains($"C{ReinforcementReport.Checksum(body)}@"); }),
            "The cutting list has a BF2D line for each mark, each ending with its checksum");

        // Settings.
        session.SetReinforcementSettings(session.Document.Reinforcement with { BeamCover = 0.05 });
        check(Near(session.CoverOf(beam), 0.05) && Throws(() => session.SetReinforcementSettings(session.Document.Reinforcement with { BeamCover = 0.5 })) &&
              Throws(() => session.SetPrecastSettings(new PrecastSettings(0.1, "P", 0.01))) && Throws(() => session.SetConcreteSettings(new ConcreteSettings("", 2400, 100))),
            "Changing the beam cover in the project changes every beam without its own; settings out of range are refused");
        session.SetReinforcementSettings(ReinforcementSettings.Default);

        // Precast panels.
        var wallId = session.AddWall(level, new Point2(600, 600), new Point2(609, 600), 0.2, 3);
        var panels = session.DivideWallIntoPanels(wallId);
        var pieces = session.Document.Walls.Where(wall => panels.Contains(wall.Id)).OrderBy(wall => wall.Start.X).ToArray();
        check(panels.Count == 4 && pieces.All(piece => Near(piece.Length, 9.0 / 4, 1e-9)) && Near(pieces[0].Start.X, 600) && Near(pieces[^1].End.X, 609) && pieces.Zip(pieces.Skip(1)).All(pair => pair.First.End == pair.Second.Start) &&
              session.Document.ElementGroups.Any(group => group.Kind == ElementGroupKind.Assembly && group.Name.StartsWith("P-", StringComparison.Ordinal) && group.MemberIds.Count == 4),
            "A 9 m wall with panels of 2.4 m at most becomes 4 panels of 2.25 m, joined end to end, gathered in an assembly");
        var panelBars = session.AddPanelReinforcement(panels);
        check(panelBars.Count == 8 && session.Document.StructuralElements.Where(item => panelBars.Contains(item.Id)).All(bar => Near(bar.Width, 0.010) && Near(bar.Offset, 1.5)), "Each panel gets two 10 mm bars at mid height");
        var tooSmall = session.AddWall(level, new Point2(620, 600), new Point2(622, 600), 0.2, 3);
        check(Throws(() => session.DivideWallIntoPanels(tooSmall)) && Throws(() => session.DivideWallIntoPanels(Guid.NewGuid())) && Throws(() => session.AddPanelReinforcement([Guid.NewGuid()])),
            "A wall no wider than a panel, or one that does not exist, is not divided");

        var file = Path.Combine(Path.GetTempPath(), $"concrete-{Guid.NewGuid():N}.g3d");
        session.SetCover(beam, 0.07);
        ProjectFile.Save(file, session.Document);
        var reopened = ProjectFile.Load(file);
        File.Delete(file);
        check(reopened.CoverOverrides.Count == 1 && reopened.StructuralElements.Any(item => item.Kind == StructuralElementKind.FabricSheet && item.Wires is { Diameter: 0.006 }) &&
              reopened.StructuralElements.Count(item => item.Kind == StructuralElementKind.Rebar) == session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.Rebar),
            "Bars, fabric with its wires and the covers survive saving and opening the project");
        check(BuildingMesher.Build(session.Document).Positions.All(point => float.IsFinite(point.X + point.Y + point.Z)), "The house with all its reinforcement still meshes");
        session.RemoveStructuralElement(beam);
        check(!session.Document.CoverOverrides.Any(item => item.Id == beam), "Deleting the beam forgets its cover");
    }
}
