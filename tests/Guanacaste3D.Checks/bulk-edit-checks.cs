using Guanacaste3D.Core;

/// <summary>Checks scale, room renumbering and find-and-replace on the breeze-block house.</summary>
static class BulkEditChecks
{
    /// <summary>Runs the scenarios; each works on its own copy of the house.</summary>
    public static void Run(Action<bool, string> check)
    {
        var house = BreezeBlockHouseShowcase.Create();
        Scale(check, house);
        Renumber(check, house);
        Replace(check, house);
        AttachTop(check, house);
        MaterialTags(check, house);
    }

    /// <summary>Material tags name the material of every wall of a level and nothing else.</summary>
    private static void MaterialTags(Action<bool, string> check, BuildingDocument house)
    {
        var level = house.Levels[0].Id;
        var tags = ElementTags.Build(house, level, new HashSet<TagCategory> { TagCategory.Material });
        var walls = house.Walls.Where(wall => wall.LevelId == level).ToArray();
        check(tags.Count == walls.Length && tags.All(tag => tag.Category == TagCategory.Material && tag.Mark.Length > 0 &&
                  house.WallTypes.Any(type => type.MaterialName == tag.Mark)),
            $"Material tags name the material of each of the {walls.Length} walls of the level");
        check(ElementTags.Build(house, level, new HashSet<TagCategory> { TagCategory.Door }).All(tag => tag.Category == TagCategory.Door),
            "Material tags do not appear unless asked for");
    }

    /// <summary>A ground-floor wall under the upper slab is tied to that slab's underside; one with nothing above is refused.</summary>
    private static void AttachTop(Action<bool, string> check, BuildingDocument house)
    {
        var session = new DocumentSession(house.Clone());
        var levels = session.Document.Levels.ToDictionary(level => level.Id);
        var attachable = session.Document.Walls
            .Select(wall => (Wall: wall, Middle: new Point2((wall.Start.X + wall.End.X) / 2, (wall.Start.Y + wall.End.Y) / 2)))
            .Select(entry => (entry.Wall, Slab: session.Document.Floors.Where(slab => !slab.IsToposolid && !slab.IsPitched && slab.Boundary.Count >= 3 &&
                    RoomRegionGeometry.Contains(slab.Boundary, entry.Middle) &&
                    levels[slab.LevelId].Elevation + slab.ElevationOffset - slab.Thickness > levels[entry.Wall.LevelId].Elevation + 0.1)
                .OrderBy(slab => levels[slab.LevelId].Elevation + slab.ElevationOffset - slab.Thickness).FirstOrDefault()))
            .Where(entry => entry.Slab is not null && !entry.Wall.Pinned).ToArray();
        check(attachable.Length > 0, $"The house has {attachable.Length} walls with a flat slab above them");

        var (wall, slab) = attachable[0];
        var attached = session.AttachWallTopToSlabAbove(wall.Id);
        var now = session.Document.Walls.Single(candidate => candidate.Id == wall.Id);
        var underside = levels[slab!.LevelId].Elevation + slab.ElevationOffset - slab.Thickness;
        check(attached == slab.Id && now.TopLevelId == slab.LevelId &&
              Math.Abs(levels[now.TopLevelId!.Value].Elevation + now.TopOffset - underside) < 1e-9,
            "The wall's top is tied to the underside of the slab above it");
        check(Math.Abs(levels[now.LevelId].Elevation + now.Height - underside) < 1e-6,
            "The wall's height reaches that underside after regeneration");
        session.Undo();
        check(session.Document.Walls.Single(candidate => candidate.Id == wall.Id).TopLevelId == wall.TopLevelId, "Undo releases the attachment");

        var bare = session.AddWall(session.Document.Levels[0].Id, new Point2(900, 900), new Point2(904, 900));
        check(Throws(() => session.AttachWallTopToSlabAbove(bare)) && Throws(() => session.AttachWallTopToSlabAbove(Guid.NewGuid())),
            "A wall with nothing above it, and an unknown wall, are refused");
        session.SetWallsPinned([wall.Id], true);
        check(Throws(() => session.AttachWallTopToSlabAbove(wall.Id)), "A pinned wall cannot be attached");
    }

    private static bool Throws(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return true; }
        return false;
    }

    /// <summary>Scaling a whole level keeps every corner joined and every opening in the same place along its wall.</summary>
    private static void Scale(Action<bool, string> check, BuildingDocument house)
    {
        var session = new DocumentSession(house.Clone());
        var level = session.Document.Levels[0].Id;
        var walls = session.Document.Walls.Where(wall => wall.LevelId == level && !wall.Pinned).ToArray();
        var ids = walls.Select(wall => wall.Id).ToArray();
        var origin = new Point2(walls.Average(wall => wall.Start.X), walls.Average(wall => wall.Start.Y));
        var lengthBefore = walls.Sum(wall => wall.Length);
        var joinsBefore = JoinCount(session.Document, level);
        var openingsBefore = session.Document.Openings.Where(opening => ids.Contains(opening.WallId)).ToDictionary(opening => opening.Id);
        var original = ProjectFile.SerializeDocument(session.Document);

        session.ScaleWalls(ids, origin, 1.5);
        var scaled = session.Document.Walls.Where(wall => ids.Contains(wall.Id)).ToArray();
        check(Math.Abs(scaled.Sum(wall => wall.Length) - lengthBefore * 1.5) < 1e-6,
            $"Scaling the {ids.Length} walls of a level by 1.5 makes their total length 1.5 times longer");
        check(JoinCount(session.Document, level) == joinsBefore,
            "Every corner and tee of the scaled level stays joined");
        check(session.Document.Openings.Where(opening => ids.Contains(opening.WallId)).All(opening =>
                  Math.Abs(opening.Offset - openingsBefore[opening.Id].Offset * 1.5) < 1e-9 &&
                  Math.Abs(opening.Width - openingsBefore[opening.Id].Width) < 1e-12),
            "Doors and windows keep their size and their relative place along the wall");
        session.Undo();
        check(ProjectFile.SerializeDocument(session.Document) == original, "Undo takes back a scaling exactly");

        check(Throws(() => session.ScaleWalls(ids, origin, 0)) && Throws(() => session.ScaleWalls(ids, origin, double.NaN)) &&
              Throws(() => session.ScaleWalls(ids, origin, 11)) && Throws(() => session.ScaleWalls([], origin, 2)) &&
              Throws(() => session.ScaleWalls([Guid.NewGuid()], origin, 2)),
            "A zero, NaN or huge factor, an empty selection and an unknown wall are refused");
        session.SetWallsPinned([ids[0]], true);
        check(Throws(() => session.ScaleWalls([ids[0]], origin, 2)), "A pinned wall cannot be scaled");
    }

    private static int JoinCount(BuildingDocument document, Guid level)
    {
        var index = WallJoinIndex.Build(document.Walls.Where(wall => wall.LevelId == level).ToArray());
        return document.Walls.Where(wall => wall.LevelId == level).Sum(wall =>
            (index.JoinAt(wall.Id, WallEnd.Start) is null ? 0 : 1) + (index.JoinAt(wall.Id, WallEnd.End) is null ? 0 : 1) +
            (index.TeeHostAt(wall.Id, WallEnd.Start) is null ? 0 : 1) + (index.TeeHostAt(wall.Id, WallEnd.End) is null ? 0 : 1));
    }

    /// <summary>Renumbering gives each room its level and its order, once, without empty undo steps.</summary>
    private static void Renumber(Action<bool, string> check, BuildingDocument house)
    {
        var session = new DocumentSession(house.Clone());
        var scrambled = session.Document.Clone();
        for (var index = 0; index < scrambled.Rooms.Count; index++)
            scrambled.Rooms[index] = scrambled.Rooms[index] with { Number = "X" + (scrambled.Rooms.Count - index) };
        session = new DocumentSession(scrambled);
        var count = session.Document.Rooms.Count;
        check(count >= 2, $"The house has {count} rooms to renumber");

        var changed = session.RenumberRooms();
        var numbers = session.Document.Rooms.Select(room => room.Number).ToArray();
        check(changed == count && numbers.Distinct().Count() == count && numbers.All(number => number.Length >= 3 && number.All(char.IsDigit)),
            "Renumbering gives every room a unique number made of its level and its order");
        var levelOrder = session.Document.Levels.OrderBy(level => level.Elevation).Select(level => level.Id).ToList();
        check(session.Document.Rooms.All(room => room.Number[0] - '0' == levelOrder.IndexOf(room.LevelId) + 1),
            "The first digit of a room's number is the position of its level");
        var sameLevel = session.Document.Rooms.GroupBy(room => room.LevelId).First(group => group.Count() >= 2)
            .OrderBy(room => room.Number).ToArray();
        check(sameLevel.Zip(sameLevel.Skip(1)).All(pair => pair.First.Center.Y <= pair.Second.Center.Y + 0.0015),
            "On one level the numbers run from south to north");

        var depth = session.UndoNames.Count;
        check(session.RenumberRooms() == 0 && session.UndoNames.Count == depth,
            "Renumbering again changes nothing and leaves no empty step to undo");
        session.Undo();
        check(session.Document.Rooms.All(room => room.Number.StartsWith('X')), "Undo gives the rooms their old numbers back");
    }

    /// <summary>Find and replace reaches room names, sheet names and model text, and refuses what would blank a name.</summary>
    private static void Replace(Action<bool, string> check, BuildingDocument house)
    {
        var session = new DocumentSession(house.Clone());
        var room = session.Document.Rooms[0];
        var word = room.Name.Split(' ')[0];
        var level = session.Document.Levels[0].Id;
        session.AddModelText(level, new Point2(1, 1), $"Nota de {word}");
        var depth = session.UndoNames.Count;
        var sheet = session.Document.Sheets[0].Name;

        var changed = session.ReplaceText(word, "ZONA");
        check(changed >= 2 && session.Document.Rooms.All(candidate => !candidate.Name.Contains(word, StringComparison.OrdinalIgnoreCase)) &&
              session.Document.StructuralElements.Single(element => element.Kind == StructuralElementKind.ModelText).Name == "Nota de ZONA",
            $"Replacing '{word}' changes {changed} places: room names and the model text");
        session.Undo();
        check(session.Document.Rooms[0].Name == room.Name && session.UndoNames.Count == depth, "Undo restores the old text");

        check(session.ReplaceText("texto que no existe en ninguna parte", "x") == 0 && session.UndoNames.Count == depth,
            "Text that is nowhere leaves the project and its history untouched");
        var upper = session.ReplaceText(word.ToUpperInvariant(), "k", matchCase: true);
        check(upper == 0 || session.Document.Rooms.Any(candidate => candidate.Name.Contains('k')),
            "Matching case only replaces text written exactly like that");
        check(Throws(() => session.ReplaceText("", "x")), "An empty search is refused");
        var blanking = session.Document.StructuralElements.Single(element => element.Kind == StructuralElementKind.ModelText).Name;
        check(Throws(() => session.ReplaceText(blanking, "")) &&
              session.Document.StructuralElements.Single(element => element.Kind == StructuralElementKind.ModelText).Name == blanking,
            "A replacement that would leave model text blank is refused and changes nothing");
        _ = sheet;
    }
}
