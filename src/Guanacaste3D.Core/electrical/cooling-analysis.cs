namespace Guanacaste3D.Core;

/// <summary>Whether a room's air conditioning matches its cooling load.</summary>
public enum CoolingStatus
{
    /// <summary>The unit is not inside a room, so there is no load to compare.</summary>
    NoRoom,
    Ok,
    /// <summary>Less than 90 % of the load: the room will not reach its set point on a hot afternoon.</summary>
    Undersized,
    /// <summary>
    /// More than 150 % of the load: the unit short-cycles and stops before it has dried the air, which in a humid climate
    /// leaves the room cold and clammy.
    /// </summary>
    Oversized
}

/// <summary>One air conditioner: the room it cools, that room's load and everything installed in it.</summary>
public sealed record CoolingReport(ElectricalDevice Unit, Room? Room, double RoomArea, double RequiredBtu, double InstalledBtu,
    CoolingStatus Status);

/// <summary>
/// Cooling check by floor area, the rule of thumb used to size minisplits in hot, humid lowlands: about
/// <see cref="ElectricalStandards.CoolingPerSquareMeter"/> BTU/h per square meter. All units in a room share its load.
/// </summary>
public static class CoolingAnalysis
{
    public static IReadOnlyList<CoolingReport> Analyze(BuildingDocument document)
    {
        var units = document.ElectricalDevices.Where(device => device.Kind == ElectricalDeviceKind.MiniSplit).ToList();
        var rooms = units.ToDictionary(unit => unit.Id, unit => RoomAt(document, unit.LevelId, unit.Position));
        return units.Select(unit =>
        {
            var room = rooms[unit.Id];
            if (room is null) return new CoolingReport(unit, null, 0, 0, unit.CoolingBtu, CoolingStatus.NoRoom);
            var area = Area(room);
            var required = Math.Round(area * ElectricalStandards.CoolingPerSquareMeter / 100) * 100;
            var installed = units.Where(other => rooms[other.Id]?.Id == room.Id).Sum(other => other.CoolingBtu);
            var status = installed < required * 0.9 ? CoolingStatus.Undersized
                : installed > required * 1.5 ? CoolingStatus.Oversized
                : CoolingStatus.Ok;
            return new CoolingReport(unit, room, area, required, installed, status);
        }).ToList();
    }

    /// <summary>
    /// The room containing a point on a level. A wall device stands on the wall face, a little outside a room drawn on the
    /// wall centrelines' inner side, so the room is grown by 35 cm for the test; the smallest room wins.
    /// </summary>
    public static Room? RoomAt(BuildingDocument document, Guid levelId, Point2 point) =>
        document.Rooms.Where(room => room.LevelId == levelId)
            .Where(room => point.X >= Math.Min(room.FirstCorner.X, room.SecondCorner.X) - 0.35 &&
                           point.X <= Math.Max(room.FirstCorner.X, room.SecondCorner.X) + 0.35 &&
                           point.Y >= Math.Min(room.FirstCorner.Y, room.SecondCorner.Y) - 0.35 &&
                           point.Y <= Math.Max(room.FirstCorner.Y, room.SecondCorner.Y) + 0.35)
            .MinBy(Area);

    public static double Area(Room room) =>
        Math.Abs(room.SecondCorner.X - room.FirstCorner.X) * Math.Abs(room.SecondCorner.Y - room.FirstCorner.Y);
}
