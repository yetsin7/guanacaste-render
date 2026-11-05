using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Rendering;

/// <summary>Checks project persistence and texture scale for render workspace features.</summary>
internal static class RenderSceneBookmarkChecks
{
    /// <summary>Exercises scene history, native persistence, and inclined-wall projection.</summary>
    internal static void Run(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Render scene checks"));
        var setup = new RenderSetup { CameraX = 7, CameraY = -4, SolarHour = 16 };
        var id = session.AddRenderScene("Exterior", setup);
        session.Undo();
        check(session.Document.RenderScenes.Count == 0, "Adding a render scene can be undone");
        session.Redo();
        var path = Path.Combine(Path.GetTempPath(), $"g3d-scene-{Guid.NewGuid():N}.g3d");
        try
        {
            NativeDocumentStore.SaveProject(path, session.Document);
            var scene = NativeDocumentStore.LoadProject(path).RenderScenes.Single();
            check(scene.Id == id && scene.Name == "Exterior" && scene.Setup.CameraX == 7 && scene.Setup.SolarHour == 16,
                "Named render scenes preserve their camera and environment through save and reopen");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
        session.RemoveRenderScene(id);
        session.Undo();
        check(session.Document.RenderScenes.Single().Id == id, "Deleting a render scene can be undone");
        var normal = Vector3.Normalize(new Vector3(1, -1, 0));
        var along = Vector3.Normalize(new Vector3(1, 1, 0));
        var origin = TextureProjection.PlaneUv(Vector3.Zero, normal);
        var end = TextureProjection.PlaneUv(along, normal);
        var reverse = TextureProjection.PlaneUv(along, -normal);
        check(Math.Abs(Math.Abs(end.U - origin.U) - 1) < 1e-5 && Math.Abs(end.U - reverse.U) < 1e-5,
            "A diagonal wall retains metric texture scale and consistent direction on opposite faces");
        using var instructions = typeof(AiRenderPromptBuilder).Assembly.GetManifestResourceStream("Guanacaste3D.AiSceneFidelity.es.txt")
            ?? throw new InvalidOperationException("The AI fidelity resource is missing.");
        using var reader = new StreamReader(instructions);
        check(AiRenderPromptBuilder.Generate(new AiRenderOptions(AiRenderStyle.Photorealistic)).Contains(reader.ReadToEnd().Trim()),
            "AI render prompts include the localized scene-fidelity instructions");
    }
}
