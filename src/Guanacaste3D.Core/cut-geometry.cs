namespace Guanacaste3D.Core;

public sealed partial class DocumentSession
{
    /// <summary>
    /// Cuts a wall with a solid that passes over it: a beam, a slab, a mass. Where the solid covers the wall in plan and starts below the
    /// wall's top, the top follows the solid's underside, so the wall stops where the solid begins. The wall and the solid must be on
    /// one level, and the solid has to reach the top of the wall, because a wall's top is all that can be shaped.
    /// </summary>
    public void CutWallBy(Guid wallId, Guid cutterId)
    {
        var wall = Document.Walls.FirstOrDefault(item => item.Id == wallId) ?? throw new InvalidDataException("The wall does not exist.");
        var cutter = RequireMember(cutterId);
        if (cutter.LevelId != wall.LevelId) throw new InvalidDataException("Two different members of one level are needed.");
        var length = wall.Length;
        var ux = (wall.End.X - wall.Start.X) / length;
        var uy = (wall.End.Y - wall.Start.Y) / length;
        var points = ElementGeometry.Parts(cutter).SelectMany(part => part.Footprint).ToArray();
        if (points.Length == 0) throw new InvalidDataException("The cutter has no solid.");
        double Along(Point2 p) => (p.X - wall.Start.X) * ux + (p.Y - wall.Start.Y) * uy;
        double Across(Point2 p) => -(p.X - wall.Start.X) * uy + (p.Y - wall.Start.Y) * ux;
        var reach = wall.Thickness;
        if (points.Max(Across) < -reach || points.Min(Across) > reach) throw new InvalidDataException("The cutter does not touch the wall.");
        var from = Math.Max(0, points.Min(Along));
        var to = Math.Min(length, points.Max(Along));
        if (to - from < 0.01) throw new InvalidDataException("The cutter does not touch the wall.");
        var bottom = cutter.Offset;
        if (bottom >= wall.Height - 1e-9 || cutter.Offset + cutter.Height < wall.Height - 1e-9) throw new InvalidDataException("The cutter does not reach the top of the wall.");
        if (bottom < WallProfiles.MinimumHeight) throw new InvalidDataException("The cutter leaves the wall too low.");

        // The old top, with a step down to the underside of the cutter between the two stations.
        const double step = 0.002;
        var stations = new SortedSet<double> { 0, length, from, to };
        if (from - step > 0) stations.Add(from - step);
        if (to + step < length) stations.Add(to + step);
        if (wall.TopProfile is { } old) foreach (var point in old) if (point.X > 0 && point.X < length) stations.Add(point.X);
        var profile = new List<Point2>();
        foreach (var station in stations)
        {
            if (profile.Count > 0 && station - profile[^1].X < 0.001) continue;
            var height = wall.TopHeightAt(station);
            if (station >= from - 1e-9 && station <= to + 1e-9) height = Math.Min(height, bottom);
            profile.Add(new Point2(station, Math.Max(WallProfiles.MinimumHeight, height)));
        }
        SetWallProfile(wallId, profile);
    }

    /// <summary>
    /// Places a mass whose base is at a chosen elevation above its level, for a volume that floats over the site (a bridge, a canopy) or
    /// starts on a podium. Returns the mass.
    /// </summary>
    public Guid AddMassAtElevation(Guid levelId, Point2 first, Point2 second, double elevation, double height, string name)
    {
        if (!double.IsFinite(elevation) || !double.IsFinite(height) || height < 0.2 || height > 500 || elevation < -100 || elevation > 1000)
            throw new InvalidDataException("The sketch is invalid.");
        if (Math.Abs(first.X - second.X) < 0.1 || Math.Abs(first.Y - second.Y) < 0.1) throw new InvalidDataException("The sketch is invalid.");
        using var group = BeginGroup("Transaction.AddMassAtElevation");
        var id = AddStructuralElement(levelId, StructuralElementKind.Mass, name, first, second);
        var mass = Document.StructuralElements.First(item => item.Id == id);
        UpdateStructuralElement(id, name, mass.Width, height, elevation, ElementStyle.Default);
        group.Assimilate();
        return id;
    }
}
