using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

/// <summary>Checks the waves of water, the ocean that replaces the endless ground, and what the render shows of it.</summary>
static class RenderWaterChecks
{
    public static void Run(Action<bool, string> check)
    {
        // The waves: finite everywhere, steeper at sea than in a pool, nothing when calm is zero, smaller far away.
        var random = new Random(20261104);
        double SeaSlope(float calm, float scale, float fade)
        {
            double sum = 0;
            for (var index = 0; index < 2000; index++)
            {
                var (_, du, dv) = SurfacePatterns.WaterSurface((float)(random.NextDouble() * 8000), (float)(random.NextDouble() * 8000), scale, calm, fade);
                sum += du * du + dv * dv;
            }
            return Math.Sqrt(sum / 2000);
        }
        var sea = SeaSlope(1f, 1f, 1f);
        var pool = SeaSlope(0.25f, 0.12f, 1f);
        check(sea is > 0.05 and < 0.4 && pool > 0.01 && pool < sea && SeaSlope(0f, 1f, 1f) == 0 && SeaSlope(1f, 1f, 0f) < sea,
            $"Sea waves lean the surface a moderate amount, a pool's far less, calm none, and fewer small waves far away ({sea:0.000} sea, {pool:0.000} pool)");
        var (height, slopeU, slopeV) = SurfacePatterns.WaterSurface(7999.5f, -7999.5f, 1f, 1f, 1f);
        check(float.IsFinite(height + slopeU + slopeV), "The waves stay finite kilometres from the origin");

        // Relief bends the normal, and less so far away, never beyond a sensible angle.
        double Angle(Vector3 bent) => Math.Acos(Math.Clamp(bent.Z, -1f, 1f)) * 180 / Math.PI;
        double near = 0, far = 0, worst = 0;
        for (var index = 0; index < 400; index++)
        {
            var position = new Vector3((float)(random.NextDouble() * 400), (float)(random.NextDouble() * 400), 0);
            var closeBend = SurfacePatterns.Relief(SurfacePattern.Waves, 1f, position, Vector3.UnitZ, 3f);
            var farBend = SurfacePatterns.Relief(SurfacePattern.Waves, 1f, position, Vector3.UnitZ, 3000f);
            near += Angle(closeBend);
            far += Angle(farBend);
            worst = Math.Max(worst, Angle(closeBend));
            if (Math.Abs(closeBend.Length() - 1) > 1e-4f) worst = 999;
        }
        check(near / 400 > far / 400 && worst < 35, $"Waves bend the normal by {near / 400:0.0}° on average close up and {far / 400:0.0}° far away, never past 35° ({worst:0.0}°)");

        // Colour: the sea has white caps on its crests, a pool does not.
        double Brightest(SurfacePattern pattern, float scale)
        {
            double top = 0;
            for (var index = 0; index < 3000; index++)
                top = Math.Max(top, SurfacePatterns.Modulate(pattern, scale, new Vector3((float)(random.NextDouble() * 400), (float)(random.NextDouble() * 400), 0), Vector3.UnitZ).X);
            return top;
        }
        var caps = Brightest(SurfacePattern.Waves, 1f);
        var ripples = Brightest(SurfacePattern.Ripples, 0.12f);
        var mean = SurfacePatterns.Average(SurfacePattern.Waves, 1f);
        check(caps > 2.5 && ripples < 1.25 && mean is > 0.85f and < 1.6f, $"The sea shows white caps ({caps:0.0}x), a pool only a faint sheen ({ripples:0.00}x), and the mean tone is sane ({mean:0.00})");
        check(MaterialLibrary.ForKind(SurfaceKind.Water).Pattern == SurfacePattern.Ripples && MaterialLibrary.ForKind(SurfaceKind.Ocean).Pattern == SurfacePattern.Waves,
            "Pool water ripples and the ocean has waves");

        // The ocean's saved text and settings.
        var settings = new OceanSettings(true, -1.5, 2.2, OceanTint.Pacific);
        check(Ocean.TryParse(Ocean.Serialize(settings)) == settings && Ocean.Serialize(OceanSettings.Off) is null &&
              Ocean.TryParse(null) == OceanSettings.Off && Ocean.TryParse("no es json") is null &&
              Ocean.TryParse("""{"V":2,"Enabled":true,"Level":0,"WaveSize":1,"Tint":0}""") is null &&
              Ocean.TryParse("""{"V":1,"Enabled":true,"Level":900,"WaveSize":1,"Tint":0}""") is null &&
              Ocean.TryParse("""{"V":1,"Enabled":true,"Level":0,"WaveSize":1,"Tint":9}""") is null,
            "The ocean reads back exactly from the project's text; damaged or out-of-range text is refused");
        check(new[] { OceanTint.Caribbean, OceanTint.Pacific, OceanTint.Lake }.Select(tint => Ocean.Material(settings with { Tint = tint }).BaseColor).Distinct().Count() == 3 &&
              Ocean.Material(settings).PatternScale == 2.2f, "Each tint has its own water colour and the wave size sets the pattern size");

        // On the cobogó house: the sea takes the endless ground's place.
        var house = BreezeBlockHouseShowcase.Create();
        var mesh = BuildingMesher.Build(house, includeGround: true);
        var triangles = mesh.TriangleCount;
        var groundHeight = mesh.Positions[^6].Z;
        var untouched = BuildingMesher.Build(house, includeGround: true);
        check(!Ocean.Apply(untouched, OceanSettings.Off) && untouched.TriangleCount == triangles && !Ocean.Apply(BuildingMesher.Build(house), settings),
            "An ocean that is off, or a mesh with no ground to replace, changes nothing");
        check(Ocean.Apply(mesh, settings) && mesh.TriangleCount == triangles && mesh.TriangleKinds[^1] == SurfaceKind.Ocean && mesh.TriangleKinds[^2] == SurfaceKind.Ocean &&
              mesh.TriangleKinds.Count(kind => kind == SurfaceKind.Ground) < untouched.TriangleKinds.Count(kind => kind == SurfaceKind.Ground) &&
              Math.Abs(mesh.Positions[^6].Z - (groundHeight - 1.5f)) < 1e-4f && Math.Abs(mesh.Positions[^6].X) > 1000,
            "The ocean replaces the endless ground by a far larger quad at the water's height, below the ground by the chosen level");

        // And in the picture: far from the house the grass is gone and water is in its place.
        var (min, max) = BuildingMesher.Build(house).Bounds();
        var camera = RenderCamera.FrameBounds(min, max);
        byte[] Shot(SceneMesh source, Func<SurfaceKind, RenderMaterial> materials)
        {
            var tracer = new PathTracer(new RenderSceneBuilder().AddBuilding(source, materials).Build(), 128, 80, camera);
            tracer.RenderPass();
            return tracer.ToBgra32(finish: ImageProcessor.Plain);
        }
        var land = Shot(BuildingMesher.Build(house, includeGround: true), MaterialLibrary.ForKind);
        var seaMesh = BuildingMesher.Build(house, includeGround: true);
        var turquoise = new OceanSettings(true, -0.5, 1, OceanTint.Caribbean);
        Ocean.Apply(seaMesh, turquoise);
        var water = Shot(seaMesh, kind => kind == SurfaceKind.Ocean ? Ocean.Material(turquoise) : MaterialLibrary.ForKind(kind));
        int BlueOverRed(byte[] image) => Enumerable.Range(0, 8).Sum(y => Enumerable.Range(0, 20).Sum(x => image[(y * 128 + x) * 4] - image[(y * 128 + x) * 4 + 2]));
        check(BlueOverRed(water) > BlueOverRed(land) + 400, $"In the render the far ground turns to water ({BlueOverRed(land)} -> {BlueOverRed(water)})");
    }
}
