using System.Diagnostics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// What a render session should produce. With <paramref name="Buckets"/> the image is rendered square by square, each to
/// its full sample count, and squares not reached yet are shown dark, as in V-Ray.
/// </summary>
public sealed record RenderJob(int Width, int Height, int TargetSamples, TimeSpan? TimeLimit = null,
    bool Denoise = true, ImageFinish? Finish = null, bool Buckets = false)
{
    /// <summary>Square bucket edge in pixels (8–256, power of two). The image size is unchanged.</summary>
    public int BucketSize { get; init; } = 32;

    /// <summary>The people and cars of the scene and the second of the movie to show them at; null leaves them where the scene has them.</summary>
    public ActorRig? Actors { get; init; }
    public double ActorSeconds { get; init; }
}

/// <summary>A displayable snapshot of a render in progress.</summary>
public sealed record RenderFrame(byte[] Bgra, int Width, int Height, int Samples, int TargetSamples, TimeSpan Elapsed,
    TimeSpan? Remaining, float ActiveFraction, bool IsFinal)
{
    /// <summary>Actual fraction of work completed, not a percentage inferred from rounded sample counters.</summary>
    public double Progress { get; init; }
}

/// <summary>Summary of a finished, stopped or failed render.</summary>
public sealed record RenderResult(RenderFrame? LastFrame, int Samples, TimeSpan Elapsed, long Rays, int Triangles,
    bool Completed, bool Cancelled, bool StoppedForMemory = false)
{
    /// <summary>Millions of rays per second over the whole run.</summary>
    public double MegaRaysPerSecond => Elapsed.TotalSeconds > 0 ? Rays / Elapsed.TotalSeconds / 1e6 : 0;
}

/// <summary>
/// Runs a progressive render in the background and publishes frames for the interface. It builds the
/// acceleration structure, adds samples in batches sized to keep the preview updating about twice per second,
/// supports pause and cancel, and stops when the sample target, the time limit or full convergence is reached.
/// </summary>
public sealed class RenderSession : IDisposable
{
    private readonly RenderScene _scene;
    private readonly RenderCamera _camera;
    private readonly RenderSettings _settings;
    private readonly RenderJob _job;
    private readonly ManualResetEventSlim _running = new(true);
    // Monotonic wall time of the current render, excluding scene/BVH preparation. Pauses are included.
    private readonly Stopwatch _renderClock = new();
    private PathTracer? _tracer;

    public RenderSession(RenderScene scene, RenderCamera camera, RenderSettings settings, RenderJob job)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(job);
        if (job.TargetSamples < 1) throw new ArgumentOutOfRangeException(nameof(job), "At least one sample is required.");
        if (job.BucketSize < 8 || job.BucketSize > 256 || (job.BucketSize & (job.BucketSize - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(job), "Bucket size must be a power of two from 8 to 256.");
        _scene = scene;
        _camera = camera;
        _settings = settings;
        _job = job;
    }

    /// <summary>Raised on a worker thread each time a new preview frame is ready. Handlers must copy what they keep.</summary>
    public event Action<RenderFrame>? FrameReady;

    /// <summary>Raised when the session changes phase, with a resource key such as "Render.Preparing".</summary>
    public event Action<string>? PhaseChanged;

    public bool IsPaused => !_running.IsSet;

    /// <summary>Measured render wall time from a monotonic clock; excludes scene preparation, includes pauses.</summary>
    public TimeSpan RenderElapsed => _renderClock.Elapsed;

    /// <summary>The tracer once preparation has finished; null before that.</summary>
    public PathTracer? Tracer => _tracer;

    /// <summary>Holds the render between passes until <see cref="Resume"/> is called.</summary>
    public void Pause() => _running.Reset();

    public void Resume() => _running.Set();

    /// <summary>Renders until the target is reached or <paramref name="cancellation"/> fires.</summary>
    public Task<RenderResult> RunAsync(CancellationToken cancellation) => Task.Run(() => RunGuarded(cancellation));

    /// <summary>
    /// Runs the render under a memory watchdog. If memory runs short the render stops where it is and the image so far is
    /// returned, flagged with <see cref="RenderResult.StoppedForMemory"/>; running out of memory is handled the same way.
    /// </summary>
    private RenderResult RunGuarded(CancellationToken cancellation)
    {
        using var safety = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var memoryStop = false;
        using var watchdog = new RenderSafety.Watchdog(
            (long)(RenderSafety.ReadMemory().TotalBytes * 0.8), () =>
            {
                memoryStop = true;
                safety.Cancel();
            });
        try
        {
            var result = Run(safety.Token);
            return memoryStop ? result with { StoppedForMemory = true, Completed = false } : result;
        }
        catch (OutOfMemoryException)
        {
            // Release what the tracer holds before reporting, so the interface has memory to show the message.
            var kept = _lastFrame;
            _tracer = null;
            GC.Collect();
            return new RenderResult(kept, kept?.Samples ?? 0, TimeSpan.Zero, 0, _scene.TriangleCount, false, true, true);
        }
    }

    private RenderFrame? _lastFrame;

    private RenderResult Run(CancellationToken cancellation)
    {
        var clock = _renderClock;
        clock.Reset();
        RenderFrame? last = null;
        try
        {
            PhaseChanged?.Invoke("Render.Preparing");
            var settings = _settings with
            {
                // Render settings carry the sample cap implicitly; the job decides the pace.
                MaxThreads = _settings.MaxThreads
            };
            var tracer = _tracer = new PathTracer(_scene, _job.Width, _job.Height, _camera, settings, cancellation);
            if (_job.Actors is { } actors && tracer.HasActors)
            {
                try
                {
                    var (positions, normals) = actors.Frame(_job.ActorSeconds);
                    tracer.MoveActors(positions, normals);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    // The people do not match this scene; they stay where the scene has them.
                }
            }
            MemoryTrim.AfterHeavyBuild();
            PhaseChanged?.Invoke("Render.Rendering");
            clock.Restart();

            var finish = _job.Finish ?? new ImageFinish();
            if (_job.Buckets)
            {
                last = RunBuckets(tracer, clock, finish, cancellation);
                return new RenderResult(last, tracer.SampleCount, clock.Elapsed, tracer.RayCount, tracer.TriangleCount,
                    true, false);
            }
            var lastFrameAt = TimeSpan.Zero;
            var frameCost = TimeSpan.FromMilliseconds(50);
            var batch = 1;
            var converged = false;
            while (tracer.SampleCount < _job.TargetSamples && !converged)
            {
                cancellation.ThrowIfCancellationRequested();
                _running.Wait(cancellation);
                var batchStart = clock.Elapsed;
                var passes = Math.Min(batch, _job.TargetSamples - tracer.SampleCount);
                for (var i = 0; i < passes; i++)
                {
                    tracer.RenderPass(cancellation);
                    if (_job.TimeLimit is { } limit && clock.Elapsed >= limit) break;
                }
                var passTime = (clock.Elapsed - batchStart) / Math.Max(1, passes);

                converged = settings.AdaptiveSampling && tracer.ActiveFraction <= 0.0005f &&
                            tracer.SampleCount >= settings.AdaptiveMinimumSamples;
                var finished = tracer.SampleCount >= _job.TargetSamples || converged ||
                               (_job.TimeLimit is { } timeLimit && clock.Elapsed >= timeLimit);

                // Refresh about twice per second, but never spend more than a quarter of the time producing frames.
                var interval = TimeSpan.FromMilliseconds(Math.Max(450, frameCost.TotalMilliseconds * 4));
                if (finished || last is null || clock.Elapsed - lastFrameAt >= interval)
                {
                    var frameStart = clock.Elapsed;
                    last = _lastFrame = MakeFrame(tracer, finish, clock.Elapsed, finished);
                    frameCost = clock.Elapsed - frameStart;
                    lastFrameAt = clock.Elapsed;
                    FrameReady?.Invoke(last);
                }
                if (finished) break;
                var wanted = interval.TotalSeconds / Math.Max(passTime.TotalSeconds, 1e-4);
                batch = (int)Math.Clamp(Math.Round(wanted * 0.6), 1, 64);
            }
            return new RenderResult(last, tracer.SampleCount, clock.Elapsed, tracer.RayCount, tracer.TriangleCount,
                true, false);
        }
        catch (OperationCanceledException)
        {
            var tracer = _tracer;
            // A stopped render still yields what it has so far, so the user can keep or save it.
            if (tracer is not null && tracer.SampleCount > 0)
            {
                last = MakeFrame(tracer, _job.Finish ?? new ImageFinish(), clock.Elapsed, isFinal: true);
                FrameReady?.Invoke(last);
            }
            return new RenderResult(last, tracer?.SampleCount ?? 0, clock.Elapsed, tracer?.RayCount ?? 0,
                tracer?.TriangleCount ?? _scene.TriangleCount, false, true);
        }
        finally
        {
            clock.Stop();
        }
    }

    /// <summary>
    /// Bucket render: squares are taken from the centre outwards by as many workers as there are threads, each rendered to
    /// completion. Preview frames show finished squares in colour, squares being rendered with corner marks, and the rest dark.
    /// </summary>
    private RenderFrame RunBuckets(PathTracer tracer, Stopwatch clock, ImageFinish finish, CancellationToken cancellation)
    {
        var buckets = new List<(int X, int Y, int W, int H)>();
        var bucketSize = _job.BucketSize;
        for (var y = 0; y < tracer.Height; y += bucketSize)
        for (var x = 0; x < tracer.Width; x += bucketSize)
            buckets.Add((x, y, Math.Min(bucketSize, tracer.Width - x), Math.Min(bucketSize, tracer.Height - y)));
        var centreX = tracer.Width / 2.0;
        var centreY = tracer.Height / 2.0;
        buckets.Sort((a, b) => Distance(a).CompareTo(Distance(b)));
        double Distance((int X, int Y, int W, int H) bucket) =>
            Math.Pow(bucket.X + bucket.W / 2.0 - centreX, 2) + Math.Pow(bucket.Y + bucket.H / 2.0 - centreY, 2);

        var state = new byte[buckets.Count]; // 0 waiting, 1 rendering, 2 done
        var gate = new object();
        var completed = 0;
        var next = -1;
        var lastFrameAt = TimeSpan.Zero;
        var frameCost = TimeSpan.FromMilliseconds(60);
        RenderFrame? last = null;

        void Publish(bool force)
        {
            lock (gate)
            {
                // Expensive 4K frame snapshots must not consume most CPU time on small machines.
                var interval = TimeSpan.FromMilliseconds(Math.Max(500, frameCost.TotalMilliseconds * 4));
                if (!force && clock.Elapsed - lastFrameAt < interval) return;
                var frameStart = clock.Elapsed;
                tracer.ReportBucketedSamples(1, false);
                var bytes = ImageProcessor.ToBgra32(tracer.LinearImage(false), tracer.Width, tracer.Height,
                    tracer.BaseExposure, tracer.WithScreenEffects(finish), tracer.CaptureFeatures);
                for (var i = 0; i < buckets.Count; i++)
                {
                    if (state[i] == 2) continue;
                    var (x, y, w, h) = buckets[i];
                    if (state[i] == 0) Darken(bytes, tracer.Width, x, y, w, h);
                    else Brackets(bytes, tracer.Width, x, y, w, h);
                }
                var progress = Math.Clamp(completed / (double)buckets.Count, 0, 1);
                TimeSpan? remaining = completed >= 4 ? clock.Elapsed * ((1 - progress) / progress) : null;
                last = _lastFrame = new RenderFrame(bytes, tracer.Width, tracer.Height,
                    (int)(_job.TargetSamples * progress), _job.TargetSamples, clock.Elapsed, remaining, 1, false)
                { Progress = progress };
                frameCost = clock.Elapsed - frameStart;
                lastFrameAt = clock.Elapsed;
                FrameReady?.Invoke(last);
            }
        }

        Publish(true);
        var workers = _settings.MaxThreads > 0 ? _settings.MaxThreads : Environment.ProcessorCount;
        Parallel.For(0, Math.Max(1, workers), new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = cancellation }, _ =>
        {
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                _running.Wait(cancellation);
                var index = Interlocked.Increment(ref next);
                if (index >= buckets.Count) return;
                lock (gate) state[index] = 1;
                var (x, y, w, h) = buckets[index];
                tracer.RenderBucket(x, y, w, h, _job.TargetSamples, cancellation);
                lock (gate)
                {
                    state[index] = 2;
                    completed++;
                }
                Publish(false);
            }
        });

        tracer.ReportBucketedSamples(_job.TargetSamples, true);
        last = _lastFrame = MakeFrame(tracer, finish, clock.Elapsed, true);
        FrameReady?.Invoke(last);
        return last;
    }

    /// <summary>Dims a square that has not been rendered yet.</summary>
    private static void Darken(byte[] bgra, int stride, int x, int y, int width, int height)
    {
        for (var row = y; row < y + height; row++)
        for (var column = x; column < x + width; column++)
        {
            var offset = (row * stride + column) * 4;
            bgra[offset] = (byte)(bgra[offset] * 40 >> 8);
            bgra[offset + 1] = (byte)(bgra[offset + 1] * 40 >> 8);
            bgra[offset + 2] = (byte)(bgra[offset + 2] * 40 >> 8);
        }
    }

    /// <summary>Marks the corners of a square being rendered, so the user sees where the work is going.</summary>
    private static void Brackets(byte[] bgra, int stride, int x, int y, int width, int height)
    {
        // The 8px bucket must keep a visible centre, rather than becoming one solid bracket.
        var arm = Math.Max(2, Math.Min(10, Math.Min(width, height) / 4));
        var thick = Math.Max(1, Math.Min(2, Math.Min(width, height) / 8));
        void Set(int column, int row)
        {
            // The final bucket on each edge can be only one pixel wide/high.
            // Never paint bracket pixels outside that bucket or the image buffer.
            if (column < x || column >= x + width || row < y || row >= y + height)
                return;
            var offset = (row * stride + column) * 4;
            bgra[offset] = 60; bgra[offset + 1] = 170; bgra[offset + 2] = 255;
        }
        for (var i = 0; i < arm; i++)
        for (var t = 0; t < thick; t++)
        {
            Set(x + i, y + t);
            Set(x + width - 1 - i, y + t);
            Set(x + i, y + height - 1 - t);
            Set(x + width - 1 - i, y + height - 1 - t);
            Set(x + t, y + i);
            Set(x + width - 1 - t, y + i);
            Set(x + t, y + height - 1 - i);
            Set(x + width - 1 - t, y + height - 1 - i);
        }
    }

    private RenderFrame MakeFrame(PathTracer tracer, ImageFinish finish, TimeSpan elapsed, bool isFinal)
    {
        var bytes = ImageProcessor.ToBgra32(tracer.LinearImage(_job.Denoise), tracer.Width, tracer.Height,
            tracer.BaseExposure, tracer.WithScreenEffects(finish), tracer.CaptureFeatures);
        TimeSpan? remaining = null;
        if (tracer.SampleCount > 0 && !isFinal)
        {
            var perPass = elapsed / tracer.SampleCount;
            remaining = perPass * Math.Max(0, _job.TargetSamples - tracer.SampleCount);
            if (_job.TimeLimit is { } limit) remaining = TimeSpan.FromTicks(Math.Min(remaining.Value.Ticks, Math.Max(0, (limit - elapsed).Ticks)));
        }
        var progress = Math.Clamp((double)tracer.SampleCount / _job.TargetSamples, 0, 1);
        if (isFinal && tracer.SampleCount >= _job.TargetSamples) progress = 1;
        return new RenderFrame(bytes, tracer.Width, tracer.Height, tracer.SampleCount, _job.TargetSamples, elapsed,
            remaining, tracer.ActiveFraction, isFinal) { Progress = progress };
    }

    /// <summary>
    /// Re-applies the picture finish to the current image without rendering more, so exposure and grading sliders
    /// feel instant. Returns null before the first sample.
    /// </summary>
    public RenderFrame? Refinish(ImageFinish finish, TimeSpan elapsed)
    {
        var tracer = _tracer;
        return tracer is null || tracer.SampleCount == 0 ? null : MakeFrame(tracer, finish, elapsed, isFinal: true);
    }

    public void Dispose() => _running.Dispose();
}
