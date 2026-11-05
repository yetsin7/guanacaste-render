using Guanacaste3D.Core.Rendering;

/// <summary>Compares mean image brightness with and without portals across lighting/bounce configurations to localize bias.</summary>
static class BiasProbe
{
    public static void Run()
    {
        var room = BenchmarkScenes.DaylitRoom();
        var scene = new RenderSceneBuilder().AddBuilding(room.Mesh).Build();
        const int width = 96, height = 54, passes = 600;
        Console.WriteLine("config                      portals off   portals on    ratio");
        foreach (var (name, settings) in new (string, RenderSettings)[]
        {
            ("sky only, 3 bounces", room.Settings with { SunIntensity = 0, MaxBounces = 3 }),
            ("sky only, 6 bounces", room.Settings with { SunIntensity = 0, MaxBounces = 6 }),
            ("sky only, 16 bounces", room.Settings with { SunIntensity = 0, MaxBounces = 16 }),
            ("sun + sky, 16 bounces", room.Settings with { MaxBounces = 16 }),
        })
        {
            double Mean(bool portals, uint seed)
            {
                var tracer = new PathTracer(scene, width, height, room.Camera, settings with { SkyPortals = portals, Seed = seed, FireflyClamp = 0 });
                for (var i = 0; i < passes; i++) tracer.RenderPass();
                return tracer.LinearImage(false).Average(p => p.Y);
            }
            var off = Mean(false, 1);
            var on = Mean(true, 2);
            var again = Mean(false, 3);
            Console.WriteLine($"{name,-28}{off,11:0.00000}  {on,11:0.00000}  {on / off,8:0.000}   (off vs off other seed: {again / off:0.000})");
        }
    }
}
