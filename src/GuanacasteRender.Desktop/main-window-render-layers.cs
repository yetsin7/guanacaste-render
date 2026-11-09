using System.IO;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;
using Guanacaste3D.Desktop.Controls;

namespace Guanacaste3D.Desktop;

/// <summary>The render scene without some levels, for the movie's layer animation.</summary>
public partial class MainWindow
{
    /// <summary>
    /// The render scene with the given levels left out. With none hidden this is the cached scene; otherwise it is built
    /// fresh for this one look (the movie asks for a handful of them, one after another) and not kept.
    /// </summary>
    private Task<RenderScene?> GetSceneAsync(IReadOnlySet<Guid> hiddenLevels)
    {
        if (hiddenLevels.Count == 0) return GetSceneAsync();
        if (!HasSceneContent()) return Task.FromResult<RenderScene?>(null);
        // The display document is already a copy, so the levels can be taken out of it directly.
        var document = ModelDisplayDocument();
        LevelVisibility.Hide(document, hiddenLevels);
        return BuildRenderSceneAsync(document);
    }
}
