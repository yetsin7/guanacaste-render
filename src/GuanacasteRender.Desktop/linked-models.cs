using System.IO;
using System.Numerics;
using Guanacaste3D.Core.Import;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop;

/// <summary>A linked model read into memory, with the light proxy the 3D view draws.</summary>
internal sealed record LoadedModel(ImportedModel Model, ProxyMesh Proxy);

/// <summary>
/// The external models linked into the open project. Files are read on a worker thread; the 3D view gets a reduced proxy
/// while the renderer uses the full geometry. Links are saved beside the project and restored when it is opened, and a
/// file that has gone missing keeps its link, flagged, so nothing is lost silently.
/// </summary>
internal sealed class LinkedModels
{
    private readonly Dictionary<Guid, LoadedModel> _loaded = [];
    private readonly Dictionary<Guid, string> _errors = [];

    public List<ModelLink> Links { get; private set; } = [];

    /// <summary>Raised on the calling thread after links or placements change.</summary>
    public event Action? Changed;

    public bool IsLoaded(Guid id) => _loaded.ContainsKey(id);

    /// <summary>The reason a link could not be read, or null.</summary>
    public string? ErrorOf(Guid id) => _errors.GetValueOrDefault(id);

    public LoadedModel? Get(Guid id) => _loaded.GetValueOrDefault(id);

    /// <summary>Visible, loaded links with their models.</summary>
    public IEnumerable<(ModelLink Link, LoadedModel Model)> Visible() =>
        Links.Where(link => link.Visible && _loaded.ContainsKey(link.Id)).Select(link => (link, _loaded[link.Id]));

    /// <summary>Bounds of all visible models with their placements applied, or null when there are none.</summary>
    public (Vector3 Min, Vector3 Max)? Bounds()
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var any = false;
        foreach (var (link, loaded) in Visible())
        {
            var (low, high) = loaded.Model.Bounds();
            var matrix = link.Placement.ToMatrix();
            foreach (var corner in Corners(low, high))
            {
                var p = Vector3.Transform(corner, matrix);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                any = true;
            }
        }
        return any ? (min, max) : null;
    }

    private static IEnumerable<Vector3> Corners(Vector3 low, Vector3 high)
    {
        for (var i = 0; i < 8; i++)
            yield return new Vector3((i & 1) == 0 ? low.X : high.X, (i & 2) == 0 ? low.Y : high.Y, (i & 4) == 0 ? low.Z : high.Z);
    }

    /// <summary>Reads a file, links it and places it. <paramref name="projectBounds"/> lets a far-away model be recentered.</summary>
    public async Task<ModelLink> AddAsync(string path, ModelImportOptions options, (Vector3 Min, Vector3 Max)? projectBounds,
        IProgress<ImportProgress>? progress, CancellationToken cancellation)
    {
        if (Links.Count >= ModelLinkFile.MaximumLinks) throw new InvalidDataException("The model file is too large to import.");
        var result = await ModelImporter.ImportAsync(path, options, progress, cancellation);
        cancellation.ThrowIfCancellationRequested();
        var proxy = await Task.Run(() => MeshProxy.Build(result.Model), cancellation);
        MemoryTrim.AfterHeavyBuild();

        var link = new ModelLink
        {
            Path = Path.GetFullPath(path), Name = result.Model.Name, UpAxis = options.UpAxis, MetersPerUnit = options.MetersPerUnit,
            CenterHorizontally = options.CenterHorizontally, PlaceOnGround = options.PlaceOnGround
        };
        // A survey-coordinate file would land kilometers from the project; bring it to the model instead of losing it.
        if (!link.CenterHorizontally && projectBounds is { } project)
        {
            var (low, high) = result.Model.Bounds();
            var modelCenter = (low + high) / 2;
            var projectCenter = (project.Min + project.Max) / 2;
            if (ModelPlacementPolicy.ShouldRecenter(modelCenter, projectCenter, (high - low).Length()))
                link.Placement = new ModelPlacement(projectCenter.X - modelCenter.X, projectCenter.Y - modelCenter.Y, 0);
        }
        Links.Add(link);
        _loaded[link.Id] = new LoadedModel(result.Model, proxy);
        _errors.Remove(link.Id);
        Changed?.Invoke();
        return link;
    }

    /// <summary>Reads every link of a freshly opened project; a missing or broken file is recorded, not fatal.</summary>
    public async Task LoadAllAsync(IProgress<ImportProgress>? progress, CancellationToken cancellation)
    {
        var count = Links.Count;
        for (var i = 0; i < count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            await LoadOneAsync(Links[i], cancellation);
            progress?.Report(new ImportProgress((i + 1.0) / count, "Import.Reading"));
        }
        Changed?.Invoke();
    }

    public async Task ReloadAsync(Guid id, CancellationToken cancellation)
    {
        if (Links.FirstOrDefault(link => link.Id == id) is not { } link) return;
        _loaded.Remove(id);
        await LoadOneAsync(link, cancellation);
        Changed?.Invoke();
    }

    private async Task LoadOneAsync(ModelLink link, CancellationToken cancellation)
    {
        try
        {
            var result = await ModelImporter.ImportAsync(link.Path, link.ToOptions(), null, cancellation);
            _loaded[link.Id] = new LoadedModel(result.Model, await Task.Run(() => MeshProxy.Build(result.Model), cancellation));
            _errors.Remove(link.Id);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _errors[link.Id] = ex.Message;
            _loaded.Remove(link.Id);
        }
    }

    public void Remove(Guid id)
    {
        Links.RemoveAll(link => link.Id == id);
        _loaded.Remove(id);
        _errors.Remove(id);
        Changed?.Invoke();
    }

    public void Update(Guid id, Action<ModelLink> change)
    {
        if (Links.FirstOrDefault(link => link.Id == id) is not { } link) return;
        change(link);
        Changed?.Invoke();
    }

    public void Clear()
    {
        Links = [];
        _loaded.Clear();
        _errors.Clear();
        Changed?.Invoke();
    }

    /// <summary>Replaces the links with those saved beside a project; models are read by <see cref="LoadAllAsync"/>.</summary>
    public void Restore(string projectPath)
    {
        Links = ModelLinkFile.Load(projectPath);
        _loaded.Clear();
        _errors.Clear();
    }

    /// <summary>Writes the links beside the project file.</summary>
    public void Save(string projectPath) => ModelLinkFile.Save(projectPath, Links);

    /// <summary>Builds the render scene contribution of every visible link.</summary>
    public void AddTo(RenderSceneBuilder builder)
    {
        foreach (var (link, loaded) in Visible()) builder.AddModel(loaded.Model, link.Placement);
    }

    /// <summary>Total triangles of visible models, for budgeting.</summary>
    public long VisibleTriangles => Visible().Sum(item => (long)item.Model.Model.TriangleCount);
}

/// <summary>Decides where an imported model lands relative to the project it is linked into.</summary>
internal static class ModelPlacementPolicy
{
    /// <summary>
    /// Whether a model whose center is <paramref name="modelCenter"/> should be moved onto the project instead of staying at
    /// its own coordinates. Survey and GIS files sit hundreds of kilometers from the origin, where the user would never find
    /// them; but a neighboring building deliberately drawn 80 m away must stay where it was drawn.
    /// </summary>
    public static bool ShouldRecenter(Vector3 modelCenter, Vector3 projectCenter, float modelSize)
    {
        // TODO(human): refine this policy. Today any model whose horizontal center is more than 500 m from the project's is
        // recentered. Consider the model's own size (a 2 km terrain legitimately reaches farther), and whether the model
        // overlaps the project at all.
        return Vector2.Distance(new Vector2(modelCenter.X, modelCenter.Y), new Vector2(projectCenter.X, projectCenter.Y)) > 500;
    }
}
