using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Checks groups, assemblies and detail groups on the breeze-block house: create, move, copy, delete, save and load.</summary>
static class ElementGroupChecks
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
        var wallsOnLevel = session.Document.Walls.Where(wall => wall.LevelId == level).Take(5).ToArray();
        var withDoors = wallsOnLevel.Where(wall => session.Document.Openings.Any(opening => opening.WallId == wall.Id)).ToArray();
        var ids = wallsOnLevel.Select(wall => wall.Id).ToArray();
        var openingsOfGroup = session.Document.Openings.Count(opening => ids.Contains(opening.WallId));
        var totalWalls = session.Document.Walls.Count;
        var totalOpenings = session.Document.Openings.Count;
        var steps = session.UndoNames.Count;

        var group = session.CreateElementGroup(ids, "Fachada norte", ElementGroupKind.Group);
        check(session.UndoNames.Count == steps + 1 && session.Document.ElementGroups.Count == 1 &&
              session.GroupElements(group).Count == ids.Length + openingsOfGroup,
            $"A group of {ids.Length} walls is created in one undoable step, and its {openingsOfGroup} doors and windows are part of it");

        var before = wallsOnLevel.ToDictionary(wall => wall.Id, wall => (wall.Start, wall.End));
        var otherBefore = session.Document.Walls.Where(wall => !ids.Contains(wall.Id)).Select(wall => (wall.Id, wall.Start, wall.End)).ToArray();
        session.MoveElementGroup(group, 3, -2);
        var moved = session.Document.Walls.Where(wall => ids.Contains(wall.Id)).All(wall =>
            Near(wall.Start.X, before[wall.Id].Start.X + 3) && Near(wall.End.Y, before[wall.Id].End.Y - 2));
        var others = session.Document.Walls.Where(wall => !ids.Contains(wall.Id)).Select(wall => (wall.Id, wall.Start, wall.End)).SequenceEqual(otherBefore);
        check(moved && others, "Moving the group moves every wall in it by the same distance and leaves all the other walls where they were");
        session.MoveElementGroup(group, -3, 2);

        var copy = session.CopyElementGroup(group, 0, 12);
        var copied = session.Document.ElementGroups.Single(item => item.Id == copy);
        check(session.Document.Walls.Count == totalWalls + ids.Length && session.Document.Openings.Count == totalOpenings + openingsOfGroup &&
              copied.Name == "Fachada norte 2" && copied.MemberIds.Count == ids.Length && !copied.MemberIds.Intersect(ids).Any(),
            "Copying makes new walls and new doors and windows in a new group called «Fachada norte 2», and leaves the original alone");
        var copiedWalls = session.Document.Walls.Where(wall => copied.MemberIds.Contains(wall.Id)).ToArray();
        check(copiedWalls.Length == ids.Length && copiedWalls.All(wall => session.Document.Walls.Any(original =>
                  ids.Contains(original.Id) && Near(original.Start.X, wall.Start.X) && Near(original.Start.Y + 12, wall.Start.Y) && Near(original.End.Y + 12, wall.End.Y))),
            "Every copied wall sits exactly 12 m further along than its original");

        session.RemoveElements([ids[0]]);
        check(session.Document.Walls.Count == totalWalls && session.Document.Openings.Count == totalOpenings &&
              session.Document.ElementGroups.Count == 1 && session.Document.ElementGroups[0].Id == copy,
            "Deleting one wall of a group deletes the whole group, its doors and windows, and nothing else; the copy stays");
        session.Undo();
        check(session.Document.Walls.Count == totalWalls + ids.Length && session.Document.ElementGroups.Count == 2, "Undo brings the group back");

        // A wall can be in one group, and in one assembly as well.
        check(Throws(() => session.CreateElementGroup([ids[1]], "Otro", ElementGroupKind.Group)) &&
              Throws(() => session.CreateElementGroup(ids, "Fachada norte", ElementGroupKind.Group)) &&
              Throws(() => session.CreateElementGroup([], "Vacío", ElementGroupKind.Group)) &&
              Throws(() => session.CreateElementGroup([Guid.NewGuid()], "Fantasma", ElementGroupKind.Group)) &&
              Throws(() => session.CreateElementGroup([ids[1]], "   ", ElementGroupKind.Assembly)),
            "An element cannot be in two groups, names cannot repeat or be empty, and nothing can be grouped that does not exist");
        var free = session.Document.Walls.First(wall => wall.LevelId == level && !ids.Contains(wall.Id) && !copied.MemberIds.Contains(wall.Id)).Id;
        var assembly = session.CreateElementGroup([ids[1], free], "Panel prefabricado", ElementGroupKind.Assembly);
        session.MoveWall(free, 1, 0);
        session.RemoveElements([free]);
        check(session.Document.ElementGroups.Any(item => item.Id == assembly) && !session.Document.Walls.Any(wall => wall.Id == free) &&
              session.Document.Walls.Any(wall => wall.Id == ids[1]) && session.Document.ElementGroups.Single(item => item.Id == assembly).MemberIds.SequenceEqual([ids[1]]) &&
              session.Document.ElementGroups.Single(item => item.Id == group).MemberIds.Count == ids.Length,
            "A wall can be in a group and in an assembly; the assembly lets its walls be deleted one by one and the deleted one leaves it");

        var detail = session.AddStructuralElement(level, StructuralElementKind.DetailLine, "Línea", new Point2(900, 900), new Point2(903, 900));
        var region = session.AddStructuralElement(level, StructuralElementKind.FilledRegion, "Región", new Point2(900, 905), new Point2(903, 907));
        var beam = session.AddStructuralElement(level, StructuralElementKind.Beam, "Viga", new Point2(910, 900), new Point2(914, 900));
        check(Throws(() => session.CreateElementGroup([detail, beam], "Mezcla", ElementGroupKind.Detail)) &&
              session.CreateElementGroup([detail, region], "Cuadro de detalle", ElementGroupKind.Detail) != Guid.Empty,
            "A detail group takes drafting elements only");

        // Saved to a file and loaded into another project, doors and windows included.
        var text = session.WriteElementGroup(group);
        var other = new DocumentSession(BuildingDocument.Create("Otro proyecto"));
        var otherLevel = other.Document.Levels[0].Id;
        var loaded = other.LoadElementGroup(text, otherLevel, 1, 1);
        var loadedGroup = other.Document.ElementGroups.Single(item => item.Id == loaded);
        var survivors = session.Document.ElementGroups.Single(item => item.Id == group).MemberIds.Count;
        check(other.Document.Walls.Count == survivors && other.Document.Walls.All(wall => wall.LevelId == otherLevel) && other.Document.Openings.Count == session.Document.Openings.Count(opening => ids.Contains(opening.WallId)) &&
              loadedGroup.Name == "Fachada norte" && other.Document.WallTypes.Count >= BuildingDocument.Create().WallTypes.Count,
            $"The group saved to a file loads into an empty project: {other.Document.Walls.Count} walls, {other.Document.Openings.Count} doors and windows");
        other.Document.Validate();
        check(Throws(() => other.LoadElementGroup("{ nada }", otherLevel, 0, 0)) && Throws(() => other.LoadElementGroup("", otherLevel, 0, 0)) &&
              Throws(() => other.LoadElementGroup(text, Guid.NewGuid(), 0, 0)) && Throws(() => other.LoadElementGroup(text, otherLevel, double.NaN, 0)),
            "A file that is not a saved group, an unknown level and a bad shift are refused");
        var again = other.LoadElementGroup(text, otherLevel, 5, 5);
        check(other.Document.ElementGroups.Single(item => item.Id == again).Name == "Fachada norte 2", "Loading it again makes a second group with a name of its own");

        var file = Path.Combine(Path.GetTempPath(), $"groups-{Guid.NewGuid():N}.g3d");
        ProjectFile.Save(file, session.Document);
        var reopened = ProjectFile.Load(file);
        File.Delete(file);
        check(reopened.ElementGroups.Count == session.Document.ElementGroups.Count && reopened.ElementGroups.Any(item => item.Kind == ElementGroupKind.Assembly),
            "Groups, assemblies and detail groups survive saving and opening the project");
        check(BuildingMesher.Build(session.Document).Positions.All(position => float.IsFinite(position.X + position.Y + position.Z)),
            "The house with all its groups still meshes");

        session.Ungroup(assembly);
        session.RenameElementGroup(group, "Fachada sur");
        check(session.Document.ElementGroups.All(item => item.Id != assembly) && session.Document.Walls.Any(wall => wall.Id == ids[1]) &&
              session.Document.ElementGroups.Single(item => item.Id == group).Name == "Fachada sur" &&
              Throws(() => session.RenameElementGroup(group, "FACHADA NORTE 2")),
            "Dissolving a group leaves its elements alone; renaming changes only the name and cannot take another group's name");
        _ = withDoors;
    }
}
