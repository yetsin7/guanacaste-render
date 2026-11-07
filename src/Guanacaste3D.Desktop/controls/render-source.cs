using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Navigation;
using Guanacaste3D.Core.Rendering;
using Microsoft.Win32;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>What the render panel needs from the window that hosts it.</summary>
internal interface IRenderSource
{
    bool HasScene => true;
    Guid? SelectedSceneItemId => null;
    bool CanExecuteSceneCommand(string command) => false;
    bool CanMoveSelection => false;
    bool CanElevateSelection => false;
    string MoveSelection(double x, double y, double z) => "Selecciona un objeto para moverlo.";
    string RotateSelection(double degrees) => "Selecciona un objeto para girarlo.";
    IReadOnlyList<RenderSceneItem> SceneItems() => [];
    void SelectSceneItem(Guid id) { }
    void SetSceneItemVisible(Guid id, bool visible) { }
    string ExecuteSceneCommand(string command) => "La acción no está disponible en esta vista.";
    /// <summary>The camera shared with the 3D view, so both always show the same place.</summary>
    NavigationCamera Camera { get; }

    string ProjectName { get; }
    /// <summary>Current project's path for resolving relative .g3dm references; null for new projects.</summary>
    string? ProjectFilePath { get; }

    (Vector3 Min, Vector3 Max) SceneBounds();

    /// <summary>The current scene, built in the background and cached; null when the project is empty.</summary>
    Task<RenderScene?> GetSceneAsync();

    /// <summary>The scene with some levels left out (the movie's layer animation); the cached one when none is hidden.</summary>
    Task<RenderScene?> GetSceneAsync(IReadOnlySet<Guid> hiddenLevels);

    /// <summary>Text a feature keeps in the project under a key (the movie, for one); null when there is none.</summary>
    string? GetExtension(string key);

    /// <summary>
    /// Saves such text as one undoable step. Unlike a model edit it does not rebuild the 3D views: it only marks the
    /// project as changed, because nothing the views draw has moved.
    /// </summary>
    void SetExtension(string key, string? value);

    /// <summary>Where the project is and how it is turned from true north, which decide where the sun stands.</summary>
    ProjectSite Site { get; }

    /// <summary>The people of the project and the routes they walk.</summary>
    IReadOnlyList<Actor> GetActors();

    /// <summary>Saves the people as one undoable step; the render scene is rebuilt, because it holds them.</summary>
    void SetActors(IReadOnlyList<Actor> actors);

    /// <summary>The project's terrain as one surface, for standing scattered things on the slope.</summary>
    Guanacaste3D.Core.Geometry.SiteTerrain Terrain();

    /// <summary>The trees, plants, rocks and cars that can be scattered: those loaded in the project and the shipped ones.</summary>
    IReadOnlyList<MassPlacementFamily> ScatterFamilies();

    /// <summary>Places the instances of a family near a ground height in one undoable step; returns the sentence to show.</summary>
    string PlaceScattered(MassPlacementFamily family, IReadOnlyList<ComponentPlacement> placements, double groundHeight);

    /// <summary>The fires and columns of smoke of the project.</summary>
    IReadOnlyList<FireEmitter> GetFires();

    /// <summary>Saves the fires as one undoable step; the render scene is rebuilt, because their flames light it.</summary>
    void SetFires(IReadOnlyList<FireEmitter> fires);

    /// <summary>The ocean of the project (off when it has none).</summary>
    OceanSettings GetOcean();

    /// <summary>Saves the ocean as one undoable step; the render scene is rebuilt, because the sea replaces the ground.</summary>
    void SetOcean(OceanSettings settings);

    /// <summary>The levels of the project, the layers a movie can show and hide.</summary>
    IReadOnlyList<Level> Levels { get; }

    /// <summary>Builds frozen, lightweight model geometry for interactive camera navigation.</summary>
    Task<ModelSceneSnapshot> GetInteractiveSceneAsync();

    /// <summary>Incremented whenever geometry, visibility, or materials invalidate the cached scene.</summary>
    int SceneVersion { get; }

    /// <summary>Ray queries for the current scene, or null until they are built.</summary>
    SceneQuery? Query { get; }

    /// <summary>Raised when geometry, links or materials change and the scene must be rebuilt.</summary>
    event Action? SceneChanged;

    /// <summary>Raised when the scene query becomes available or changes.</summary>
    event Action? QueryChanged;

    /// <summary>The project's render texture per surface category.</summary>
    IReadOnlyList<RenderMaterialAssignment> RenderMaterials { get; }

    /// <summary>Named render scenes persisted in the current project.</summary>
    IReadOnlyList<RenderSceneBookmark> RenderScenes { get; }

    /// <summary>Adds a named setup to the project as an undoable edit.</summary>
    void AddRenderScene(string name, RenderSetup setup);

    /// <summary>Removes a named setup as an undoable edit.</summary>
    void RemoveRenderScene(Guid id);

    /// <summary>Adds a photo (camera, style, filters and preview) without a model transaction or scene rebuild.</summary>
    Guid AddRenderPhoto(string name, RenderSetup setup, string? thumbnail, int? index = null);

    /// <summary>Changes a photo's setup, name or preview; null arguments keep the current value.</summary>
    void UpdateRenderPhoto(Guid id, RenderSetup? setup = null, string? name = null, string? thumbnail = null);

    /// <summary>Deletes a photo.</summary>
    void DeleteRenderPhoto(Guid id);

    /// <summary>Finds an editable render surface beneath a point in the shared viewport.</summary>
    (Guid ElementId, SurfaceKind Category)? PickRenderMaterial(Point position, FrameworkElement surface);

    /// <summary>Assigns a texture to a category, or returns it to its stock material when the path is null.</summary>
    void AssignRenderMaterial(SurfaceKind category, string? texturePath, double size, double relief, Guid? elementId = null);

    /// <summary>Set or remove independently loaded PBR maps, preserving the selected base material and undo history.</summary>
    void SetRenderMaterialMaps(SurfaceKind category, Guid? elementId,
        string? normalPath, string? roughnessPath, string? metallicPath);
    /// <summary>Non-destructive per-surface sliders: one undoable transaction, never edits the .g3dm manifest.</summary>
    void SetRenderMaterialAppearance(SurfaceKind category, Guid? elementId,
        double? roughness, double? metallic, double relief, string? tintHex = null, double? textureSize = null);

    /// <summary>Render-only stone tessellation. Off leaves low-power machines and the BIM model unaffected.</summary>
    bool StoneMicrogeometry { get; set; }

    /// <summary>Whether light fixtures are on; changing it rebuilds the scene.</summary>
    bool ArtificialLights { get; set; }

    /// <summary>Whether the render uses neutral study materials instead of project assignments.</summary>
    bool ClayMode { get; set; }
}

/// <summary>A native scene object, grouped by level and category in the render inspector.</summary>
internal sealed record RenderSceneItem(Guid Id, string Name, string Category, string Layer, bool Visible);
