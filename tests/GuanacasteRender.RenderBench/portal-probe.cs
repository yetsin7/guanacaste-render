using Guanacaste3D.Core.Rendering;

static class PortalProbe
{
    public static void Run()
    {
        foreach (var scene in new[] { BenchmarkScenes.DaylitRoom(), BenchmarkScenes.HouseExterior() })
        {
            var render = new RenderSceneBuilder().AddBuilding(scene.Mesh).Build();
            var glass = Enumerable.Range(0, render.TriangleCount).Where(t => render.Materials[render.TriangleMaterial[t]].Transmission > 0).ToArray();
            var opaque = new Bvh(render.Positions, render.Indices, Enumerable.Range(0, render.TriangleCount).Except(glass).ToArray());
            var probes = SkyPortals.Probe(render, glass, opaque, 1e-3f);
            Console.WriteLine($"{scene.Name}: {probes.Count} glass triangles");
            var closedSide = probes.Select(p => MathF.Min(p.Front, p.Back)).OrderBy(x => x).ToArray();
            Console.WriteLine("  closed-side open fraction: min {0:0.00} median {1:0.00} max {2:0.00}; open side median {3:0.00}",
                closedSide[0], closedSide[closedSide.Length / 2], closedSide[^1], probes.Select(p => MathF.Max(p.Front, p.Back)).OrderBy(x => x).ElementAt(probes.Count / 2));
            foreach (var p in probes.Where(p => MathF.Min(p.Front, p.Back) < 0.1f).Take(12))
                Console.WriteLine($"    closed pane at ({p.Center.X:0.00},{p.Center.Y:0.00},{p.Center.Z:0.00}) n=({p.Normal.X:0.00},{p.Normal.Y:0.00},{p.Normal.Z:0.00}) front {p.Front:0.00} back {p.Back:0.00}");
            Console.WriteLine("  histogram of closed-side fraction: " + string.Join(" ", Enumerable.Range(0, 10).Select(b => $"[{b / 10.0:0.0}-{(b + 1) / 10.0:0.0}):{closedSide.Count(x => x >= b / 10f && x < (b + 1) / 10f + (b == 9 ? 1 : 0))}")));
        }
    }
}
