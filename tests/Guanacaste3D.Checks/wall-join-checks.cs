using Guanacaste3D.Core;

/// <summary>Regression checks for mitered wall corners, joined moves, chained corners, and plan guides.</summary>
static class WallJoinChecks
{
    /// <summary>Verifies corner miters, flat ends where walls do not form a simple corner, and guide visibility.</summary>
    public static void Run(Action<bool, string> check)
    {
        var levelId = Guid.NewGuid();
        var first = new Wall(Guid.NewGuid(), levelId, new Point2(0, 0), new Point2(4, 0), 0.2, 3);
        var second = new Wall(Guid.NewGuid(), levelId, new Point2(4, 0), new Point2(4, 3), 0.2, 3);

        var joins = WallJoinIndex.Build([first, second]);
        var firstFootprint = joins.Footprint(first);
        var secondFootprint = joins.Footprint(second);
        check(Near(firstFootprint.EndLeft, new Point2(3.9, 0.1)) && Near(firstFootprint.EndRight, new Point2(4.1, -0.1)),
            "Mitered corner puts inner and outer faces on the bisector of the two walls");
        check(Near(secondFootprint.StartLeft, firstFootprint.EndLeft) &&
              Near(secondFootprint.StartRight, firstFootprint.EndRight),
            "Joined walls share the same corner points, so no notch remains");

        // Adjacent spatial cells must still join; rounding a point to one cell
        // would silently break valid near-coincident corners.
        var edgeFirst = first with
        {
            Id = Guid.NewGuid(),
            End = new Point2(4.000099, 0)
        };
        var edgeSecond = second with
        {
            Id = Guid.NewGuid(),
            Start = new Point2(4.000101, 0),
            End = new Point2(4.000101, 3)
        };
        var edgeFootprint = WallJoinIndex.Build([edgeFirst, edgeSecond]).Footprint(edgeFirst);
        check(Math.Abs(edgeFootprint.EndLeft.X - edgeFirst.End.X) > 0.05,
            "Near-coincident corners across adjacent positive spatial cells remain joined");

        var negativeFirst = first with
        {
            Id = Guid.NewGuid(),
            Start = new Point2(-4, 0),
            End = new Point2(-0.000001, 0)
        };
        var negativeSecond = second with
        {
            Id = Guid.NewGuid(),
            Start = new Point2(0.000001, 0),
            End = new Point2(0.000001, 3)
        };
        var negativeFootprint = WallJoinIndex.Build([negativeFirst, negativeSecond]).Footprint(negativeFirst);
        check(Math.Abs(negativeFootprint.EndLeft.X - negativeFirst.End.X) > 0.05,
            "Near-coincident corners across negative and positive spatial cells remain joined");

        // Proximity is not transitive: the center endpoint sees two neighbors,
        // but each outer endpoint sees only the center. All three must stay square;
        // a one-sided miter would leave a notch or overlapping wall faces.
        var ambiguousFirst = first with
        {
            Id = Guid.NewGuid(),
            Start = new Point2(-4, 0),
            End = new Point2(0, 0)
        };
        var ambiguousSecond = second with
        {
            Id = Guid.NewGuid(),
            Start = new Point2(0.00009, 0),
            End = new Point2(0.00009, 3)
        };
        var ambiguousThird = second with
        {
            Id = Guid.NewGuid(),
            Start = new Point2(-0.00009, 0),
            End = new Point2(-0.00009, -3)
        };
        var ambiguousIndex = WallJoinIndex.Build([ambiguousFirst, ambiguousSecond, ambiguousThird]);
        var ambiguousFirstFace = ambiguousIndex.Footprint(ambiguousFirst);
        var ambiguousSecondFace = ambiguousIndex.Footprint(ambiguousSecond);
        var ambiguousThirdFace = ambiguousIndex.Footprint(ambiguousThird);
        check(Near(ambiguousFirstFace.EndLeft, new Point2(0, 0.1)) &&
              Near(ambiguousSecondFace.StartLeft, new Point2(-0.09991, 0)) &&
              Near(ambiguousSecondFace.StartRight, new Point2(0.10009, 0)) &&
              Near(ambiguousThirdFace.StartLeft, new Point2(0.09991, 0)),
            "Ambiguous near-coincident wall endpoints never create one-sided miters");

        var differentLevel = second with { Id = Guid.NewGuid(), LevelId = Guid.NewGuid() };
        var isolated = WallJoinIndex.Build([first, differentLevel]).Footprint(first);
        check(Near(isolated.EndLeft, new Point2(4, 0.1)),
            "Spatial endpoint index never joins walls belonging to different levels");

        var dispersedWalls = Enumerable.Range(0, 3000).Select(index =>
            new Wall(Guid.NewGuid(), levelId,
                new Point2(index * 10, 100), new Point2(index * 10 + 2, 100), 0.2, 3)).ToArray();
        var dispersedIndex = WallJoinIndex.Build(dispersedWalls);
        check(Near(dispersedIndex.Footprint(dispersedWalls[0]).StartLeft, new Point2(0, 100.1)) &&
              Near(dispersedIndex.Footprint(dispersedWalls[^1]).EndRight,
                  new Point2(dispersedWalls[^1].End.X, 99.9)),
            "Thousands of isolated BIM walls are indexed without spurious joins");

        var square = WallJoinIndex.Build([first]).Footprint(first);
        check(Near(square.EndLeft, new Point2(4, 0.1)) && Near(square.EndRight, new Point2(4, -0.1)),
            "Unjoined wall ends stay square to the centerline");

        var tee = new Wall(Guid.NewGuid(), levelId, new Point2(4, 0), new Point2(8, 0), 0.2, 3);
        var teeFootprint = WallJoinIndex.Build([first, second, tee]).Footprint(first);
        check(Near(teeFootprint.EndLeft, new Point2(4, 0.1)), "Three walls meeting at one point keep flat ends");

        var otherType = new Wall(Guid.NewGuid(), levelId, new Point2(4, 0), new Point2(4, 3), 0.2, 3)
        {
            WallTypeId = Guid.NewGuid()
        };
        var differentTypeFootprint = WallJoinIndex.Build([first, otherType]).Footprint(first);
        check(Near(differentTypeFootprint.EndLeft, firstFootprint.EndLeft) &&
              Near(differentTypeFootprint.EndRight, firstFootprint.EndRight),
            "Walls of different types meet at the same geometric corner");

        var outline = joins.SectionOutline(first, 0, first.Length);
        check(Near(outline[1], new Point2(3.9, 0.1)) && Near(outline[2], new Point2(4.1, -0.1)),
            "Mesh outline of a joined end uses the same miter as the plan");

        var crossing = WallPlacementGeometry.CenterlineIntersection(
            new WallCenterline(new Point2(0, 0.1), new Point2(4, 0.1)),
            new WallCenterline(new Point2(4.1, 0), new Point2(4.1, -3)));
        check(crossing is Point2 meeting && Near(meeting, new Point2(4.1, 0.1)),
            "Offset chain walls meet where their offset lines cross");
        check(WallPlacementGeometry.CenterlineIntersection(
                new WallCenterline(new Point2(0, 0), new Point2(1, 0)),
                new WallCenterline(new Point2(0, 1), new Point2(2, 1))) is null,
            "Parallel offset lines produce no corner");

        var document = BuildingDocument.Create();
        var session = new DocumentSession(document);
        var level = document.Levels[0].Id;
        var firstId = session.AddWall(level, new Point2(0, 0), new Point2(4, 0));
        var secondId = session.AddWall(level, new Point2(4, 0), new Point2(4, 3));
        session.MoveWall(firstId, 0, 1);
        var moved = session.Document.Walls.Single(wall => wall.Id == firstId);
        var neighbour = session.Document.Walls.Single(wall => wall.Id == secondId);
        check(moved.Start == new Point2(0, 1) && moved.End == new Point2(4, 1) &&
              neighbour.Start == new Point2(4, 1) && neighbour.End == new Point2(4, 3),
            "Moving a wall carries the coincident corner of its joined neighbour");
        session.Undo();
        check(session.Document.Walls.Single(wall => wall.Id == secondId).Start == new Point2(4, 0),
            "Undo restores the joined neighbour corner");

        var visible = new PlanBounds(-10, -10, 10, 10);
        var guides = PlanAlignment.Find(new Point2(4.02, 3), [new Point2(4, 0), new Point2(20, 3)], visible, 0.05);
        check(guides.Count == 1 && guides[0].Axis == PlanGuideAxis.Vertical && guides[0].Corner == new Point2(4, 0),
            "Reference line appears only for a visible corner on the pointer's axis");
        var offScreen = PlanAlignment.Find(new Point2(4.02, 3), [new Point2(4, 0)],
            new PlanBounds(5, -10, 10, 10), 0.05);
        check(offScreen.Count == 0, "Reference line stays hidden when the aligned corner is off screen");
    }

    /// <summary>Compares two plan points with a tolerance far below any drawing precision.</summary>
    private static bool Near(Point2 first, Point2 second) => first.DistanceTo(second) < 1e-9;
}
