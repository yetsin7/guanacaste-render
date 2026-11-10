using System.Numerics;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

/// <summary>Renders a real 360° panorama of the demonstration house and checks what a viewer depends on.</summary>
internal static class RenderPanoramaChecks
{
    private const int Width = 768;
    private const int Height = 384;

    /// <summary>Average absolute difference per channel between two pixel columns of a BGRA image.</summary>
    private static double ColumnDifference(byte[] bgra, int first, int second)
    {
        double sum = 0;
        for (var row = 0; row < Height; row++)
            for (var channel = 0; channel < 3; channel++)
                sum += Math.Abs(bgra[(row * Width + first) * 4 + channel] - bgra[(row * Width + second) * 4 + channel]);
        return sum / (Height * 3);
    }

    /// <summary>Average of one channel over a pixel row.</summary>
    private static double RowAverage(byte[] bgra, int row, int channel) =>
        Enumerable.Range(0, Width).Average(column => (double)bgra[(row * Width + column) * 4 + channel]);

    /// <summary>Renders the panorama, writes it for inspection and checks its seam, zenith and nadir.</summary>
    internal static void Run(Action<bool, string> check)
    {
        var house = BenchmarkScenes.HouseExterior();
        // The benchmark mesh includes a wide lawn; the camera stands on it, a few metres off the building's corner.
        var (min, max) = BuildingMesher.Build(Guanacaste3D.Core.SampleProjects.CreateDemonstrationHouse()).Bounds();
        var centre = (min + max) / 2;
        var eye = new Vector3(min.X - 5, min.Y - 5, 1.7f);
        var camera = new RenderCamera(eye, new Vector3(centre.X, centre.Y, 1.7f), 60) { Panoramic = true };
        var scene = new RenderSceneBuilder().AddBuilding(house.Mesh).Build();
        var tracer = new PathTracer(scene, Width, Height, camera, house.Settings with { Seed = 11 });
        var timer = System.Diagnostics.Stopwatch.StartNew();
        for (var pass = 0; pass < 24; pass++) tracer.RenderPass();
        var bgra = tracer.ToBgra32(denoise: true);
        var seconds = timer.Elapsed.TotalSeconds;

        var output = Path.Combine("artifacts", "panorama-checks");
        Directory.CreateDirectory(output);
        PngCodec.Save(Path.Combine(output, "house-panorama.png"), RasterImage.FromBgra32(Width, Height, bgra));

        // The left and right edges show the same direction (behind the camera), so they must join without a seam:
        // no worse than two neighbouring columns elsewhere in that part of the picture, plus sampling noise.
        var seam = ColumnDifference(bgra, 0, Width - 1);
        var neighbours = ColumnDifference(bgra, 1, 2);
        check(seam < neighbours + 6,
            $"A rendered panorama joins at its left and right edges (seam {seam:0.0} against {neighbours:0.0} between neighbours, of 255)");
        var features = tracer.CaptureFeatures();
        var top = Enumerable.Range(0, Width).Count(column => float.IsInfinity(features.Depth[column]));
        var bottom = Enumerable.Range(0, Width).Count(column => float.IsFinite(features.Depth[(Height - 1) * Width + column]));
        check(top == Width && bottom == Width, $"The top row of a panorama is sky and the bottom row is the lawn under the camera ({top} and {bottom} of {Width})");
        check(RowAverage(bgra, 2, 0) > RowAverage(bgra, 2, 2) && RowAverage(bgra, 2, 0) > 60,
            $"The zenith of a sunny panorama is a lit blue sky (blue {RowAverage(bgra, 2, 0):0}, red {RowAverage(bgra, 2, 2):0})");
        // Just above the horizon only the building has depth; the lawn is below it and the sky has none.
        var houseColumns = Enumerable.Range(0, Width).Count(column => float.IsFinite(features.Depth[(Height / 2 - 8) * Width + column]));
        check(houseColumns > Width / 20 && houseColumns < Width / 2,
            $"The house fills part of the horizon and open sky the rest ({houseColumns} of {Width} columns in {seconds:0.0} s)");
    }
}
