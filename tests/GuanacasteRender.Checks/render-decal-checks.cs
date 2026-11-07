using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

/// <summary>Checks the decals: their saved text, whether they fit a wall, their corners, their triangles and what the render shows.</summary>
static class RenderDecalChecks
{
    public static void Run(Action<bool, string> check)
    {
        var wallId = Guid.NewGuid();
        var levelId = Guid.NewGuid();
        var wall = new Wall(wallId, levelId, new Point2(0, 0), new Point2(4, 0), 0.2, 3);
        var decal = new Decal(Guid.NewGuid(), wallId, DecalFace.Left, 2, 1, 1, 1, "rotulo.png");

        // Saved text.
        var text = Decals.Serialize([decal]);
        check(text is not null && Decals.TryParse(text) is { Count: 1 } back && back[0] == decal &&
              Decals.Serialize([]) is null && Decals.TryParse(null)!.Count == 0,
            "A decal reads back exactly from the text the project keeps, and no decals keep nothing");
        string[] bad = ["no es json", "{}", """{"V":2,"Items":[]}""",
            """{"V":1,"Items":[{"Id":"00000000-0000-0000-0000-000000000000","WallId":"00000000-0000-0000-0000-000000000000","Face":0,"Along":1,"CenterHeight":1,"Width":1,"Height":1,"ImagePath":"a.png"}]}"""];
        check(bad.All(item => Decals.TryParse(item) is null), "Damaged decal text is refused");

        // Fit.
        check(Decals.Fit(wall, decal) is null, "A decal inside the wall's face fits");
        check(Decals.Fit(wall, decal with { Along = 3.8 }) is { } along && along.Contains("a lo largo") &&
              Decals.Fit(wall, decal with { CenterHeight = 2.8 }) is { } high && high.Contains("altura") &&
              Decals.Fit(wall, decal with { Width = 0.001 }) is not null &&
              Decals.Fit(wall with { BaseOffset = 0.5 }, decal with { CenterHeight = 0.7 }) is not null,
            "A decal that leaves the wall's face, or is absurdly small, is turned down with the reason");

        // Corners: the right way round, facing out of the face they are on.
        foreach (var face in new[] { DecalFace.Left, DecalFace.Right })
        {
            var (bottomLeft, bottomRight, topRight, topLeft) = Decals.Corners(wall, 0.5, decal with { Face = face });
            var sign = face == DecalFace.Left ? 1f : -1f;
            var facing = Vector3.Cross(bottomRight - bottomLeft, topRight - bottomLeft);
            check(Math.Abs(bottomLeft.Y - sign * 0.103f) < 1e-4 && facing.Y * sign > 0.5f && Math.Abs(facing.X) < 1e-4 &&
                  Math.Abs(bottomLeft.Z - 1f) < 1e-4 && Math.Abs(topLeft.Z - 2f) < 1e-4 && Math.Abs((bottomRight - bottomLeft).Length() - 1) < 1e-4 &&
                  (face == DecalFace.Left ? bottomLeft.X > bottomRight.X : bottomLeft.X < bottomRight.X),
                $"The {face} face's decal sits 3 mm off the wall, faces outward, and reads left to right for the person in front of it");
        }

        // Triangles for the render.
        var level = new Level(levelId, "Planta baja", 0.5);
        var document = BuildingDocument.Create("Calcos");
        document.Levels = [level];
        document.Walls = [wall];
        var picture = RenderTexture.FromRgba(2, 2, [255, 0, 255, 255, 255, 0, 255, 255, 255, 0, 255, 255, 255, 0, 255, 255]);
        var loads = 0;
        RenderTexture? Load(string path) { loads++; return path == "falta.png" ? null : picture; }
        var second = decal with { Id = Guid.NewGuid(), Along = 1, Face = DecalFace.Right };
        var missing = decal with { Id = Guid.NewGuid(), ImagePath = "falta.png" };
        var orphan = decal with { Id = Guid.NewGuid(), WallId = Guid.NewGuid() };
        document.Extensions[Decals.Key] = Decals.Serialize([decal, second, missing, orphan])!;
        var mesh = new SceneMesh();
        var added = Decals.AppendTo(mesh, document, Load);
        check(added == 2 && mesh.TriangleCount == 4 && mesh.Textures.Count == 1 && mesh.TexturedTriangles.Count == 4 && loads == 2,
            "Each decal adds a textured quad; one image used twice is loaded once; a missing wall or image leaves its decal out");

        // The picture is not mirrored: red on the left half and blue on the right half reads that way from either face.
        var halves = RenderTexture.FromRgba(2, 1, [255, 0, 0, 255, 0, 0, 255, 255]);
        foreach (var face in new[] { DecalFace.Left, DecalFace.Right })
        {
            var flat = BuildingDocument.Create("Espejo");
            flat.Levels = [level];
            flat.Walls = [wall];
            flat.Extensions[Decals.Key] = Decals.Serialize([decal with { Face = face, Width = 2, Height = 1 }])!;
            var flatMesh = new SceneMesh();
            Decals.AppendTo(flatMesh, flat, _ => halves);
            var centre = new Vector3(2, face == DecalFace.Left ? 0.103f : -0.103f, 1.5f);
            var viewer = centre + new Vector3(0, face == DecalFace.Left ? 3f : -3f, 0);
            var tracer = new PathTracer(new RenderSceneBuilder().AddBuilding(flatMesh).Build(), 64, 40, new RenderCamera(viewer, centre, 40));
            for (var pass = 0; pass < 3; pass++) tracer.RenderPass();
            var shot = tracer.ToBgra32(finish: ImageProcessor.Plain);
            int Channel(int x, int channel) => shot[(20 * 64 + x) * 4 + channel];
            check(Channel(24, 2) > Channel(24, 0) + 40 && Channel(40, 0) > Channel(40, 2) + 40,
                $"A picture stuck on the {face} face reads the right way round, not mirrored (red {Channel(24, 2)}/{Channel(24, 0)}, blue {Channel(40, 2)}/{Channel(40, 0)})");
        }

        // What the render shows, on the cobogó house: a magenta decal, seen from in front of a wall.
        var house = BreezeBlockHouseShowcase.Create();
        var shown = false;
        var candidates = house.Walls.OrderByDescending(item => item.Length).Take(6).ToList();
        foreach (var candidate in candidates)
        {
            var houseLevel = house.Levels.First(item => item.Id == candidate.LevelId);
            var size = Math.Min(candidate.Length * 0.6, 2.0);
            var up = Math.Clamp(candidate.BaseOffset + 1.5, candidate.BaseOffset + size / 2, candidate.Height - size / 2);
            var onBoth = new[] { DecalFace.Left, DecalFace.Right }.Select(face =>
                new Decal(Guid.NewGuid(), candidate.Id, face, candidate.Length / 2, up, size, size, "magenta.png")).ToList();
            if (onBoth.Any(item => Decals.Fit(candidate, item) is not null)) continue;
            var withDecals = house.Clone();
            withDecals.Extensions[Decals.Key] = Decals.Serialize(onBoth)!;
            var (bottomLeft, _, topRight, _) = Decals.Corners(candidate, houseLevel.Elevation, onBoth[0]);
            var centre = (bottomLeft + topRight) / 2;
            var normal = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, new Vector3((float)(candidate.End.X - candidate.Start.X), (float)(candidate.End.Y - candidate.Start.Y), 0)));
            var camera = new RenderCamera(centre + normal * 3f, centre, 50);

            byte[] Shot(BuildingDocument source, bool decals)
            {
                var sceneMesh = BuildingMesher.Build(source, includeGround: true);
                if (decals) Decals.AppendTo(sceneMesh, source, _ => picture);
                var tracer = new PathTracer(new RenderSceneBuilder().AddBuilding(sceneMesh).Build(), 64, 40, camera);
                for (var pass = 0; pass < 2; pass++) tracer.RenderPass();
                return tracer.ToBgra32(finish: ImageProcessor.Plain);
            }

            var without = Shot(house, false);
            var with = Shot(withDecals, true);
            int Pixel(byte[] image, int channel) => image[(20 * 64 + 32) * 4 + channel];
            var magenta = Pixel(with, 2) > Pixel(with, 1) + 30 && Pixel(with, 0) > Pixel(with, 1) + 30;
            var wasMagenta = Pixel(without, 2) > Pixel(without, 1) + 30 && Pixel(without, 0) > Pixel(without, 1) + 30;
            if (magenta && !wasMagenta) { shown = true; break; }
        }
        check(shown, "On the cobogó house a decal shows in the render, in front of the wall it is stuck on");
    }
}
