using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Desktop;

/// <summary>Runs the Core model and geometry pipeline once so the first workspace opens with it already compiled.</summary>
internal static class StartupWarmup
{
    /// <summary>Builds a throwaway document, session and mesh. Pure Core code, safe to run off the UI thread.</summary>
    public static void LoadModelLibraries()
    {
        var document = BuildingDocument.Create("Startup");
        _ = new DocumentSession(document);
        _ = BuildingMesher.Build(document);
    }
}
