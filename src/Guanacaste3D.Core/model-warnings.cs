namespace Guanacaste3D.Core;

/// <summary>How serious a model warning is.</summary>
public enum WarningSeverity
{
    Info,
    Warning
}

/// <summary>
/// A suspicious situation in the model. <see cref="Code"/> names the kind of problem so the interface can describe
/// it in the user's language; <see cref="Value"/> is a measure that goes with it (a length or an area).
/// </summary>
public sealed record ModelWarning(string Code, WarningSeverity Severity, IReadOnlyList<Guid> ElementIds, double Value = 0);

/// <summary>Looks through a project for things that are legal but probably not intended (Revit's "Review Warnings").</summary>
public static class ModelWarnings
{
    /// <summary>Walls shorter than this, in meters, are reported.</summary>
    public const double ShortWallLength = 0.10;

    /// <summary>Openings closer than this to a wall end, in meters, are reported.</summary>
    public const double OpeningEdgeDistance = 0.10;

    /// <summary>Rooms smaller than this, in square meters, are reported.</summary>
    public const double TinyRoomArea = 1.0;

    /// <summary>Wall ends this close to another wall without touching it are reported as a gap, in meters.</summary>
    public const double WallGap = 0.15;

    /// <summary>
    /// Elements farther than this from the origin, in meters, mesh with a visible loss of precision: the 3D view
    /// stores positions as 32-bit floats, which resolve about 8 mm at this distance and 6 cm at a million.
    /// </summary>
    public const double FarFromOrigin = 100000;

    /// <summary>Levels with more walls than this skip the pairwise checks, which grow with the square of the count.</summary>
    public const int PairwiseWallLimit = 2000;

    /// <summary>Returns every warning of the document, ordered by severity and kind so the list is stable.</summary>
    public static IReadOnlyList<ModelWarning> Analyze(BuildingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var warnings = new List<ModelWarning>();

        foreach (var wall in document.Walls.Where(wall => wall.Length < ShortWallLength))
            warnings.Add(new ModelWarning("ShortWall", WarningSeverity.Warning, [wall.Id], wall.Length));

        var far = document.Walls.Where(wall => Math.Max(
            Math.Max(Math.Abs(wall.Start.X), Math.Abs(wall.Start.Y)), Math.Max(Math.Abs(wall.End.X), Math.Abs(wall.End.Y))) > FarFromOrigin).ToArray();
        if (far.Length > 0)
            warnings.Add(new ModelWarning("FarFromOrigin", WarningSeverity.Warning, far.Select(wall => wall.Id).ToArray(),
                far.Max(wall => Math.Max(Math.Max(Math.Abs(wall.Start.X), Math.Abs(wall.Start.Y)),
                    Math.Max(Math.Abs(wall.End.X), Math.Abs(wall.End.Y))))));

        var hosts = document.Walls.ToDictionary(wall => wall.Id);
        foreach (var opening in document.Openings)
        {
            if (!hosts.TryGetValue(opening.WallId, out var host)) continue;
            var edge = Math.Min(opening.Begin, host.Length - opening.End);
            if (edge < OpeningEdgeDistance) warnings.Add(new ModelWarning("OpeningNearWallEnd", WarningSeverity.Warning, [opening.Id], edge));
        }

        foreach (var level in document.Walls.GroupBy(wall => wall.LevelId))
        {
            var walls = level.ToArray();
            AddDuplicateWalls(walls, warnings);
            if (walls.Length <= PairwiseWallLimit) AddWallGaps(walls, warnings);
        }

        // The model audit owns the checks that need a spatial index; its wall gap rule measures to the centerline, so the
        // body-based WallGap check above stays. Both are shown in the same list.
        foreach (var issue in ModelAudit.Analyze(document).Where(issue => issue.Code != ModelAuditCode.NearWallGap))
            warnings.Add(new ModelWarning(issue.Code.ToString(), issue.Code == ModelAuditCode.LowOpeningHeadClearance
                    ? WarningSeverity.Info : WarningSeverity.Warning,
                issue.RelatedId is { } related ? [issue.ElementId, related] : [issue.ElementId], issue.ValueMeters));

        foreach (var group in document.Rooms.GroupBy(room => (room.LevelId, room.Number.Trim().ToUpperInvariant()))
                     .Where(group => group.Key.Item2.Length > 0 && group.Count() > 1))
            warnings.Add(new ModelWarning("DuplicateRoomNumber", WarningSeverity.Warning, group.Select(room => room.Id).ToArray(), group.Count()));
        foreach (var room in document.Rooms.Where(room => room.Area < TinyRoomArea))
            warnings.Add(new ModelWarning("TinyRoom", WarningSeverity.Info, [room.Id], room.Area));

        foreach (var group in document.Columns.GroupBy(column => (column.LevelId, Math.Round(column.Position.X, 3), Math.Round(column.Position.Y, 3)))
                     .Where(group => group.Count() > 1))
            warnings.Add(new ModelWarning("DuplicateColumns", WarningSeverity.Warning, group.Select(column => column.Id).ToArray(), group.Count()));


        return warnings.OrderBy(warning => warning.Severity == WarningSeverity.Warning ? 0 : 1)
            .ThenBy(warning => warning.Code, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Two walls with the same ends and thickness hide each other and count twice in every quantity.</summary>
    private static void AddDuplicateWalls(Wall[] walls, List<ModelWarning> warnings)
    {
        static (long, long, long, long, long) Key(Wall wall)
        {
            long Q(double value) => (long)Math.Round(value * 1000);
            var a = (Q(wall.Start.X), Q(wall.Start.Y));
            var b = (Q(wall.End.X), Q(wall.End.Y));
            var (first, second) = a.CompareTo(b) <= 0 ? (a, b) : (b, a);
            return (first.Item1, first.Item2, second.Item1, second.Item2, Q(wall.Thickness));
        }

        foreach (var group in walls.GroupBy(Key).Where(group => group.Count() > 1))
            warnings.Add(new ModelWarning("DuplicateWalls", WarningSeverity.Warning, group.Select(wall => wall.Id).ToArray(), group.Count()));
    }

    /// <summary>A wall end that stops a little short of another wall: almost a corner, but open.</summary>
    private static void AddWallGaps(Wall[] walls, List<ModelWarning> warnings)
    {
        var index = WallJoinIndex.Build(walls);
        foreach (var wall in walls)
        foreach (var end in new[] { WallEnd.Start, WallEnd.End })
        {
            if (index.JoinAt(wall.Id, end) is not null || index.TeeHostAt(wall.Id, end) is not null) continue;
            var point = end == WallEnd.Start ? wall.Start : wall.End;
            // The gap is measured to the body of the other wall, so ending on its face is not a gap.
            var nearest = walls.Where(other => other.Id != wall.Id)
                .Select(other => (Wall: other, Gap: DistanceToSegment(point, other) - other.Thickness / 2))
                .OrderBy(candidate => candidate.Gap).FirstOrDefault();
            if (nearest.Wall is not null && nearest.Gap > 1e-3 && nearest.Gap <= WallGap)
                warnings.Add(new ModelWarning("WallGap", WarningSeverity.Warning, [wall.Id, nearest.Wall.Id], nearest.Gap));
        }
    }

    private static double DistanceToSegment(Point2 point, Wall wall)
    {
        var dx = wall.End.X - wall.Start.X;
        var dy = wall.End.Y - wall.Start.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 1e-18) return point.DistanceTo(wall.Start);
        var t = Math.Clamp(((point.X - wall.Start.X) * dx + (point.Y - wall.Start.Y) * dy) / lengthSquared, 0, 1);
        return point.DistanceTo(new Point2(wall.Start.X + dx * t, wall.Start.Y + dy * t));
    }
}
