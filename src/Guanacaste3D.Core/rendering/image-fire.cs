using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

public static partial class ImageProcessor
{
    /// <summary>
    /// Paints the particles of fires into the exposed (linear) picture, before bloom and tone mapping so the flames glow. Smoke
    /// goes first as a grey veil over what is behind it, then the flames as added light. A particle is hidden where the scene is
    /// nearer to the camera than it is (a wall, a tree in front of the fire), using the depth the renderer already keeps.
    /// </summary>
    private static void DrawFire(Vector3[] image, int width, int height, IReadOnlyList<FireSprite> sprites, float[]? depth)
    {
        foreach (var sprite in sprites)
        {
            // A flame is taller than wide: its particle is an ellipse stretched upward.
            float radius = sprite.Radius, tall = radius * sprite.Aspect;
            int x0 = Math.Max(0, (int)MathF.Floor(sprite.Column - radius)), x1 = Math.Min(width - 1, (int)MathF.Ceiling(sprite.Column + radius));
            int y0 = Math.Max(0, (int)MathF.Floor(sprite.Row - tall)), y1 = Math.Min(height - 1, (int)MathF.Ceiling(sprite.Row + tall));
            float inverseX = 1 / (radius * radius), inverseY = 1 / (tall * tall);
            for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - sprite.Column, dy = y + 0.5f - sprite.Row;
                    var r2 = dx * dx * inverseX + dy * dy * inverseY;
                    if (r2 >= 1) continue;
                    var index = y * width + x;
                    if (depth is not null && depth[index] < sprite.Distance - sprite.WorldRadius * 0.5f) continue;
                    var falloff = (1 - r2) * (1 - r2);
                    if (sprite.Smoke)
                    {
                        var current = image[index];
                        var veil = sprite.Color * (0.22f + 0.3f * MathF.Min(Sampling.Luminance(current), 2f));
                        image[index] = Vector3.Lerp(current, veil, Math.Clamp(sprite.Strength * falloff, 0f, 0.85f));
                    }
                    else if (sprite.Dark) image[index] = Vector3.Lerp(image[index], image[index] * 0.15f + sprite.Color * 0.05f, Math.Clamp(sprite.Strength * falloff, 0f, 0.9f));
                    else image[index] += sprite.Color * (sprite.Strength * falloff * 0.9f);
                }
        }
    }
}
