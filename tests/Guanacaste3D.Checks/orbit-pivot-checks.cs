using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Navigation;

/// <summary>Guards orbit continuity after panning and selection centers in world coordinates.</summary>
internal static class OrbitPivotChecks
{
    /// <summary>Exercises off-center, zero-delta, pole-limited, repeated, and reversed rotations.</summary>
    internal static void Run(Action<bool, string> check)
    {
        var camera = new NavigationCamera { Position = new(-9, -12, 8), FieldOfViewDegrees = 57 };
        camera.LookAt(new(2, 0, 1));
        var pivot = new Vector3(4, 1, 2);
        var original = camera.Clone();
        var pixel = camera.Project(pivot, 1200, 800)!.Value;
        var distance = Vector3.Distance(camera.Position, pivot);
        camera.OrbitAround(pivot, 0, 0);
        check(camera.Position == original.Position && camera.YawDegrees == original.YawDegrees &&
              camera.PitchDegrees == original.PitchDegrees, "A zero-motion orbit never snaps a panned camera");
        camera.OrbitAround(pivot, 45, 12);
        var rotated = camera.Project(pivot, 1200, 800)!.Value;
        check(Math.Abs(rotated.X - pixel.X) < 0.002 && Math.Abs(rotated.Y - pixel.Y) < 0.002 &&
              Math.Abs(Vector3.Distance(camera.Position, pivot) - distance) < 0.0001,
            "Orbit preserves the off-center selection's pixel and camera distance");
        camera.OrbitAround(pivot, -45, -12);
        check(Vector3.Distance(camera.Position, original.Position) < 0.0001 && camera.FieldOfViewDegrees == 57,
            "Reversing an orbit restores the panned camera without changing zoom");
        for (var index = 0; index < 720; index++) camera.OrbitAround(pivot, 1.25f, 0);
        check(Vector3.Distance(camera.Position, original.Position) < 0.01 &&
              Math.Abs(Vector3.Distance(camera.Position, pivot) - distance) < 0.01,
            "A full orbit stays on the same radius without accumulating a visible jump");
        camera.OrbitAround(pivot, 8, 10000);
        check(camera.PitchDegrees == -89 && float.IsFinite(camera.Position.Length()) &&
              Math.Abs(Vector3.Distance(camera.Position, pivot) - distance) < 0.01,
            "Vertical orbit limits keep the camera finite and preserve radius at the pole");
        var top = new NavigationCamera { Position = new(0, 0, 10), PitchDegrees = -90 };
        top.OrbitAround(Vector3.Zero, 0, 0);
        check(top.PitchDegrees == -90 && top.Position == new Vector3(0, 0, 10),
            "Starting an orbit leaves an exact top view unchanged until the pointer moves");
        SelectionBounds(check);
    }

    /// <summary>Confirms that hosted openings and multipart stairs have independent centers at their level elevation.</summary>
    private static void SelectionBounds(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Orbit bounds regression"));
        var level = session.Document.Levels.OrderByDescending(item => item.Elevation).First();
        var wall = session.AddWall(level.Id, new(0, 0), new(10, 0), height: 4);
        var opening = session.AddOpening(wall, OpeningKind.Window, 2, width: 1, height: 1, sill: 1.5);
        var preset = ArchitecturalLibrary.PresetsFor(StructuralElementKind.Stair).Single(item => item.Style == ElementStyle.UShaped);
        var stair = session.AddElementFromPreset(level.Id, preset, new(20, 0), new(24, 0));
        var picker = new ElementPicker(session.Document);
        var wallBounds = picker.Bounds(PickedKind.Wall, wall)!.Value;
        var openingBounds = picker.Bounds(PickedKind.Opening, opening)!.Value;
        check(Vector3.Distance((wallBounds.Min + wallBounds.Max) / 2, new(5, 0, (float)level.Elevation + 2)) < 0.0001,
            "A selected wall orbits about its center at the correct level elevation");
        check(Math.Abs((openingBounds.Min.Z + openingBounds.Max.Z) / 2 - (level.Elevation + 2)) < 0.0001 &&
              openingBounds.Max.X - openingBounds.Min.X < 1.01,
            "A selected window uses its own sill and width instead of its host wall");
        var stairBounds = picker.Bounds(PickedKind.Structural, stair)!.Value;
        var outline = picker.Outline(PickedKind.Structural, stair).SelectMany(edge => new[] { edge.A, edge.B }).ToArray();
        check(stairBounds.Min == outline.Aggregate(new Vector3(float.PositiveInfinity), Vector3.Min) &&
              stairBounds.Max == outline.Aggregate(new Vector3(float.NegativeInfinity), Vector3.Max),
            "A stair selection includes both flights and the landing in its orbit bounds");
        check(picker.Bounds(PickedKind.Wall, Guid.NewGuid()) is null,
            "A removed selection has no stale element bounds");
    }
}
