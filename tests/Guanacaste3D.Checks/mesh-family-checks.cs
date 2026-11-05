using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Checks mesh component families (.g3df): storage, placement, scaling, the viewport preview and the shipped files.</summary>
static class MeshFamilyChecks
{
    public static void Run(Action<bool, string> check)
    {
        // A 2 × 1 × 3 m box family, its base already centered, plus a one-triangle preview.
        Vector3[] corners = [new(-1, -0.5f, 0), new(1, -0.5f, 0), new(1, 0.5f, 0), new(-1, 0.5f, 0),
            new(-1, -0.5f, 3), new(1, -0.5f, 3), new(1, 0.5f, 3), new(-1, 0.5f, 3)];
        int[] faces = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7];
        var mesh = FamilyMesh.Create(corners, faces, Enumerable.Repeat(SurfaceKind.CarPaint, 12).ToArray());
        mesh.Preview = FamilyMesh.Create(corners, [0, 1, 6], [SurfaceKind.Metal]);
        var family = new FamilyDocument
        {
            Name = "Caja de prueba", Category = FamilyCategory.Entourage,
            Types = [new FamilyTypeDefinition(Guid.NewGuid(), "Estándar", [])], Mesh = mesh
        };
        family.Metadata["PlanShape"] = nameof(ResidentialComponentShape.Car);
        var path = Path.Combine(Path.GetTempPath(), $"mesh-family-{Guid.NewGuid():N}.g3df");
        NativeDocumentStore.SaveFamily(path, family);
        var loaded = NativeDocumentStore.LoadFamily(path);
        File.Delete(path);
        check(loaded.Mesh!.TriangleCount == 12 && loaded.Mesh.Preview!.TriangleCount == 1 &&
              Vector3.Distance(loaded.Mesh.Decode().Size, new Vector3(2, 1, 3)) < 1e-5f,
            "A mesh family survives a .g3df save and reload with its preview");

        var bad = new FamilyMesh { Positions = mesh.Positions, Indices = Convert.ToBase64String(BitConverter.GetBytes(99)), Kinds = mesh.Kinds };
        check(Throws(bad.Validate), "A mesh whose indices point past its vertices is rejected");

        var session = new DocumentSession(ArchitecturalLibrary.CreateProject("Familias de malla"));
        var level = session.Document.Levels[0].Id;
        var id = session.LoadComponentFamily(loaded);
        check(session.LoadComponentFamily(loaded) == id && session.Document.ComponentFamilies.Count == 1,
            "Loading a family twice replaces it instead of duplicating it");
        var placed = session.PlaceComponentFamily(level, id, new Point2(10, 5), Math.PI / 2);
        var element = session.Document.StructuralElements.Single(candidate => candidate.Id == placed);
        check(element.Kind == StructuralElementKind.Component && element.FamilyId == id &&
              element.ComponentShape == ResidentialComponentShape.Car && Math.Abs(element.Depth - 1) < 1e-6,
            "A placed mesh family is a component at its modeled size with its plan shape");

        var scene = BuildingMesher.Build(session.Document, plants: PlantDetail.Render);
        var (min, max) = scene.Bounds();
        // Turned a quarter, the 2 m width runs along Y and the 1 m depth along X.
        check(scene.TriangleKinds.Count(kind => kind == SurfaceKind.CarPaint) == 12 &&
              Math.Abs(max.X - min.X - 1) < 1e-4 && Math.Abs(max.Y - min.Y - 2) < 1e-4 && Math.Abs(max.Z - 3) < 1e-4 &&
              Math.Abs((min.X + max.X) / 2 - 10) < 1e-4,
            "The renderer draws the full mesh turned and placed at the insertion point");
        check(BuildingMesher.Build(session.Document, plants: PlantDetail.Model).TriangleKinds.SequenceEqual([SurfaceKind.Metal]),
            "The interactive view draws the light preview instead");

        session.UpdateStructuralElement(placed, element.Name, 4, 6, 0, element.Style);
        var stretched = BuildingMesher.Build(session.Document, plants: PlantDetail.Render).Bounds();
        check(Math.Abs(stretched.Max.Z - 6) < 1e-4 && Math.Abs(stretched.Max.Y - stretched.Min.Y - 4) < 1e-4,
            "Changing an instance's width and height stretches its mesh");

        var copy = session.Document.Clone();
        copy.Validate();
        var json = Path.Combine(Path.GetTempPath(), $"mesh-project-{Guid.NewGuid():N}.g3");
        ProjectFile.Save(json, copy);
        var reopened = ProjectFile.Load(json);
        File.Delete(json);
        check(reopened.ComponentFamilies.Single().Mesh!.TriangleCount == 12 && reopened.StructuralElements.Single().FamilyId == id,
            "A project keeps its loaded mesh families and their instances when saved");
        var orphan = copy.Clone();
        orphan.ComponentFamilies.Clear();
        check(Throws(orphan.Validate), "An instance of a family that is not loaded is rejected");

        Textured(check);
        Shipped(check);
    }

    /// <summary>
    /// A textured card: a 2 × 2 m square whose image is opaque on its left half only. The renderer sees the left half
    /// and passes through the right one, for camera rays and shadows alike.
    /// </summary>
    private static void Textured(Action<bool, string> check)
    {
        Vector3[] corners = [new(-1, 0, 0), new(1, 0.01f, 0), new(1, 0.01f, 2), new(-1, 0, 2)];
        int[] faces = [0, 1, 2, 0, 2, 3];
        Vector2[] uvs = [new(0, 1), new(1, 1), new(1, 0), new(0, 0)];
        var rgba = new byte[4 * 4 * 4];
        for (var pixel = 0; pixel < 16; pixel++)
        {
            rgba[pixel * 4 + 1] = 200;
            rgba[pixel * 4 + 3] = (byte)(pixel % 4 < 2 ? 255 : 0);
        }
        var mesh = FamilyMesh.Create(corners, faces, [SurfaceKind.Leaf, SurfaceKind.Leaf], null, uvs, [0, 0],
            [FamilyMeshTexture.Create("hoja", 4, 4, rgba, 0.5f)]);
        var family = new FamilyDocument
        {
            Name = "Tarjeta de hoja", Category = FamilyCategory.Planting,
            Types = [new FamilyTypeDefinition(Guid.NewGuid(), "Estándar", [])], Mesh = mesh
        };
        var path = Path.Combine(Path.GetTempPath(), $"leaf-card-{Guid.NewGuid():N}.g3df");
        NativeDocumentStore.SaveFamily(path, family);
        var loaded = NativeDocumentStore.LoadFamily(path);
        File.Delete(path);
        check(loaded.Mesh!.Textures.Single().Texture().HasAlpha && loaded.Mesh.Decode().Uvs!.Length == 4,
            "A textured family survives a .g3df save with its image, mask and coordinates");

        var scene = new SceneMesh();
        loaded.Mesh.AppendTo(scene, new Point2(0, 0), 0, 2, 0.01, 2, 0);
        check(scene.Textures.Count == 1 && scene.TexturedTriangles.Count == 2, "Placing the family brings its image into the scene");
        var render = new Guanacaste3D.Core.Rendering.RenderSceneBuilder().AddBuilding(scene).Build();
        check(render.TriangleUvs is { Length: 6 } && render.Materials.Any(material => material.UvMapped && material.AlphaCutoff > 0),
            "The render scene maps the image by coordinates with its cut-out");
        var cutout = Guanacaste3D.Core.Rendering.AlphaCutout.For(render);
        var bvh = new Guanacaste3D.Core.Rendering.Bvh(render.Positions, render.Indices, null) { Cutout = cutout };
        bool Hits(float x)
        {
            var hit = new Guanacaste3D.Core.Rendering.RayHit { Distance = float.MaxValue };
            return bvh.Intersect(new Vector3(x, -5, 1), Vector3.UnitY, 0, ref hit);
        }
        check(cutout is not null && Hits(-0.5f) && !Hits(0.5f) &&
              !bvh.Occluded(new Vector3(0.5f, -5, 1), Vector3.UnitY, 0, 10) && bvh.Occluded(new Vector3(-0.5f, -5, 1), Vector3.UnitY, 0, 10),
            "Rays and shadows pass through the transparent part of a card and stop on the opaque one");

        var broken = new FamilyMesh
        {
            Positions = mesh.Positions, Indices = mesh.Indices, Kinds = mesh.Kinds, Uvs = mesh.Uvs,
            TriangleTextures = Convert.ToBase64String(new byte[] { 0, 3 }), Textures = mesh.Textures
        };
        var rejected = false;
        try { broken.Validate(); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "A triangle that names a missing image is rejected");

        // Normal maps: a flat texel leaves the normal alone; one leaning toward +u tips it along the image's u direction.
        var up = Vector3.UnitZ;
        Vector3 p0 = Vector3.Zero, p1 = Vector3.UnitX, p2 = Vector3.UnitY;
        Vector2 t0 = Vector2.Zero, t1 = Vector2.UnitX, t2 = Vector2.UnitY;
        var flat = Guanacaste3D.Core.Rendering.NormalMap.Perturb(up, Vector3.UnitZ, p0, p1, p2, t0, t1, t2);
        var leaning = Guanacaste3D.Core.Rendering.NormalMap.Perturb(up, Vector3.Normalize(new Vector3(0.5f, 0, 1)), p0, p1, p2, t0, t1, t2);
        var map = Guanacaste3D.Core.Rendering.NormalMap.FromRgb(1, 1, [128, 128, 255]);
        check(Vector3.Distance(flat, up) < 1e-5f && leaning.X > 0.4f && leaning.Z > 0.8f && Math.Abs(leaning.Y) < 1e-5f &&
              Vector3.Distance(map.Sample(0.3f, 0.7f), Vector3.UnitZ) < 0.02f,
            "Normal maps tip the shading normal along the image's axes and leave flat texels alone");
    }

    /// <summary>The CC0 jacaranda and cars shipped as families, and the saved breeze-block house that uses them.</summary>
    private static void Shipped(Action<bool, string> check)
    {
        string? root = null;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Guanacaste3D.slnx"))) { root = directory.FullName; break; }
        if (root is null) return;
        var resources = Path.Combine(root, "src", "Guanacaste3D.Desktop", "resources");
        var families = Directory.GetFiles(Path.Combine(resources, "families"), "*.g3df").Select(NativeDocumentStore.LoadFamily).ToArray();
        check(new[] { BreezeBlockHouseShowcase.TreeFamily, BreezeBlockHouseShowcase.SedanFamily, BreezeBlockHouseShowcase.CoupeFamily }
                .All(name => families.Any(family => family.Name == name && family.Metadata.GetValueOrDefault("License") == "CC0-1.0")),
            "The detailed jacaranda and cars ship as CC0 mesh families");
        var tree = families.Single(family => family.Name == BreezeBlockHouseShowcase.TreeFamily).Mesh!;
        check(tree.Preview is { } preview && preview.TriangleCount < tree.TriangleCount / 10 &&
              tree.Decode().Kinds.Contains(SurfaceKind.Leaf) && tree.Decode().Kinds.Contains(SurfaceKind.Bark),
            "The jacaranda has leaves, bark and a light preview for the viewport");

        var sample = NativeDocumentStore.LoadProject(Path.Combine(resources, "samples", "casa-del-cobogo.g3d"));
        sample.Validate();
        check(sample.ComponentFamilies.Count >= 9 &&
              sample.StructuralElements.Count(element => element.FamilyId is not null) >= 70,
            "The saved breeze-block house opens with its jacarandas, cars, garden and indoor plants");
        var sampleTree = sample.ComponentFamilies.Single(family => family.Name == BreezeBlockHouseShowcase.TreeFamily).Mesh!;
        var sampleTrunk = sampleTree.Textures.Single(texture => texture.Name.Contains("trunk", StringComparison.OrdinalIgnoreCase));
        check(sampleTrunk.Width == 2048 && sampleTrunk.Normal() is { Width: 2048, Height: 2048 },
            "The saved house retains the detailed jacaranda trunk normal map");
        var fern = families.Single(family => family.Name == BreezeBlockHouseShowcase.FernFamily).Mesh!;
        check(fern.Textures.Count == 1 && fern.Textures[0].AlphaCutoff > 0 && fern.Decode().Uvs is not null &&
              fern.Decode().TriangleTextures!.All(texture => texture == 0),
            "Leaf families carry their image, its cut-out mask and texture coordinates");
        var jacaranda = families.Single(family => family.Name == BreezeBlockHouseShowcase.TreeFamily).Mesh!;
        var barkTriangles = jacaranda.Decode().Kinds.Select((kind, index) => (kind, index)).Where(pair => pair.kind == SurfaceKind.Bark)
            .Select(pair => pair.index).ToArray();
        var trunk = jacaranda.Textures.Single(texture => texture.Name.Contains("trunk", StringComparison.OrdinalIgnoreCase));
        check(jacaranda.Textures.Count == 3 && jacaranda.Textures.All(texture => texture.Normal() is not null && texture.Width >= 1024) &&
              trunk.Width == 2048 && trunk.NormalWidth == 2048 && trunk.NormalHeight == 2048 &&
              barkTriangles.All(triangle => jacaranda.Decode().TriangleTextures![triangle] != FamilyMesh.NoTexture),
            "The jacaranda has a 2K trunk color and normal map and retains textured bark after reduction");
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (InvalidDataException) { return true; }
    }
}
