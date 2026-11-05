using System.Text.RegularExpressions;

namespace Guanacaste3D.Core;

/// <summary>
/// A place in the project for features that keep their own data (the render's animation keys, say) without each needing a field of its
/// own in the document: a few named pieces of text, saved with the project, changed in undoable steps and checked when the project loads.
/// Keys look like "render.layers"; a value is usually JSON and is the owner's to read and write.
/// </summary>
public static partial class ProjectExtensions
{
    public const int MaximumKeys = 50;
    public const int MaximumValueLength = 500_000;

    [GeneratedRegex("^[a-z][a-z0-9]*(\\.[a-z][a-z0-9]*){0,3}$")]
    private static partial Regex KeyPattern();

    public static bool IsValidKey(string? key) => key is not null && key.Length <= 64 && KeyPattern().IsMatch(key);

    public static bool IsValid(Dictionary<string, string>? extensions) =>
        extensions is not null && extensions.Count <= MaximumKeys && extensions.All(pair => IsValidKey(pair.Key) && pair.Value is not null && pair.Value.Length <= MaximumValueLength);
}

public sealed partial class DocumentSession
{
    /// <summary>What a feature saved under a key, or null when it saved nothing.</summary>
    public string? GetExtension(string key) => Document.Extensions.TryGetValue(key ?? string.Empty, out var value) ? value : null;

    /// <summary>Saves text under a key as one undoable step; null or empty removes the key. Nothing changes when the value is the same.</summary>
    public void SetExtension(string key, string? value)
    {
        if (!ProjectExtensions.IsValidKey(key)) throw new InvalidDataException("The extension key is invalid.");
        if (value is { Length: > ProjectExtensions.MaximumValueLength }) throw new InvalidDataException("The extension value is too long.");
        var remove = string.IsNullOrEmpty(value);
        if (remove ? !Document.Extensions.ContainsKey(key) : GetExtension(key) == value) return;
        if (!remove && !Document.Extensions.ContainsKey(key) && Document.Extensions.Count >= ProjectExtensions.MaximumKeys) throw new InvalidDataException("The project holds too many extensions.");
        Commit("Transaction.SetExtension", document =>
        {
            if (remove) document.Extensions.Remove(key);
            else document.Extensions[key] = value!;
        });
    }
}
