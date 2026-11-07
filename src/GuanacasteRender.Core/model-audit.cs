namespace Guanacaste3D.Core;

/// <summary>Geometric conditions that deserve a designer's review but do not invalidate a project.</summary>
public enum ModelAuditCode { NearWallGap, OverlappingRooms, LowOpeningHeadClearance, OverlappingWalls }

/// <summary>A stable element reference and metric measurement for one advisory model finding.</summary>
public sealed record ModelAuditIssue(ModelAuditCode Code, Guid ElementId, Guid? RelatedId,
    Guid LevelId, double ValueMeters);

/// <summary>Severity of an advisory BIM coordination finding.</summary>
public enum ModelAuditSeverity { Information, Warning, Critical }

/// <summary>Actionable audit item with a stable identity and human-readable explanation.</summary>
public sealed record ModelAuditFinding(ModelAuditIssue Issue, ModelAuditSeverity Severity,
    string Description, string SuggestedAction);

/// <summary>Issue counts grouped by level and severity for project health dashboards.</summary>
public sealed record ModelAuditLevelSummary(Guid LevelId, int Total, int Information,
    int Warnings, int Critical);

/// <summary>Converts measured audit issues into actionable coordination findings.</summary>
public static class ModelAuditReporting
{
    /// <summary>Produces stable per-level totals without changing the model.</summary>
    public static IReadOnlyList<ModelAuditLevelSummary> SummarizeByLevel(IEnumerable<ModelAuditFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        return findings.GroupBy(finding => finding.Issue.LevelId)
            .OrderBy(group => group.Key)
            .Select(group => new ModelAuditLevelSummary(group.Key, group.Count(),
                group.Count(f => f.Severity == ModelAuditSeverity.Information),
                group.Count(f => f.Severity == ModelAuditSeverity.Warning),
                group.Count(f => f.Severity == ModelAuditSeverity.Critical)))
            .ToArray();
    }

    public static IReadOnlyList<ModelAuditFinding> Describe(IEnumerable<ModelAuditIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        return issues.Select(issue => issue.Code switch
        {
            ModelAuditCode.NearWallGap => new ModelAuditFinding(issue, ModelAuditSeverity.Warning,
                $"Wall endpoints are {issue.ValueMeters * 1000:0.#} mm apart.",
                "Review the wall junction and join or intentionally separate the walls."),
            ModelAuditCode.OverlappingRooms => new ModelAuditFinding(issue, ModelAuditSeverity.Warning,
                $"Room annotations overlap by {issue.ValueMeters:0.###} m².",
                "Adjust room boundaries or verify that the overlap is intentional."),
            ModelAuditCode.LowOpeningHeadClearance => new ModelAuditFinding(issue,
                issue.ValueMeters < 0 ? ModelAuditSeverity.Critical : ModelAuditSeverity.Warning,
                $"Remaining opening head clearance is {issue.ValueMeters * 1000:0.#} mm.",
                "Review the opening sill, height and host wall elevation."),
            ModelAuditCode.OverlappingWalls => new ModelAuditFinding(issue, ModelAuditSeverity.Warning,
                $"Two walls overlap along {issue.ValueMeters:0.##} m.",
                "Delete one of the walls or move it so they no longer share the same space."),
            _ => throw new ArgumentOutOfRangeException(nameof(issues), "Unknown model audit code.")
        }).ToArray();
    }
}

/// <summary>Finds drafting and coordination issues without changing the validated BIM document.</summary>
public static class ModelAudit
{
    private static void FindWallOverlaps(BuildingDocument document, List<ModelAuditIssue> issues)
    {
        for (var first = 0; first < document.Walls.Count; first++)
            for (var second = first + 1; second < document.Walls.Count; second++)
            {
                var (a, b) = (document.Walls[first], document.Walls[second]);
                if (a.LevelId != b.LevelId) continue;
                var shared = WallOverlap.SharedLength(a.Start, a.End, a.Thickness, b.Start, b.End, b.Thickness);
                if (shared > 0) issues.Add(new ModelAuditIssue(ModelAuditCode.OverlappingWalls, a.Id, b.Id, a.LevelId, shared));
            }
    }

    /// <summary>
    /// Reviews near misses, room overlap, and opening head clearance. Distances use
    /// model meters; callers can use the returned ids to select affected elements.
    /// </summary>
    public static IReadOnlyList<ModelAuditIssue> Analyze(BuildingDocument document,
        double wallGapLimit = 0.10, double openingHeadLimit = 0.10)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Validate();
        if (!double.IsFinite(wallGapLimit) || wallGapLimit is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(wallGapLimit), "Audit limits must be within one meter.");
        if (!double.IsFinite(openingHeadLimit) || openingHeadLimit is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(openingHeadLimit), "Audit limits must be within one meter.");

        var issues = new List<ModelAuditIssue>();
        FindWallGaps(document, wallGapLimit, issues);
        FindWallOverlaps(document, issues);
        FindRoomOverlaps(document, issues);
        FindLowOpeningHeads(document, openingHeadLimit, issues);
        return issues.OrderBy(issue => issue.LevelId).ThenBy(issue => issue.Code)
            .ThenBy(issue => issue.ElementId).ThenBy(issue => issue.RelatedId).ToArray();
    }

    /// <summary>Finds endpoints that almost meet another wall segment on the same level.</summary>
    private static void FindWallGaps(BuildingDocument document, double limit, List<ModelAuditIssue> issues)
    {
        const double cellSize = 5;
        var cells = new Dictionary<(Guid LevelId, long X, long Y), List<Wall>>();
        var longWalls = new Dictionary<Guid, List<Wall>>();
        foreach (var candidate in document.Walls)
        {
            var left = Cell(Math.Min(candidate.Start.X, candidate.End.X) - limit, cellSize);
            var right = Cell(Math.Max(candidate.Start.X, candidate.End.X) + limit, cellSize);
            var below = Cell(Math.Min(candidate.Start.Y, candidate.End.Y) - limit, cellSize);
            var above = Cell(Math.Max(candidate.Start.Y, candidate.End.Y) + limit, cellSize);
            if ((right - left + 1) * (above - below + 1) > 500)
            {
                if (!longWalls.TryGetValue(candidate.LevelId, out var overflow))
                    longWalls[candidate.LevelId] = overflow = [];
                overflow.Add(candidate);
                continue;
            }
            for (var x = left; x <= right; x++)
            for (var y = below; y <= above; y++)
            {
                var key = (candidate.LevelId, x, y);
                if (!cells.TryGetValue(key, out var bucket)) cells[key] = bucket = [];
                bucket.Add(candidate);
            }
        }
        foreach (var wall in document.Walls)
        foreach (var endpoint in new[] { wall.Start, wall.End })
        {
            Wall? nearest = null;
            var shortest = limit;
            var key = (wall.LevelId, Cell(endpoint.X, cellSize), Cell(endpoint.Y, cellSize));
            var nearby = cells.GetValueOrDefault(key) ?? [];
            var overflow = longWalls.GetValueOrDefault(wall.LevelId) ?? [];
            foreach (var candidate in nearby.Concat(overflow))
            {
                if (candidate.Id == wall.Id || candidate.LevelId != wall.LevelId) continue;
                var distance = DistanceToSegment(endpoint, candidate.Start, candidate.End);
                if (InsideWallBody(endpoint, candidate, distance)) continue;
                if (distance <= 0.002 || distance >= shortest) continue;
                nearest = candidate;
                shortest = distance;
            }
            if (nearest is not null)
                issues.Add(new(ModelAuditCode.NearWallGap, wall.Id, nearest.Id, wall.LevelId, shortest));
        }
    }

    /// <summary>Maps metric coordinates to a stable spatial bucket, including negative coordinates.</summary>
    private static long Cell(double coordinate, double size) => (long)Math.Floor(coordinate / size);

    /// <summary>Measures the true point-to-segment distance, including end caps.</summary>
    private static double DistanceToSegment(Point2 point, Point2 start, Point2 end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var fraction = Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) /
            (dx * dx + dy * dy), 0, 1);
        return point.DistanceTo(new(start.X + fraction * dx, start.Y + fraction * dy));
    }

    /// <summary>Suppresses a near-miss when an endpoint already lies inside the other wall's solid width.</summary>
    private static bool InsideWallBody(Point2 point, Wall wall, double distance)
    {
        var dx = wall.End.X - wall.Start.X;
        var dy = wall.End.Y - wall.Start.Y;
        var fraction = ((point.X - wall.Start.X) * dx + (point.Y - wall.Start.Y) * dy) /
            (dx * dx + dy * dy);
        return fraction is >= 0 and <= 1 && distance <= wall.Thickness / 2 + 0.002;
    }

    /// <summary>Reports the intersection area of distinct rectangular room annotations.</summary>
    private static void FindRoomOverlaps(BuildingDocument document, List<ModelAuditIssue> issues)
    {
        for (var first = 0; first < document.Rooms.Count; first++)
        for (var second = first + 1; second < document.Rooms.Count; second++)
        {
            var left = document.Rooms[first];
            var right = document.Rooms[second];
            if (left.LevelId != right.LevelId) continue;
            var width = Math.Min(Math.Max(left.FirstCorner.X, left.SecondCorner.X),
                Math.Max(right.FirstCorner.X, right.SecondCorner.X)) -
                Math.Max(Math.Min(left.FirstCorner.X, left.SecondCorner.X),
                    Math.Min(right.FirstCorner.X, right.SecondCorner.X));
            var height = Math.Min(Math.Max(left.FirstCorner.Y, left.SecondCorner.Y),
                Math.Max(right.FirstCorner.Y, right.SecondCorner.Y)) -
                Math.Max(Math.Min(left.FirstCorner.Y, left.SecondCorner.Y),
                    Math.Min(right.FirstCorner.Y, right.SecondCorner.Y));
            if (width * height > 0.01 && width > 0 && height > 0)
                issues.Add(new(ModelAuditCode.OverlappingRooms, left.Id, right.Id, left.LevelId, width * height));
        }
    }

    /// <summary>Checks the remaining vertical wall above each hosted opening.</summary>
    private static void FindLowOpeningHeads(BuildingDocument document, double limit, List<ModelAuditIssue> issues)
    {
        var walls = document.Walls.ToDictionary(wall => wall.Id);
        foreach (var opening in document.Openings)
        {
            var wall = walls[opening.WallId];
            // Under a profiled top the clearance is the least over the opening's width, not the wall's full height.
            var clearance = new[] { opening.Begin, opening.End }.Concat((wall.TopProfile ?? []).Select(point => point.X).Where(x => x > opening.Begin && x < opening.End)).Min(wall.TopHeightAt) - opening.Sill - opening.Height;
            if (clearance < limit)
                issues.Add(new(ModelAuditCode.LowOpeningHeadClearance, opening.Id, wall.Id,
                    wall.LevelId, clearance));
        }
    }
}
