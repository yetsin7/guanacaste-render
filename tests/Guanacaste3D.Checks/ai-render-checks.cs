using Guanacaste3D.Core.Rendering;

internal static class AiRenderChecks
{
    public static void Run()
    {
        var options = new AiRenderOptions(AiRenderStyle.ArchitecturalModel, AddTrees: true, AddPeople: true,
            AdditionalInstructions: "Conserva las ventanas originales.");
        var markdown = AiRenderPromptBuilder.Generate(options);
        Check(markdown.Contains("maqueta arquitectónica") && markdown.Contains("árboles") &&
              markdown.Contains("personas") && markdown.Contains("Conserva las ventanas originales.") &&
              markdown.Contains("geometría") && markdown.Contains("imagen de referencia"),
            "Render con IA: prompt Markdown reflects the selected style and options");
        var notSelected = AiRenderPromptBuilder.Generate(new AiRenderOptions(AiRenderStyle.Photorealistic,
            AddTrees: false, AddPeople: false, AddFurniture: false));
        Check(notSelected.Contains("No añadas personas") && notSelected.Contains("No agregues árboles"),
            "Render con IA: disabled options do not silently add new objects");
        var root = Path.Combine(Path.GetTempPath(), "g3d-ai-render-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "Casa.g3d");
            var png = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aWO8AAAAASUVORK5CYII=");
            var request = AiRenderVault.CreateRequest(projectPath, "Casa", options, png);
            Check(File.Exists(request.PromptPath) && File.Exists(request.ReferencePath) &&
                request.DirectoryPath.StartsWith(projectPath + ".renders", StringComparison.Ordinal) &&
                File.ReadAllText(request.PromptPath) == markdown,
                "Render con IA: reference PNG and original prompt are stored by project");
            var imported = Path.Combine(root, "externo.png");
            File.WriteAllBytes(imported, png);
            var first = AiRenderVault.ImportResult(request, imported);
            var second = AiRenderVault.ImportResult(request, imported);
            Check(first != second && File.Exists(first) && File.Exists(second) &&
                AiRenderVault.ListRequests(projectPath, "Casa").Single().ResultCount == 2,
                "Render con IA: multiple AI outputs retain the original prompt and reference");
            var invalid = Path.Combine(root, "falso.png");
            File.WriteAllText(invalid, "No es una imagen");
            bool rejected = false;
            try { AiRenderVault.ImportResult(request, invalid); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Render con IA: invalid imported images are rejected");
            Check(AiRenderVault.ListRequests(projectPath, "Casa").Single().Prompt == markdown,
                "Render con IA: sessions reopen with the same Markdown provenance");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new Exception("FAILED: " + message);
        Console.WriteLine("OK: " + message);
    }
}
