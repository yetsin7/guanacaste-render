using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

/// <summary>Regression checks for render-quality trees, leaf translucency, wall faces, door hardware and light fixtures.</summary>
static class RenderDetailChecks
{
    public static void Run(Action<bool, string> check)
    {
        Trees(check);
        StoneGeometry(check);
        HeightMaps(check);
        ScopedMaterials(check);
        Translucency(check);
        WallFaces(check);
        Fixtures(check);
    }

    private static void Trees(Action<bool, string> check)
    {
        foreach (var preset in ArchitecturalLibrary.PresetsFor(StructuralElementKind.Planting))
        {
            var document = BuildingDocument.Create("Tree");
            document.StructuralElements.Add(new StructuralElement(Guid.NewGuid(), document.Levels[0].Id, StructuralElementKind.Planting,
                "Tree", new Point2(3, 4), new Point2(3, 4), preset.Width, preset.Height, 0) { Style = preset.Style });
            var model = BuildingMesher.Build(document);
            var render = BuildingMesher.Build(document, plants: PlantDetail.Render);
            var leaves = render.TriangleKinds.Count(kind => kind == SurfaceKind.Leaf);
            var (min, max) = render.Bounds();
            check(leaves > 2000 && render.TriangleCount > model.TriangleCount * 3 &&
                  Math.Abs(max.Z - (preset.Height - 0.08)) < 0.02 && Math.Abs(min.Z + 0.08) < 0.01,
                $"The render-quality {preset.Style} has branches and single leaves and is embedded slightly below ground");
            var again = BuildingMesher.Build(document, plants: PlantDetail.Render);
            check(again.Positions.Count == render.Positions.Count && again.Positions[^1] == render.Positions[^1],
                $"The render-quality {preset.Style} regenerates identically");
        }
    }

    private static void StoneGeometry(Action<bool, string> check)
    {
        var mesh = new SceneMesh();
        var wallOwner = Guid.NewGuid();
        mesh.AddQuad(new(0, 0, 0), new(2, 0, 0), new(2, 0, 2), new(0, 0, 2), SurfaceKind.WallStone);
        mesh.MarkElement(wallOwner, 0);
        var original = mesh.Positions.ToArray();
        var triangles = mesh.TriangleCount;
        var added = RenderStoneDisplacement.Append(mesh, triangleBudget: 256, targetEdgeMeters: 0.1f);
        var raised = mesh.Positions.Skip(original.Length).ToArray();
        check(added is > 10 and <= 256 && mesh.TriangleCount == triangles + added &&
              mesh.Positions.Take(original.Length).SequenceEqual(original) &&
              mesh.ElementRanges.Last().ElementId == wallOwner,
            "Optional render-only stone microgeometry has a strict budget and does not mutate original BIM triangles");
        check(raised.Length > 0 && raised.All(p => p.Y < -0.001f && p.Y > -0.029f) &&
              raised.Select(p => p.Y).Max() - raised.Select(p => p.Y).Min() > 0.002f,
            "Raised stone faces have real varied silhouettes and remain within 2.8 cm of the source wall");
        var plain = new SceneMesh();
        plain.AddQuad(new(0, 0, 0), new(2, 0, 0), new(2, 0, 2), new(0, 0, 2), SurfaceKind.WallStone);
        check(RenderStoneDisplacement.Append(plain, triangleBudget: 0) == 0 && plain.TriangleCount == 2,
            "Disabled displacement adds no geometry or cost on low-power machines");
    }

    private static void HeightMaps(Action<bool, string> check)
    {
        // Mip levels and border wrapping must be stable on distant building surfaces.
        var height = RenderScalarMap.FromGray(2, 2, [0, 255, 0, 255]);
        check(Math.Abs(height.Sample(.25f, .75f, 0)) < .001f &&
              Math.Abs(height.Sample(.75f, .75f, 0) - 1) < .001f &&
              Math.Abs(height.Sample(.25f, .75f, 2) - .5f) < .01f,
            "Height maps sample linear data and produce the expected distant mip");
        check(Math.Abs(height.TexelSize(0) - .5f) < .001f &&
              Math.Abs(height.TexelSize(2) - 1f) < .001f,
            "Bump-map derivatives use the actual filtered texel size");
        check(Math.Abs(height.TexelSize(float.MaxValue) - 1f) < .001f &&
              Math.Abs(height.Sample(.25f, .75f, float.MaxValue) - .5f) < .01f,
            "Finite extreme footprints select the coarsest mip without overflow");
        // Distant PBR maps must preserve area coverage, not overweight the
        // bottom-right texel of odd-sized textures as successive mips shrink.
        var oddPixels = new byte[9];
        oddPixels[4] = 255;
        var odd = RenderScalarMap.FromGray(3, 3, oddPixels);
        check(odd.LevelCount == 3 &&
              Math.Abs(odd.Sample(.5f, .5f, 10f) - 1f / 9f) < .012f,
            "Odd-sized scalar maps preserve the source-area mean at distant mips");
        var constant = RenderScalarMap.FromGray(5, 3, Enumerable.Repeat((byte)73, 15).ToArray());
        check(Math.Abs(constant.Sample(.17f, .68f, 10f) - 73f / 255f) < .001f,
            "Odd-sized uniform roughness and metalness remain constant through all mip levels");
        check(ReferenceEquals((new RenderMaterial { HeightMap = height, TextureRelief = .7f }).Sanitized().HeightMap, height),
            "Sanitization retains the height channel");
    }

    private static void ScopedMaterials(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Per-element appearance"));
        var level = session.Document.Levels[0].Id;
        session.AddWall(level, new Point2(0, 0), new Point2(3, 0));
        session.AddWall(level, new Point2(0, 4), new Point2(3, 4));
        var left = session.Document.Walls[0].Id;
        var right = session.Document.Walls[1].Id;

        var common = new RenderMaterialAssignment(SurfaceKind.Wall, "material:stone") { TextureSize = 1.0 };
        var selected = new RenderMaterialAssignment(SurfaceKind.Wall, "material:oak") { ElementId = left, TextureSize = 0.4 };
        session.SetRenderMaterial(common);
        session.SetRenderMaterial(selected);
        RenderMaterialRules.Validate(session.Document);
        check(session.Document.RenderMaterials.Count == 2 &&
              session.Document.RenderMaterials.Any(a => a.ElementId is null) &&
              session.Document.RenderMaterials.Any(a => a.ElementId == left),
            "Per-element and project-wide material assignments coexist and survive model validation");

        var mesh = BuildingMesher.Build(session.Document);
        var scene = new RenderSceneBuilder().AddBuilding(mesh,
            _ => new RenderMaterial { Name = "Global", BaseColor = new Vector3(.2f) },
            (id, _) => new RenderMaterial { Name = id == left ? "Selected" : "Global",
                BaseColor = id == left ? new Vector3(.8f, .2f, .1f) : new Vector3(.2f) }).Build();
        var l = mesh.ElementRanges.First(range => range.ElementId == left);
        var r = mesh.ElementRanges.First(range => range.ElementId == right);
        check(scene.Materials[scene.TriangleMaterial[l.First]].Name == "Selected" &&
              scene.Materials[scene.TriangleMaterial[r.First]].Name == "Global",
            "Two BIM walls with the same SurfaceKind can render independent finishes");

        session.ClearRenderMaterial(SurfaceKind.Wall, left);
        check(session.Document.RenderMaterials.Count == 1 &&
              session.Document.RenderMaterials[0].ElementId is null,
            "Restoring one selected wall does not delete the project-wide material");
        session.Undo();
        check(session.Document.RenderMaterials.Any(a => a.ElementId == left),
            "Scoped material edits are undoable");
    }

    private static void Translucency(Action<bool, string> check)
    {
        // A leaf-like sheet between the camera and the sun: lit only from behind.
        var mesh = new SceneMesh();
        mesh.AddQuad(new(-1, 0, 0), new(1, 0, 0), new(1, 0, 2), new(-1, 0, 2), SurfaceKind.Leaf);
        var camera = new RenderCamera(new Vector3(0, -3, 1), new Vector3(0, 0, 1), 30);
        var settings = new RenderSettings { SunDirection = new Vector3(0, 1, 0.3f), SkyIntensity = 0, CloudCover = 0, Seed = 5 };
        float Brightness(float translucency)
        {
            var scene = new RenderSceneBuilder().AddBuilding(mesh, kind =>
                MaterialLibrary.ForKind(SurfaceKind.Leaf) with { Translucency = translucency, Pattern = SurfacePattern.None }).Build();
            var tracer = new PathTracer(scene, 16, 16, camera, settings);
            for (var pass = 0; pass < 16; pass++) tracer.RenderPass();
            return tracer.LinearImage(false).Average(Sampling.Luminance);
        }
        var opaque = Brightness(0);
        var translucent = Brightness(0.6f);
        check(opaque < 1e-4f && translucent > 0.01f, "Sunlight passes through a translucent leaf from behind, not through an opaque one");
    }

    private static void WallFaces(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Faces"));
        var level = session.Document.Levels[0].Id;
        Point2[] corners = [new(0, 0), new(6, 0), new(6, 5), new(0, 5)];
        for (var index = 0; index < 4; index++) session.AddWall(level, corners[index], corners[(index + 1) % 4]);
        var door = session.Document.OpeningTypes.First(type => type.Kind == OpeningKind.Door && type.Operation == OpeningOperation.Standard);
        session.AddOpening(session.Document.Walls[0].Id, OpeningKind.Door, offset: 3);
        var mesh = BuildingMesher.Build(session.Document);
        Vector3 Center(int triangle) => (mesh.Positions[mesh.Indices[triangle * 3]] + mesh.Positions[mesh.Indices[triangle * 3 + 1]] +
                                        mesh.Positions[mesh.Indices[triangle * 3 + 2]]) / 3;
        var faces = Enumerable.Range(0, mesh.TriangleCount).Where(triangle =>
            mesh.TriangleKinds[triangle] is SurfaceKind.Wall or SurfaceKind.InteriorWall).ToArray();
        var outsideOk = faces.Where(triangle => Center(triangle).Y < -0.09f).All(triangle => mesh.TriangleKinds[triangle] == SurfaceKind.Wall);
        var insideOk = faces.Where(triangle => Center(triangle).Y is > 0.09f and < 0.2f && Center(triangle).X is > 0.5f and < 5.5f)
            .All(triangle => mesh.TriangleKinds[triangle] == SurfaceKind.InteriorWall);
        check(outsideOk && insideOk && faces.Any(triangle => mesh.TriangleKinds[triangle] == SurfaceKind.InteriorWall),
            "Walls face the outside with the facade material and the rooms with plaster");
        check(mesh.TriangleKinds.Count(kind => kind == SurfaceKind.Metal) >= 24,
            "A swing door carries lever handles on both faces and hinges in metal");
        _ = door;
    }

    private static void Fixtures(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Lights"));
        var level = session.Document.Levels[0].Id;
        var ceiling = ArchitecturalLibrary.ElementPresets.First(preset => preset.ComponentShape == ResidentialComponentShape.CeilingLight);
        session.AddElementFromPreset(level, ceiling, new Point2(2, 2), new Point2(2, 2));
        var mesh = BuildingMesher.Build(session.Document);
        var emitter = mesh.TriangleKinds.Select((kind, index) => (kind, index)).Where(item => item.kind == SurfaceKind.LightEmitter)
            .Select(item => mesh.Positions[mesh.Indices[item.index * 3]].Z).DefaultIfEmpty(-1).Min();
        var canopy = mesh.TriangleKinds.Select((kind, index) => (kind, index)).Where(item => item.kind == SurfaceKind.Metal)
            .Select(item => mesh.Positions[mesh.Indices[item.index * 3]].Z).DefaultIfEmpty(-1).Max();
        check(emitter > 2 && canopy > emitter && MaterialLibrary.ForKind(SurfaceKind.LightEmitter).IsEmissive,
            "A ceiling light hangs its glowing diffuser under a metal canopy");
    }
}
