using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Lumion-style mass placement: even spacing along paths, Poisson-disk areas, limits, and one undo step.</summary>
static class MassPlacementChecks
{
    public static void Run(Action<bool, string> check)
    {
        var fixedTurn = new MassPlacementOptions { Spacing = 4, ScaleVariation = 0, RandomRotation = false };
        Point2[] corner = [new(0, 0), new(10, 0), new(10, 10)];
        var row = MassPlacement.AlongPath(corner, fixedTurn);
        // 0, 4, 8 on the first leg; the spacing carries round the corner to 12, 16 and 20 meters along.
        check(row.Count == 6 && row[3].At.DistanceTo(new Point2(10, 2)) < 1e-9 && row[^1].At.DistanceTo(new Point2(10, 10)) < 1e-9,
            "Mass placement along a path keeps the spacing even across a corner");
        check(Math.Abs(row[0].Rotation) < 1e-9 && Math.Abs(row[^1].Rotation - Math.PI / 2) < 1e-9 && row.All(item => item.Scale == 1),
            "Without random turns the instances face along the path at their modeled size");

        var options = new MassPlacementOptions { Spacing = 3, ScaleVariation = 0.2, Seed = 7 };
        Point2[] square = [new(0, 0), new(30, 0), new(30, 30), new(0, 30)];
        var grove = MassPlacement.InArea(square, options);
        var closest = double.MaxValue;
        for (var i = 0; i < grove.Count; i++)
            for (var j = i + 1; j < grove.Count; j++)
                closest = Math.Min(closest, grove[i].At.DistanceTo(grove[j].At));
        check(grove.Count > 50 && closest >= 3 - 1e-9 && grove.All(item => item.Scale is >= 0.8 and <= 1.2),
            "An area fills evenly with no two instances closer than the spacing");
        check(MassPlacement.InArea(square, options).SequenceEqual(grove) &&
              !MassPlacement.InArea(square, options with { Seed = 8 }).SequenceEqual(grove),
            "The same seed repeats an arrangement and another seed changes it");

        Point2[] ell = [new(0, 0), new(20, 0), new(20, 8), new(8, 8), new(8, 20), new(0, 20)];
        check(MassPlacement.InArea(ell, options).All(item => item.At.X <= 8 || item.At.Y <= 8),
            "Nothing lands in the notch of a concave area");

        check(Throws(() => MassPlacement.InArea([new(0, 0), new(1000, 0), new(1000, 1000), new(0, 1000)], options with { Spacing = 0.5 })) &&
              Throws(() => MassPlacement.AlongPath([new(0, 0)], options)) &&
              Throws(() => MassPlacement.AlongPath([new(0, 0), new(0, 0), new(0, 0)], options)) &&
              Throws(() => MassPlacement.AlongPath(corner, options with { Spacing = double.NaN })),
            "Mass placement refuses floods, single points and invalid spacing");

        // A near-cap area is permitted by the density estimate; the actual generator must
        // never return more than the transaction's 2,000-instance hard limit.
        var bounded = true;
        try
        {
            Point2[] nearCap = [new(0, 0), new(54, 0), new(54, 55), new(0, 55)];
            bounded = MassPlacement.InArea(nearCap, options with { Spacing = 1 }).Count <= MassPlacement.MaximumInstances;
        }
        catch (InvalidDataException) { /* Rejection at the hard limit is also valid. */ }
        check(bounded, "Area mass placement never exceeds the transaction instance limit");

        // One undo step for the lot, each instance at its own size.
        Vector3[] corners = [new(-1, -1, 0), new(1, -1, 0), new(0, 1, 0), new(0, 0, 4)];
        var family = new FamilyDocument
        {
            Name = "Árbol de prueba", Category = FamilyCategory.Planting,
            Types = [new FamilyTypeDefinition(Guid.NewGuid(), "Estándar", [])],
            Mesh = FamilyMesh.Create(corners, [0, 1, 3, 1, 2, 3, 2, 0, 3], [SurfaceKind.Leaf, SurfaceKind.Leaf, SurfaceKind.Leaf])
        };
        var session = new DocumentSession(ArchitecturalLibrary.CreateProject("Colocación masiva"));
        var level = session.Document.Levels[0].Id;
        var id = session.LoadComponentFamily(family);
        var before = session.Document.StructuralElements.Count;
        var undoDepth = session.UndoNames.Count;
        var placed = session.PlaceComponentFamilies(level, id, grove);
        var heights = session.Document.StructuralElements.Where(element => element.FamilyId == id).Select(element => element.Height).ToArray();
        check(placed.Count == grove.Count && heights.Length == grove.Count && heights.Distinct().Count() > 10 &&
              session.UndoNames.Count == undoDepth + 1 && session.UndoName == "Transaction.MassPlaceComponents",
            "A mass placement adds every instance, each at its own size, in one undo step");
        session.Undo();
        check(session.Document.StructuralElements.Count == before, "Undoing a mass placement removes every instance at once");

        check(Throws(() => session.PlaceComponentFamilies(level, id, [new(new Point2(1, 1), 0, 1), new(new Point2(2, 2), 0, 0)])) &&
              session.Document.StructuralElements.Count == before &&
              Throws(() => session.PlaceComponentFamilies(level, Guid.NewGuid(), grove)),
            "An invalid instance or an unknown family places nothing");

        // A 10 cm grass tuft is narrower than the narrowest planting the model accepts: it grows evenly instead of failing.
        var tuft = family.Clone();
        tuft.Id = Guid.NewGuid();
        tuft.Name = "Mata de prueba";
        tuft.Mesh = FamilyMesh.Create(corners.Select(corner => corner * 0.05f).ToArray(), [0, 1, 3, 1, 2, 3, 2, 0, 3],
            [SurfaceKind.Leaf, SurfaceKind.Leaf, SurfaceKind.Leaf]);
        var tuftId = session.LoadComponentFamily(tuft);
        var tufts = session.PlaceComponentFamilies(level, tuftId, [new(new Point2(3, 3), 0, 0.8), new(new Point2(4, 3), 0, 1)]);
        var tiny = session.Document.StructuralElements.Where(element => tufts.Contains(element.Id)).ToArray();
        // 0.1 m across and 0.2 m tall: doubled to the minimum width, its height doubles with it.
        check(tiny.Length == 2 && tiny.All(element => Math.Abs(element.Width - 0.2) < 1e-5 && Math.Abs(element.Height - 0.4) < 1e-5),
            "A planting smaller than the minimum grows evenly to 0.2 m instead of failing");
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (InvalidDataException) { return true; }
    }
}
