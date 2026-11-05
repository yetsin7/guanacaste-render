using System.Numerics;
using Guanacaste3D.Core.Navigation;

namespace Guanacaste3D.Core.Rendering;

/// <summary>Progress of a video render.</summary>
public readonly record struct TourProgress(int Frame, int Total, double Seconds, int Sample = 0, int Samples = 0);

/// <summary>
/// Layer animation for a video: the animation says which levels are hidden at each moment, and <see cref="SceneFor"/>
/// builds the scene with those levels out (null when nothing would be left to draw).
/// </summary>
public sealed record LayerSource(LayerAnimation Animation, Func<IReadOnlySet<Guid>, RenderScene?> SceneFor);

/// <summary>Where the finished frames of a movie go besides (or instead of) PNG files: an encoder that writes them into one video file.</summary>
public interface IFrameSink : IDisposable
{
    /// <summary>True once the sink has given up (the encoder failed); the renderer then keeps the frames as PNG images.</summary>
    bool Failed { get; }

    /// <summary>Takes the next frame, BGRA with the top row first.</summary>
    void Add(byte[] bgra, int width, int height);
}

/// <summary>Outcome of a video render.</summary>
public sealed record TourResult(int FramesWritten, int TotalFrames, bool Cancelled, bool StoppedForMemory, string Folder);

/// <summary>
/// Renders a walk-through as numbered PNG frames. One path tracer, and so one acceleration structure, serves every
/// frame; only the camera changes between them, which makes a long video affordable. If memory runs short or the
/// user cancels, the frames already written are kept.
/// </summary>
public static class TourRenderer
{
    public static Task<TourResult> RenderAsync(RenderScene scene, CameraPath path, RenderSettings settings, int width, int height,
        int framesPerSecond, int samplesPerFrame, ImageFinish finish, string folder, IProgress<TourProgress>? progress,
        CancellationToken cancellation, LayerSource? layers = null, ActorRig? actors = null, IFrameSink? sink = null, int keyframesPerSecond = 0,
        int lightingDivisor = 1) =>
        Task.Run(() => Render(scene, path, settings, width, height, framesPerSecond, samplesPerFrame, finish, folder, progress, cancellation, layers, actors, sink, keyframesPerSecond,
            lightingDivisor));

    private static TourResult Render(RenderScene scene, CameraPath path, RenderSettings settings, int width, int height,
        int framesPerSecond, int samplesPerFrame, ImageFinish finish, string folder, IProgress<TourProgress>? progress,
        CancellationToken cancellation, LayerSource? layers, ActorRig? actors, IFrameSink? sink, int keyframesPerSecond,
        int lightingDivisor)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(path);
        if (path.Marks.Count < 2) throw new InvalidOperationException("A tour needs at least two camera marks.");
        var total = path.FrameCount(framesPerSecond);
        var written = 0;
        var memoryStop = false;

        using var safety = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        using var watchdog = new RenderSafety.Watchdog((long)(RenderSafety.ReadMemory().TotalBytes * 0.8), () =>
        {
            memoryStop = true;
            safety.Cancel();
        });
        try
        {
            var first = path.Frame(0, framesPerSecond);
            PathTracer Open(RenderScene opened, (Vector3 Position, Vector3 Target, float FieldOfView) view) =>
                new(opened, width, height, new RenderCamera(view.Position, view.Target, view.FieldOfView) with { CorrectVerticals = false },
                    settings with { AdaptiveSampling = true }, safety.Token);
            // With layer animation the first scene is the one at time zero; each change of the hidden levels opens another,
            // one at a time, so memory never holds two scenes. Frames that look alike share the tracer already built.
            var animated = layers is { Animation.IsEmpty: false };
            var state = animated ? LayerAnimation.StateKey(layers!.Animation.HiddenAt(0)) : null;
            var startScene = animated ? layers!.SceneFor(layers.Animation.HiddenAt(0)) ?? scene : scene;
            var tracer = Open(startScene, first);
            MemoryTrim.AfterHeavyBuild();
            double TimeOf(int frame) => total <= 1 ? 0 : path.Duration * frame / (total - 1);

            // The frames that are really rendered. With a rate of real frames below the movie's, only some are: the others are made
            // from their two neighbours (see FrameInterpolator), which is what puts a smooth, sharp minute within reach.
            var keys = new List<int>();
            if (keyframesPerSecond <= 0 || keyframesPerSecond >= framesPerSecond || total <= 2)
                keys.AddRange(Enumerable.Range(0, total));
            else
            {
                var step = framesPerSecond / (double)keyframesPerSecond;
                for (var k = 0; Math.Round(k * step) < total - 1; k++) keys.Add((int)Math.Round(k * step));
                keys.Add(total - 1);
            }

            KeyFrame RenderKey(int frame)
            {
                safety.Token.ThrowIfCancellationRequested();
                var (position, target, fieldOfView) = path.Frame(frame, framesPerSecond);
                if (animated)
                {
                    var hidden = layers!.Animation.HiddenAt((float)TimeOf(frame));
                    var key = LayerAnimation.StateKey(hidden);
                    if (key != state && layers.SceneFor(hidden) is { } changed)
                    {
                        tracer = null!;
                        MemoryTrim.AfterHeavyBuild();
                        tracer = Open(changed, (position, target, fieldOfView));
                        MemoryTrim.AfterHeavyBuild();
                        state = key;
                    }
                }
                // People walk with the movie: the same instant of the movie that places the camera places them.
                if (actors is not null && tracer.HasActors)
                {
                    var (positions, normals) = actors.Frame(TimeOf(frame));
                    tracer.MoveActors(positions, normals);
                }
                // The wind moves with the movie too: the gusts are where they are at this instant.
                if (settings.WindStrength > 0 && settings.GrassHeight > 0)
                    tracer.UpdateSettings(settings with { AdaptiveSampling = true, WindSeconds = (float)TimeOf(frame) });
                // The light is traced at a fraction of the size (a quarter of the paths at half size) and brought to full size with the
                // full-size surfaces, which keeps edges and textures sharp: the one big saving that costs a picture almost nothing.
                var divisor = Math.Clamp(lightingDivisor, 1, 4);
                int lowWidth = Math.Max(32, width / divisor), lowHeight = Math.Max(18, height / divisor);
                var shown = new RenderCamera(position, target, fieldOfView);
                if (divisor > 1) tracer.Resize(lowWidth, lowHeight);
                tracer.SetCamera(shown);
                for (var sample = 0; sample < samplesPerFrame; sample++)
                {
                    tracer.RenderPass(safety.Token);
                    if (sample == 0 || (sample + 1) % 4 == 0 || sample + 1 == samplesPerFrame)
                        progress?.Report(new TourProgress(written, total, written / (double)Math.Max(1, framesPerSecond), sample + 1, samplesPerFrame));
                    if (tracer.ActiveFraction <= 0.0005f && sample >= settings.AdaptiveMinimumSamples) break;
                }
                var exposure = tracer.BaseExposure;
                var image = tracer.LinearImage(denoise: true);
                var features = FrameInterpolator.Copy(tracer.CaptureFeatures());
                if (divisor > 1)
                {
                    var lowFeatures = features;
                    tracer.Resize(width, height);
                    tracer.SetCamera(shown);
                    features = FrameInterpolator.Copy(tracer.CaptureFeatures());
                    image = FrameInterpolator.UpscaleLighting(image, lowFeatures, features);
                }
                return new KeyFrame(image, features,
                    tracer.Projection() ?? throw new InvalidOperationException("A panorama has no flat picture to interpolate."), exposure);
            }

            void Emit(int frame, Vector3[] radiance, FeatureBuffer features, float exposure)
            {
                // Fires and smoke burn with the movie: the same instant that places the camera shapes their flames.
                var frameFinish = tracer.WithScreenEffects(finish with { FireSeconds = (float)TimeOf(frame) });
                var bytes = ImageProcessor.ToBgra32(radiance, width, height, exposure, frameFinish, () => features);
                // With a video file the frame goes into it; as images only when there is no video or the encoder gave up.
                sink?.Add(bytes, width, height);
                if (sink is null || sink.Failed)
                {
                    Directory.CreateDirectory(folder);
                    PngCodec.Save(Path.Combine(folder, $"frame_{frame + 1:00000}.png"), RasterImage.FromBgra32(width, height, bytes));
                }
                written++;
                progress?.Report(new TourProgress(written, total, written / (double)Math.Max(1, framesPerSecond)));
            }

            var previous = RenderKey(keys[0]);
            Emit(keys[0], previous.Radiance, previous.Features, previous.Exposure);
            for (var index = 1; index < keys.Count; index++)
            {
                var current = RenderKey(keys[index]);
                for (var frame = keys[index - 1] + 1; frame < keys[index]; frame++)
                {
                    safety.Token.ThrowIfCancellationRequested();
                    // The camera of this frame in between, set only to know where the picture falls (nothing is rendered with it).
                    var (position, target, fieldOfView) = path.Frame(frame, framesPerSecond);
                    tracer.SetCamera(new RenderCamera(position, target, fieldOfView));
                    var along = (frame - keys[index - 1]) / (float)(keys[index] - keys[index - 1]);
                    var between = FrameInterpolator.Between(previous, current, along, tracer.Projection()!, width, height);
                    var nearest = along < 0.5f ? previous : current;
                    Emit(frame, between, nearest.Features, nearest.Exposure);
                }
                Emit(keys[index], current.Radiance, current.Features, current.Exposure);
                previous = current;
            }
        }
        catch (OperationCanceledException)
        {
            return new TourResult(written, total, !memoryStop, memoryStop, folder);
        }
        catch (OutOfMemoryException)
        {
            return new TourResult(written, total, false, true, folder);
        }
        return new TourResult(written, total, false, false, folder);
    }
}
