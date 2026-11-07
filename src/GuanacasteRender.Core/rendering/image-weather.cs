using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// The Rain and Snow effects. Both are drawn on the finished picture from a hash of the pixel position, so they cost no
/// rays and no memory, repeat exactly for the same picture and size, and can be changed without rendering again. Their
/// size follows the picture height, so a 4K render shows the same weather as the preview. Snow also lies on the surfaces
/// that face up, read from the normals the renderer already keeps.
/// </summary>
public static partial class ImageProcessor
{
    private static readonly Vector3 RainTint = new(0.82f, 0.88f, 0.97f);
    private static readonly Vector3 SnowWhite = new(0.93f, 0.95f, 0.99f);

    /// <summary>The picture scale of the weather: 1 at 540 pixels tall, so streaks and flakes keep their look at any size.</summary>
    private static float WeatherUnit(int height) => Math.Max(0.5f, height / 540f);

    private static uint WeatherHash(int x, int y, int layer)
    {
        unchecked
        {
            var h = (uint)x * 0x9E3779B1u ^ (uint)y * 0x85EBCA77u ^ (uint)(layer + 1) * 0xC2B2AE3Du;
            h ^= h >> 15; h *= 0x2C1B3C6Du;
            h ^= h >> 12; h *= 0x297A2D39u;
            h ^= h >> 15;
            return h;
        }
    }

    private static float Unit(uint hash, int shift) => ((hash >> shift) & 0xFFu) / 255f;

    /// <summary>
    /// Dims and greys the scene a little (rain is overcast light) and lays slanted streaks in three depth layers: near
    /// ones long, wide and bright, far ones short and faint. The pixel is sheared so every streak is a straight vertical
    /// segment inside its own grid cell, which makes the test one hash and a few comparisons per layer.
    /// </summary>
    private static Vector3 ApplyRain(Vector3 color, int column, int row, float unit, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        var luminance = Sampling.Luminance(color);
        color = Vector3.Lerp(color, new Vector3(luminance), 0.28f * amount) * (1 - 0.2f * amount);

        const float slant = 0.2f;
        var sheared = column + slant * row;
        for (var layer = 0; layer < 3; layer++)
        {
            var scale = layer switch { 0 => 1f, 1 => 0.62f, _ => 0.38f };
            var cellWidth = 24f * unit * scale;
            var cellHeight = 96f * unit * scale;
            var cellX = (int)MathF.Floor(sheared / cellWidth);
            var cellY = (int)MathF.Floor(row / cellHeight);
            var hash = WeatherHash(cellX, cellY, layer);
            // How many cells hold a streak grows with the amount; far layers are denser but fainter.
            if (Unit(hash, 0) > amount * (0.35f + 0.2f * layer)) continue;

            var half = MathF.Max(0.6f, 0.9f * unit * scale);
            var centre = half + Unit(hash, 8) * (cellWidth - 2 * half);
            var top = Unit(hash, 16) * cellHeight * 0.2f;
            var length = cellHeight * (0.45f + 0.3f * Unit(hash, 24));
            var localX = sheared - cellX * cellWidth;
            var localY = row - cellY * cellHeight;
            var across = 1f - MathF.Abs(localX - centre) / half;
            var along = (localY - top) / length;
            if (across <= 0 || along <= 0 || along >= 1) continue;
            // Brightest in the middle of the streak, fading to nothing at both ends.
            var alpha = across * MathF.Sin(along * MathF.PI) * (0.55f - 0.12f * layer) * (0.4f + 0.6f * amount);
            color = Vector3.Lerp(color, RainTint, Math.Clamp(alpha, 0f, 0.8f));
        }
        return color;
    }

    /// <summary>
    /// Flakes in four depth layers (near ones large and bright), a cold cast over the whole picture, and a white cover on
    /// every surface that faces up. Sky pixels (no surface) get flakes and the cast but no cover.
    /// </summary>
    private static Vector3 ApplySnow(Vector3 color, int column, int row, float unit, float amount, Vector3? normal, bool surface)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        color *= Vector3.Lerp(Vector3.One, new Vector3(0.94f, 0.98f, 1.05f), 0.6f * amount);
        if (surface && normal is { } facing)
        {
            // Flat roofs, the ground and ledges take the most; walls, which stand upright, stay clear.
            var up = Math.Clamp((facing.Z - 0.45f) / 0.4f, 0f, 1f);
            up = up * up * (3 - 2 * up);
            var luminance = Sampling.Luminance(color);
            var snow = SnowWhite * Math.Clamp(0.62f + 0.55f * luminance, 0.62f, 1f);
            color = Vector3.Lerp(color, snow, 0.88f * amount * up);
        }

        for (var layer = 0; layer < 4; layer++)
        {
            var scale = layer switch { 0 => 1f, 1 => 0.7f, 2 => 0.5f, _ => 0.34f };
            var cell = 44f * unit * scale;
            var cellX = (int)MathF.Floor(column / cell);
            var cellY = (int)MathF.Floor(row / cell);
            var hash = WeatherHash(cellX, cellY, layer + 8);
            if (Unit(hash, 0) > amount * 0.85f) continue;

            var radius = MathF.Max(0.8f, 2.8f * unit * scale);
            var centreX = cellX * cell + radius + Unit(hash, 8) * (cell - 2 * radius);
            var centreY = cellY * cell + radius + Unit(hash, 16) * (cell - 2 * radius);
            var dx = column + 0.5f - centreX;
            var dy = row + 0.5f - centreY;
            var distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance >= radius) continue;
            var edge = Math.Clamp((radius - distance) / (radius * 0.6f), 0f, 1f);
            var strength = layer switch { 0 => 0.95f, 1 => 0.8f, 2 => 0.62f, _ => 0.45f };
            color = Vector3.Lerp(color, SnowWhite, edge * strength);
        }
        return color;
    }
}
