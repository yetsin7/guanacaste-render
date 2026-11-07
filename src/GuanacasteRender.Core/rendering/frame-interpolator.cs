using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>A rendered frame of a movie kept for making the frames between it and the next: its picture, depth, camera and the buffers effects need.</summary>
internal sealed record KeyFrame(Vector3[] Radiance, FeatureBuffer Features, ScreenProjection View, float Exposure);

/// <summary>
/// Frames between two rendered ones, made the way a movie renderer does it: every pixel of each rendered frame is moved to where it
/// falls in the camera of the frame in between, using its depth, so near things shift more than far ones (parallax) and the picture
/// is the real one seen from the right place, not a blend of two. The two warped pictures are mixed by how far along the frame is;
/// where they disagree about what is in front (something uncovered or hidden by the move) the nearer surface wins, and the few
/// pixels neither saw are filled from their neighbours. For a camera that glides, the frames in between look like rendered ones
/// at a hundredth of the cost, which is what makes a smooth, sharp minute of video affordable.
/// </summary>
internal static class FrameInterpolator
{
    /// <summary>Distance used for sky, whose pixels turn with the camera but do not shift with its position.</summary>
    private const float SkyDistance = 10000f;

    /// <summary>
    /// A full-size picture from one lit at a lower resolution: the light is moved to each full-size pixel from the low-resolution
    /// pixels that agree with it about surface and distance, and the colour comes back from the full-size surface, so edges and
    /// textures are as sharp as in a full-size render while only a fraction of the paths were traced. The sky, which has no surface to
    /// agree with, is read smoothly from the low-resolution picture.
    /// </summary>
    public static Vector3[] UpscaleLighting(Vector3[] low, FeatureBuffer lowFeatures, FeatureBuffer highFeatures)
    {
        var image = new GuidedUpsampler().Upscale(low, lowFeatures, highFeatures);
        int width = highFeatures.Width, height = highFeatures.Height, lowWidth = lowFeatures.Width, lowHeight = lowFeatures.Height;
        Parallel.For(0, height, row =>
        {
            for (var column = 0; column < width; column++)
            {
                var index = row * width + column;
                if (!float.IsInfinity(highFeatures.Depth[index])) continue;
                float u = (column + 0.5f) * lowWidth / width - 0.5f, v = (row + 0.5f) * lowHeight / height - 0.5f;
                int x0 = Math.Clamp((int)MathF.Floor(u), 0, lowWidth - 1), y0 = Math.Clamp((int)MathF.Floor(v), 0, lowHeight - 1);
                int x1 = Math.Min(x0 + 1, lowWidth - 1), y1 = Math.Min(y0 + 1, lowHeight - 1);
                float fx = Math.Clamp(u - x0, 0, 1), fy = Math.Clamp(v - y0, 0, 1);
                image[index] = Vector3.Lerp(Vector3.Lerp(low[y0 * lowWidth + x0], low[y0 * lowWidth + x1], fx),
                                            Vector3.Lerp(low[y1 * lowWidth + x0], low[y1 * lowWidth + x1], fx), fy);
            }
        });
        return image;
    }

    /// <summary>A copy of the buffers of a frame, because the renderer reuses its own for the next one.</summary>
    public static FeatureBuffer Copy(FeatureBuffer source)
    {
        var copy = new FeatureBuffer(source.Width, source.Height) { MaterialNames = source.MaterialNames };
        Array.Copy(source.Normal, copy.Normal, source.Normal.Length);
        Array.Copy(source.Albedo, copy.Albedo, source.Albedo.Length);
        Array.Copy(source.Depth, copy.Depth, source.Depth.Length);
        Array.Copy(source.Material, copy.Material, source.Material.Length);
        return copy;
    }

    /// <summary>
    /// The picture seen by <paramref name="target"/> at <paramref name="along"/> (0 at <paramref name="from"/>, 1 at <paramref name="to"/>).
    /// </summary>
    public static Vector3[] Between(KeyFrame from, KeyFrame to, float along, ScreenProjection target, int width, int height)
    {
        var (colourA, depthA) = Warp(from, target, width, height);
        var (colourB, depthB) = Warp(to, target, width, height);
        var result = new Vector3[width * height];
        var filled = new bool[width * height];
        along = Math.Clamp(along, 0f, 1f);
        Parallel.For(0, height, row =>
        {
            for (var column = 0; column < width; column++)
            {
                var index = row * width + column;
                float a = depthA[index], b = depthB[index];
                var haveA = float.IsFinite(a);
                var haveB = float.IsFinite(b);
                if (haveA && haveB)
                {
                    // Close in depth: the same surface seen twice, so mix. Far apart: one hides the other, so the nearer is what is seen.
                    result[index] = MathF.Abs(a - b) > 0.06f * MathF.Min(a, b) + 0.05f
                        ? (a < b ? colourA[index] : colourB[index])
                        : Vector3.Lerp(colourA[index], colourB[index], along);
                    filled[index] = true;
                }
                else if (haveA) { result[index] = colourA[index]; filled[index] = true; }
                else if (haveB) { result[index] = colourB[index]; filled[index] = true; }
            }
        });
        FillHoles(result, filled, width, height, along < 0.5f ? from.Radiance : to.Radiance);
        return result;
    }

    /// <summary>Moves every pixel of a rendered frame into the target camera: the colours, and the distance of what each pixel shows (infinite where nothing landed).</summary>
    private static (Vector3[] Colour, float[] Depth) Warp(KeyFrame frame, ScreenProjection target, int width, int height)
    {
        var colour = new Vector3[width * height];
        var depth = new float[width * height];
        Array.Fill(depth, float.PositiveInfinity);
        var source = frame.Features.Depth;
        var origin = frame.View.Position;
        // Several source pixels may land on one target pixel and the nearest must win, so each target row has its own lock.
        var bands = Math.Max(1, Environment.ProcessorCount);
        var locks = new object[height];
        for (var i = 0; i < height; i++) locks[i] = new object();
        Parallel.For(0, bands, band =>
        {
            for (var row = band; row < height; row += bands)
                for (var column = 0; column < width; column++)
                {
                    var index = row * width + column;
                    var distance = float.IsFinite(source[index]) ? source[index] : SkyDistance;
                    var point = origin + frame.View.Direction(column + 0.5f, row + 0.5f) * distance;
                    if (!target.TryProject(point, out var x, out var y, out var nearness, out _)) continue;
                    // The pixel lands in the target pixel that holds the point; any pixel nothing lands in (the camera moving closer
                    // stretches the picture) is filled afterwards from its neighbours.
                    int px = (int)MathF.Floor(x), py = (int)MathF.Floor(y);
                    if ((uint)px >= (uint)width || (uint)py >= (uint)height) continue;
                    var slot = py * width + px;
                    lock (locks[py])
                    {
                        if (nearness >= depth[slot]) continue;
                        depth[slot] = nearness;
                        colour[slot] = frame.Radiance[index];
                    }
                }
        });
        return (colour, depth);
    }

    /// <summary>Gives the pixels no frame reached the mean of their filled neighbours, a few rings deep, then the nearest key frame's own pixel.</summary>
    private static void FillHoles(Vector3[] image, bool[] filled, int width, int height, Vector3[] fallback)
    {
        for (var pass = 0; pass < 6; pass++)
        {
            var changed = new List<(int, Vector3)>();
            for (var row = 0; row < height; row++)
                for (var column = 0; column < width; column++)
                {
                    var index = row * width + column;
                    if (filled[index]) continue;
                    var sum = Vector3.Zero;
                    var count = 0;
                    if (column > 0 && filled[index - 1]) { sum += image[index - 1]; count++; }
                    if (column < width - 1 && filled[index + 1]) { sum += image[index + 1]; count++; }
                    if (row > 0 && filled[index - width]) { sum += image[index - width]; count++; }
                    if (row < height - 1 && filled[index + width]) { sum += image[index + width]; count++; }
                    if (count > 0) changed.Add((index, sum / count));
                }
            if (changed.Count == 0) break;
            foreach (var (index, value) in changed)
            {
                image[index] = value;
                filled[index] = true;
            }
        }
        for (var index = 0; index < image.Length; index++)
            if (!filled[index]) image[index] = fallback[index];
    }
}
