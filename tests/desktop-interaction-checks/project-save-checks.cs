using System.IO;
using System.Reflection;
using Guanacaste3D.Core;
using Guanacaste3D.Desktop;

internal static partial class InteractionChecks
{
    private static void ProjectSaveNameChecks()
    {
        var title = "Casa del cobogó: volumen blanco sobre cochera de concreto";
        var safeName = (string)typeof(MainWindow).GetMethod("SafeProjectFileStem",
            BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [title])!;
        Check(!safeName.Any(Path.GetInvalidFileNameChars().Contains) &&
              safeName == "Casa del cobogó- volumen blanco sobre cochera de concreto",
            "The default project filename replaces Windows-invalid characters while preserving the project title");

        var folder = Path.Combine(Path.GetTempPath(), "guanacaste-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, safeName + ProjectFile.RenderExtension);
            ProjectFile.Save(path, ArchitecturalLibrary.CreateProject(title));
            Check(File.Exists(path) && ProjectFile.Load(path).Name == title,
                "A project with a colon in its title saves under a valid filename without changing the title");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
