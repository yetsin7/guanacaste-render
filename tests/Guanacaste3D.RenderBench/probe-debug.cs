using System.Numerics;
using Guanacaste3D.Core.Rendering;

static class ProbeDebug
{
    public static void Run()
    {
        var scene = BenchmarkScenes.HouseExterior();
        var render = new RenderSceneBuilder().AddBuilding(scene.Mesh).Build();
        var glass = Enumerable.Range(0, render.TriangleCount).Where(t => render.Materials[render.TriangleMaterial[t]].Transmission > 0).ToArray();
        var opaque = new Bvh(render.Positions, render.Indices, Enumerable.Range(0, render.TriangleCount).Except(glass).ToArray());
        foreach (var origin in new[] { new Vector3(0.92f, 1, 1.01f), new Vector3(0.92f, -1, 1.01f), new Vector3(0.92f, 2, 1.01f), new Vector3(3, 3, 1.5f) })
        {
            var hit = new RayHit { Distance = float.MaxValue };
            var found = opaque.Intersect(origin, Vector3.UnitZ, 1e-4f, ref hit);
            var kind = found ? scene.Mesh.TriangleKinds[hit.Triangle].ToString() : "-";
            Console.WriteLine($"up from {origin}: hit={found} d={hit.Distance:0.00} kind={kind}");
        }
    }
}
