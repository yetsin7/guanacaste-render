using System.Diagnostics;
using System.Numerics;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

/// <summary>A/B experiment: the binary BVH used so far versus the four-wide, leaf-ordered Bvh4.</summary>
static class BvhExperiment
{
    public static void Run()
    {
        var house = BenchmarkScenes.HouseExterior();
        Compare("house (" + house.Mesh.TriangleCount + " tris)", house.Mesh, house.Camera.Position);
        var big = RandomSoup(600_000, 300);
        Compare("soup (" + big.TriangleCount + " tris)", big, new Vector3(0, 0, 0));
    }

    private static void Compare(string name, SceneMesh mesh, Vector3 viewpoint)
    {
        var (min, max) = mesh.Bounds();
        var rays = new (Vector3 O, Vector3 D)[400_000];
        var random = new Random(3);
        for (var i = 0; i < rays.Length; i++)
        {
            var origin = i % 2 == 0
                ? viewpoint
                : min + (max - min) * new Vector3(random.NextSingle(), random.NextSingle(), random.NextSingle());
            var direction = new Vector3(random.NextSingle() - 0.5f, random.NextSingle() - 0.5f, random.NextSingle() - 0.5f);
            rays[i] = (origin, Vector3.Normalize(direction + new Vector3(1e-3f)));
        }

        var timer = Stopwatch.StartNew();
        var legacy = new Bvh(mesh);
        var legacyBuild = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        var wide = new Bvh4(mesh);
        var wideBuild = timer.Elapsed.TotalMilliseconds;
        Console.WriteLine($"\n{name}: build binary {legacyBuild:0} ms, 4-wide {wideBuild:0} ms · 4-wide memory {wide.MemoryBytes / 1048576.0:0.0} MB");

        var mismatches = 0;
        for (var i = 0; i < 20_000; i++)
        {
            var a = new RayHit { Distance = float.MaxValue };
            var b = new RayHit { Distance = float.MaxValue };
            var ha = legacy.Intersect(rays[i].O, rays[i].D, 1e-4f, ref a);
            var hb = wide.Intersect(rays[i].O, rays[i].D, 1e-4f, ref b);
            if (ha != hb || (ha && MathF.Abs(a.Distance - b.Distance) > 1e-3f * MathF.Max(1, a.Distance))) mismatches++;
        }
        Console.WriteLine($"  agreement on 20000 rays: {mismatches} mismatches");

        for (var round = 0; round < 3; round++)
        {
            Console.WriteLine($"  binary closest-hit : {Rate(rays, (o, d) => { var h = new RayHit { Distance = float.MaxValue }; return legacy.Intersect(o, d, 1e-4f, ref h); }):0.0} Mray/s" +
                              $" | 4-wide: {Rate(rays, (o, d) => { var h = new RayHit { Distance = float.MaxValue }; return wide.Intersect(o, d, 1e-4f, ref h); }):0.0} Mray/s" +
                              $" | binary any-hit: {Rate(rays, (o, d) => legacy.Occluded(o, d, 1e-4f, 50)):0.0} | 4-wide any-hit: {Rate(rays, (o, d) => wide.Occluded(o, d, 1e-4f, 50)):0.0}");
        }
    }

    private static double Rate((Vector3 O, Vector3 D)[] rays, Func<Vector3, Vector3, bool> trace)
    {
        var timer = Stopwatch.StartNew();
        var hits = 0;
        Parallel.For(0, Environment.ProcessorCount, worker =>
        {
            var local = 0;
            for (var i = worker; i < rays.Length; i += Environment.ProcessorCount)
                if (trace(rays[i].O, rays[i].D)) local++;
            Interlocked.Add(ref hits, local);
        });
        return rays.Length / timer.Elapsed.TotalSeconds / 1e6;
    }

    /// <summary>Random small triangles spread through a cube: a stress test with no architectural structure.</summary>
    private static SceneMesh RandomSoup(int count, float size)
    {
        var mesh = new SceneMesh();
        var random = new Random(11);
        for (var i = 0; i < count; i++)
        {
            var c = new Vector3(random.NextSingle(), random.NextSingle(), random.NextSingle() * 0.3f) * size;
            Vector3 Jitter() => new((random.NextSingle() - 0.5f) * 2, (random.NextSingle() - 0.5f) * 2, (random.NextSingle() - 0.5f) * 2);
            mesh.AddTriangle(c + Jitter(), c + Jitter(), c + Jitter(), SurfaceKind.Wall);
        }
        return mesh;
    }
}
