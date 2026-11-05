using Guanacaste3D.Core;

/// <summary>Regression checks for idempotent BIM finish edits and their undo/redo semantics.</summary>
static class FinishTransactionChecks
{
    public static void Run(Action<bool, string> check)
    {
        var wallSession = new DocumentSession(BuildingDocument.Create("Wall finish"));
        var wallId = wallSession.AddWall(wallSession.Document.Levels[0].Id,
            new Point2(0, 0), new Point2(4, 0));
        var wallEvents = 0;
        wallSession.Changed += (_, _) => wallEvents++;
        wallSession.SetWallFinish(wallId, FinishKind.Paint);
        var wallDocument = wallSession.Document;
        wallSession.SetWallFinish(wallId, FinishKind.Paint);
        check(ReferenceEquals(wallDocument, wallSession.Document) && wallEvents == 1 &&
              wallSession.UndoName == "Transaction.SetWallFinish",
            "Repeating an identical wall finish does not invalidate the BIM document or add undo history");
        wallSession.Undo();
        wallSession.SetWallFinish(wallId, FinishKind.None);
        check(wallSession.CanRedo && wallSession.Document.Walls.Single().Finish == FinishKind.None,
            "An identical wall finish after undo preserves redo history");
        wallSession.Redo();
        check(wallSession.Document.Walls.Single().Finish == FinishKind.Paint,
            "Redo still restores the wall finish after a no-op edit");

        var floorSession = new DocumentSession(BuildingDocument.Create("Floor finish"));
        var floorId = floorSession.AddFloor(floorSession.Document.Levels[0].Id,
            [new Point2(0, 0), new Point2(4, 0), new Point2(4, 3), new Point2(0, 3)]);
        var floorEvents = 0;
        floorSession.Changed += (_, _) => floorEvents++;
        floorSession.SetFloorFinish(floorId, FinishKind.Ceramic);
        var floorDocument = floorSession.Document;
        floorSession.SetFloorFinish(floorId, FinishKind.Ceramic);
        check(ReferenceEquals(floorDocument, floorSession.Document) && floorEvents == 1 &&
              floorSession.UndoName == "Transaction.SetFloorFinish",
            "Repeating an identical floor finish does not invalidate the BIM document or add undo history");
        floorSession.Undo();
        floorSession.SetFloorFinish(floorId, FinishKind.None);
        check(floorSession.CanRedo && floorSession.Document.Floors.Single().Finish == FinishKind.None,
            "An identical floor finish after undo preserves redo history");
        floorSession.Redo();
        check(floorSession.Document.Floors.Single().Finish == FinishKind.Ceramic,
            "Redo still restores the floor finish after a no-op edit");
    }
}
