using System.Numerics;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core.Rendering;

/// <summary>A deterministic scene with the camera and lighting that the benchmark and regression suites render.</summary>
public sealed record BenchmarkScene(string Name, SceneMesh Mesh, RenderCamera Camera, RenderSettings Settings)
{
    /// <summary>Explicit lights added to the scene (none for daylight scenes).</summary>
    public IReadOnlyList<RenderLight> Lights { get; init; } = [];

    /// <summary>Material of each surface category; null uses the stock library.</summary>
    public Func<SurfaceKind, RenderMaterial>? Materials { get; init; }
}

/// <summary>
/// Reproducible scenes for measuring renderer quality, speed and memory. They are generated in code so
/// every build renders exactly the same geometry, which is what makes image comparisons meaningful.
/// </summary>
public static class BenchmarkScenes
{
    /// <summary>The demonstration house seen from outside in direct sun.</summary>
    public static BenchmarkScene HouseExterior()
    {
        var house = SampleProjects.CreateDemonstrationHouse();
        var (min, max) = BuildingMesher.Build(house).Bounds();
        return new BenchmarkScene("house-exterior", BuildingMesher.Build(house, includeGround: true),
            RenderCamera.FrameBounds(min, max), new RenderSettings());
    }

    /// <summary>
    /// A closed 5 × 4 × 2.8 m room whose only light enters through one glazed window: the hard case for
    /// path tracing, because daylight reaches the camera almost entirely through indirect bounces.
    /// </summary>
    public static BenchmarkScene DaylitRoom()
    {
        var mesh = new SceneMesh();
        AddBox(mesh, new(-0.2f, -0.2f, -0.3f), new(5.2f, 4.2f, 0), SurfaceKind.Floor);
        AddBox(mesh, new(-0.2f, -0.2f, 2.8f), new(5.2f, 4.2f, 3.0f), SurfaceKind.Wall);
        AddBox(mesh, new(-0.2f, 0, 0), new(0, 4, 2.8f), SurfaceKind.Wall);
        AddBox(mesh, new(5, 0, 0), new(5.2f, 4, 2.8f), SurfaceKind.Wall);
        AddBox(mesh, new(-0.2f, 4, 0), new(5.2f, 4.2f, 2.8f), SurfaceKind.Wall);
        // South wall around a 2.0 × 1.3 m window with its sill at 0.9 m.
        AddBox(mesh, new(-0.2f, -0.2f, 0), new(5.2f, 0, 0.9f), SurfaceKind.Wall);
        AddBox(mesh, new(-0.2f, -0.2f, 2.2f), new(5.2f, 0, 2.8f), SurfaceKind.Wall);
        AddBox(mesh, new(-0.2f, -0.2f, 0.9f), new(1.5f, 0, 2.2f), SurfaceKind.Wall);
        AddBox(mesh, new(3.5f, -0.2f, 0.9f), new(5.2f, 0, 2.2f), SurfaceKind.Wall);
        mesh.AddQuad(new(1.5f, -0.1f, 0.9f), new(3.5f, -0.1f, 0.9f), new(3.5f, -0.1f, 2.2f), new(1.5f, -0.1f, 2.2f),
            SurfaceKind.Glass);
        // A low structure in the room gives the indirect light something to occlude and bounce from.
        AddBox(mesh, new(3.6f, 1.6f, 0), new(4.6f, 2.6f, 0.45f), SurfaceKind.Structure);
        return new BenchmarkScene("daylit-room", mesh,
            new RenderCamera(new(4.6f, 3.7f, 1.4f), new(1.8f, 0.3f, 1.0f), 75),
            new RenderSettings { SunDirection = new Vector3(-0.35f, -0.75f, 0.55f), Exposure = 3.0f });
    }

    /// <summary>
    /// A single-storey building of 6 x 5 rooms, each 4 x 4 m with 12 ceiling lights (360 luminaires), lit only at night.
    /// The camera looks across several rooms through their doorways. Light in other rooms is real but mostly blocked,
    /// which is exactly where choosing lights badly wastes rays.
    /// </summary>
    public static BenchmarkScene ManyLights()
    {
        var mesh = new SceneMesh();
        const float room = 4f, height = 2.8f, wall = 0.15f;
        const int columns = 6, rows = 5;
        AddBox(mesh, new(-1, -1, -0.2f), new(columns * room + 1, rows * room + 1, 0), SurfaceKind.Floor);
        AddBox(mesh, new(-1, -1, height), new(columns * room + 1, rows * room + 1, height + 0.2f), SurfaceKind.Wall);
        var lights = new List<RenderLight>();
        for (var row = 0; row <= rows; row++)       // walls running east-west, with a doorway in each room
            for (var column = 0; column < columns; column++)
            {
                var x0 = column * room;
                var y = row * room;
                AddBox(mesh, new(x0, y - wall / 2, 0), new(x0 + 1.4f, y + wall / 2, height), SurfaceKind.Wall);
                AddBox(mesh, new(x0 + 2.4f, y - wall / 2, 0), new(x0 + room, y + wall / 2, height), SurfaceKind.Wall);
                AddBox(mesh, new(x0 + 1.4f, y - wall / 2, 2.1f), new(x0 + 2.4f, y + wall / 2, height), SurfaceKind.Wall);
            }
        for (var column = 0; column <= columns; column++)  // walls running north-south, solid
            AddBox(mesh, new(column * room - wall / 2, 0, 0), new(column * room + wall / 2, rows * room, height), SurfaceKind.Wall);
        for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
                for (var i = 0; i < 12; i++)
                    lights.Add(new RenderLight(
                        new Vector3(column * room + 0.7f + (i % 4) * 0.87f, row * room + 0.8f + (i / 4) * 1.2f, height - 0.05f),
                        new Vector3(1.0f, 0.86f, 0.70f) * 0.55f, Radius: 0.06f));
        return new BenchmarkScene("many-lights", mesh,
            new RenderCamera(new(2.0f, 2.0f, 1.5f), new(15f, 10.5f, 1.2f), 70),
            new RenderSettings { SunIntensity = 0, SkyIntensity = 0.01f, Exposure = 0.6f, MaxBounces = 3, SkyPortals = false })
        { Lights = lights };
    }

    /// <summary>Appends an axis-aligned box as twelve triangles.</summary>
    public static void AddBox(SceneMesh mesh, Vector3 min, Vector3 max, SurfaceKind kind)
    {
        var a = new Vector3(min.X, min.Y, min.Z);
        var b = new Vector3(max.X, min.Y, min.Z);
        var c = new Vector3(max.X, max.Y, min.Z);
        var d = new Vector3(min.X, max.Y, min.Z);
        var e = new Vector3(min.X, min.Y, max.Z);
        var f = new Vector3(max.X, min.Y, max.Z);
        var g = new Vector3(max.X, max.Y, max.Z);
        var h = new Vector3(min.X, max.Y, max.Z);
        mesh.AddQuad(a, d, c, b, kind); // bottom
        mesh.AddQuad(e, f, g, h, kind); // top
        mesh.AddQuad(a, b, f, e, kind); // south
        mesh.AddQuad(b, c, g, f, kind); // east
        mesh.AddQuad(c, d, h, g, kind); // north
        mesh.AddQuad(d, a, e, h, kind); // west
    }
}
