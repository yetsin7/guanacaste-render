using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Navigation;
using Guanacaste3D.Core.Rendering;

/// <summary>Checks fire and smoke: their saved text, where they fall in the picture, their particles, what they hide behind, their light, and the movie.</summary>
static class RenderFireChecks
{
    public static void Run(Action<bool, string> check)
    {
        var campfire = new FireEmitter(Guid.NewGuid(), "Fogata 1", 0, 0, 0, 0.9, 0.5, 1.2);
        var smoke = campfire with { Id = Guid.NewGuid(), Name = "Humo 2", Flames = 0, Smoke = 0.7 };

        // Saved text.
        check(Fires.TryParse(Fires.Serialize([campfire, smoke])) is { Count: 2 } read && read[0] == campfire && read[1] == smoke &&
              Fires.Serialize([]) is null && Fires.TryParse(null)!.Count == 0,
            "Fires read back exactly from the text the project keeps, and none keep nothing");
        string[] bad = ["no es json", "{}", """{"V":2,"Items":[]}""",
            """{"V":1,"Items":[{"Id":"11111111-1111-1111-1111-111111111111","Name":"x","X":0,"Y":0,"Z":0,"Flames":2,"Smoke":0,"Size":1}]}""",
            """{"V":1,"Items":[{"Id":"11111111-1111-1111-1111-111111111111","Name":"x","X":0,"Y":0,"Z":0,"Flames":0,"Smoke":0,"Size":1}]}""",
            """{"V":1,"Items":[{"Id":"11111111-1111-1111-1111-111111111111","Name":"x","X":0,"Y":0,"Z":0,"Flames":1,"Smoke":0,"Size":0}]}"""];
        check(bad.All(item => Fires.TryParse(item) is null), "Damaged fire text, strengths out of range, a fire with neither flames nor smoke, and a size of nothing are refused");

        // Where things fall in the picture.
        var ground = new SceneMesh();
        ground.AddQuad(new(-200, -200, 0), new(200, -200, 0), new(200, 200, 0), new(-200, 200, 0), SurfaceKind.Ground);
        const int width = 160, height = 100;
        var camera = new RenderCamera(new Vector3(0, -8, 1.8f), new Vector3(0, 0, 1.2f), 50);
        var settings = new RenderSettings { Seed = 5 };
        var tracer = new PathTracer(new RenderSceneBuilder().AddBuilding(ground).Build(), width, height, camera, settings);
        var view = tracer.Projection()!;
        var centred = view.TryProject(camera.Target, out var column, out var row, out var distance, out var pixels);
        check(centred && Math.Abs(column - width / 2f) < 0.6f && Math.Abs(row - height / 2f) < 0.6f && Math.Abs(distance - Vector3.Distance(camera.Position, camera.Target)) < 1e-3f,
            $"The camera's target falls in the middle of the picture ({column:0.0}, {row:0.0})");
        view.TryProject(camera.Target + new Vector3(1, 0, 0), out var rightColumn, out _, out _, out _);
        view.TryProject(camera.Target + new Vector3(0, 0, 1), out _, out var upRow, out _, out _);
        var expected = height / 2f / (MathF.Tan(25 * MathF.PI / 180) * 8.2f);
        check(rightColumn > column && upRow < row && Math.Abs(pixels - expected) / expected < 0.03f && !view.TryProject(camera.Position - new Vector3(0, 5, 0), out _, out _, out _, out _),
            $"A point to the right falls to the right, one above falls higher, a metre measures the right number of pixels ({pixels:0.0}) and what is behind the camera is not drawn");
        check(new PathTracer(new RenderSceneBuilder().AddBuilding(ground).Build(), 32, 16, camera with { Panoramic = true }).Projection() is null,
            "A 360° panorama has no flat picture to project onto");

        // Particles.
        var flameSprites = FireParticles.Build([campfire with { Smoke = 0 }], view, 1.0);
        var smokeSprites = FireParticles.Build([smoke], view, 1.0);
        check(flameSprites.Count > 60 && flameSprites.All(sprite => !sprite.Smoke) && smokeSprites.Count > 60 && smokeSprites.All(sprite => sprite.Smoke),
            $"A fire is drawn with flame particles and smoke with smoke particles ({flameSprites.Count} and {smokeSprites.Count})");
        check(FireParticles.Build([campfire], view, 1.0).SequenceEqual(FireParticles.Build([campfire], view, 1.0)) &&
              !FireParticles.Build([campfire], view, 1.0).SequenceEqual(FireParticles.Build([campfire], view, 1.13)),
            "The same instant always burns the same way, and the next instant moves the flames");
        double Strength(double flames) => FireParticles.Build([campfire with { Flames = flames, Smoke = 0 }], view, 1.0).Sum(sprite => sprite.Strength);
        check(Strength(1) > Strength(0.3) && FireParticles.Build([campfire with { Flames = 0 }], view, 1.0).All(sprite => sprite.Smoke) &&
              FireParticles.Build([campfire with { Smoke = 0 }], view, 1.0).All(sprite => !sprite.Smoke),
            "Stronger flames give more light, and a fire without flames or without smoke draws only the other");

        // Sparks and ash: only live flames throw them, they are small, and ash is dark where sparks are light.
        var live = FireParticles.Build([campfire with { Smoke = 0 }], view, 1.0);
        var ash = live.Where(sprite => sprite.Dark).ToList();
        var bright = live.Where(sprite => !sprite.Dark).ToList();
        var sparks = bright.Where(sprite => sprite.Aspect == 1).ToList();
        check(ash.Count >= 10 && sparks.Count >= 6 && ash.All(sprite => !sprite.Smoke && sprite.Color.X < 0.4f) &&
              sparks.Average(sprite => sprite.WorldRadius) < bright.Where(sprite => sprite.Aspect > 1).Average(sprite => sprite.WorldRadius) / 3 &&
              !FireParticles.Build([campfire with { Flames = 0 }], view, 1.0).Any(sprite => sprite.Dark) &&
              !FireParticles.Build([campfire with { Smoke = 0 }], view, 1.0).SequenceEqual(FireParticles.Build([campfire with { Smoke = 0 }], view, 2.0)),
            $"Live flames throw small bright sparks ({sparks.Count}) and drifting dark flecks of ash ({ash.Count}); a fire with no flames throws none");


        // Rocks: a surface of their own, and four shipped families made of it that sit on the ground.
        var rockMaterial = MaterialLibrary.ForKind(SurfaceKind.Rock);
        string? repository = null;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Guanacaste3D.slnx"))) { repository = directory.FullName; break; }
        var families = repository is null ? new List<FamilyMesh.Decoded>() : new[] { "rock-small", "rock-medium", "rock-large", "rock-flat" }.Select(name =>
            NativeDocumentStore.LoadFamily(Path.Combine(repository, "src", "Guanacaste3D.Desktop", "resources", "families", name + ".g3df")).Mesh!.Decode()).ToList();
        check(rockMaterial.Name == "Roca" && rockMaterial.Roughness > 0.85f && families.Count == 4 && families.All(rock => rock.Kinds.All(kind => kind == SurfaceKind.Rock) &&
              rock.TriangleCount >= 500 && Math.Abs(rock.Min.Z) < 0.1f && rock.Size.Z > 0.2f && rock.Size.Z < rock.Size.X) && families[0].Size.X < families[2].Size.X / 2,
            "Rocks have a grey, rough surface of their own, and the four shipped ones are faceted stones that sit on the ground, from a pebble to a boulder");

        // The picture: flames show, and what stands in front of them hides them.
        tracer.RenderPass();
        var plain = ImageProcessor.Plain;
        var baseline = tracer.ToBgra32(finish: plain);
        int Differing(byte[] a, byte[] b, int x0, int y0, int x1, int y1)
        {
            var count = 0;
            for (var y = y0; y < y1; y++)
                for (var x = x0; x < x1; x++)
                    if (Enumerable.Range(0, 3).Any(channel => Math.Abs(a[(y * width + x) * 4 + channel] - b[(y * width + x) * 4 + channel]) > 25)) count++;
            return count;
        }
        var burning = tracer.ToBgra32(finish: plain with { Fires = [campfire with { Smoke = 0 }], FireSeconds = 1 });
        var shown = Differing(baseline, burning, 0, 0, width, height);
        check(shown > 80 && Differing(baseline, burning, 0, 0, 12, 12) == 0, $"Flames paint a patch of the picture and leave a far corner alone ({shown} pixels)");

        var wall = new SceneMesh();
        wall.AddQuad(new(-200, -200, 0), new(200, -200, 0), new(200, 200, 0), new(-200, 200, 0), SurfaceKind.Ground);
        wall.AddQuad(new(-6, -4, 0), new(6, -4, 0), new(6, -4, 5), new(-6, -4, 5), SurfaceKind.Wall);
        var hidden = new PathTracer(new RenderSceneBuilder().AddBuilding(wall).Build(), width, height, camera, settings);
        hidden.RenderPass();
        var screen = hidden.ToBgra32(finish: plain);
        var behind = hidden.ToBgra32(finish: plain with { Fires = [campfire with { Smoke = 0 }], FireSeconds = 1 });
        check(Differing(screen, behind, 0, 0, width, height) < shown / 5, $"A wall between the camera and the fire hides it ({Differing(screen, behind, 0, 0, width, height)} pixels against {shown})");

        var veiled = tracer.ToBgra32(finish: plain with { Fires = [smoke], FireSeconds = 1 });
        check(Differing(baseline, veiled, 0, 0, width, height) > 150, "Smoke veils a good part of the picture");
        check(ReferenceEquals(tracer.WithScreenEffects(plain), plain) && tracer.WithScreenEffects(plain with { Fires = [] }).FireSeconds == 0,
            "A finish without fires is left as it was");

        // The light of the flames.
        var night = new RenderSettings { Seed = 5, SunIntensity = 0, SkyIntensity = 0.02f };
        byte[] Night(bool lit)
        {
            var builder = new RenderSceneBuilder().AddBuilding(ground);
            if (lit && Fires.Light(campfire) is { } glow) builder.AddLight(glow);
            var dark = new PathTracer(builder.Build(), width, height, camera, night);
            for (var pass = 0; pass < 3; pass++) dark.RenderPass();
            return dark.ToBgra32(finish: ImageProcessor.Plain);
        }
        long Brightness(byte[] image) => Enumerable.Range(0, width * height).Sum(pixel => (long)image[pixel * 4] + image[pixel * 4 + 1] + image[pixel * 4 + 2]);
        check(Fires.Light(smoke) is null && Brightness(Night(true)) > Brightness(Night(false)) * 1.5,
            "Flames light the ground around them at night, and a column of smoke gives no light");

        // The movie: the camera is still and the fire burns.
        var path = new CameraPath();
        path.Add(new CameraMark(camera.Position, camera.Target, 50, 0));
        path.Add(new CameraMark(camera.Position, camera.Target, 50, 2));
        var scene = new RenderSceneBuilder().AddBuilding(ground).Build();
        var folder = Path.Combine(Path.GetTempPath(), "guanacaste-fire-" + Guid.NewGuid().ToString("N"));
        try
        {
            TourRenderer.RenderAsync(scene, path, settings, width, height, 4, 3, new ImageFinish { Fires = [campfire] }, Path.Combine(folder, "burning"), null, CancellationToken.None)
                .GetAwaiter().GetResult();
            TourRenderer.RenderAsync(scene, path, settings, width, height, 4, 3, new ImageFinish(), Path.Combine(folder, "bare"), null, CancellationToken.None)
                .GetAwaiter().GetResult();
            var burningFrames = Directory.GetFiles(Path.Combine(folder, "burning"), "frame_*.png").Order().ToArray();
            var bareFrames = Directory.GetFiles(Path.Combine(folder, "bare"), "frame_*.png").Order().ToArray();
            int Changed(string a, string b)
            {
                var (x, y) = (PngCodec.Load(a).Rgb, PngCodec.Load(b).Rgb);
                return Enumerable.Range(0, x.Length / 3).Count(pixel => Enumerable.Range(0, 3).Any(channel => Math.Abs(x[pixel * 3 + channel] - y[pixel * 3 + channel]) > 25));
            }
            // Frame after frame, so a flame that has come back to where it was after a whole cycle is not mistaken for a still one.
            int Flicker(string[] frames) => Enumerable.Range(1, 6).Sum(index => Changed(frames[index - 1], frames[index]));
            check(burningFrames.Length == bareFrames.Length && burningFrames.Length >= 8 && Flicker(burningFrames) > Flicker(bareFrames) + 40,
                $"In the video the camera is still and the flames move ({Flicker(burningFrames)} pixels change from frame to frame against {Flicker(bareFrames)})");
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }

        // The cobogó house reads its fires from the project.
        var house = BreezeBlockHouseShowcase.Create();
        check(Fires.Of(house).Count == 0, "A project without fires has none");
        house.Extensions[Fires.Key] = Fires.Serialize([campfire])!;
        check(Fires.Of(house).Count == 1 && Fires.Of(house)[0] == campfire, "A project's fires are read from its extension store");
    }
}
