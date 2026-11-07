using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Interop;

/// <summary>
/// Property-based checks for the wall editing commands. Instead of one hand-written scenario per command, it builds
/// random layouts (corners, tees, free walls, angled walls, doors) and applies random sequences of commands, including
/// hostile arguments, then checks the rules that must hold after every step:
/// a refused command changes nothing, an accepted one leaves a valid project that survives saving, undo and redo are
/// exact, and the geometry stays finite. Every failure names its seed and step, so it can be replayed.
/// </summary>
static class RandomizedWallChecks
{
    private const int DefaultSeeds = 24;
    private const int StepsPerSeed = 45;

    /// <summary>How many times each command was accepted and changed the project, so the run cannot pass by being refused.</summary>
    private static readonly Dictionary<string, int> Accepted = [];

    /// <summary>Runs the random sequences and the targeted random properties.</summary>
    public static void Run(Action<bool, string> check)
    {
        var seeds = int.TryParse(Environment.GetEnvironmentVariable("G3D_FUZZ_SEEDS"), out var requested) && requested > 0
            ? requested : DefaultSeeds;

        var farFailures = new List<string>();
        foreach (var distance in new[] { 0.0, 1e3, 1e5, 1e6, 5e6, 1e7 })
        {
            try
            {
                var far = new DocumentSession(BuildingDocument.Create());
                var farLevel = far.Document.Levels[0].Id;
                var wall = far.AddWall(farLevel, new Point2(distance, distance), new Point2(distance + 5, distance));
                far.AddWall(farLevel, new Point2(distance + 5, distance), new Point2(distance + 5, distance + 3));
                far.AddOpening(wall, OpeningKind.Door, offset: 2);
                CheckGeometry(far.Document, mesh: true);
            }
            catch (Exception exception)
            {
                farFailures.Add($"{distance:0.##E+0} m from the origin: {exception.GetType().Name}: {exception.Message}");
            }
        }
        check(farFailures.Count == 0,
            "Walls far from the origin, such as survey coordinates, still mesh" +
            (farFailures.Count == 0 ? "" : ":\n  " + string.Join("\n  ", farFailures)));

        var failures = new Dictionary<string, string>();
        var steps = 0;
        for (var seed = 1; seed <= seeds; seed++) steps += RunSequence(seed, failures);
        check(failures.Count == 0,
            $"{steps} random wall commands over {seeds} seeds keep the project valid, atomic and exactly undoable" +
            (failures.Count == 0 ? "" : ":\n  " + string.Join("\n  ", failures.Values.Take(8))));

        var neverAccepted = Operations.Select(operation => operation.Name).Where(name => Accepted.GetValueOrDefault(name) == 0).ToArray();
        check(neverAccepted.Length == 0,
            $"Every random command is accepted at least once, so none is only ever refused ({Accepted.Values.Sum()} accepted of {steps})" +
            (neverAccepted.Length == 0 ? "" : ": never accepted " + string.Join(", ", neverAccepted)));

        var propertyFailures = new List<string>();
        for (var seed = 1; seed <= seeds * 4; seed++) Properties(seed, propertyFailures);
        check(propertyFailures.Count == 0,
            $"Random walls keep the algebraic properties of the editing commands over {seeds * 4} seeds" +
            (propertyFailures.Count == 0 ? "" : ":\n  " + string.Join("\n  ", propertyFailures.Take(8))));
    }

    // ------------------------------------------------------------------ random sequences

    private static int RunSequence(int seed, Dictionary<string, string> failures)
    {
        var random = new Random(seed);
        var session = new DocumentSession(House().Clone());
        var houseJson = ProjectFile.SerializeDocument(session.Document);
        var level = session.Document.Levels[0].Id;
        var upper = session.Document.Levels.Count > 1 ? session.Document.Levels[1].Id : session.AddLevel();
        for (var i = 0; i < 2; i++)
        {
            try { AddRandomWall(random, session, level); }
            catch (InvalidDataException) { /* a hostile type id is refused; the sequence starts with fewer walls */ }
        }

        var steps = 0;
        for (var step = 0; step < StepsPerSeed; step++)
        {
            var (name, run) = Operations[random.Next(Operations.Length)];
            var before = ProjectFile.SerializeDocument(session.Document);
            var undoable = session.CanUndo;
            steps++;
            string Where() => $"seed {seed} step {step} {name}";
            void Fail(string kind, string detail) => failures.TryAdd(name + kind, $"{Where()}: {kind} {detail}");

            try
            {
                run(random, session, level, upper);
            }
            catch (InvalidDataException)
            {
                if (ProjectFile.SerializeDocument(session.Document) != before) Fail("not atomic:", "a refused command changed the project");
                continue;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Fail("unexpected exception:", $"{exception.GetType().Name}: {exception.Message}");
                break;
            }

            var after = ProjectFile.SerializeDocument(session.Document);
            if (after == before) continue;
            Accepted[name] = Accepted.GetValueOrDefault(name) + 1;
            try
            {
                session.Document.Validate();
                if (ProjectFile.SerializeDocument(ProjectFile.DeserializeDocument(after)) != after)
                    Fail("round trip:", "saving and reopening changed the project");
                CheckGeometry(session.Document, step % 5 == 0);
                if (Accepted.Values.Sum() % 6 == 0) CheckExports(session.Document);
            }
            catch (Exception exception)
            {
                Fail("invalid result:", $"{exception.GetType().Name}: {exception.Message} @ {Frames(exception)}");
                break;
            }

            if (!session.CanUndo) { Fail("undo:", "a change left nothing to undo"); break; }
            session.Undo();
            if (ProjectFile.SerializeDocument(session.Document) != before) { Fail("undo:", "did not restore the project exactly"); break; }
            session.Redo();
            if (ProjectFile.SerializeDocument(session.Document) != after) { Fail("redo:", "did not reapply the change exactly"); break; }
            _ = undoable;
        }
        // Whatever the sequence did, undoing all of it brings the house back exactly as it was drawn.
        // Undo keeps the last 40 steps, so the house is only reachable while the history has not been trimmed.
        var trimmed = session.UndoNames.Count >= 40;
        var guard = 0;
        while (session.CanUndo && guard++ < 10000) session.Undo();
        if (!trimmed && ProjectFile.SerializeDocument(session.Document) != houseJson)
            failures.TryAdd("undo all", $"seed {seed}: undoing every command did not bring the house back exactly");
        return steps;
    }

    /// <summary>
    /// Every exporter must cope with whatever the commands produce: angled walls, tees, butt joins, pinned and
    /// flipped walls. Output must be repeatable and must not contain a number the receiving program cannot read.
    /// </summary>
    private static void CheckExports(BuildingDocument document)
    {
        var stamp = new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        var texts = new Dictionary<string, Func<string>>
        {
            ["IFC"] = () => IfcExporter.ToStep(document, stamp),
            ["DXF"] = () => DxfExporter.ToAscii(document),
            ["quantities"] = () => QuantitySchedule.ToCsv(document),
            ["wall layers"] = () => WallLayerQuantitySchedule.ToCsv(document),
            ["roofs"] = () => RoofQuantitySchedule.ToCsv(document),
            ["reveals"] = () => WallOpeningRevealSchedule.ToCsv(document)
        };
        foreach (var (name, export) in texts)
        {
            var first = export();
            if (first != export()) throw new InvalidDataException($"the {name} export is not repeatable");
            // Text between quotes is not a number, and an IFC global id is a random string that contains "NaN" now and then.
            var numbers = System.Text.RegularExpressions.Regex.Replace(first, "'[^']*'", "''");
            foreach (var marker in new[] { "NaN", "Infinity", "∞" })
                if (numbers.IndexOf(marker, StringComparison.Ordinal) is var at and >= 0)
                    throw new InvalidDataException($"the {name} export contains a number that is not finite: ...{numbers.Substring(Math.Max(0, at - 90), Math.Min(numbers.Length - Math.Max(0, at - 90), 150))}...");
        }
        var ifc = texts["IFC"]();
        var wallEntities = ifc.Split((char)10).Count(line => line.Contains("=IFCWALL", StringComparison.Ordinal));
        if (wallEntities < document.Walls.Count)
            throw new InvalidDataException($"the IFC export has {wallEntities} walls for {document.Walls.Count} in the project");

        var mesh = BuildingMesher.Build(document);
        var (obj, material) = MeshExporter.ToObj(mesh, "materials.mtl");
        if (obj.Contains("NaN") || material.Contains("NaN")) throw new InvalidDataException("the OBJ export contains NaN");
        if (!MeshExporter.ToGlb(mesh, "random").AsSpan().SequenceEqual(MeshExporter.ToGlb(mesh, "random")))
            throw new InvalidDataException("the GLB export is not repeatable");
        if (PdfPlanExporter.Export(document, SheetPaperSize.A3, stamp).Length == 0)
            throw new InvalidDataException("the PDF export is empty");
        _ = ModelWarnings.Analyze(document);
    }

    /// <summary>The first frames of an exception, so a failure points at the code that raised it.</summary>
    private static string Frames(Exception exception) =>
        string.Join(" < ", (exception.StackTrace ?? string.Empty).Split((char)10).Take(5)
            .Select(line => line.Trim().Split(" in ")[0]));

    /// <summary>The geometry of a result must be finite, whatever commands made it.</summary>
    private static void CheckGeometry(BuildingDocument document, bool mesh)
    {
        var joins = WallJoinIndex.Build(document.Walls);
        foreach (var wall in document.Walls)
        {
            var footprint = joins.Footprint(wall);
            var points = new[] { footprint.StartLeft, footprint.EndLeft, footprint.EndRight, footprint.StartRight };
            if (points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
                throw new InvalidDataException("a wall footprint is not finite");
            var area = 0.0;
            // Relative to the first corner: absolute coordinates would cancel to nothing far from the origin.
            for (var i = 1; i < points.Length - 1; i++)
                area += (points[i].X - points[0].X) * (points[i + 1].Y - points[0].Y) -
                        (points[i + 1].X - points[0].X) * (points[i].Y - points[0].Y);
            if (Math.Abs(area) / 2 < 1e-9)
                throw new InvalidDataException(
                    $"a wall footprint has no area: wall {wall.Start}→{wall.End} t={wall.Thickness:0.###} " +
                    $"joins {wall.StartJoin}/{wall.EndJoin} corners {string.Join(" ", points)} " +
                    $"startJoin={joins.JoinAt(wall.Id, WallEnd.Start)?.Other.Id} endJoin={joins.JoinAt(wall.Id, WallEnd.End)?.Other.Id} " +
                    $"tees={joins.TeeHostAt(wall.Id, WallEnd.Start)?.Id}/{joins.TeeHostAt(wall.Id, WallEnd.End)?.Id}");
        }
        if (!mesh) return;
        var scene = BuildingMesher.Build(document);
        if (scene.Positions.Any(position => !float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z)))
            throw new InvalidDataException("the mesh has a vertex that is not finite");
        if (scene.Indices.Any(index => index < 0 || index >= scene.Positions.Count))
            throw new InvalidDataException("the mesh has an index outside its vertices");
    }

    private delegate void Operation(Random random, DocumentSession session, Guid level, Guid upper);

    private static readonly (string Name, Operation Run)[] Operations =
    [
        ("AddWall", (r, s, l, _) => AddRandomWall(r, s, l)),
        ("AddArcWall", (r, s, l, _) => s.AddArcWall(l, Point(r), Point(r), Point(r), Number(r, 0.5, 6), Type(r, s))),
        ("UpdateWallType", (r, s, _, _) => s.UpdateWallType(Type(r, s), "T", WallFunction.Interior, Thickness(r), "M")),
        ("SetWallType", (r, s, _, _) => s.SetWallType(AnyWall(r, s), Type(r, s))),
        ("UpdateWall", (r, s, _, _) => s.UpdateWall(AnyWall(r, s), Thickness(r), Number(r, 0.1, 6))),
        ("MirrorWalls", (r, s, _, _) => s.MirrorWalls(Some(r, s), Point(r), Point(r), r.Next(2) == 0)),
        ("OffsetWalls", (r, s, _, _) => s.OffsetWalls(Some(r, s), Number(r, -3, 3), r.Next(2) == 0)),
        ("ArrayLinear", (r, s, _, _) => s.ArrayWallsLinear(Some(r, s), r.Next(0, 6), Number(r, -4, 4), Number(r, -4, 4))),
        ("ArrayRadial", (r, s, _, _) => s.ArrayWallsRadial(Some(r, s), r.Next(0, 6), Point(r), Number(r, -120, 120))),
        ("SplitWall", (r, s, _, _) => s.SplitWall(AnyWall(r, s), PointOn(r, s))),
        ("TrimToCorner", (r, s, _, _) => s.TrimToCorner(AnyWall(r, s), Point(r), AnyWall(r, s), Point(r))),
        ("AlignWall", (r, s, _, _) => AlignRandomly(r, s)),
        ("FlipWalls", (r, s, _, _) => s.FlipWalls(Some(r, s))),
        ("PinWalls", (r, s, _, _) => s.SetWallsPinned(Some(r, s), r.Next(2) == 0)),
        ("SetWallJoins", (r, s, _, _) => s.SetWallJoins(Some(r, s), (WallJoinSetting)r.Next(0, 5), r.Next(3) switch { 0 => WallEnd.Start, 1 => WallEnd.End, _ => null })),
        ("MoveWall", (r, s, _, _) => s.MoveWall(AnyWall(r, s), Number(r, -3, 3), Number(r, -3, 3))),
        ("RotateWall", (r, s, _, _) => s.RotateWall(AnyWall(r, s), Number(r, -200, 200))),
        ("RemoveWall", (r, s, _, _) => s.RemoveWall(AnyWall(r, s))),
        ("DuplicateWall", (r, s, _, _) => s.DuplicateWall(AnyWall(r, s), Number(r, -3, 3), Number(r, -3, 3))),
        ("AddOpening", (r, s, _, _) => s.AddOpening(AnyWall(r, s), r.Next(2) == 0 ? OpeningKind.Door : OpeningKind.Window, Number(r, -1, 8))),
        ("PasteWalls", (r, s, l, u) => s.PasteWalls(WallClipboard.Capture(s.Document, Some(r, s)), r.Next(2) == 0 ? l : u, Number(r, -3, 3), Number(r, -3, 3))),
        ("MatchProperties", (r, s, _, _) => s.MatchWallProperties(AnyWall(r, s), Some(r, s))),
        ("TopConstraint", (r, s, _, u) => s.SetWallTopConstraint(AnyWall(r, s), r.Next(2) == 0 ? u : null, Number(r, -1, 1))),
        ("HideCategories", (r, s, _, _) => s.SetViewHiddenCategories(s.Document.Views[0].Id, Enumerable.Range(0, r.Next(0, 4)).Select(_ => (ViewCategory)r.Next(0, 17)).ToArray())),
    ];

    // ------------------------------------------------------------------ random inputs

    /// <summary>
    /// Random walls are almost never parallel, so most of the time this first makes a parallel copy of a wall and
    /// aligns the wall to it. Both steps are one undo step, as one command would be.
    /// </summary>
    private static void AlignRandomly(Random random, DocumentSession session)
    {
        var wall = AnyWall(random, session);
        if (random.Next(4) == 0 || session.Document.Walls.All(candidate => candidate.Id != wall))
        {
            session.AlignWall(wall, random.Next(-2, 3), AnyWall(random, session), random.Next(-2, 3));
            return;
        }
        using var group = session.BeginGroup("Align");
        var parallel = session.OffsetWalls([wall], Number(random, -4, 4), copy: true)[0];
        session.AlignWall(wall, random.Next(-1, 2), parallel, random.Next(-1, 2));
        group.Assimilate();
    }

    /// <summary>Mostly sensible numbers, now and then the values that break careless code.</summary>
    private static double Number(Random random, double low, double high) => random.Next(40) switch
    {
        0 => double.NaN,
        1 => double.PositiveInfinity,
        2 => 0,
        3 => 1e9,
        4 => -1e9,
        _ => low + random.NextDouble() * (high - low)
    };

    private static double Thickness(Random random) => random.Next(12) == 0 ? Number(random, 0, 1) : 0.06 + random.NextDouble() * 0.5;

    private static Point2 Point(Random random) => new(Number(random, -15, 15), Number(random, -15, 15));

    private static Guid Type(Random random, DocumentSession session) =>
        random.Next(15) == 0 ? Guid.NewGuid() : session.Document.WallTypes[random.Next(session.Document.WallTypes.Count)].Id;

    private static Guid AnyWall(Random random, DocumentSession session) =>
        session.Document.Walls.Count == 0 || random.Next(25) == 0
            ? Guid.NewGuid()
            : session.Document.Walls[random.Next(session.Document.Walls.Count)].Id;

    private static IReadOnlyCollection<Guid> Some(Random random, DocumentSession session) =>
        Enumerable.Range(0, random.Next(0, 4)).Select(_ => AnyWall(random, session)).ToArray();

    /// <summary>A point on a random wall's centerline, now and then a point off it.</summary>
    private static Point2 PointOn(Random random, DocumentSession session)
    {
        if (session.Document.Walls.Count == 0 || random.Next(10) == 0) return Point(random);
        var wall = session.Document.Walls[random.Next(session.Document.Walls.Count)];
        var t = random.NextDouble();
        return new Point2(wall.Start.X + (wall.End.X - wall.Start.X) * t, wall.Start.Y + (wall.End.Y - wall.Start.Y) * t);
    }

    /// <summary>Adds a free wall, a wall continuing another, or one resting on another's body, at a random angle.</summary>
    private static void AddRandomWall(Random random, DocumentSession session, Guid level)
    {
        var walls = session.Document.Walls;
        var angle = (random.Next(3) == 0 ? random.NextDouble() * 360 : random.Next(24) * 15) * Math.PI / 180;
        var length = random.Next(8) == 0 ? 0.05 + random.NextDouble() * 0.2 : 0.5 + random.NextDouble() * 10;
        var start = walls.Count == 0 || random.Next(3) == 0
            ? new Point2(Math.Round(random.NextDouble() * 30 - 15, 2), Math.Round(random.NextDouble() * 30 - 15, 2))
            : random.Next(2) == 0
                ? walls[random.Next(walls.Count)].End
                : PointOn(random, session);
        var end = new Point2(start.X + Math.Cos(angle) * length, start.Y + Math.Sin(angle) * length);
        session.AddWall(level, start, end, wallTypeId: Type(random, session),
            locationReference: (WallLocationReference)random.Next(0, 3));
    }

    // ------------------------------------------------------------------ properties on the real house

    /// <summary>
    /// Properties that hold for every wall of the breeze-block house, not for a wall made up for the test. Each seed
    /// takes a fresh copy of the house, so the walls are joined, tee-ed and pierced the way an architect drew them.
    /// </summary>
    private static void Properties(int seed, List<string> failures)
    {
        var random = new Random(seed * 7919);
        void Fail(string text) => failures.Add($"seed {seed}: {text}");
        var session = new DocumentSession(House().Clone());

        // A new thickness for a wall type: every wall not tied to an axis keeps the plane it was placed by (the walls
        // of that type) or both its faces (all others), and every opening keeps its place along its wall.
        var typeIds = session.Document.Walls.Select(wall => wall.WallTypeId).Distinct().OrderBy(id => id).ToArray();
        var typeId = typeIds[random.Next(typeIds.Length)];
        var type = session.Document.WallTypes.Single(candidate => candidate.Id == typeId);
        var before = session.Document.Walls.ToDictionary(wall => wall.Id);
        var openingsBefore = AlongWall(session.Document);
        var thickness = 0.06 + random.NextDouble() * 0.5;
        try
        {
            session.UpdateWallType(typeId, type.Name, type.Function, thickness, type.MaterialName);
            foreach (var wall in session.Document.Walls)
            {
                if (wall.GridAxisId is not null) continue;
                var old = before[wall.Id];
                foreach (var side in wall.WallTypeId == typeId
                             ? new[] { FixedSide(wall.LocationReference) }
                             : new[] { -1, 0, 1 })
                    if (Math.Abs(FacePlane(old, side) - FacePlane(wall, side)) > 1e-7)
                        Fail($"wall {wall.Id.ToString()[..8]} ({wall.LocationReference}) moved its plane {side} by {FacePlane(wall, side) - FacePlane(old, side):0.#########} m when type {type.Name} went {type.Thickness:0.###} to {thickness:0.###}");
            }
            foreach (var (id, station) in AlongWall(session.Document))
                if (openingsBefore.TryGetValue(id, out var was) && Math.Abs(was - station) > 1e-6)
                    Fail($"opening {id.ToString()[..8]} moved {station - was:0.#######} m along its wall when a wall type changed thickness");
        }
        catch (InvalidDataException)
        {
            // A finish thicker than the wall, or an opening that no longer fits: refusing is the right answer.
        }

        // Flipping twice and mirroring twice about one axis are the identity, for walls of the house.
        session = new DocumentSession(House().Clone());
        var pool = session.Document.Walls.Where(wall => !wall.Pinned).ToArray();
        var chosen = Enumerable.Range(0, random.Next(1, 4)).Select(_ => pool[random.Next(pool.Length)].Id).Distinct().ToArray();
        var snapshot = session.Document.Walls.Where(wall => chosen.Contains(wall.Id)).ToDictionary(wall => wall.Id);
        var openingSnapshot = session.Document.Openings.Where(opening => chosen.Contains(opening.WallId)).ToDictionary(opening => opening.Id);
        session.FlipWalls(chosen);
        session.FlipWalls(chosen);
        if (!SameWalls(session.Document, snapshot, openingSnapshot)) Fail("flipping the same walls twice did not restore them and their openings");
        var axisStart = new Point2(random.NextDouble() * 16, random.NextDouble() * 12);
        var axisEnd = new Point2(axisStart.X + 1 + random.NextDouble() * 6, axisStart.Y + random.NextDouble() * 6 - 3);
        session.MirrorWalls(chosen, axisStart, axisEnd, copy: false);
        session.MirrorWalls(chosen, axisStart, axisEnd, copy: false);
        if (!SameWalls(session.Document, snapshot, openingSnapshot)) Fail("mirroring the same walls twice about one axis did not restore them");

        // A split keeps the total length, every opening and the place of each opening in the model.
        session = new DocumentSession(House().Clone());
        var target = session.Document.Walls[random.Next(session.Document.Walls.Count)];
        var cut = 0.1 + random.NextDouble() * (target.Length - 0.2);
        var worldBefore = OpeningWorld(session.Document);
        var point = new Point2(target.Start.X + (target.End.X - target.Start.X) * cut / target.Length,
            target.Start.Y + (target.End.Y - target.Start.Y) * cut / target.Length);
        try
        {
            var second = session.SplitWall(target.Id, point);
            var parts = session.Document.Walls.Where(wall => wall.Id == target.Id || wall.Id == second).ToArray();
            if (Math.Abs(parts.Sum(wall => wall.Length) - target.Length) > 1e-8) Fail($"splitting wall {target.Id.ToString()[..8]} changed its total length");
            if (session.Document.Openings.Count != worldBefore.Count) Fail("splitting a wall lost or added an opening");
            var worldAfter = OpeningWorld(session.Document);
            foreach (var (id, was) in worldBefore)
                if (worldAfter.TryGetValue(id, out var now) && now.DistanceTo(was) > 1e-8)
                    Fail($"splitting wall {target.Id.ToString()[..8]} moved opening {id.ToString()[..8]}");
        }
        catch (InvalidDataException)
        {
            // The cut may fall within a door or window: refusing is the right answer.
        }

        // Copying a wall of the house and pasting it leaves the original alone, and undo takes the copy away exactly.
        session = new DocumentSession(House().Clone());
        var original = ProjectFile.SerializeDocument(session.Document);
        var source = session.Document.Walls[random.Next(session.Document.Walls.Count)];
        var dx = random.NextDouble() * 6 - 3;
        var dy = random.NextDouble() * 6 - 3;
        var pasted = session.PasteWalls(WallClipboard.Capture(session.Document, [source.Id]), source.LevelId, dx, dy)[0];
        var copy = session.Document.Walls.Single(wall => wall.Id == pasted);
        if (Math.Abs(copy.Start.X - source.Start.X - dx) > 1e-9 || Math.Abs(copy.End.Y - source.End.Y - dy) > 1e-9)
            Fail("a pasted wall is not the source wall moved by the requested distance");
        if (session.Document.Openings.Count(opening => opening.WallId == pasted) != session.Document.Openings.Count(opening => opening.WallId == source.Id))
            Fail("a pasted wall does not bring as many openings as its source");
        session.Undo();
        if (ProjectFile.SerializeDocument(session.Document) != original) Fail("undoing a paste did not restore the house exactly");
    }

    /// <summary>The plane a wall keeps fixed when it changes thickness: the right face, the left face or the centerline.</summary>
    private static int FixedSide(WallLocationReference reference) => reference switch
    {
        WallLocationReference.ExteriorFace => -1,
        WallLocationReference.InteriorFace => 1,
        _ => 0
    };

    /// <summary>Where each opening lies along its wall's direction, measured from the origin so a sliding start does not matter.</summary>
    private static Dictionary<Guid, double> AlongWall(BuildingDocument document)
    {
        var walls = document.Walls.ToDictionary(wall => wall.Id);
        return document.Openings.ToDictionary(opening => opening.Id, opening =>
        {
            var wall = walls[opening.WallId];
            var directionX = (wall.End.X - wall.Start.X) / wall.Length;
            var directionY = (wall.End.Y - wall.Start.Y) / wall.Length;
            return wall.Start.X * directionX + wall.Start.Y * directionY + opening.Offset;
        });
    }

    /// <summary>The model position of each opening's center on its wall's centerline.</summary>
    private static Dictionary<Guid, Point2> OpeningWorld(BuildingDocument document)
    {
        var walls = document.Walls.ToDictionary(wall => wall.Id);
        return document.Openings.ToDictionary(opening => opening.Id, opening =>
        {
            var wall = walls[opening.WallId];
            return new Point2(wall.Start.X + (wall.End.X - wall.Start.X) * opening.Offset / wall.Length,
                wall.Start.Y + (wall.End.Y - wall.Start.Y) * opening.Offset / wall.Length);
        });
    }

    /// <summary>Whether the listed walls and openings are back to the geometry they had, up to rounding.</summary>
    private static bool SameWalls(BuildingDocument document, Dictionary<Guid, Wall> walls, Dictionary<Guid, WallOpening> openings) =>
        walls.All(entry => document.Walls.SingleOrDefault(wall => wall.Id == entry.Key) is { } now &&
            now.Start.DistanceTo(entry.Value.Start) < 1e-7 && now.End.DistanceTo(entry.Value.End) < 1e-7 &&
            now.LocationReference == entry.Value.LocationReference && now.StartJoin == entry.Value.StartJoin &&
            now.EndJoin == entry.Value.EndJoin) &&
        openings.All(entry => document.Openings.SingleOrDefault(opening => opening.Id == entry.Key) is { } now &&
            Math.Abs(now.Offset - entry.Value.Offset) < 1e-7 && now.FlipHand == entry.Value.FlipHand &&
            now.FlipFacing == entry.Value.FlipFacing);

    /// <summary>The breeze-block house, built once; every sequence works on its own copy.</summary>
    private static BuildingDocument House() => _house ??= BreezeBlockHouseShowcase.Create();

    private static BuildingDocument? _house;

    /// <summary>Signed distance of a wall's centerline (side 0) or a face (-1 right, 1 left) from the origin along its left normal.</summary>
    private static double FacePlane(Wall wall, int side)
    {
        var normalX = -(wall.End.Y - wall.Start.Y) / wall.Length;
        var normalY = (wall.End.X - wall.Start.X) / wall.Length;
        return wall.Start.X * normalX + wall.Start.Y * normalY + side * wall.Thickness / 2;
    }
}
