namespace Guanacaste3D.Core;

/// <summary>Represents one normalized row in the coordinated project schedule.</summary>
public sealed record ProjectScheduleRow(
    string Category,
    string Number,
    string Name,
    string Level,
    double Quantity,
    string Unit);

/// <summary>Builds deterministic schedule rows from the current BIM document.</summary>
public static class ProjectScheduleService
{
    /// <summary>Builds wall, room, floor, and sheet rows for UI and export consumers.</summary>
    public static IReadOnlyList<ProjectScheduleRow> Build(BuildingDocument document)
    {
        document.Validate();
        var levelNames = document.Levels.ToDictionary(level => level.Id, level => level.Name);
        var wallTypeNames = document.WallTypes.ToDictionary(type => type.Id, type => type.Name);
        var rows = new List<ProjectScheduleRow>();

        rows.AddRange(document.Walls.Select(wall => new ProjectScheduleRow(
            "Wall",
            wall.Id.ToString("N")[..8],
            wallTypeNames[wall.WallTypeId],
            levelNames[wall.LevelId],
            wall.Length,
            "m")));
        rows.AddRange(document.Rooms.Select(room => new ProjectScheduleRow(
            "Room",
            room.Number,
            room.Name,
            levelNames[room.LevelId],
            room.Area,
            "m²")));
        rows.AddRange(document.Floors.Select(floor => new ProjectScheduleRow(
            "Floor",
            floor.Id.ToString("N")[..8],
            floor.Name,
            levelNames[floor.LevelId],
            floor.CalculateArea(),
            "m²")));
        rows.AddRange(document.Sheets.Select(sheet => new ProjectScheduleRow(
            "Sheet",
            sheet.Number,
            sheet.Name,
            string.Empty,
            1,
            sheet.PaperSize.ToString())));

        return rows
            .OrderBy(row => row.Category, StringComparer.Ordinal)
            .ThenBy(row => row.Number, StringComparer.Ordinal)
            .ToArray();
    }
}
