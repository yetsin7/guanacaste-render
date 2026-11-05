using Guanacaste3D.Core;

/// <summary>Checks the dimension between two elements on the breeze-block house.</summary>
static class DimensionBetweenChecks
{
    /// <summary>Runs the scenarios on a copy of the house.</summary>
    public static void Run(Action<bool, string> check)
    {
        var session = new DocumentSession(BreezeBlockHouseShowcase.Create());
        var level = session.Document.Levels[0].Id;
        var west = session.AddWall(level, new Point2(500, 500), new Point2(500, 504));
        var east = session.AddWall(level, new Point2(507, 500), new Point2(507, 504));

        var baseline = session.Document.Dimensions.Count;
        var id = session.AddDimensionBetween(east, west);
        var dimension = session.Document.Dimensions.Single(item => item.Id == id);
        var gap = PlanDimensions.Resolve(session.Document, dimension)!.Value.Value;
        check(dimension.Kind == PlanDimensionKind.Left && Math.Abs(gap - 6.8) < 1e-9,
            $"Two walls 7 m apart centre to centre get a dimension of their clear gap, {gap:0.00} m");
        session.MoveWall(east, 3, 0);
        check(Math.Abs(PlanDimensions.Resolve(session.Document, session.Document.Dimensions.Single(item => item.Id == id))!.Value.Value - 9.8) < 1e-9,
            "The dimension follows the wall when it moves");
        check(session.AddDimensionBetween(east, west) == id && session.Document.Dimensions.Count == baseline + 1,
            "Dimensioning the same pair again returns the same dimension");
        session.Undo();
        session.Undo();
        check(session.Document.Dimensions.Count == baseline, "Undo removes the dimension and leaves the house's own");

        var north = session.AddWall(level, new Point2(500, 520), new Point2(504, 520));
        session.AddDimensionBetween(north, west);
        check(session.Document.Dimensions.Single(item => item.OwnerId == north).Kind == PlanDimensionKind.Below,
            "Walls separated vertically are measured in that direction");

        check(Throws(() => session.AddDimensionBetween(west, west)) && Throws(() => session.AddDimensionBetween(west, Guid.NewGuid())) &&
              Throws(() => session.AddDimensionBetween(Guid.NewGuid(), west)),
            "The same element twice and unknown elements are refused");
        var other = session.AddLevel();
        var upstairs = session.AddWall(other, new Point2(530, 500), new Point2(530, 504));
        check(Throws(() => session.AddDimensionBetween(upstairs, west)), "Elements on different levels are refused");
        var crossing = session.AddWall(level, new Point2(498, 502), new Point2(510, 502));
        check(Throws(() => session.AddDimensionBetween(crossing, west)), "Walls that touch or overlap have no distance to measure");
    }

    private static bool Throws(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return true; }
        return false;
    }
}
