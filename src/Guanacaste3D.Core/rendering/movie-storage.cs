using System.Numerics;
using System.Text.Json;
using Guanacaste3D.Core.Navigation;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// The movie of a project (camera marks and layer keys) as the text the project keeps in its extension store, so it
/// survives saving and reopening. Reading is strict: a value that does not fit is refused as a whole, and the person
/// starts from an empty movie instead of a half-loaded one.
/// </summary>
public static class MovieStorage
{
    public const string Key = "render.movie";
    private const int Version = 1;
    private const int MaximumMarks = 500;
    private const int MaximumLayerKeys = 2000;
    private const int MaximumNameLength = 80;

    private sealed record MarkData(float[] P, float[] T, float Fov, float S, string? N);
    private sealed record LayerData(string L, float S, bool V);
    private sealed record MovieData(int V, bool Ease, List<MarkData> Marks, List<LayerData> Layers);

    /// <summary>The movie as text, or null when there is nothing to keep (no marks and no layer keys).</summary>
    public static string? Serialize(CameraPath path, LayerAnimation layers)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(layers);
        if (path.Marks.Count == 0 && layers.IsEmpty) return null;
        var data = new MovieData(Version, path.Ease,
            [.. path.Marks.Take(MaximumMarks).Select(mark => new MarkData(
                [mark.Position.X, mark.Position.Y, mark.Position.Z], [mark.Target.X, mark.Target.Y, mark.Target.Z],
                mark.FieldOfViewDegrees, mark.SecondsFromPrevious, string.IsNullOrEmpty(mark.Name) ? null : mark.Name))],
            [.. layers.Keys.Take(MaximumLayerKeys).Select(key => new LayerData(key.LevelId.ToString("N"), key.Seconds, key.Visible))]);
        return JsonSerializer.Serialize(data);
    }

    /// <summary>Reads a saved movie; false (with empty results) when the text is not one this version understands.</summary>
    public static bool TryParse(string? text, out CameraPath path, out LayerAnimation layers)
    {
        path = new CameraPath();
        layers = new LayerAnimation();
        if (string.IsNullOrWhiteSpace(text)) return true;
        try
        {
            var data = JsonSerializer.Deserialize<MovieData>(text, new JsonSerializerOptions { MaxDepth = 8 });
            if (data is null || data.V != Version || data.Marks is null || data.Layers is null ||
                data.Marks.Count > MaximumMarks || data.Layers.Count > MaximumLayerKeys) return Refuse(out path, out layers);
            var parsedPath = new CameraPath { Ease = data.Ease };
            foreach (var mark in data.Marks)
            {
                if (mark.P is not { Length: 3 } || mark.T is not { Length: 3 } || !float.IsFinite(mark.Fov) || !float.IsFinite(mark.S) ||
                    mark.P.Concat(mark.T).Any(value => !float.IsFinite(value)) || mark.Fov is < 5 or > 150 ||
                    mark.N is { Length: > MaximumNameLength }) return Refuse(out path, out layers);
                parsedPath.Add(new CameraMark(new Vector3(mark.P[0], mark.P[1], mark.P[2]), new Vector3(mark.T[0], mark.T[1], mark.T[2]),
                    mark.Fov, mark.S, mark.N ?? ""));
            }
            var parsedLayers = new LayerAnimation();
            foreach (var key in data.Layers)
            {
                if (!Guid.TryParseExact(key.L, "N", out var level) || !float.IsFinite(key.S) || key.S < 0) return Refuse(out path, out layers);
                parsedLayers.Set(level, key.S, key.V);
            }
            path = parsedPath;
            layers = parsedLayers;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException)
        {
            return Refuse(out path, out layers);
        }
    }

    private static bool Refuse(out CameraPath path, out LayerAnimation layers)
    {
        path = new CameraPath();
        layers = new LayerAnimation();
        return false;
    }
}
