using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Threading;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Navigation;
using Guanacaste3D.Core.Rendering;
using Guanacaste3D.Desktop.Controls;

namespace Guanacaste3D.Desktop;

/// <summary>
/// The camera, scene and navigation shared by the 3D view and the Render tab. Both show the same
/// <see cref="NavigationCamera"/>; the scene is built once in the background, cached, and used by the live preview, the
/// final render, cursor-centered zoom and collisions.
/// </summary>
public partial class MainWindow
{
    private const int Model3DTabIndex = 1;
    private const int RenderTabIndex = 6;

    private readonly NavigationCamera _navigation = NavigationCamera.Frame(new Vector3(-5, -5, 0), new Vector3(5, 5, 3));
    private SceneNavigator? _modelNavigator;
    private readonly LinkedModels _links = new();
    private Task<RenderScene?>? _sceneTask;
    private SceneQuery? _query;
    private int _sceneVersion;
    private (int Version, (Vector3 Min, Vector3 Max) Bounds)? _boundsCache;
    private DispatcherTimer? _sceneDebounce;
    private bool _modelDirty = true;
    private bool _renderClay;
    private bool _stoneMicrogeometry;
    private int _modelBuildRequest;
    private readonly System.Threading.SemaphoreSlim _modelBuildGate = new(1, 1);
    private event Action? SceneChanged;
    private event Action? QueryChanged;

    /// <summary>Connects navigation to the 3D view and the render panel; called once from the constructor.</summary>
    private void InitializeNavigationAndRender()
    {
        _modelNavigator = new SceneNavigator(ModelSurface, _navigation)
        {
            Bounds = SceneBounds,
            Pick = (origin, direction) => _query?.Pick(origin, direction)
        };
        _modelNavigator.Changed += UpdateModelCamera;
        RenderPanelControl.Attach(new RenderSource(this));
        InitializeModelSelection();
        InitializeViewTabs();
        InitializeProjectedViewZoom();
        InitializeViewCube();
        InitializeToolbarDemos();
        _links.Changed += OnLinksChanged;
        _sceneDebounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(350) };
        _sceneDebounce.Tick += (_, _) =>
        {
            _sceneDebounce.Stop();
            if (ViewTabs.SelectedIndex == RenderTabIndex || (Model3DVisible && !_modelDirty)) _ = GetSceneAsync();
            SceneChanged?.Invoke();
        };
        ViewTabs.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, ViewTabs)) return;
            if (Model3DVisible)
            {
                if (_modelDirty) RebuildModelVisual();
                else UpdateModelCamera();
            }
            UpdateModelSelection(true);
            if (ViewTabs.SelectedIndex == RenderTabIndex) _ = GetSceneAsync();
        };
    }

    /// <summary>Adapts the window to what the render panel asks of its host.</summary>
    private sealed class RenderSource(MainWindow window) : IRenderSource
    {
        public string ExecuteSceneCommand(string command)
        {
            var args = new RoutedEventArgs();
            switch (command)
            {
                case "import": window.ImportModel_Click(window, args); break;
                case "rotate": window.Rotate_Click(window, args); break;
                case "delete": window.Delete_Click(window, args); break;
                case "undo": window.Undo_Click(window, args); break;
                case "duplicate":
                    if (window._selectedStructuralId is Guid id)
                    {
                        window._selectedStructuralId = window._session.DuplicateRenderComponent(id);
                        window.RefreshAll();
                        return "Objeto duplicado a un metro del original.";
                    }
                    window.Duplicate_Click(window, args);
                    break;
            }
            return window.StatusText.Text;
        }
        public NavigationCamera Camera => window._navigation;
        public string ProjectName => window._session.Document.Name;
        public (Vector3 Min, Vector3 Max) SceneBounds() => window.SceneBounds();
        public Task<RenderScene?> GetSceneAsync() => window.GetSceneAsync();
        public Task<RenderScene?> GetSceneAsync(IReadOnlySet<Guid> hiddenLevels) => window.GetSceneAsync(hiddenLevels);
        public IReadOnlyList<Level> Levels => window._session.Document.Levels;
        public ProjectSite Site => window._session.Document.Site;
        public IReadOnlyList<FireEmitter> GetFires() => Fires.Of(window._session.Document);
        public void SetFires(IReadOnlyList<FireEmitter> fires) => window._session.SetExtension(Fires.Key, Fires.Serialize(fires));
        public IReadOnlyList<Actor> GetActors() => Actors.Of(window._session.Document);
        public void SetActors(IReadOnlyList<Actor> actors) => window._session.SetExtension(Actors.Key, Actors.Serialize(actors));
        public IReadOnlyList<MassPlacementFamily> ScatterFamilies() => window.ScatterFamilies();
        public Guanacaste3D.Core.Geometry.SiteTerrain Terrain() => new(window._session.Document);
        public string PlaceScattered(MassPlacementFamily family, IReadOnlyList<ComponentPlacement> placements, double groundHeight) =>
            window.PlaceScattered(family, placements, groundHeight);
        public OceanSettings GetOcean() => Ocean.Of(window._session.Document);
        public void SetOcean(OceanSettings settings) => window._session.SetExtension(Ocean.Key, Ocean.Serialize(settings));
        public string? GetExtension(string key) => window._session.GetExtension(key);
        public void SetExtension(string key, string? value)
        {
            // A committed step normally refreshes every view and rebuilds the render scene; the movie changes none of that.
            window._session.Changed -= window.OnDocumentChanged;
            try { window._session.SetExtension(key, value); }
            finally { window._session.Changed += window.OnDocumentChanged; }
            window.OnRenderPhotosChanged(null, EventArgs.Empty);
        }
        /// <summary>Copies document and material state before building WPF geometry off the UI thread.</summary>
        public Task<ModelSceneSnapshot> GetInteractiveSceneAsync()
        {
            var document = window.ModelDisplayDocument();
            var textures = RenderTextureLibrary.Previews3D(document.RenderMaterials, window._filePath);
            var scopedTextures = new Dictionary<(Guid, SurfaceKind), (System.Windows.Media.ImageSource Image, double Size)>();
            foreach (var assignment in document.RenderMaterials.Where(item => item.ElementId is not null))
            {
                var preview = RenderTextureLibrary.Previews3D([assignment with { ElementId = null }], window._filePath);
                if (preview.TryGetValue(assignment.Category, out var image))
                    scopedTextures[(assignment.ElementId!.Value, assignment.Category)] = image;
            }
            var links = window._links.Visible().ToArray();
            var clay = window._renderClay;
            return Task.Run(() =>
            {
                var snapshot = ModelSceneSnapshot.Build(document, clay, false, false, textures, includeGround: true, scopedTextures: scopedTextures);
                var models = snapshot.Models.ToList();
                foreach (var (link, loaded) in links)
                    models.AddRange(ScenePresentation.ToWpfModels(loaded.Proxy, loaded.Model.Materials, link.Placement, clay));
                return snapshot with { Models = models };
            });
        }
        public int SceneVersion => window._sceneVersion;
        public SceneQuery? Query => window._query;
        public IReadOnlyList<RenderMaterialAssignment> RenderMaterials => window._session.Document.RenderMaterials;
        public IReadOnlyList<RenderSceneBookmark> RenderScenes => window._session.Document.RenderScenes;
        /// <summary>Persists a scene in the active document.</summary>
        public void AddRenderScene(string name, RenderSetup setup) => window._session.AddRenderScene(name, setup);
        /// <summary>Deletes a scene from the active document.</summary>
        public void RemoveRenderScene(Guid id) => window._session.RemoveRenderScene(id);
        public Guid AddRenderPhoto(string name, RenderSetup setup, string? thumbnail, int? index = null) =>
            window._session.AddRenderPhoto(name, setup, thumbnail, index);
        public void UpdateRenderPhoto(Guid id, RenderSetup? setup = null, string? name = null, string? thumbnail = null) =>
            window._session.UpdateRenderPhoto(id, setup, name, thumbnail);
        public void DeleteRenderPhoto(Guid id) => window._session.DeleteRenderPhoto(id);
        /// <summary>Resolves a material drop against the active project model.</summary>
        public (Guid ElementId, SurfaceKind Category)? PickRenderMaterial(Point position, FrameworkElement surface) =>
            window.PickRenderMaterial(position, surface);
        public string? ProjectFilePath => window._filePath;

        /// <summary>Limits the neutral material override to rendered scenes.</summary>
        public bool ClayMode
        {
            get => window._renderClay;
            set
            {
                if (window._renderClay == value) return;
                window._renderClay = value;
                window.MarkSceneDirty();
            }
        }

        public bool StoneMicrogeometry
        {
            get => window._stoneMicrogeometry;
            set
            {
                if (window._stoneMicrogeometry == value) return;
                window._stoneMicrogeometry = value;
                window.MarkSceneDirty();
            }
        }

        public bool ArtificialLights
        {
            get => window._artificialLights;
            set
            {
                if (window._artificialLights == value) return;
                window._artificialLights = value;
                window.MarkSceneDirty();
            }
        }
        public void AssignRenderMaterial(SurfaceKind category, string? texturePath, double size, double relief, Guid? elementId = null) =>
            window.AssignElementRenderMaterial(category, texturePath, size, relief, elementId);

        public void SetRenderMaterialMaps(SurfaceKind category, Guid? elementId,
            string? normalPath, string? roughnessPath, string? metallicPath) =>
            window.SetRenderMaterialMaps(category, elementId, normalPath, roughnessPath, metallicPath);
        public void SetRenderMaterialAppearance(SurfaceKind category, Guid? elementId,
            double? roughness, double? metallic, double relief, string? tintHex = null, double? textureSize = null) =>
            window.SetRenderMaterialAppearance(category, elementId, roughness, metallic, relief, tintHex, textureSize);

        public event Action? SceneChanged
        {
            add => window.SceneChanged += value;
            remove => window.SceneChanged -= value;
        }

        public event Action? QueryChanged
        {
            add => window.QueryChanged += value;
            remove => window.QueryChanged -= value;
        }
    }

    // ---------------------------------------------------------------- scene

    /// <summary>Whether the project has anything to show: model elements or visible linked models.</summary>
    private bool HasSceneContent()
    {
        return HasModelElements() || _links.Visible().Any();
    }

    /// <summary>Bounds of the model and visible linked models, or a 10 m site for an empty project. Cached per scene version.</summary>
    private (Vector3 Min, Vector3 Max) SceneBounds()
    {
        if (_boundsCache is { } cached && cached.Version == _sceneVersion) return cached.Bounds;
        var document = ModelDisplayDocument();
        var hasModel = document.Walls.Count + document.Floors.Count + document.Columns.Count + document.StructuralElements.Count > 0;
        var min = new Vector3(-5, -5, 0);
        var max = new Vector3(5, 5, 3);
        if (hasModel) (min, max) = BuildingMesher.Build(document).Bounds();
        if (_links.Bounds() is { } linked)
        {
            (min, max) = hasModel ? (Vector3.Min(min, linked.Min), Vector3.Max(max, linked.Max)) : linked;
        }
        _boundsCache = (_sceneVersion, (min, max));
        return (min, max);
    }

    /// <summary>Whether light fixtures glow and light the render (the Render panel's switch).</summary>
    private bool _artificialLights = true;

    /// <summary>Assigns (or, with no path, clears) the render texture of a surface category as one undoable step.</summary>
    // Keep this four-argument entry point for existing callers and reflection-based interaction tests.
    private void AssignRenderMaterial(SurfaceKind category, string? texturePath, double size, double relief) =>
        AssignElementRenderMaterial(category, texturePath, size, relief, null);

    private void AssignElementRenderMaterial(SurfaceKind category, string? texturePath, double size, double relief, Guid? elementId)
    {
        try
        {
            if (texturePath is null) _session.ClearRenderMaterial(category, elementId);
            else
            {
                var previous = _session.Document.RenderMaterials.FirstOrDefault(a =>
                    a.Category == category && a.ElementId == elementId);
                _session.SetRenderMaterial(new RenderMaterialAssignment(category, texturePath)
                {
                    ElementId = elementId, TextureSize = size, Relief = relief,
                    NormalPath = previous?.NormalPath, RoughnessPath = previous?.RoughnessPath,
                    MetallicPath = previous?.MetallicPath,
                    RoughnessOverride = previous?.RoughnessOverride, MetallicOverride = previous?.MetallicOverride,
                    TintHex = previous?.TintHex
                });
            }
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
        }
        RefreshAll();
    }

    /// <summary>One undoable, project-scoped update; never edits a shared .g3dm file.</summary>
    private void SetRenderMaterialAppearance(SurfaceKind category, Guid? elementId,
        double? roughness, double? metallic, double relief, string? tintHex = null, double? textureSize = null)
    {
        try
        {
            var current = _session.Document.RenderMaterials.FirstOrDefault(a =>
                a.Category == category && a.ElementId == elementId);
            var assignment = (current ?? new RenderMaterialAssignment(category, "stock:") { ElementId = elementId }) with
            {
                RoughnessOverride = roughness, MetallicOverride = metallic, Relief = relief, TintHex = tintHex,
                TextureSize = textureSize ?? (current ?? new RenderMaterialAssignment(category, "stock:")).TextureSize
            };
            _session.SetRenderMaterial(assignment);
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
        }
        RefreshAll();
    }

    /// <summary>One undoable update of a material's independent PBR maps; stock materials may also receive maps.</summary>
    private void SetRenderMaterialMaps(SurfaceKind category, Guid? elementId,
        string? normalPath, string? roughnessPath, string? metallicPath)
    {
        var current = _session.Document.RenderMaterials.FirstOrDefault(a =>
            a.Category == category && a.ElementId == elementId);
        var updated = (current ?? new RenderMaterialAssignment(category, "stock:") { ElementId = elementId }) with
        {
            NormalPath = normalPath, RoughnessPath = roughnessPath, MetallicPath = metallicPath
        };
        if (updated.TexturePath == "stock:" && normalPath is null && roughnessPath is null && metallicPath is null)
            _session.ClearRenderMaterial(category, elementId);
        else
            _session.SetRenderMaterial(updated);
        RefreshAll();
    }

    /// <summary>Marks the cached scene stale; it is rebuilt a moment after edits stop, and only if a view needs it.</summary>
    private void MarkSceneDirty()
    {
        _sceneVersion++;
        _sceneTask = null;
        _query = null;
        _modelDirty = true;
        if (_modelNavigator is not null) _modelNavigator.Collision = null;
        _sceneDebounce?.Stop();
        _sceneDebounce?.Start();
    }

    /// <summary>The current render scene, built on a worker thread and cached until the project changes.</summary>
    private Task<RenderScene?> GetSceneAsync()
    {
        if (_sceneTask is { } existing) return existing;
        if (!HasSceneContent()) return _sceneTask = Task.FromResult<RenderScene?>(null);

        // The document is copied here (records are immutable, so the copy is cheap) and meshed off-thread: the render
        // gets trees with branches and tens of thousands of leaves, which must not stall the interface. The 3D view
        // keeps the light modelling canopies.
        // The 3D view draws its own visibility: elements hidden or isolated in that view are left out of the copy.
        var version = _sceneVersion;
        var task = BuildRenderSceneAsync(ModelDisplayDocument());
        _sceneTask = task;
        _ = task.ContinueWith(finished =>
        {
            if (finished.IsCompletedSuccessfully && finished.Result is { } scene && version == _sceneVersion)
                Dispatcher.BeginInvoke(() => BuildQuery(scene, version));
        }, TaskScheduler.Default);
        return task;
    }

    /// <summary>
    /// Builds a render scene from a copy of the document, with the project's links, materials and render options as they
    /// are now. The copy is taken here, on the calling thread, and meshed on a worker.
    /// </summary>
    private Task<RenderScene?> BuildRenderSceneAsync(BuildingDocument document)
    {
        var links = _links.Visible().ToList();
        var materials = _session.Document.RenderMaterials.ToList();
        var projectFile = _filePath;
        var lightsOn = _artificialLights;
        var clay = _renderClay;
        var stoneDetail = _stoneMicrogeometry;
        return Task.Run<RenderScene?>(() =>
        {
            var mesh = BuildingMesher.Build(document, includeGround: true, plants: PlantDetail.Render);
            // The ocean takes the endless ground's place; it must be put in before anything else is appended to the mesh.
            var sea = Ocean.Of(document);
            Ocean.Apply(mesh, sea);
            if (stoneDetail) RenderStoneDisplacement.Append(mesh);
            // Images stuck on walls (Insert > Decal) are part of what the render shows.
            Decals.AppendTo(mesh, document, image => RenderTextureLibrary.Load(DecalImagePath(image, projectFile)));
            var stock = RenderTextureLibrary.Resolver(materials, projectFile, lightsOn);
            var perElement = RenderTextureLibrary.ElementResolver(materials, projectFile, lightsOn);
            var builder = new RenderSceneBuilder().AddBuilding(mesh,
                kind => kind == SurfaceKind.Ocean ? Ocean.Material(sea) : stock(kind),
                (id, kind) => kind == SurfaceKind.Ocean ? Ocean.Material(sea) : perElement(id, kind));
            foreach (var (link, loaded) in links) builder.AddModel(loaded.Model, link.Placement);
            // Flames light their surroundings: one warm point light for each fire that has them.
            foreach (var fire in Fires.Of(document))
                if (Fires.Light(fire) is { } glow) builder.AddLight(glow);
            // The people go in last: they are the one block the movie moves, from the place they start at.
            if (ActorRig.Of(document, CarLibrary.Load) is { } people)
            {
                var (start, startNormals) = people.Frame(0);
                builder.AddDynamic(start, people.Indices, people.TriangleMaterial, people.Materials, people.HasNormals ? startNormals : null);
            }
            var scene = builder.Build();
            if (clay) scene = RenderSceneStyles.Clay(scene);
            MemoryTrim.AfterHeavyBuild();
            return scene.TriangleCount == 0 ? null : scene;
        });
    }

    /// <summary>Builds ray queries for navigation off-thread, then publishes them on the UI thread if the scene is still current.</summary>
    private void BuildQuery(RenderScene scene, int version)
    {
        _ = Task.Run(() => SceneQuery.Build(scene)).ContinueWith(finished =>
        {
            // Without a query (for example, out of memory) zoom falls back to the scene size and collisions stay off.
            if (!finished.IsCompletedSuccessfully) return;
            var query = finished.Result;
            Dispatcher.BeginInvoke(() =>
            {
                if (version != _sceneVersion) return;
                _query = query;
                if (_modelNavigator is not null) _modelNavigator.Collision = query.Collision();
                QueryChanged?.Invoke();
            });
        }, TaskScheduler.Default);
    }

    // ---------------------------------------------------------------- 3D view

    /// <summary>Places the shared camera so the whole model is in view.</summary>
    private void FrameCameraToScene()
    {
        if (_modelNavigator?.IsLocked == true) return;
        var (min, max) = SceneBounds();
        var framed = NavigationCamera.Frame(min, max);
        _navigation.Position = framed.Position;
        _navigation.YawDegrees = framed.YawDegrees;
        _navigation.PitchDegrees = framed.PitchDegrees;
        _navigation.FieldOfViewDegrees = framed.FieldOfViewDegrees;
        UpdateModelCamera();
        RenderPanelControl?.Navigator?.FrameAll();
    }

    /// <summary>Restores the three-quarter view of the whole model.</summary>
    private void ResetOrbit_Click(object sender, RoutedEventArgs e) => FrameCameraToScene();

    /// <summary>Shows the photorealistic render view.</summary>
    private void ShowRender_Click(object sender, RoutedEventArgs e) => ViewTabs.SelectedIndex = RenderTabIndex;

    /// <summary>Opens the Render tab and starts a final render with the panel's settings.</summary>
    private void StartRender_Click(object sender, RoutedEventArgs e)
    {
        ViewTabs.SelectedIndex = RenderTabIndex;
        RenderPanelControl.RequestRender();
    }

    /// <summary>Keeps the interactive camera aspect in sync with the viewport.</summary>
    private void ModelViewport_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateModelCamera();

    /// <summary>Stops any render or video before the window closes.</summary>
    private void CancelRender() => RenderPanelControl.CancelAll();

    /// <summary>Whether the pointer is flying through the 3D view or the render preview, so letters mean movement, not shortcuts.</summary>
    private bool IsNavigating3D() => _modelNavigator?.IsActive == true || RenderPanelControl?.Navigator?.IsActive == true;

    /// <summary>Whether a key belongs to flight navigation right now.</summary>
    private bool NavigationOwnsKey(System.Windows.Input.Key key) =>
        _modelNavigator?.OwnsKey(key) == true || RenderPanelControl?.Navigator?.OwnsKey(key) == true;
}
