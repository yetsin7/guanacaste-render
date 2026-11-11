using System.IO;
using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Temporary bridge to the Rust studio: opens a project without showing any window and writes it as a triangulated
/// <c>.grscene</c> file with the materials assigned in the project. It exists only until the Rust engine reads and
/// meshes projects itself (see <c>docs/engine/RENDER_NEXT_STEPS.md</c>). Linked models, decals, people, the ocean and
/// textures are not exported.
/// </summary>
public static class SceneExportBridge
{
    public const string Switch = "--export-scene";

    /// <summary>Runs <c>--export-scene &lt;project&gt; &lt;output.grscene&gt;</c>. Returns a process exit code.</summary>
    public static int Run(string[] arguments)
    {
        if (arguments.Length != 3) return 2;
        try
        {
            Export(Path.GetFullPath(arguments[1]), Path.GetFullPath(arguments[2]));
            return 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
        {
            // The caller has no console to read; the reason goes next to the output it asked for.
            try { File.WriteAllText(arguments[2] + ".error.txt", ex.Message); }
            catch (Exception inner) when (inner is IOException or UnauthorizedAccessException or ArgumentException) { }
            return 1;
        }
    }

    public static void Export(string projectPath, string outputPath)
    {
        var document = ProjectFile.Load(projectPath);
        var mesh = BuildingMesher.Build(document, includeGround: true, plants: PlantDetail.Render);
        var assignments = document.RenderMaterials.ToList();
        var stock = RenderTextureLibrary.Resolver(assignments, projectPath);
        var perElement = RenderTextureLibrary.ElementResolver(assignments, projectPath);
        var scene = new RenderSceneBuilder().AddBuilding(mesh, stock, perElement).Build();

        var (min, max) = BuildingMesher.Build(document).Bounds();
        var camera = RenderCamera.FrameBounds(min, max);
        if (document.RenderSetup is { } setup)
        {
            var position = new Vector3((float)setup.CameraX, (float)setup.CameraY, (float)setup.CameraZ);
            var yaw = setup.YawDegrees * Math.PI / 180;
            var pitch = setup.PitchDegrees * Math.PI / 180;
            var forward = new Vector3((float)(Math.Cos(pitch) * Math.Cos(yaw)), (float)(Math.Cos(pitch) * Math.Sin(yaw)),
                (float)Math.Sin(pitch));
            camera = new RenderCamera(position, position + forward * 10, (float)Math.Clamp(setup.FieldOfViewDegrees, 5, 150));
        }
        var temporary = outputPath + ".tmp";
        SceneExchange.Write(temporary, scene, camera, new RenderSettings());
        File.Move(temporary, outputPath, overwrite: true);
    }
}
