using System.Numerics;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

/// <summary>Exercises live rendering independently of navigation and camera path checks.</summary>
internal static class LivePreviewChecks
{
    /// <summary>Verifies refinement, camera updates, resizing, lighting, suspension, and resumption.</summary>
    public static async Task Run(Action<bool, string> check)
    {
        await CheckSuspensionDuringDelivery(check);
        var mesh = new SceneMesh();
        mesh.AddQuad(new(-10, -10, 0), new(10, -10, 0), new(10, 10, 0), new(-10, 10, 0), SurfaceKind.Ground);
        mesh.AddQuad(new(-2, -2, 0), new(2, -2, 0), new(2, 2, 0), new(-2, 2, 0), SurfaceKind.Floor);
        var scene = new RenderSceneBuilder().AddBuilding(mesh).Build();
        var frames = new List<RenderFrame>();
        var phases = new List<string>();
        using var preview = new LivePreview { MaximumSamples = 6 };
        preview.FrameReady += frame => { lock (frames) frames.Add(frame); };
        preview.PhaseChanged += phase => { lock (phases) phases.Add(phase); };
        preview.SetSize(96, 54);
        preview.SetSettings(new RenderSettings());
        preview.SetCamera(new RenderCamera(new Vector3(6, -6, 4), Vector3.Zero, 45));
        preview.SetScene(scene);
        preview.Start();
        check(await Until(() => { lock (frames) return frames.Count > 0 && frames[^1].Samples >= 6; }, 20), "The live preview shows a first frame and refines up to its sample limit");
        int count;
        lock (frames) count = frames.Count;
        await Task.Delay(500);
        lock (frames) check(frames.Count == count, "A converged preview goes idle and produces no more frames");
        RenderFrame first;
        lock (frames) first = frames[^1];

        preview.SetCamera(new RenderCamera(new Vector3(-6, 6, 4), Vector3.Zero, 45));
        check(await Until(() => { lock (frames) return frames.Count > count; }, 10), "Moving the camera wakes the preview and renders again");
        lock (frames) check(!frames[^1].Bgra.SequenceEqual(first.Bgra) || frames[^1].Samples < first.Samples, "The new view produces a different picture");

        lock (frames) count = frames.Count;
        preview.SetSize(128, 72);
        check(await Until(() => { lock (frames) return frames.Skip(count).Any(f => f.Width == 128 && f.Height == 72); }, 10), "Changing the preview size resizes the picture without rebuilding the scene");
        preview.SetSettings(new RenderSettings { SunIntensity = 0 });
        lock (frames) count = frames.Count;
        check(await Until(() => { lock (frames) return frames.Count > count; }, 10), "Changing the lighting refreshes the picture");

        preview.Suspend();
        await Task.Delay(300);
        lock (frames) count = frames.Count;
        preview.SetCamera(new RenderCamera(new Vector3(0, -8, 3), Vector3.Zero, 45));
        await Task.Delay(500);
        lock (frames) check(frames.Count == count, "A suspended preview does nothing, even when the camera moves");
        preview.Resume();
        check(await Until(() => { lock (frames) return frames.Count > count; }, 10), "A resumed preview rebuilds and shows the current view");
        lock (phases) check(phases.Contains("Render.Preparing") && phases.Contains("Render.Preview"), "The preview reports when it is preparing and when pictures flow");
    }

    /// <summary>Forces suspension immediately before delivery, without relying on render timing.</summary>
    private static async Task CheckSuspensionDuringDelivery(Action<bool, string> check)
    {
        var mesh = new SceneMesh();
        mesh.AddQuad(new(-2, -2, 0), new(2, -2, 0), new(2, 2, 0), new(-2, 2, 0), SurfaceKind.Floor);
        using var preview = new LivePreview { MaximumSamples = 1 };
        var firstFrame = new TaskCompletionSource<RenderFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        var suspendedPass = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shouldSuspend = 1;
        preview.FrameReady += frame => firstFrame.TrySetResult(frame);
        preview.PhaseChanged += phase =>
        {
            if (phase != "Render.Preview" || Interlocked.Exchange(ref shouldSuspend, 0) == 0) return;
            preview.Suspend();
            preview.SetSize(128, 72);
            suspendedPass.TrySetResult();
        };
        preview.SetSize(96, 54);
        preview.SetScene(new RenderSceneBuilder().AddBuilding(mesh).Build());
        preview.Start();
        await suspendedPass.Task.WaitAsync(TimeSpan.FromSeconds(15));
        // Resume can race with the worker's finishing step; neither ordering may deliver the old-size frame.
        preview.Resume();
        var resumedFrame = await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(15));
        check(resumedFrame.Width == 128 && resumedFrame.Height == 72,
            "Suspending an in-flight pass discards its frame even when immediately resumed");
    }

    /// <summary>Waits for asynchronous preview progress within a bounded test deadline.</summary>
    private static async Task<bool> Until(Func<bool> condition, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(40);
        }
        return condition();
    }
}
