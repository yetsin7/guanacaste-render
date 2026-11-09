using Guanacaste3D.Core;

/// <summary>Checks that a wall keeps the face it was placed by when its thickness changes, and stays joined.</summary>
static class WallReferenceLineChecks
{
    /// <summary>Runs the location-reference scenarios against the shared document session.</summary>
    public static void Run(Action<bool, string> check)
    {
        FaceStaysPut(check);
        CornerStaysJoined(check);
        OpeningStaysInPlace(check);
        AxisLinkedWallStaysOnAxis(check);
        PersistenceAndValidation(check);
    }

    /// <summary>Each reference keeps its own plane when the shared wall type gets thicker.</summary>
    private static void FaceStaysPut(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create());
        var level = session.Document.Levels[0].Id;
        var exterior = session.AddWall(level, new Point2(0, 0), new Point2(4, 0), wallTypeId: BuiltInWallTypes.Basic200Id,
            locationReference: WallLocationReference.ExteriorFace);
        var interior = session.AddWall(level, new Point2(0, 10), new Point2(4, 10), wallTypeId: BuiltInWallTypes.Basic200Id,
            locationReference: WallLocationReference.InteriorFace);
        var centered = session.AddWall(level, new Point2(0, 20), new Point2(4, 20), wallTypeId: BuiltInWallTypes.Basic200Id);

        session.UpdateWallType(BuiltInWallTypes.Basic200Id, "Basic 300 mm", WallFunction.Interior, 0.30, "Concrete masonry");

        var a = Wall(session, exterior);
        var b = Wall(session, interior);
        var c = Wall(session, centered);
        // Looking along +X the exterior face is the right-hand one (y - t/2) and the interior face the left (y + t/2).
        check(Near(a.Start.Y - a.Thickness / 2, -0.10) && Near(a.End.Y - a.Thickness / 2, -0.10) && Near(a.Thickness, 0.30),
            "A wall placed by its exterior face keeps that face when its type gets thicker");
        check(Near(b.Start.Y + b.Thickness / 2, 10.10) && Near(b.End.Y + b.Thickness / 2, 10.10),
            "A wall placed by its interior face keeps that face when its type gets thicker");
        check(Near(c.Start.Y, 20) && Near(c.End.Y, 20),
            "A wall placed by its centerline stays centered when its type gets thicker");

        session.Undo();
        check(Near(Wall(session, exterior).Start.Y, 0) && Near(Wall(session, exterior).Thickness, 0.20),
            "Undo restores the original centerline and thickness together");

        session.SetWallType(interior, BuiltInWallTypes.Exterior300Id);
        var changedType = Wall(session, interior);
        check(Near(changedType.Start.Y + changedType.Thickness / 2, 10.10) && Near(changedType.Thickness, 0.30),
            "Assigning a thicker type to one wall keeps its placement face");
        session.UpdateWall(interior, 0.20, 3);
        check(Near(Wall(session, interior).Start.Y + 0.10, 10.10),
            "Editing an instance thickness keeps its placement face too");
    }

    /// <summary>Two walls meeting in a corner stay joined and keep their outer corner.</summary>
    private static void CornerStaysJoined(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create());
        var level = session.Document.Levels[0].Id;
        var south = session.AddWall(level, new Point2(0, 0), new Point2(4, 0), wallTypeId: BuiltInWallTypes.Basic200Id,
            locationReference: WallLocationReference.ExteriorFace);
        var east = session.AddWall(level, new Point2(4, 0), new Point2(4, 3), wallTypeId: BuiltInWallTypes.Basic200Id,
            locationReference: WallLocationReference.ExteriorFace);

        session.UpdateWallType(BuiltInWallTypes.Basic200Id, "Basic 300 mm", WallFunction.Interior, 0.30, "Concrete masonry");

        var first = Wall(session, south);
        var second = Wall(session, east);
        check(Near(first.End, second.Start) && Near(first.End, new Point2(3.95, 0.05)),
            "A thicker exterior-referenced corner moves both ends to the crossing of the new centerlines");
        var joins = WallJoinIndex.Build(session.Document.Walls);
        check(joins.JoinAt(first.Id, WallEnd.End) is not null && joins.JoinAt(second.Id, WallEnd.Start) is not null,
            "The corner is still a joined, mitered corner after the thickness change");
        var footprint = joins.Footprint(first);
        check(Near(footprint.EndRight, new Point2(4.10, -0.10)),
            "The outer corner of the joined walls stays where it was placed");

        session.Undo();
        check(Near(Wall(session, south).End, new Point2(4, 0)) && Near(Wall(session, east).Start, new Point2(4, 0)),
            "Undo restores both ends of the corner");
    }

    /// <summary>A door keeps its model position while its wall's start slides to close a corner.</summary>
    private static void OpeningStaysInPlace(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create());
        var level = session.Document.Levels[0].Id;
        session.AddWall(level, new Point2(0, 0), new Point2(4, 0), wallTypeId: BuiltInWallTypes.Basic200Id,
            locationReference: WallLocationReference.ExteriorFace);
        var east = session.AddWall(level, new Point2(4, 0), new Point2(4, 6), wallTypeId: BuiltInWallTypes.Basic200Id,
            locationReference: WallLocationReference.ExteriorFace);
        var doorId = session.AddOpening(east, OpeningKind.Door, offset: 3);

        session.UpdateWallType(BuiltInWallTypes.Basic200Id, "Basic 300 mm", WallFunction.Interior, 0.30, "Concrete masonry");

        var wall = Wall(session, east);
        var door = session.Document.Openings.Single(opening => opening.Id == doorId);
        check(Near(wall.Start.Y + door.Offset, 3.0),
            "A hosted door keeps its position in the model when the wall start slides");
    }

    /// <summary>A wall tied to a grid axis is never moved off it by a thickness change.</summary>
    private static void AxisLinkedWallStaysOnAxis(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create());
        var level = session.Document.Levels[0].Id;
        session.AddGridAxis("A", new Point2(0, 0), new Point2(8, 0));
        var wallId = session.AddWall(level, new Point2(0, 0), new Point2(8, 0), wallTypeId: BuiltInWallTypes.Basic200Id,
            locationReference: WallLocationReference.ExteriorFace);
        var wall = Wall(session, wallId);
        check(wall.GridAxisId is not null && wall.LocationReference == WallLocationReference.Centerline,
            "A wall drawn on an axis is referenced by its centerline");

        session.UpdateWallType(BuiltInWallTypes.Basic200Id, "Basic 300 mm", WallFunction.Interior, 0.30, "Concrete masonry");
        check(Near(Wall(session, wallId).Start.Y, 0) && Near(Wall(session, wallId).Thickness, 0.30),
            "An axis-linked wall changes thickness without leaving its axis");
    }

    /// <summary>The reference survives saving, and invalid values are rejected.</summary>
    private static void PersistenceAndValidation(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create());
        var level = session.Document.Levels[0].Id;
        var id = session.AddWall(level, new Point2(0, 0), new Point2(4, 0), wallTypeId: BuiltInWallTypes.Basic200Id,
            locationReference: WallLocationReference.InteriorFace);

        var path = Path.Combine(Path.GetTempPath(), $"g3d-location-{Guid.NewGuid():N}.g3d");
        try
        {
            ProjectFile.Save(path, session.Document);
            var loaded = ProjectFile.Load(path);
            check(loaded.Walls.Single(wall => wall.Id == id).LocationReference == WallLocationReference.InteriorFace,
                "A wall's location reference survives saving and reopening the project");
        }
        finally
        {
            File.Delete(path);
        }

        var clone = session.Document.Clone();
        clone.Walls[0] = clone.Walls[0] with { LocationReference = (WallLocationReference)99 };
        var rejected = false;
        try { clone.Validate(); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "An undefined location reference is rejected when validating a project");
        check(new Wall(Guid.NewGuid(), level, new Point2(0, 0), new Point2(1, 0), 0.2, 3).LocationReference ==
              WallLocationReference.Centerline,
            "Walls without a stored reference, such as those in older projects, are centered");
    }

    private static Wall Wall(DocumentSession session, Guid id) => session.Document.Walls.Single(wall => wall.Id == id);

    private static bool Near(double first, double second) => Math.Abs(first - second) < 1e-9;

    private static bool Near(Point2 first, Point2 second) => first.DistanceTo(second) < 1e-9;
}
