using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Regression checks for editable and persistent dimensions and direction-independent wall corners.</summary>
internal static class PlanDimensionChecks
{
    /// <summary>Checks exact edits, atomic rejection, history, persistence, deletion and circulation geometry.</summary>
    public static void Run(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create());
        var level = session.Document.Levels[0].Id;
        var wallId = session.AddWall(level, new(0, 0), new(12, 0));
        var doorId = session.AddOpening(wallId, OpeningKind.Door, 3.45);
        var start = PlanDimensions.ForSelection(session.Document, doorId).Single(item => item.Kind == PlanDimensionKind.OpeningStart);
        check(Math.Abs(PlanDimensions.Resolve(session.Document, start)!.Value.Value - 3) < 1e-9, "Door begins three meters from host start");
        session.EditDimension(start, 5);
        check(Math.Abs(session.Document.Openings.Single().Begin - 5) < 1e-9, "Editing three to five moves the door exactly two meters");
        session.Undo();
        check(Math.Abs(session.Document.Openings.Single().Begin - 3) < 1e-9, "Dimension edit is undoable");
        session.Redo();
        session.AddDimension(start);
        session.AddDimension(start);
        check(session.Document.Dimensions.Count == 1, "Promotion is persistent and duplicate-safe");
        var before = session.Document;
        try { session.EditDimension(start, 50); } catch (InvalidDataException) { }
        check(ReferenceEquals(session.Document, before), "Out-of-host dimension edit leaves document and history unchanged");
        var windowId = session.AddOpening(wallId, OpeningKind.Window, 8);
        before = session.Document;
        try { session.EditDimension(start, 7.55); } catch (InvalidDataException) { }
        check(ReferenceEquals(session.Document, before), "Dimension edit rejects collision with another opening");
        var loaded = ProjectFile.DeserializeDocument(ProjectFile.SerializeDocument(session.Document));
        check(loaded.Dimensions.Count == 1 && Math.Abs(PlanDimensions.Resolve(loaded, loaded.Dimensions[0])!.Value.Value - 5) < 1e-9,
            "Native round trip retains semantic measurement references");
        var legacy = session.Document.Clone();
        legacy.SchemaVersion = 9;
        legacy.Dimensions.Clear();
        check(ProjectFile.DeserializeDocument(ProjectFile.SerializeDocument(legacy)).SchemaVersion == BuildingDocument.CurrentSchemaVersion, "Schema nine migrates without losing model content");
        session.RemoveOpening(doorId);
        check(session.Document.Dimensions.Count == 0, "Deleting an owner removes dependent dimensions");
        session.Undo();
        check(session.Document.Dimensions.Count == 1, "Undo restores both owner and annotation");
        var columnId = session.AddColumn(level, new(4, 4));
        var gap = PlanDimensions.ForSelection(session.Document, columnId).Single(item => item.Kind == PlanDimensionKind.Below);
        session.EditDimension(gap, 5);
        check(Math.Abs(PlanDimensions.Resolve(session.Document, gap)!.Value.Value - 5) < 1e-9, "Column gap edit solves the clear distance to a wall face");
        session.AddDimension(gap);
        check(TemplateRules.StripModelContent(session.Document).Dimensions.Count == 0, "Templates remove annotations with their model owners");
        DimensionExportChecks.Run(check);
        CheckCorners(check);
        var stair = session.AddStructuralElement(level, StructuralElementKind.Stair, "Straight stair", new(1, 1), new(6, 1));
        var railing = session.AddStructuralElement(level, StructuralElementKind.Railing, "Straight guard", new(1, 2), new(6, 2));
        var steps = CirculationGeometry.Parts(session.Document.StructuralElements.Single(item => item.Id == stair)).ToArray();
        check(steps.Length == 18 && Math.Abs(steps[^1].Height - 3) < 1e-9, "Straight stair has eighteen real rising treads");
        var rails = CirculationGeometry.Parts(session.Document.StructuralElements.Single(item => item.Id == railing)).ToArray();
        check(rails.Length == 8 && rails.Count(item => item.Length == 0) == 6, "Guard has posts and two rails rather than a solid wall");
        check(BuildingMesher.Build(session.Document).TriangleCount > 300, "Circulation parts participate in the shared 3D mesh");
    }

    /// <summary>Reversing either wall must preserve the same physical footprint, including unequal thicknesses.</summary>
    private static void CheckCorners(Action<bool, string> check)
    {
        var level = Guid.NewGuid();
        foreach (var angle in new[] { 35, 60, 90, 120, 145 })
        {
            var radians = angle * Math.PI / 180;
            var first = new Wall(Guid.NewGuid(), level, new(-4, 0), new(0, 0), 0.2, 3);
            var second = new Wall(Guid.NewGuid(), level, new(0, 0), new(4 * Math.Cos(radians), 4 * Math.Sin(radians)), 0.3, 3);
            var original = WallJoinIndex.Build(new[] { first, second }).Footprint(first).Points;
            foreach (var reverseFirst in new[] { false, true })
            foreach (var reverseSecond in new[] { false, true })
            {
                var a = reverseFirst ? first with { Start = first.End, End = first.Start } : first;
                var b = reverseSecond ? second with { Start = second.End, End = second.Start } : second;
                var footprint = WallJoinIndex.Build(new[] { a, b }).Footprint(a).Points;
                check(original.All(point => footprint.Any(other => point.DistanceTo(other) < 1e-9)),
                    $"Corner at {angle} degrees is invariant under endpoint reversals {reverseFirst}/{reverseSecond}");
            }
        }
    }
}
