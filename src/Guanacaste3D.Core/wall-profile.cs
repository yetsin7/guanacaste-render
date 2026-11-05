using System.Globalization;

namespace Guanacaste3D.Core;

/// <summary>
/// The elevation profile of a wall's top: points of (distance along the wall from its start, height of the top), joined
/// by straight lines. Before the first point and after the last one the top keeps that point's height. Without a
/// profile the top is flat at the wall's height; a profile never rises above the wall's height.
/// </summary>
public static class WallProfiles
{
    public const int MaximumPoints = 64;
    public const double MinimumHeight = 0.05;
    private const double MinimumStep = 0.001;

    /// <summary>Whether the points can be a profile: finite, between 2 and 64, in increasing order and at a usable height.</summary>
    public static bool IsValid(IReadOnlyList<Point2>? points)
    {
        if (points is null || points.Count < 2 || points.Count > MaximumPoints) return false;
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || point.X < -10000 || point.X > 10000 ||
                point.Y < MinimumHeight || point.Y > 1000) return false;
            if (index > 0 && point.X - points[index - 1].X < MinimumStep - 1e-12) return false;
        }
        return true;
    }

    /// <summary>The top of the wall, in meters above its base, at a distance along it.</summary>
    public static double HeightAt(IReadOnlyList<Point2> points, double along)
    {
        if (along <= points[0].X) return points[0].Y;
        for (var index = 1; index < points.Count; index++)
        {
            if (along > points[index].X) continue;
            var from = points[index - 1];
            var to = points[index];
            return from.Y + (to.Y - from.Y) * (along - from.X) / (to.X - from.X);
        }
        return points[^1].Y;
    }

    /// <summary>The gable of a pitched roof: low at both ends and highest in the middle.</summary>
    public static IReadOnlyList<Point2> Gable(double length, double peak, double pitch = 0.3) =>
        [new(0, Math.Max(0.5, peak - pitch * length / 2)), new(length / 2, peak), new(length, Math.Max(0.5, peak - pitch * length / 2))];

    /// <summary>A top that slopes steadily from one end up to the other.</summary>
    public static IReadOnlyList<Point2> Shed(double length, double high, double pitch = 0.15) =>
        [new(0, Math.Max(0.5, high - pitch * length)), new(length, high)];

    /// <summary>A top that climbs in three steps toward the end.</summary>
    public static IReadOnlyList<Point2> Stepped(double length, double high)
    {
        var rise = Math.Min(0.6, high / 4);
        var third = length / 3;
        return
        [
            new(0, high - 2 * rise), new(third - MinimumStep, high - 2 * rise), new(third, high - rise),
            new(2 * third - MinimumStep, high - rise), new(2 * third, high), new(length, high)
        ];
    }

    /// <summary>The profile as it is written in the editor: "0 2.40; 2.50 3.20; 5 2.40".</summary>
    public static string ToText(IReadOnlyList<Point2> points) =>
        string.Join("; ", points.Select(point => string.Create(CultureInfo.InvariantCulture, $"{point.X:0.###} {point.Y:0.###}")));

    /// <summary>Reads "distance height; distance height; …" (a comma between the two numbers also works). Null when it is not a profile.</summary>
    public static IReadOnlyList<Point2>? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var points = new List<Point2>();
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var numbers = part.Split((char)32, StringSplitOptions.RemoveEmptyEntries);
            if (numbers.Length == 1 && numbers[0].Contains(',')) numbers = numbers[0].Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (numbers.Length != 2 ||
                !double.TryParse(numbers[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var along) ||
                !double.TryParse(numbers[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var height)) return null;
            points.Add(new Point2(along, height));
        }
        return IsValid(points) ? points : null;
    }

    /// <summary>The profile seen from the other end of a wall of the given length.</summary>
    public static List<Point2> Mirror(IReadOnlyList<Point2> points, double length) =>
        points.Reverse().Select(point => new Point2(length - point.X, point.Y)).ToList();

    /// <summary>The profile of the part of a wall before <paramref name="station"/>.</summary>
    public static List<Point2> KeepBefore(IReadOnlyList<Point2> points, double station)
    {
        var kept = points.Where(point => point.X < station - MinimumStep).ToList();
        kept.Add(new Point2(station, HeightAt(points, station)));
        if (kept.Count < 2) kept.Insert(0, new Point2(station - 1, kept[0].Y));
        return kept;
    }

    /// <summary>The profile of the part of a wall after <paramref name="station"/>, measured from the new start.</summary>
    public static List<Point2> KeepAfter(IReadOnlyList<Point2> points, double station, double newLength)
    {
        var kept = new List<Point2> { new(0, HeightAt(points, station)) };
        kept.AddRange(points.Where(point => point.X > station + MinimumStep).Select(point => new Point2(point.X - station, point.Y)));
        if (kept.Count < 2) kept.Add(new Point2(Math.Max(newLength, MinimumStep * 2), kept[0].Y));
        return kept;
    }
}

public sealed partial class DocumentSession
{
    /// <summary>
    /// Gives walls a top that follows a profile instead of a flat line, as one undoable step. The profile must stay above
    /// every door and window the wall hosts, with the clearance the opening needs under the top.
    /// </summary>
    public void SetWallProfile(Guid wallId, IReadOnlyList<Point2> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (!WallProfiles.IsValid(points)) throw new InvalidDataException("The wall profile is invalid.");
        var wall = Document.Walls.FirstOrDefault(candidate => candidate.Id == wallId) ?? throw new InvalidDataException("The wall does not exist.");
        var candidate = wall with { TopProfile = [.. points] };
        if (points.Min(point => point.Y) < wall.BaseOffset + WallProfiles.MinimumHeight) throw new InvalidDataException("The profile cuts below the base of the wall.");
        foreach (var opening in Document.Openings.Where(item => item.WallId == wallId))
        {
            var span = points.Select(point => point.X).Where(x => x > opening.Begin && x < opening.End).Append(opening.Begin).Append(opening.End);
            if (span.Min(x => candidate.TopHeightAt(x)) < opening.Sill + opening.Height + 0.05)
                throw new InvalidDataException("The profile cuts below a door or window of the wall.");
        }
        var copy = candidate.TopProfile!;
        Commit("Transaction.SetWallProfile", document =>
        {
            EnsureNotPinned(document, wallId);
            var index = document.Walls.FindIndex(item => item.Id == wallId);
            document.Walls[index] = document.Walls[index] with { TopProfile = copy };
        });
    }

    /// <summary>Takes the profile off walls, so their tops are flat again. Walls that have none are left as they are.</summary>
    public int ResetWallProfiles(IReadOnlyCollection<Guid> wallIds)
    {
        ArgumentNullException.ThrowIfNull(wallIds);
        var targets = Document.Walls.Where(wall => wallIds.Contains(wall.Id) && wall.TopProfile is not null).Select(wall => wall.Id).ToArray();
        if (targets.Length == 0) return 0;
        Commit("Transaction.ResetWallProfile", document =>
        {
            foreach (var id in targets) EnsureNotPinned(document, id);
            for (var index = 0; index < document.Walls.Count; index++)
                if (targets.Contains(document.Walls[index].Id)) document.Walls[index] = document.Walls[index] with { TopProfile = null };
        });
        return targets.Length;
    }
}
