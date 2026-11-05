using System.IO;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>
/// The car meshes the render's animated cars use: the sedan and the coupé that ship with the program. Each is read once and
/// kept, because a movie asks for them again for every scene it builds. A model that is missing or damaged is left out (null),
/// and the cars that use it are not drawn.
/// </summary>
internal static class CarLibrary
{
    private static readonly string[] Files = ["sedan-car", "coupe-car-silver"];
    private static readonly Dictionary<int, FamilyMesh.Decoded?> Loaded = [];

    /// <summary>The mesh of a car model: 0 is the sedan, 1 the coupé.</summary>
    public static FamilyMesh.Decoded? Load(int model)
    {
        lock (Loaded)
        {
            if (Loaded.TryGetValue(model, out var cached)) return cached;
            FamilyMesh.Decoded? mesh = null;
            if ((uint)model < (uint)Files.Length)
            {
                var path = Path.Combine(AppContext.BaseDirectory, "resources", "families", Files[model] + ".g3df");
                try { mesh = File.Exists(path) ? NativeDocumentStore.LoadFamily(path).Mesh?.Decode() : null; }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException) { mesh = null; }
            }
            return Loaded[model] = mesh;
        }
    }
}
