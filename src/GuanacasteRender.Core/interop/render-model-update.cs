using System.Reflection;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core.Interop;

/// <summary>How many elements of one document list an update added, changed and removed.</summary>
public sealed record ModelUpdateCount(int Added, int Changed, int Removed)
{
    /// <summary>Total number of elements the update touched in the list.</summary>
    public int Total => Added + Changed + Removed;
}

/// <summary>
/// What a model update did to a render project: the model changes it brought in, the render work it kept, and the
/// places where both applications edited the same element.
/// </summary>
public sealed record RenderModelUpdateReport(
    bool UsedBaseline,
    IReadOnlyDictionary<string, ModelUpdateCount> ByCollection,
    int KeptRenderEdits,
    int MaterialsDropped,
    IReadOnlyList<MergeConflict> Conflicts,
    bool ProjectChanged)
{
    public int Added => ByCollection.Values.Sum(count => count.Added);
    public int Changed => ByCollection.Values.Sum(count => count.Changed);
    public int Removed => ByCollection.Values.Sum(count => count.Removed);

    /// <summary>False when the incoming model brought nothing the render project did not already have.</summary>
    public bool HasChanges => Added + Changed + Removed + MaterialsDropped > 0;
}

/// <summary>The merged project and the account of what the update changed.</summary>
public sealed record RenderModelUpdateResult(BuildingDocument Document, RenderModelUpdateReport Report);

/// <summary>
/// Brings a newer model from the modeling application into a render project without discarding the work done in
/// the render application. The model owns the building; the render project owns its cameras, photos, render
/// materials, "render.*" extensions and the scene components placed there.
/// </summary>
public static class RenderModelUpdate
{
    /// <summary>Extension key naming the model state both applications last agreed on.</summary>
    public const string SyncTokenKey = "render.modelsync";

    private const string RenderExtensionPrefix = "render.";

    /// <summary>Lists whose content belongs to the render project and is never reported as a model change.</summary>
    private static readonly HashSet<string> RenderOwnedLists = [nameof(BuildingDocument.RenderScenes)];

    /// <summary>The sync token stored in a document, or an empty string when it never took part in an update.</summary>
    public static string SyncTokenOf(BuildingDocument document) =>
        document.Extensions.TryGetValue(SyncTokenKey, out var token) ? token : string.Empty;

    /// <summary>
    /// Merges <paramref name="incoming"/> into <paramref name="mine"/>. <paramref name="baseline"/> is the model both
    /// descend from; without it the incoming model replaces the building and only render-owned data is kept.
    /// </summary>
    public static RenderModelUpdateResult Merge(BuildingDocument? baseline, BuildingDocument mine, BuildingDocument incoming)
    {
        ArgumentNullException.ThrowIfNull(mine);
        ArgumentNullException.ThrowIfNull(incoming);
        var common = baseline ?? BaselineWithoutHistory(mine, incoming);
        var merge = ModelMerge.Merge(common, mine, incoming, (collection, id) => ModelWinsConflict(collection, id, mine));
        var merged = merge.Document;
        merged.Id = mine.Id;
        merged.SchemaVersion = mine.SchemaVersion;

        // The render project always keeps its own view, look and feature data, even when it left them untouched
        // since the last update: the modeling application rewrites its own copy on every export.
        if (mine.RenderSetup is not null) merged.RenderSetup = mine.RenderSetup;
        merged.Extensions = MergeExtensions(mine, incoming);
        merged.RenderMaterials = MergeMaterials(common, mine, incoming);
        var dropped = DropOrphanMaterials(merged);
        merged.Validate();

        var counts = ModelMerge.Differences(mine, merged)
            .Where(pair => !RenderOwnedLists.Contains(pair.Key))
            .ToDictionary(pair => pair.Key,
                pair => new ModelUpdateCount(pair.Value.Added.Count, pair.Value.Changed.Count, pair.Value.Removed.Count));
        var projectChanged = ProjectFile.SerializeDocument(mine) != ProjectFile.SerializeDocument(merged);
        return new RenderModelUpdateResult(merged, new RenderModelUpdateReport(
            baseline is not null, counts, merge.Report.KeptFromMine, dropped, merge.Report.Details, projectChanged));
    }

    /// <summary>
    /// Decides who keeps an element both applications edited differently since the common model.
    /// Render work (photos, view graphics and scene components) stays; the building follows the model.
    /// </summary>
    private static bool ModelWinsConflict(string collection, Guid id, BuildingDocument mine) => collection switch
    {
        nameof(BuildingDocument.RenderScenes) or nameof(BuildingDocument.Views) => false,
        nameof(BuildingDocument.StructuralElements) =>
            mine.StructuralElements.FirstOrDefault(element => element.Id == id)?.Kind != StructuralElementKind.Component,
        _ => true
    };

    /// <summary>
    /// A stand-in for the missing common model: the render project without the scene components and photos that
    /// only it has. Those then count as render additions and survive, while the rest follows the incoming model.
    /// </summary>
    private static BuildingDocument BaselineWithoutHistory(BuildingDocument mine, BuildingDocument incoming)
    {
        var known = incoming.StructuralElements.Select(element => element.Id).ToHashSet();
        var stand = mine.Clone();
        stand.StructuralElements.RemoveAll(element => element.Kind == StructuralElementKind.Component && !known.Contains(element.Id));
        var photos = incoming.RenderScenes.Select(scene => scene.Id).ToHashSet();
        stand.RenderScenes.RemoveAll(scene => !photos.Contains(scene.Id));
        return stand;
    }

    /// <summary>Render feature data comes from the render project; every other extension follows the model.</summary>
    private static Dictionary<string, string> MergeExtensions(BuildingDocument mine, BuildingDocument incoming)
    {
        var result = incoming.Extensions
            .Where(pair => !pair.Key.StartsWith(RenderExtensionPrefix, StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var pair in mine.Extensions.Where(pair => pair.Key.StartsWith(RenderExtensionPrefix, StringComparison.Ordinal)))
            result[pair.Key] = pair.Value;
        return result;
    }

    /// <summary>
    /// Three-way merge of render materials by surface category and element. The render project wins when both
    /// sides changed one assignment; assignments only the model added or removed follow the model.
    /// </summary>
    private static List<RenderMaterialAssignment> MergeMaterials(BuildingDocument common, BuildingDocument mine, BuildingDocument incoming)
    {
        static Dictionary<(SurfaceKind, Guid?), RenderMaterialAssignment> Index(BuildingDocument document)
        {
            var index = new Dictionary<(SurfaceKind, Guid?), RenderMaterialAssignment>();
            foreach (var material in document.RenderMaterials) index[(material.Category, material.ElementId)] = material;
            return index;
        }
        var before = Index(common);
        var kept = Index(mine);
        var theirs = Index(incoming);
        var result = new List<RenderMaterialAssignment>();
        foreach (var key in kept.Keys.Concat(theirs.Keys.Where(key => !kept.ContainsKey(key))))
        {
            before.TryGetValue(key, out var old);
            kept.TryGetValue(key, out var own);
            theirs.TryGetValue(key, out var other);
            var mineChanged = !Equals(own, old);
            var theirsChanged = !Equals(other, old);
            var chosen = mineChanged || !theirsChanged ? own : other;
            if (chosen is not null) result.Add(chosen);
        }
        return result;
    }

    /// <summary>Removes per-element materials whose element left the model and returns how many were removed.</summary>
    private static int DropOrphanMaterials(BuildingDocument document)
    {
        if (document.RenderMaterials.All(material => material.ElementId is null)) return 0;
        var ids = ElementIds(document);
        return document.RenderMaterials.RemoveAll(material => material.ElementId is { } id && !ids.Contains(id));
    }

    /// <summary>Every identifier carried by an element of the document's keyed lists.</summary>
    private static HashSet<Guid> ElementIds(BuildingDocument document)
    {
        var ids = new HashSet<Guid>();
        foreach (var property in typeof(BuildingDocument).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.PropertyType.IsGenericType || property.PropertyType.GetGenericTypeDefinition() != typeof(List<>)) continue;
            var key = property.PropertyType.GetGenericArguments()[0].GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
            if (key?.PropertyType != typeof(Guid) || property.GetValue(document) is not System.Collections.IEnumerable items) continue;
            foreach (var item in items) ids.Add((Guid)key.GetValue(item)!);
        }
        return ids;
    }

    /// <summary>
    /// Makes relative texture, sky and filter paths of a model absolute against its own folder, so they keep
    /// resolving after the model's data moves into a render project stored elsewhere.
    /// </summary>
    public static void RebaseAssets(BuildingDocument document, string sourceFolder)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.RenderMaterials = document.RenderMaterials.Select(material => material with
        {
            TexturePath = Rebase(material.TexturePath, sourceFolder)!,
            NormalPath = Rebase(material.NormalPath, sourceFolder),
            RoughnessPath = Rebase(material.RoughnessPath, sourceFolder),
            MetallicPath = Rebase(material.MetallicPath, sourceFolder)
        }).ToList();
    }

    private static string? Rebase(string? value, string sourceFolder)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var prefix = value.StartsWith("pbr:", StringComparison.OrdinalIgnoreCase) ? "pbr:" : "";
        var path = value[prefix.Length..];
        if (prefix.Length == 0 && new[] { "builtin:", "material:", "stock:" }.Any(scheme => path.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)))
            return value;
        return prefix + (Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(sourceFolder, path)));
    }
}
