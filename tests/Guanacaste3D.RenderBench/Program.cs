using System.Diagnostics;
using Guanacaste3D.Core.Rendering;

// Quality-versus-time benchmark of the renderer. Every row is the error of an image against a
// converged reference produced by the same algorithm, so techniques can be compared fairly.
//   dotnet run -c Release --project tests/Guanacaste3D.RenderBench -- [--out folder] [--width 480] [--ref 1024]
var options = Enumerable.Range(0, Math.Max(0, args.Length - 1))
    .Where(index => args[index].StartsWith("--", StringComparison.Ordinal) && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
    .ToDictionary(index => args[index], index => args[index + 1]);
var width = options.TryGetValue("--width", out var w) ? int.Parse(w) : 480;
var height = width * 9 / 16;
var referencePasses = options.TryGetValue("--ref", out var r) ? int.Parse(r) : 1024;
var outputFolder = options.GetValueOrDefault("--out");
if (outputFolder is not null) Directory.CreateDirectory(outputFolder);

if (args.Contains("--ab"))
{
    var which = options.GetValueOrDefault("--ab") ?? args[(Array.IndexOf(args, "--ab") + 1)..].FirstOrDefault() ?? "portals";
    var abWidth = options.TryGetValue("--width", out var abw) ? int.Parse(abw) : 320;
    var abReference = options.TryGetValue("--ref", out var abr) ? int.Parse(abr) : 1024;
    Experiments.Run(which, abWidth, abReference, outputFolder);
    return;
}

if (args.Contains("--portal-probe"))
{
    PortalProbe.Run();
    return;
}

if (args.Contains("--probe-debug"))
{
    ProbeDebug.Run();
    return;
}

if (args.Contains("--bias"))
{
    BiasProbe.Run();
    return;
}

if (args.Contains("--recon"))
{
    ReconstructionExperiment.Run(options.TryGetValue("--width", out var rw) ? int.Parse(rw) : 320,
        options.TryGetValue("--ref", out var rr) ? int.Parse(rr) : 1024);
    return;
}

if (args.Contains("--cache-view"))
{
    CacheView.Run(outputFolder ?? ".", options.TryGetValue("--cell", out var cv) ? float.Parse(cv, System.Globalization.CultureInfo.InvariantCulture) : 0.25f);
    return;
}

if (args.Contains("--diagnostics"))
{
    DiagnosticsSheet.Run(args[(Array.IndexOf(args, "--diagnostics") + 1)..].FirstOrDefault() ?? "diagnostics");
    return;
}

if (args.Contains("--shot"))
{
    var shotHeight = options.TryGetValue("--height", out var sh) ? int.Parse(sh) : height;
    ShowcaseShot.Run(outputFolder ?? "shots", width, shotHeight, options.TryGetValue("--passes", out var sp) ? int.Parse(sp) : 64);
    return;
}

if (args.Contains("--denoise-speed"))
{
    DenoiseSpeed.Run();
    return;
}

if (args.Contains("--cost"))
{
    CostBreakdown.Run();
    return;
}

if (options.ContainsKey("--bvh") || args.Contains("--bvh"))
{
    BvhExperiment.Run();
    return;
}

Console.WriteLine($"cores={Environment.ProcessorCount} simd={System.Numerics.Vector.IsHardwareAccelerated} " +
                  $"image={width}x{height} reference={referencePasses}spp");

foreach (var scene in new[] { BenchmarkScenes.HouseExterior(), BenchmarkScenes.DaylitRoom() })
{
    Console.WriteLine($"\n== {scene.Name} · {scene.Mesh.TriangleCount} triangles");
    var clock = Stopwatch.StartNew();
    var reference = new PathTracer(scene.Mesh, width, height, scene.Camera, scene.Settings with { Seed = 99 });
    var buildMs = clock.Elapsed.TotalMilliseconds;
    for (var pass = 0; pass < referencePasses; pass++) reference.RenderPass();
    var referenceSeconds = clock.Elapsed.TotalSeconds;
    var truth = Display(reference, denoise: false);
    Console.WriteLine($"setup {buildMs:0} ms · reference {referenceSeconds:0.0} s " +
                      $"({referenceSeconds * 1000 / referencePasses:0.00} ms/pass, " +
                      $"{(double)width * height * referencePasses / referenceSeconds / 1e6:0.0} Mpaths/s)");
    if (outputFolder is not null) Save(Path.Combine(outputFolder, scene.Name + "-reference.png"), reference, false);

    Console.WriteLine("  spp |  time ms | raw PSNR  SSIM  dE   | denoised PSNR  SSIM  dE");
    var tracer = new PathTracer(scene.Mesh, width, height, scene.Camera, scene.Settings with { Seed = 7 });
    var timer = Stopwatch.StartNew();
    var next = 1;
    for (var pass = 1; pass <= 128; pass++)
    {
        tracer.RenderPass();
        if (pass != next) continue;
        next *= 2;
        timer.Stop(); // metric evaluation must not be charged to the renderer
        var elapsed = timer.Elapsed.TotalMilliseconds;
        var raw = ImageMetrics.Compare(Display(tracer, false), truth, width, height);
        var clean = ImageMetrics.Compare(Display(tracer, true), truth, width, height);
        Console.WriteLine($"  {pass,3} | {elapsed,8:0} | {raw.Psnr,6:0.0} {raw.Ssim,6:0.000} {raw.MeanDeltaE,4:0.0} | " +
                          $"{clean.Psnr,6:0.0} {clean.Ssim,6:0.000} {clean.MeanDeltaE,4:0.0}");
        timer.Start();
    }
    if (outputFolder is not null) Save(Path.Combine(outputFolder, scene.Name + "-denoised-128.png"), tracer, true);
}

static System.Numerics.Vector3[] Display(PathTracer tracer, bool denoise) =>
    RasterImage.FromBgra32(tracer.Width, tracer.Height, tracer.ToBgra32(denoise)).ToDisplay();

static void Save(string path, PathTracer tracer, bool denoise) =>
    PngCodec.Save(path, RasterImage.FromBgra32(tracer.Width, tracer.Height, tracer.ToBgra32(denoise)));
