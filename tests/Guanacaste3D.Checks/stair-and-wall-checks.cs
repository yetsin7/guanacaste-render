using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Checks editable stair riser and tread with their comfort warnings, and warnings for walls laid over one another.</summary>
static class StairAndWallChecks
{
    public static void Run(Action<bool, string> check)
    {
        var document = BuildingDocument.Create();
        var session = new DocumentSession(document);
        var level = document.Levels[0].Id;
        var stairId = session.AddStructuralElement(level, StructuralElementKind.Stair, "Stair", new Point2(0, 0), new Point2(4, 0));
        session.UpdateStructuralElement(stairId, "Stair", 1.2, 3.0, 0, ElementStyle.Default);
        check(StairGeometry.Check(session.Document.StructuralElements.Single(e => e.Id == stairId)).Count == 0 ||
              StairGeometry.Check(session.Document.StructuralElements.Single(e => e.Id == stairId)).All(w => w.Kind != StairWarningKind.RiserTooHigh),
            "Automatic riser stays within the recommended maximum");

        session.SetStairSteps(stairId, 0.17, 0.30);
        var stair = session.Document.StructuralElements.Single(e => e.Id == stairId);
        var count = StairGeometry.RiserCount(stair);
        check(count == 18 && Math.Abs(stair.Length - count * 0.30) < 1e-9 && Math.Abs(StairGeometry.ActualTread(stair) - 0.30) < 1e-9,
            "Requested riser and tread set the riser count and resize the run");
        check(StairGeometry.Check(stair).Count == 0, "A 17 cm riser with a 30 cm tread raises no warning");

        session.SetStairSteps(stairId, 0.22, 0.20);
        var steep = StairGeometry.Check(session.Document.StructuralElements.Single(e => e.Id == stairId)).Select(w => w.Kind).ToArray();
        check(steep.Contains(StairWarningKind.RiserTooHigh) && steep.Contains(StairWarningKind.TreadTooShallow),
            "Too tall a riser and too shallow a tread are both reported");

        var rejected = false;
        try { session.SetStairSteps(stairId, 0.01, 0.30); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "An absurd riser is rejected");
        session.Undo();

        var first = session.AddWall(level, new Point2(0, 0), new Point2(5, 0));
        var overlap = WallOverlap.Find(session.Document.Walls, level, new Point2(2, 0.05), new Point2(7, 0.05), 0.2);
        check(overlap is { Length: > 2.9 and < 3.1 }, "A wall laid partly over another reports the shared length");
        check(WallOverlap.Find(session.Document.Walls, level, new Point2(5, 0), new Point2(5, 4), 0.2) is null,
            "A perpendicular corner is not an overlap");
        check(WallOverlap.Find(session.Document.Walls, level, new Point2(0, 0.2), new Point2(5, 0.2), 0.2) is null,
            "Walls side by side are not an overlap");
        check(WallOverlap.Find(session.Document.Walls, level, new Point2(0, 0), new Point2(5, 0), 0.2, first) is null,
            "A wall does not overlap itself");
        session.AddWall(level, new Point2(1, 0), new Point2(4, 0));
        check(ModelAudit.Analyze(session.Document).Any(issue => issue.Code == ModelAuditCode.OverlappingWalls),
            "The model audit lists overlapping walls");
    }
}
