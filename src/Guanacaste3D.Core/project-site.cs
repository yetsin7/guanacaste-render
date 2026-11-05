namespace Guanacaste3D.Core;

/// <summary>
/// Where the project is on Earth and how it is turned. <see cref="TrueNorthDegrees"/> is the angle from the project's +Y
/// axis to true north, counter-clockwise; the shared coordinates (East, North) of the project origin tie the model to a
/// survey. The model itself keeps its own project coordinates.
/// </summary>
public sealed record ProjectSite(double Latitude, double Longitude, double ElevationMeters, double TrueNorthDegrees,
    double SharedEast, double SharedNorth)
{
    /// <summary>Guanacaste, Costa Rica, with the project facing true north and its origin as the survey origin.</summary>
    public static ProjectSite Default { get; } = new(10.63, -85.44, 0, 0, 0, 0);

    public bool IsValid => new[] { Latitude, Longitude, ElevationMeters, TrueNorthDegrees, SharedEast, SharedNorth }.All(double.IsFinite) &&
        Latitude is >= -90 and <= 90 && Longitude is >= -180 and <= 180 && ElevationMeters is >= -500 and <= 9000 &&
        TrueNorthDegrees is >= -360 and <= 360 && Math.Abs(SharedEast) <= 1e9 && Math.Abs(SharedNorth) <= 1e9;

    private double Radians => TrueNorthDegrees * Math.PI / 180;

    /// <summary>The direction of true east, in project coordinates.</summary>
    public Point2 EastDirection => new(Math.Cos(Radians), Math.Sin(Radians));

    /// <summary>The direction of true north, in project coordinates.</summary>
    public Point2 NorthDirection => new(-Math.Sin(Radians), Math.Cos(Radians));

    /// <summary>A point of the project, in meters, as shared (East, North) coordinates.</summary>
    public Point2 ToShared(Point2 point) => new(
        SharedEast + point.X * EastDirection.X + point.Y * EastDirection.Y,
        SharedNorth + point.X * NorthDirection.X + point.Y * NorthDirection.Y);

    /// <summary>The project point that has the given shared (East, North) coordinates.</summary>
    public Point2 ToProject(Point2 shared)
    {
        var east = shared.X - SharedEast;
        var north = shared.Y - SharedNorth;
        return new(east * EastDirection.X + north * NorthDirection.X, east * EastDirection.Y + north * NorthDirection.Y);
    }

    /// <summary>A direction given against true north (x east, y north), turned into the project's coordinates.</summary>
    public (double X, double Y) TrueToProject(double east, double north) =>
        (east * EastDirection.X + north * NorthDirection.X, east * EastDirection.Y + north * NorthDirection.Y);
}

public sealed partial class DocumentSession
{
    /// <summary>Sets the project's place and orientation, as one undoable step. The model is not touched.</summary>
    public void SetProjectSite(ProjectSite site)
    {
        ArgumentNullException.ThrowIfNull(site);
        if (!site.IsValid) throw new InvalidDataException("The project site is invalid.");
        if (site == Document.Site) return;
        Commit("Transaction.SetProjectSite", document => document.Site = site);
    }
}
