using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

/// <summary>
/// Checks that the live preview and still renders show the people, cars and fires of a movie where the playhead puts them, and
/// that fire is drawn in them at all (the preview and the sessions finish their pictures themselves).
/// </summary>
internal static class RenderMotionChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var ground = new SceneMesh();
        ground.AddQuad(new(-60, -60, 0), new(60, -60, 0), new(60, 60, 0), new(-60, 60, 0), SurfaceKind.Ground);
        var walker = new Actor(Guid.NewGuid(), "Ana", 0, 1.5, false, 0, [new ActorPoint(-1.5, 0, 0), new ActorPoint(1.5, 0, 0)]);
        var rig = ActorRig.From([walker])!;
        var (start, normals) = rig.Frame(0);
        var scene = new RenderSceneBuilder().AddBuilding(ground).AddDynamic(start, rig.Indices, rig.TriangleMaterial, rig.Materials, rig.HasNormals ? normals : null).Build();
        var camera = new RenderCamera(new Vector3(0, -8, 1.8f), new Vector3(0, 0, 1.0f), 50);
        var fire = new FireEmitter(Guid.NewGuid(), "Fogata", 2.2, -2.5, 0, 0.9, 0.5, 1.5);
        const int width = 96, height = 64;

        int Differing(byte[] a, byte[] b) => Enumerable.Range(0, a.Length / 4).Count(pixel => Enumerable.Range(0, 3).Any(channel => Math.Abs(a[pixel * 4 + channel] - b[pixel * 4 + channel]) > 25));

        // The live preview: people follow the playhead, and fire is drawn.
        var frames = new List<RenderFrame>();
        using var preview = new LivePreview { MaximumSamples = 4 };
        preview.FrameReady += frame => { lock (frames) frames.Add(frame); };
        preview.SetSize(width, height);
        preview.SetSettings(new RenderSettings());
        preview.SetCamera(camera);
        preview.SetScene(scene);
        preview.Start();

        async Task<RenderFrame?> Settled(Action change, int minimumSamples = 4)
        {
            int before;
            lock (frames) before = frames.Count;
            change();
            for (var wait = 0; wait < 400; wait++)
            {
                await Task.Delay(50);
                lock (frames)
                    if (frames.Count > before && frames[^1].Samples >= minimumSamples) return frames[^1];
            }
            return null;
        }

        var begin = await Settled(() => preview.SetActors(rig, 0));
        var later = await Settled(() => preview.SetActors(rig, 1.6));
        check(begin is not null && later is not null && Differing(begin.Bgra, later.Bgra) > 40,
            $"In the live preview the person walks when the playhead moves ({(begin is null || later is null ? -1 : Differing(begin.Bgra, later.Bgra))} pixels differ)");
        var burning = await Settled(() => preview.SetFinish(new ImageFinish { Fires = [fire], FireSeconds = 1 }), 4);
        check(later is not null && burning is not null && Differing(later.Bgra, burning.Bgra) > 40,
            $"The live preview draws fire ({(later is null || burning is null ? -1 : Differing(later.Bgra, burning.Bgra))} pixels)");
        var next = await Settled(() => preview.SetFinish(new ImageFinish { Fires = [fire], FireSeconds = 1.17f }), 4);
        check(burning is not null && next is not null && Differing(burning.Bgra, next.Bgra) > 5, "The flames move as the playhead moves");

        // A rig that does not match the scene (people changed after the scene was built) must not stop the preview.
        var stranger = ActorRig.From([walker, walker with { Id = Guid.NewGuid() }])!;
        var survived = await Settled(() => preview.SetActors(stranger, 0.5));
        check(survived is not null, "A rig that does not match the scene is ignored and the preview keeps showing pictures");
        var again = await Settled(() => preview.SetActors(rig, 1.6));
        check(again is not null && next is not null && Differing(again.Bgra, next.Bgra) < 12, "And the right rig puts the person back where the playhead says");

        // A still: the session shows the person at the playhead's second, and fire.
        async Task<byte[]> Still(double seconds, bool withFire)
        {
            var finish = withFire ? new ImageFinish { Fires = [fire], FireSeconds = (float)seconds } : new ImageFinish();
            using var session = new RenderSession(scene, camera, new RenderSettings(), new RenderJob(width, height, 4, null, false, finish) { Actors = rig, ActorSeconds = seconds });
            var result = await session.RunAsync(CancellationToken.None);
            return result.LastFrame!.Bgra;
        }
        var stillAtStart = await Still(0, false);
        var stillLater = await Still(1.6, false);
        check(Differing(stillAtStart, stillLater) > 40, $"A final render shows the person where the playhead puts it ({Differing(stillAtStart, stillLater)} pixels differ between 0 s and 1.6 s)");
        check(Differing(stillLater, await Still(1.6, true)) > 40, "A final render draws the fire too");
    }
}
