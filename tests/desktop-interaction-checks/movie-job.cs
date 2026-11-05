using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Navigation;
using Guanacaste3D.Desktop;
using Guanacaste3D.Desktop.Controls;

internal static partial class InteractionChecks
{
    /// <summary>
    /// A movie of the breeze-block house made through the same code as the Render panel's button (G3D_ONLY=movie): an orbit of
    /// <c>G3D_MOVIE_SECONDS</c> seconds (default 60) at <c>G3D_MOVIE_FPS</c> frames a second, in the quality and size picked by
    /// <c>G3D_MOVIE_QUALITY</c> (1 draft … 4 final) and <c>G3D_MOVIE_SIZE</c> (1 HD, 2 Full HD, 3 QHD), written to <c>G3D_MOVIE_OUT</c>.
    /// </summary>
    private static void MovieJob()
    {
        static int Number(string name, int fallback) => int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;
        var output = Environment.GetEnvironmentVariable("G3D_MOVIE_OUT") ?? throw new InvalidOperationException("Set G3D_MOVIE_OUT to the .mp4 to write.");
        double seconds = Number("G3D_MOVIE_SECONDS", 60);
        var fpsIndex = Number("G3D_MOVIE_FPS", 30) switch { 24 => 0, 60 => 2, _ => 1 };
        var opened = new MainWindow(showStartScreen: true) { Width = 1440, Height = 900, WindowState = WindowState.Normal };
        _window = opened;
        opened.Show();
        Pump();
        var host = (IStartScreenHost)opened;
        host.OpenSample(host.Samples.Single(sample => sample.Id == "cobogo"));
        var panel = (RenderPanel)opened.FindName("RenderPanelControl");
        var deadline = DateTime.UtcNow.AddSeconds(120);
        var viewport = (Viewport3D)panel.FindName("InteractiveViewport");
        while (viewport.Children.Count <= 2 && DateTime.UtcNow < deadline) Pump();

        ((ComboBox)panel.FindName("QualityPicker")).SelectedIndex = Number("G3D_MOVIE_QUALITY", 2);
        ((ComboBox)panel.FindName("SizePicker")).SelectedIndex = Number("G3D_MOVIE_SIZE", 1);
        ((ComboBox)panel.FindName("FpsPicker")).SelectedIndex = fpsIndex;
        ((ComboBox)panel.FindName("MovieLightingPicker")).SelectedIndex = Number("G3D_MOVIE_LIGHTING", 2) == 1 ? 1 : 0;
        ((ComboBox)panel.FindName("KeyFpsPicker")).SelectedIndex = Number("G3D_MOVIE_PANEL_KEYFPS", 6) switch { 0 => 0, 10 => 1, 4 => 3, _ => 2 };

        // An orbit round the house, a little more than a third of a turn, rising and closing in: the camera glides, it never jumps.
        var (min, max) = ((System.Numerics.Vector3 Min, System.Numerics.Vector3 Max))typeof(MainWindow).GetMethod("SceneBounds", PrivateInstance)!.Invoke(opened, null)!;
        var house = new System.Numerics.Vector3((min.X + max.X) / 2, (min.Y + max.Y) / 2, 1.6f);
        var path = (CameraPath)typeof(RenderPanel).GetField("_path", PrivateInstance)!.GetValue(panel)!;
        path.Clear();
        path.Ease = true;
        const int marks = 9;
        for (var i = 0; i < marks; i++)
        {
            var t = i / (double)(marks - 1);
            var angle = Math.PI * (-0.95 + 1.15 * t);
            var radius = 19 - 6 * t;
            var position = new System.Numerics.Vector3(house.X + (float)(Math.Cos(angle) * radius), house.Y + (float)(Math.Sin(angle) * radius), 3.4f + (float)(2.4 * Math.Sin(t * Math.PI)));
            path.Add(new CameraMark(position, house with { Z = 2.2f }, 52, i == 0 ? 0 : (float)(seconds / (marks - 1))));
        }

        // G3D_MOVIE_FRAMES=<folder>: the keyframes of a slow orbit as PNG images, at G3D_MOVIE_KEYFPS a second, at the chosen size and
        // samples; a movie this long is then finished outside (interpolated to full speed and encoded), because path tracing every one of
        // 30 frames a second would take hours.
        if (Environment.GetEnvironmentVariable("G3D_MOVIE_FRAMES") is { Length: > 0 } frames)
        {
            var keyFps = Number("G3D_MOVIE_KEYFPS", 4);
            var samples = Number("G3D_MOVIE_SAMPLES", 12);
            var width = Number("G3D_MOVIE_WIDTH", 1280);
            var height = Number("G3D_MOVIE_HEIGHT", 720);
            var source = typeof(RenderPanel).GetField("_source", PrivateInstance)!.GetValue(panel)!;
            var scene = ((Task<Guanacaste3D.Core.Rendering.RenderScene?>)source.GetType().GetMethod("GetSceneAsync", Type.EmptyTypes)!.Invoke(source, null)!).GetAwaiter().GetResult()!;
            var quality = (Guanacaste3D.Core.Rendering.RenderQuality)Number("G3D_MOVIE_QUALITY", 2);
            var lighting = (Guanacaste3D.Core.Rendering.RenderSettings)typeof(RenderPanel).GetMethod("LightingFor", PrivateInstance)!.Invoke(panel, [quality, Guanacaste3D.Core.Rendering.ResourceMode.Maximum])!;
            lighting = lighting with { MaxThreads = Environment.ProcessorCount };
            var finish = (Guanacaste3D.Core.Rendering.ImageFinish)typeof(RenderPanel).GetMethod("CurrentFinish", PrivateInstance)!.Invoke(panel, null)!;
            var statusText = (TextBlock)panel.FindName("StatusText");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var progress = new Progress<Guanacaste3D.Core.Rendering.TourProgress>(p => { });
            var job = Guanacaste3D.Core.Rendering.TourRenderer.RenderAsync(scene, path, lighting, width, height, keyFps, samples, finish, frames, progress, CancellationToken.None);
            var last = DateTime.UtcNow;
            while (!job.IsCompleted)
            {
                Thread.Sleep(50);
                if (DateTime.UtcNow - last > TimeSpan.FromSeconds(30))
                {
                    last = DateTime.UtcNow;
                    var done = Directory.Exists(frames) ? Directory.GetFiles(frames, "*.png").Length : 0;
                    Console.WriteLine($"[{clock.Elapsed.TotalMinutes:0.0} min] {done} frames");
                }
            }
            var result = job.GetAwaiter().GetResult();
            Console.WriteLine($"DONE {result.FramesWritten}/{result.TotalFrames} frames in {clock.Elapsed.TotalMinutes:0.0} min");
            return;
        }

        var context = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));
        try
        {
            var status = (TextBlock)panel.FindName("StatusText");
            var task = (Task)typeof(RenderPanel).GetMethod("RunTourAsync", PrivateInstance)!.Invoke(panel, [output])!;
            var started = DateTime.UtcNow;
            var lastReport = DateTime.UtcNow;
            while (!task.IsCompleted)
            {
                Pump();
                if (DateTime.UtcNow - lastReport > TimeSpan.FromSeconds(20))
                {
                    lastReport = DateTime.UtcNow;
                    Console.WriteLine($"[{(DateTime.UtcNow - started).TotalMinutes:0.0} min] {status.Text} · process {System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64 / 1048576} MB, heap {GC.GetTotalMemory(false) / 1048576} MB, system memory load {Guanacaste3D.Core.Rendering.RenderSafety.ReadMemory().Load:P0}");
                }
            }
            Console.WriteLine($"DONE in {(DateTime.UtcNow - started).TotalMinutes:0.0} min: {status.Text}");
            if (task.IsFaulted) throw task.Exception!.GetBaseException();
            Console.WriteLine(File.Exists(output) ? $"FILE {output} {new FileInfo(output).Length / 1024 / 1024.0:0.0} MB" : "NO FILE");
        }
        finally { SynchronizationContext.SetSynchronizationContext(context); }
    }
}
