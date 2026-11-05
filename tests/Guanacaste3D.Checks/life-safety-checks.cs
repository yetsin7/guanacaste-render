using Guanacaste3D.Core;

/// <summary>Checks that preliminary safety findings retain their limited scope.</summary>
static class LifeSafetyChecks
{
    /// <summary>Exercises empty, small, and multi-storey cases without claiming code compliance.</summary>
    public static void Run(Action<bool, string> check)
    {
        var empty = BuildingDocument.Create("Screening");
        var emptyReport = LifeSafetyAnalysis.Analyze(empty);
        var emptyText = BuildingReview.Format(BuildingReview.Review(empty));
        check(!emptyReport.HasFindings && emptyReport.Storeys == 0 &&
              emptyText.Contains("does not establish design completeness or code compliance", StringComparison.Ordinal) &&
              !emptyText.Contains("model is complete", StringComparison.Ordinal),
            "An empty screening does not claim completeness or compliance");

        var session = new DocumentSession(empty);
        var ground = session.Document.Levels.OrderBy(level => level.Elevation).First().Id;
        session.AddRoom(ground, new(0, 0), new(5, 5), "Bedroom");
        var houseReport = LifeSafetyAnalysis.Analyze(session.Document);
        check(houseReport.Storeys == 1 && !houseReport.HasFindings,
            "The preliminary multi-storey screen does not claim to assess a one-storey house");

        var second = session.Document.Levels.OrderBy(level => level.Elevation).Last().Id;
        session.AddRoom(second, new(0, 0), new(5, 5), "Bedroom");
        var third = session.AddLevel();
        session.AddRoom(third, new(0, 0), new(5, 5), "Bedroom");
        var fourth = session.AddLevel();
        session.AddRoom(fourth, new(0, 0), new(5, 5), "Bedroom");
        var towerReport = LifeSafetyAnalysis.Analyze(session.Document);
        check(towerReport.HasFindings && towerReport.SuggestsElevator && towerReport.SuggestsSprinklers &&
              towerReport.Issues.Any(issue => issue.Code == LifeSafetyCode.NoElevator) &&
              towerReport.Issues.Any(issue => issue.Code == LifeSafetyCode.SprinklerCoverage),
            "The multi-storey screen reports missing modeled elements as preliminary findings");
        var findingsText = BuildingReview.Format(BuildingReview.Review(session.Document));
        check(findingsText.Contains("Preliminary model screening only", StringComparison.Ordinal) &&
              findingsText.Contains("code compliance", StringComparison.Ordinal),
            "Reports with findings also explain their limited scope");

        var elevator = session.AddElevator(ground, fourth, new(0, 0), new(2, 2), "Elevator");
        check(Math.Abs(session.Document.StructuralElements.Single(element => element.Id == elevator).GridHeight - 3) < 1e-9,
            "Regular landing levels produce matching elevator spacing");
        session.UpdateLevel(third, "Third level", 6.5);
        var previousCount = session.Document.StructuralElements.Count;
        var rejected = false;
        try { session.AddElevator(ground, fourth, new(3, 3), new(5, 5), "Elevator"); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected && session.Document.StructuralElements.Count == previousCount,
            "Irregular landing levels are rejected before changing the model");
    }
}
