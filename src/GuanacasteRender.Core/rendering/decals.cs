using System.Numerics;
using System.Text.Json;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core.Rendering;

/// <summary>Which face of a wall a decal is on: the one to the left or to the right of the wall's drawing direction.</summary>
public enum DecalFace { Left, Right }

/// <summary>
/// An image stuck on a wall (a sign, a mural, a logo) that shows in the render. <see cref="Along"/> is the distance of its
/// centre from the wall's start and <see cref="CenterHeight"/> the height of its centre above the wall's level, both in
/// metres; <see cref="Width"/> and <see cref="Height"/> are its size. PNG images with transparency keep their holes.
/// </summary>
public sealed record Decal(Guid Id, Guid WallId, DecalFace Face, double Along, double CenterHeight, double Width, double Height,
    string ImagePath)
{
    public string Name => Path.GetFileNameWithoutExtension(ImagePath);
}

/// <summary>Decals as the text the project keeps, their placement on a wall, and their triangles for the render.</summary>
public static class Decals
{
    public const string Key = "render.decals";
    public const int Maximum = 200;
    private const float Lift = 0.003f;
    private const double Tolerance = 0.01;

    public static string? Serialize(IReadOnlyCollection<Decal> decals)
    {
        if (decals.Count == 0) return null;
        return JsonSerializer.Serialize(new { V = 1, Items = decals.Take(Maximum) });
    }

    /// <summary>Reads the saved decals; null when the text is damaged, an empty list when there are none.</summary>
    public static IReadOnlyList<Decal>? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.GetProperty("V").GetInt32() != 1) return null;
            var items = root.GetProperty("Items");
            if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > Maximum) return null;
            var decals = new List<Decal>();
            foreach (var item in items.EnumerateArray())
            {
                var decal = new Decal(item.GetProperty("Id").GetGuid(), item.GetProperty("WallId").GetGuid(),
                    (DecalFace)item.GetProperty("Face").GetInt32(), item.GetProperty("Along").GetDouble(),
                    item.GetProperty("CenterHeight").GetDouble(), item.GetProperty("Width").GetDouble(),
                    item.GetProperty("Height").GetDouble(), item.GetProperty("ImagePath").GetString() ?? "");
                if (!IsSane(decal)) return null;
                decals.Add(decal);
            }
            return decals;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>The decals saved in a document; empty when there are none or the text cannot be read.</summary>
    public static IReadOnlyList<Decal> Of(BuildingDocument document) =>
        TryParse(document.Extensions.GetValueOrDefault(Key)) ?? [];

    private static bool IsSane(Decal decal) =>
        decal.Id != Guid.Empty && decal.WallId != Guid.Empty && Enum.IsDefined(decal.Face) &&
        new[] { decal.Along, decal.CenterHeight, decal.Width, decal.Height }.All(double.IsFinite) &&
        decal.Width is >= 0.05 and <= 100 && decal.Height is >= 0.05 and <= 100 &&
        !string.IsNullOrWhiteSpace(decal.ImagePath) && decal.ImagePath.Length <= 520;

    /// <summary>Why a decal does not fit its wall, or null when it does (it must sit inside the wall's face).</summary>
    public static string? Fit(Wall wall, Decal decal)
    {
        if (!IsSane(decal)) return "El tamaño del estampado no es válido (de 5 cm a 100 m).";
        var length = wall.Length;
        if (decal.Along - decal.Width / 2 < -Tolerance || decal.Along + decal.Width / 2 > length + Tolerance)
            return $"El estampado se sale del muro a lo largo: el muro mide {length:0.##} m.";
        if (decal.CenterHeight - decal.Height / 2 < wall.BaseOffset - Tolerance || decal.CenterHeight + decal.Height / 2 > wall.Height + Tolerance)
            return $"El estampado se sale del muro en altura: el muro va de {wall.BaseOffset:0.##} a {wall.Height:0.##} m.";
        return null;
    }

    /// <summary>
    /// The four corners of the decal, bottom-left, bottom-right, top-right and top-left as the person standing in front of
    /// that face sees them, a few millimetres off the wall so the image never fights the wall for the surface.
    /// </summary>
    public static (Vector3 BottomLeft, Vector3 BottomRight, Vector3 TopRight, Vector3 TopLeft) Corners(Wall wall, double levelElevation, Decal decal)
    {
        var length = Math.Max(1e-6, wall.Length);
        var (dx, dy) = ((wall.End.X - wall.Start.X) / length, (wall.End.Y - wall.Start.Y) / length);
        var (nx, ny) = decal.Face == DecalFace.Left ? (-dy, dx) : (dy, -dx);
        var offset = wall.Thickness / 2 + Lift;
        var centre = new Vector3((float)(wall.Start.X + dx * decal.Along + nx * offset), (float)(wall.Start.Y + dy * decal.Along + ny * offset),
            (float)(levelElevation + decal.CenterHeight));
        var normal = new Vector3((float)nx, (float)ny, 0);
        // The viewer's right, standing in front of the face and looking at it: up × normal.
        var right = Vector3.Cross(Vector3.UnitZ, normal);
        var (halfWidth, halfHeight) = ((float)decal.Width / 2, (float)decal.Height / 2);
        return (centre - right * halfWidth - Vector3.UnitZ * halfHeight, centre + right * halfWidth - Vector3.UnitZ * halfHeight,
            centre + right * halfWidth + Vector3.UnitZ * halfHeight, centre - right * halfWidth + Vector3.UnitZ * halfHeight);
    }

    /// <summary>
    /// Adds every decal of the document to the render mesh as a textured quad on its wall. A decal whose wall is gone, that
    /// no longer fits, or whose image cannot be read is left out; the rest of the scene is not affected. Returns how many
    /// were added.
    /// </summary>
    public static int AppendTo(SceneMesh mesh, BuildingDocument document, Func<string, RenderTexture?> loadImage)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(loadImage);
        var textures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var decal in Of(document))
        {
            var wall = document.Walls.FirstOrDefault(item => item.Id == decal.WallId);
            var level = wall is null ? null : document.Levels.FirstOrDefault(item => item.Id == wall.LevelId);
            if (wall is null || level is null || Fit(wall, decal) is not null) continue;
            if (!textures.TryGetValue(decal.ImagePath, out var texture))
            {
                if (loadImage(decal.ImagePath) is not { } image) continue;
                texture = mesh.Textures.Count;
                mesh.Textures.Add(new SceneTexture(image, image.HasAlpha ? 0.5f : 0f));
                textures[decal.ImagePath] = texture;
            }
            var (bottomLeft, bottomRight, topRight, topLeft) = Corners(wall, level.Elevation, decal);
            var first = mesh.TriangleCount;
            mesh.AddQuad(bottomLeft, bottomRight, topRight, topLeft, SurfaceKind.Wall);
            mesh.TexturedTriangles[first] = new TexturedTriangle(new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), texture);
            mesh.TexturedTriangles[first + 1] = new TexturedTriangle(new Vector2(0, 1), new Vector2(1, 0), new Vector2(0, 0), texture);
            added++;
        }
        return added;
    }
}
