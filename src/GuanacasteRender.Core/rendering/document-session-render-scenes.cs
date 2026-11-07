using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Core;

public sealed partial class DocumentSession
{
    /// <summary>Adds a named render setup as an undoable project change.</summary>
    public Guid AddRenderScene(string name, RenderSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        setup.Validate();
        name = name.Trim();
        if (name.Length is < 1 or > 120) throw new InvalidDataException("The render scene name is invalid.");
        var scene = new RenderSceneBookmark(Guid.NewGuid(), name, setup);
        Commit("Transaction.AddRenderScene", document => document.RenderScenes.Add(scene));
        return scene.Id;
    }

    /// <summary>Removes a saved render scene in one undoable transaction.</summary>
    public void RemoveRenderScene(Guid id)
    {
        if (!Document.RenderScenes.Any(scene => scene.Id == id))
            throw new InvalidDataException("The render scene does not exist.");
        Commit("Transaction.RemoveRenderScene", document => document.RenderScenes.RemoveAll(scene => scene.Id == id));
    }

    // ------------------------------------------------------------------ photos
    // Photos are working data of the Render workspace. Like StoreRenderSetup they change neither the model nor the
    // undo history, so adjusting an effect never rebuilds the scene; the project is only marked as edited.

    /// <summary>Raised after a photo is added, changed, renamed, moved or removed without a model transaction.</summary>
    public event EventHandler? RenderPhotosChanged;

    /// <summary>Adds a photo at the end (or at <paramref name="index"/>) and returns its identifier.</summary>
    public Guid AddRenderPhoto(string name, RenderSetup setup, string? thumbnail, int? index = null)
    {
        ArgumentNullException.ThrowIfNull(setup);
        setup.Validate();
        name = ValidPhotoName(name);
        ValidThumbnail(thumbnail);
        if (Document.RenderScenes.Count >= 1000) throw new InvalidDataException("The render scene collection is invalid.");
        var photo = new RenderSceneBookmark(Guid.NewGuid(), name, setup) { Thumbnail = thumbnail };
        var at = index is { } requested ? Math.Clamp(requested, 0, Document.RenderScenes.Count) : Document.RenderScenes.Count;
        Document.RenderScenes.Insert(at, photo);
        RenderPhotosChanged?.Invoke(this, EventArgs.Empty);
        return photo.Id;
    }

    /// <summary>Replaces a photo's effects, camera, name or preview. A changed setup without a new preview clears the stale thumbnail; renaming keeps it.</summary>
    public void UpdateRenderPhoto(Guid id, RenderSetup? setup = null, string? name = null, string? thumbnail = null)
    {
        var at = Document.RenderScenes.FindIndex(scene => scene.Id == id);
        if (at < 0) throw new InvalidDataException("The render scene does not exist.");
        setup?.Validate();
        ValidThumbnail(thumbnail);
        var current = Document.RenderScenes[at];
        Document.RenderScenes[at] = current with
        {
            Setup = setup ?? current.Setup,
            Name = name is null ? current.Name : ValidPhotoName(name),
            Thumbnail = thumbnail ?? (setup is not null && setup != current.Setup ? null : current.Thumbnail)
        };
        RenderPhotosChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Deletes a photo.</summary>
    public void DeleteRenderPhoto(Guid id)
    {
        if (Document.RenderScenes.RemoveAll(scene => scene.Id == id) == 0)
            throw new InvalidDataException("The render scene does not exist.");
        RenderPhotosChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string ValidPhotoName(string name)
    {
        name = (name ?? string.Empty).Trim();
        if (name.Length is < 1 or > 120) throw new InvalidDataException("The render scene name is invalid.");
        return name;
    }

    private static void ValidThumbnail(string? thumbnail)
    {
        if (thumbnail is { Length: > RenderSceneBookmark.MaximumThumbnailLength })
            throw new InvalidDataException("The render scene preview is too large.");
    }
}
