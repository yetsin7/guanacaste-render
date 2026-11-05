namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// A "photo" of the project, in the manner of Lumion: a named camera together with its own style, filter, sky and
/// finishing effects (all inside <see cref="Setup"/>), plus a small preview picture.
/// </summary>
public sealed record RenderSceneBookmark(Guid Id, string Name, RenderSetup Setup)
{
    /// <summary>Largest accepted preview (base64 characters), so a project file never grows because of thumbnails.</summary>
    public const int MaximumThumbnailLength = 300_000;

    /// <summary>Small JPEG preview of the photo as Base64; null when none was captured yet.</summary>
    public string? Thumbnail { get; init; }
}
