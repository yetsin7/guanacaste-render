using System.Numerics;
using Guanacaste3D.Core.Import;
using Guanacaste3D.Core.Rendering;

/// <summary>Regression checks for linked-model UV coordinates through import metadata and render scene assembly.</summary>
static class ImportedUvChecks
{
    public static void Run(Action<bool, string> check)
    {
        var texture = RenderTexture.FromBgra32(1, 1, new byte[] { 255, 255, 255, 255 });
        var textured = new RenderMaterial { Name = "UV test", Texture = texture, UvMapped = true };
        var model = new ImportedModel
        {
            Positions = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
            Indices = [0, 1, 2],
            Uvs = [new(0.1f, 0.2f), new(0.8f, 0.2f), new(0.1f, 0.9f)],
            TriangleMaterial = [0],
            Materials = [textured]
        };

        var relinked = model.WithSource("linked.glb", "glTF", "Linked model");
        check(relinked.SourcePath == "linked.glb" && relinked.Uvs is { Length: 3 } &&
              relinked.Uvs.SequenceEqual(model.Uvs!),
            "Re-attributing a linked model preserves its imported UV coordinates");

        var scene = new RenderSceneBuilder().AddModel(relinked).Build();
        check(scene.TriangleCount == 1 && scene.TriangleUvs is { Length: 3 } &&
              scene.TriangleUvs[0] == model.Uvs![0] &&
              scene.TriangleUvs[1] == model.Uvs[1] &&
              scene.TriangleUvs[2] == model.Uvs[2] &&
              scene.Materials[scene.TriangleMaterial[0]].UvMapped,
            "Linked UV-textured triangles retain their three texture coordinates in the renderer");

        // The invalid first triangle must not shift the UVs of the second, valid triangle.
        var withBadFace = new ImportedModel
        {
            Positions = model.Positions,
            Indices = [0, 1, 99, 0, 1, 2],
            Uvs = model.Uvs,
            TriangleMaterial = [0, 0],
            Materials = [textured]
        };
        var filtered = new RenderSceneBuilder().AddModel(withBadFace).Build();
        check(filtered.DroppedTriangles == 1 && filtered.TriangleCount == 1 &&
              filtered.TriangleUvs is { Length: 3 } &&
              filtered.TriangleUvs[0] == model.Uvs![0] &&
              filtered.TriangleUvs[2] == model.Uvs[2],
            "Rejected imported faces do not misalign the remaining UVs");

        var untextured = new ImportedModel
        {
            Positions = model.Positions,
            Indices = model.Indices,
            Uvs = model.Uvs,
            TriangleMaterial = [0],
            Materials = [MaterialLibrary.Imported]
        };
        var mixed = new RenderSceneBuilder().AddModel(untextured).AddModel(model).Build();
        check(mixed.TriangleUvs is { Length: 6 } &&
              mixed.TriangleUvs[0] == Vector2.Zero &&
              mixed.TriangleUvs[3] == model.Uvs![0] &&
              mixed.TriangleUvs[5] == model.Uvs[2],
            "UV coordinates stay aligned when untextured and textured models share a scene");

        var incomplete = new ImportedModel
        {
            Positions = model.Positions,
            Indices = model.Indices,
            Uvs = [Vector2.Zero],
            TriangleMaterial = [0],
            Materials = [textured]
        };
        check(new RenderSceneBuilder().AddModel(incomplete).Build().TriangleUvs is null,
            "Incomplete imported UV arrays are ignored instead of indexing beyond their bounds");
    }
}
