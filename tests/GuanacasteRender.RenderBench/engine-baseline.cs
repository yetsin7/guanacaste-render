using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

/// <summary>
/// Reproducible baseline of the C# engine, and the bridge to the Rust engine: every scene is written as a
/// <c>.grscene</c> file together with the exact rays that were timed, so <c>engine/crates/gr-bench</c> measures the same
/// queries on the same triangles. Results go to <c>baseline-csharp.json</c> in the output folder.
///   dotnet run -c Release --project tests/GuanacasteRender.RenderBench -- --baseline --out artifacts/engine-bench
/// </summary>
static class EngineBaseline
{
    private const int RayCount = 400_000;

    public static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var rows = new List<object>();
        var scenes = new (string Name, Func<BenchmarkScene> Create, bool Trace)[]
        {
            ("house-exterior", BenchmarkScenes.HouseExterior, true),
            ("daylit-room", BenchmarkScenes.DaylitRoom, true),
            ("villa", ShowcaseShot.Villa, true),
            ("cobogo", ShowcaseShot.Cobogo, true),
            ("soup-600k", Soup, false)
        };
        foreach (var (name, create, trace) in scenes)
        {
            GC.Collect();
            var managedBefore = GC.GetTotalMemory(true);
            var clock = Stopwatch.StartNew();
            var source = create();
            var builder = new RenderSceneBuilder().AddBuilding(source.Mesh, source.Materials);
            foreach (var light in source.Lights) builder.AddLight(light);
            var scene = builder.Build();
            var geometryMs = clock.Elapsed.TotalMilliseconds;

            var rays = Rays(scene, source.Camera.Position);
            SceneExchange.Write(Path.Combine(folder, name + SceneExchange.Extension), scene, source.Camera, source.Settings);
            SceneExchange.WriteRays(Path.Combine(folder, name + ".grrays"), rays);

            var buildMs = double.MaxValue;
            Bvh? bvh = null;
            for (var round = 0; round < 3; round++)
            {
                clock.Restart();
                bvh = new Bvh(scene.Positions, scene.Indices, null);
                buildMs = Math.Min(buildMs, clock.Elapsed.TotalMilliseconds);
            }

            // Single-threaded reference pass: a checksum another engine must reproduce.
            var hits = 0;
            var distanceSum = 0.0;
            foreach (var (origin, direction) in rays)
            {
                var hit = new RayHit { Distance = float.MaxValue };
                if (!bvh!.Intersect(origin, direction, 1e-4f, ref hit)) continue;
                hits++;
                distanceSum += hit.Distance;
            }
            var closest = Best(() => Rate(rays, (o, d) =>
            {
                var hit = new RayHit { Distance = float.MaxValue };
                return bvh!.Intersect(o, d, 1e-4f, ref hit);
            }, Environment.ProcessorCount));
            var closestSingle = Best(() => Rate(rays, (o, d) =>
            {
                var hit = new RayHit { Distance = float.MaxValue };
                return bvh!.Intersect(o, d, 1e-4f, ref hit);
            }, 1));
            var anyHit = Best(() => Rate(rays, (o, d) => bvh!.Occluded(o, d, 1e-4f, 50), Environment.ProcessorCount));

            double passMs = 0, megaRays = 0, raysPerPixel = 0;
            if (trace)
            {
                const int width = 480, height = 270;
                var tracer = new PathTracer(scene, width, height, source.Camera, source.Settings);
                tracer.RenderPass();
                passMs = double.MaxValue;
                for (var batch = 0; batch < 5; batch++)
                {
                    var before = tracer.RayCount;
                    clock.Restart();
                    for (var i = 0; i < 3; i++) tracer.RenderPass();
                    var ms = clock.Elapsed.TotalMilliseconds / 3;
                    if (ms >= passMs) continue;
                    passMs = ms;
                    var perPass = (tracer.RayCount - before) / 3.0;
                    megaRays = perPass / ms / 1000;
                    raysPerPixel = perPass / (width * height);
                }
            }
            var managedMb = (GC.GetTotalMemory(false) - managedBefore) / 1048576.0;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{name,-16} {scene.TriangleCount,8} tris · geometry {geometryMs,6:0} ms · bvh {buildMs,6:0.0} ms · " +
                $"closest {closest,6:0.0} Mray/s (1 thread {closestSingle:0.00}) · any-hit {anyHit,6:0.0} · " +
                $"path {passMs,6:0.0} ms/pass {megaRays:0.0} Mray/s · hits {hits} Σd {distanceSum:0.0}"));
            rows.Add(new
            {
                scene = name, triangles = scene.TriangleCount, vertices = scene.Positions.Length,
                materials = scene.Materials.Length, geometryMs, bvhBuildMs = buildMs,
                closestHitMraysPerSecond = closest, closestHitSingleThreadMraysPerSecond = closestSingle,
                anyHitMraysPerSecond = anyHit, referenceHits = hits, referenceDistanceSum = distanceSum,
                pathTracerMsPerPass480x270 = passMs, pathTracerMraysPerSecond = megaRays,
                pathTracerRaysPerPixel = raysPerPixel, managedMegabytes = managedMb
            });
        }
        var process = Process.GetCurrentProcess();
        var report = new
        {
            engine = "csharp", measuredAtUtc = DateTime.UtcNow, cpu = HardwareProfile.Detect().CpuName,
            logicalCores = Environment.ProcessorCount, rays = RayCount,
            peakWorkingSetMegabytes = process.PeakWorkingSet64 / 1048576.0, scenes = rows
        };
        File.WriteAllText(Path.Combine(folder, "baseline-csharp.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static double Best(Func<double> measure)
    {
        var best = 0.0;
        for (var round = 0; round < 4; round++) best = Math.Max(best, measure());
        return best;
    }

    /// <summary>Half the rays leave the camera, half start anywhere inside the scene bounds; directions are uniform in a cube.</summary>
    private static (Vector3 Origin, Vector3 Direction)[] Rays(RenderScene scene, Vector3 viewpoint)
    {
        var (min, max) = scene.Bounds();
        var rays = new (Vector3, Vector3)[RayCount];
        var random = new Random(3);
        for (var i = 0; i < rays.Length; i++)
        {
            var origin = i % 2 == 0
                ? viewpoint
                : min + (max - min) * new Vector3(random.NextSingle(), random.NextSingle(), random.NextSingle());
            var direction = new Vector3(random.NextSingle() - 0.5f, random.NextSingle() - 0.5f, random.NextSingle() - 0.5f);
            rays[i] = (origin, Vector3.Normalize(direction + new Vector3(1e-3f)));
        }
        return rays;
    }

    private static double Rate((Vector3 O, Vector3 D)[] rays, Func<Vector3, Vector3, bool> trace, int workers)
    {
        var timer = Stopwatch.StartNew();
        Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers }, worker =>
        {
            var local = 0;
            for (var i = worker; i < rays.Length; i += workers)
                if (trace(rays[i].O, rays[i].D)) local++;
            GC.KeepAlive(local);
        });
        return rays.Length / timer.Elapsed.TotalSeconds / 1e6;
    }

    /// <summary>Random small triangles spread through a slab: a stress test with no architectural structure.</summary>
    private static BenchmarkScene Soup()
    {
        var mesh = new SceneMesh();
        var random = new Random(11);
        for (var i = 0; i < 600_000; i++)
        {
            var c = new Vector3(random.NextSingle(), random.NextSingle(), random.NextSingle() * 0.3f) * 300;
            Vector3 Jitter() => new((random.NextSingle() - 0.5f) * 2, (random.NextSingle() - 0.5f) * 2, (random.NextSingle() - 0.5f) * 2);
            mesh.AddTriangle(c + Jitter(), c + Jitter(), c + Jitter(), SurfaceKind.Wall);
        }
        return new BenchmarkScene("soup-600k", mesh, new RenderCamera(new Vector3(-20, -20, 60), new Vector3(150, 150, 45), 60),
            new RenderSettings());
    }
}
