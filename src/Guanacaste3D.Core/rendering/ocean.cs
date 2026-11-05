using System.Numerics;
using System.Text.Json;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core.Rendering;

/// <summary>The colour of the water of the sea: warm and turquoise, deep and blue, or a green lake.</summary>
public enum OceanTint { Caribbean, Pacific, Lake }

/// <summary>
/// An ocean for the render, as Lumion's does: it takes the place of the endless ground and runs to the horizon with waves.
/// <paramref name="Level"/> is the height of the water above the ground in metres (negative is below it, so the land stands
/// above the sea), <paramref name="WaveSize"/> multiplies the length of the waves, and <paramref name="Tint"/> the colour.
/// </summary>
public sealed record OceanSettings(bool Enabled, double Level, double WaveSize, OceanTint Tint)
{
    public static OceanSettings Off { get; } = new(false, -0.5, 1, OceanTint.Caribbean);

    public bool IsValid => double.IsFinite(Level) && Level is >= -50 and <= 50 && double.IsFinite(WaveSize) && WaveSize is >= 0.1 and <= 10 &&
        Enum.IsDefined(Tint);
}

/// <summary>The ocean as the text the project keeps, its material, and how it is put in the render mesh.</summary>
public static class Ocean
{
    public const string Key = "render.ocean";

    /// <summary>The text to keep, or null when the ocean is off and at its defaults (nothing to remember).</summary>
    public static string? Serialize(OceanSettings settings) =>
        settings == OceanSettings.Off ? null : JsonSerializer.Serialize(new { V = 1, settings.Enabled, settings.Level, settings.WaveSize, Tint = (int)settings.Tint });

    /// <summary>Reads saved settings; null when the text is damaged, the defaults when there is none.</summary>
    public static OceanSettings? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return OceanSettings.Off;
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.GetProperty("V").GetInt32() != 1) return null;
            var settings = new OceanSettings(root.GetProperty("Enabled").GetBoolean(), root.GetProperty("Level").GetDouble(),
                root.GetProperty("WaveSize").GetDouble(), (OceanTint)root.GetProperty("Tint").GetInt32());
            return settings.IsValid ? settings : null;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    public static OceanSettings Of(BuildingDocument document) =>
        TryParse(document.Extensions.GetValueOrDefault(Key)) ?? OceanSettings.Off;

    /// <summary>The material of the sea: deep dark water that mirrors the sky, with waves, and a tint of the person's choosing.</summary>
    public static RenderMaterial Material(OceanSettings settings) => MaterialLibrary.ForKind(SurfaceKind.Ocean) with
    {
        BaseColor = settings.Tint switch
        {
            OceanTint.Pacific => new Vector3(0.006f, 0.05f, 0.10f),
            OceanTint.Lake => new Vector3(0.03f, 0.09f, 0.07f),
            _ => new Vector3(0.010f, 0.125f, 0.150f)
        },
        PatternScale = (float)Math.Clamp(settings.WaveSize, 0.1, 10)
    };

    /// <summary>
    /// Puts the ocean in the render mesh in place of the endless ground, which the mesher added last: that quad is taken
    /// out and a far larger one of <see cref="SurfaceKind.Ocean"/> goes in at the water's height. Call it right after the
    /// building is meshed, before anything else is appended. Returns false, changing nothing, when the ocean is off or the
    /// mesh has no ground quad at its end.
    /// </summary>
    public static bool Apply(SceneMesh mesh, OceanSettings settings)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (!settings.Enabled || !settings.IsValid) return false;
        var triangles = mesh.TriangleCount;
        if (triangles < 2 || mesh.TriangleKinds[^1] != SurfaceKind.Ground || mesh.TriangleKinds[^2] != SurfaceKind.Ground ||
            mesh.Positions.Count < 6 || mesh.Indices.Count < 6) return false;
        var groundHeight = mesh.Positions[^6].Z;
        mesh.Positions.RemoveRange(mesh.Positions.Count - 6, 6);
        mesh.Indices.RemoveRange(mesh.Indices.Count - 6, 6);
        mesh.TriangleKinds.RemoveRange(triangles - 2, 2);
        if (mesh.Normals is { } normals && normals.Count >= 6) normals.RemoveRange(normals.Count - 6, 6);
        if (mesh.EmissionScale is { } emission && emission.Count > triangles - 2) emission.RemoveRange(triangles - 2, emission.Count - (triangles - 2));

        var (min, max) = mesh.Bounds();
        var centre = (min + max) / 2;
        var extent = MathF.Max(4000f, (max - min).Length() * 40f);
        var z = groundHeight + (float)settings.Level;
        mesh.AddQuad(new Vector3(centre.X - extent, centre.Y - extent, z), new Vector3(centre.X + extent, centre.Y - extent, z),
            new Vector3(centre.X + extent, centre.Y + extent, z), new Vector3(centre.X - extent, centre.Y + extent, z), SurfaceKind.Ocean);
        return true;
    }
}
