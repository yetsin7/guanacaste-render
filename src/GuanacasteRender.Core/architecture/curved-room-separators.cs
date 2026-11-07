namespace Guanacaste3D.Core;

/// <summary>
/// Parametric, non-solid curved room boundary. Angle is measured counter-clockwise from +X,
/// in degrees; a 360° sweep makes a complete circular courtyard boundary.
/// This is a room separator only, not a curved structural wall.
/// </summary>
public sealed record CurvedRoomSeparator(
    Guid Id,
    Guid LevelId,
    Point2 Center,
    double Radius,
    double StartAngleDegrees,
    double SweepAngleDegrees)
{
    public bool IsValid =>
        Id != Guid.Empty && LevelId != Guid.Empty &&
        double.IsFinite(Center.X) && double.IsFinite(Center.Y) &&
        double.IsFinite(Radius) && Radius is >= 0.1 and <= 10000 &&
        double.IsFinite(StartAngleDegrees) &&
        double.IsFinite(SweepAngleDegrees) &&
        Math.Abs(SweepAngleDegrees) is >= 1 and <= 360;

    /// <summary>
    /// Approximates a circular arc with a maximum 1 mm sagitta, up to 512 straight segments.
    /// The approximation is shared by room calculations and the plan view.
    /// </summary>
    public IReadOnlyList<Point2> SamplePoints(double maxSagitta = 0.001)
    {
        if (!IsValid) throw new InvalidDataException("The curved room separator is invalid.");
        var allowed = 2 * Math.Acos(Math.Clamp(1 - maxSagitta / Radius, -1, 1));
        var sweep = SweepAngleDegrees * Math.PI / 180;
        var count = Math.Clamp((int)Math.Ceiling(Math.Abs(sweep) / allowed), 2, 512);
        var start = StartAngleDegrees * Math.PI / 180;
        var points = new List<Point2>(count + 1);
        for (var i = 0; i <= count; i++)
        {
            var angle = start + sweep * i / count;
            points.Add(new Point2(Center.X + Radius * Math.Cos(angle),
                Center.Y + Radius * Math.Sin(angle)));
        }
        return points;
    }
}

/// <summary>Transactional creation and manipulation of curved, non-solid plan boundaries.</summary>
public sealed partial class DocumentSession
{
    public Guid AddCurvedRoomSeparator(Guid levelId, Point2 center, double radius,
        double startAngleDegrees, double sweepAngleDegrees)
    {
        var arc = new CurvedRoomSeparator(Guid.NewGuid(), levelId, center, radius,
            startAngleDegrees, sweepAngleDegrees);
        if (!arc.IsValid || !Document.Levels.Any(level => level.Id == levelId))
            throw new InvalidDataException("The curved room separator is invalid.");
        Commit("Transaction.AddCurvedRoomSeparator", document =>
        {
            document.CurvedRoomSeparators.Add(arc);
            RefreshRoomBoundariesIfNeeded(document, levelId);
        });
        return arc.Id;
    }

    public void UpdateCurvedRoomSeparator(Guid id, Point2 center, double radius,
        double startAngleDegrees, double sweepAngleDegrees)
    {
        var previous = Document.CurvedRoomSeparators.FirstOrDefault(arc => arc.Id == id)
            ?? throw new InvalidDataException("The curved room separator does not exist.");
        var after = previous with { Center = center, Radius = radius,
            StartAngleDegrees = startAngleDegrees, SweepAngleDegrees = sweepAngleDegrees };
        if (!after.IsValid) throw new InvalidDataException("The curved room separator is invalid.");
        Commit("Transaction.UpdateCurvedRoomSeparator", document =>
        {
            var index = document.CurvedRoomSeparators.FindIndex(arc => arc.Id == id);
            document.CurvedRoomSeparators[index] = after;
            RefreshRoomBoundariesIfNeeded(document, previous.LevelId);
        });
    }

    public void RemoveCurvedRoomSeparator(Guid id)
    {
        var arc = Document.CurvedRoomSeparators.FirstOrDefault(candidate => candidate.Id == id);
        if (arc is null) return;
        Commit("Transaction.RemoveCurvedRoomSeparator", document =>
        {
            document.CurvedRoomSeparators.RemoveAll(candidate => candidate.Id == id);
            RefreshRoomBoundariesIfNeeded(document, arc.LevelId);
        });
    }
}
